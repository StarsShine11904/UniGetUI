# UniGetUI Package Broker HTTP Over Named Pipe Protocol

This document defines a proposed local transport for package operation requests that are evaluated by the UniGetUI package policy engine. It keeps the package operation request body identical to `schemas/unigetui.package-request.schema.1.0.json` and defines how that document is carried over HTTP/1.1 on a Windows named pipe.

The transport is intended for a future elevated broker. The current C# simulator remains unelevated and uses loopback HTTP, but it now mirrors the same versioned routes and response envelope.

## Goals

1. Use a local-only transport that is easier to ACL and audit than a TCP listener.
2. Keep the broker API request body as structured package operation data, not command text.
3. Let the broker authenticate the caller through named pipe security and compare it with request metadata.
4. Return a stable decision envelope for allow, deny, and validation-failure outcomes.
5. Preserve HTTP semantics so the client and broker can use ordinary request parsing, headers, status codes, and content negotiation.

## Pipe Endpoint

Production pipe name:

```text
\\.\pipe\UniGetUI.PackageBroker.v1
```

The `v1` suffix is part of the transport contract. A future breaking wire-protocol revision should use a new pipe name, such as `UniGetUI.PackageBroker.v2`, even if individual schema versions also change.

The server creates a byte-stream named pipe. Clients write one complete HTTP/1.1 request and read one complete HTTP/1.1 response. Persistent connections are allowed but optional; clients must not pipeline requests. The broker may close idle connections after 30 seconds.

## Security Profile

The named pipe is a local security boundary, not a network boundary.

The broker should:

1. Create the pipe with an ACL owned by `SYSTEM` or `Administrators`.
2. Allow connection from interactive authenticated users that UniGetUI supports.
3. Use pipe impersonation to capture the caller SID, session id, integrity level, and authentication id.
4. Use `GetNamedPipeClientProcessId` or equivalent platform APIs to identify the client process.
5. Verify the client process path and signature if production policy requires only the official UniGetUI client.
6. Treat request `broker.effectiveUser` as a claim to verify, not as authority. A mismatch between the request body and the authenticated pipe token must fail closed.
7. Generate server-side audit ids and timestamps; clients must not choose those values.

The broker must never execute a client-supplied command. The request schema intentionally has no command field. Allowed commands are built by the broker from validated request fields and UniGetUI manager helper semantics.

## HTTP Profile

The pipe carries HTTP/1.1 messages using ASCII headers, CRLF line endings, and UTF-8 bodies.

Required request rules:

| Rule | Requirement |
| --- | --- |
| Request target | Origin-form only, such as `/v1/package-operations/evaluate` |
| Host header | Required; use `Host: unigetui-broker` |
| Body framing | `Content-Length` is required for requests with a body |
| Chunking | `Transfer-Encoding: chunked` is not allowed |
| Compression | Request and response compression are not allowed |
| Trailers | HTTP trailers are not allowed |
| Character set | JSON request bodies are UTF-8 |
| Maximum header size | 32 KiB |
| Maximum request body size | 256 KiB |
| Maximum response body size | 1 MiB |

The production wire format should use JSON. YAML remains an admin authoring format for policy files and a simulator convenience, but production clients should send canonical JSON.

## Media Types

Request media type:

```text
application/vnd.unigetui.package-request+json; version=1.0
```

Response media type:

```text
application/vnd.unigetui.package-broker-response+json; version=1.0
```

The broker may accept `application/json` as a compatibility alias during development, but production clients should send the vendor media type.

## Common Headers

Requests should include:

| Header | Required | Description |
| --- | --- | --- |
| `UniGetUI-Protocol-Version` | Yes | Wire protocol version, `1.0` |
| `UniGetUI-Request-Id` | Yes | Must match body `requestId` |
| `Content-Type` | Yes | Request media type |
| `Accept` | Yes | Response media type |
| `Content-Length` | Yes | Exact body byte count |

Responses include:

| Header | Description |
| --- | --- |
| `UniGetUI-Protocol-Version` | Wire protocol version used by the broker |
| `UniGetUI-Audit-Id` | Server-generated audit id |
| `UniGetUI-Policy-Id` | Active policy id used for evaluation |
| `UniGetUI-Policy-Revision` | Active policy revision used for evaluation |
| `Content-Type` | Response media type |
| `Content-Length` | Exact body byte count |

## Endpoints

### `GET /v1/health`

Returns readiness information. It does not expose policy rules.

Successful response: `200 OK`.

### `GET /v1/capabilities`

Returns supported protocol versions, schema ids, managers, operations, and maximum payload sizes. Clients should call this after connecting if they need to adapt to broker capabilities.

Successful response: `200 OK`.

### `POST /v1/package-operations/evaluate`

Validates one package operation request, evaluates it against the active policy, and returns the command the broker would execute when the decision is `allow`. This endpoint does not execute package managers.

Request body schema: `schemas/unigetui.package-request.schema.1.0.json`.

Response body schema: `schemas/unigetui.package-broker-response.schema.1.0.json`.

### `POST /v1/package-operations`

Reserved for the future production operation that evaluates policy and executes the package manager when allowed. The request and response envelopes should stay the same, but `execution.mode` should be `elevated` and the response should include execution result fields in a future response schema revision.

## Status Codes

| Status | Meaning | Response body |
| --- | --- | --- |
| `200 OK` | Request validated and policy allowed the operation | Broker response with `decision: allow` |
| `403 Forbidden` | Request validated and policy denied the operation | Broker response with `decision: deny` |
| `400 Bad Request` | HTTP message was incomplete or body was missing | Broker response with `decision: deny` when possible |
| `415 Unsupported Media Type` | `Content-Type` is not supported | Broker response with `decision: deny` when possible |
| `422 Unprocessable Content` | Body parsed but failed schema or semantic validation | Broker response with `decision: deny` and `ruleId: <validation-failure>` |
| `503 Service Unavailable` | Broker is starting, has no valid policy, or cannot evaluate | Broker response or `application/problem+json` |

For policy denials and validation failures, the JSON body is more important than the HTTP status code. Clients should read `decision`, `ruleId`, `reason`, and `wouldExecute` from the response body.

## Request Body

The package operation request body is the same canonical document used by the policy simulator:

```json
{
  "requestVersion": "1.0.0",
  "requestType": "packageOperation",
  "requestId": "req-winget-vscode-install",
  "createdAt": "2026-05-05T12:00:00Z",
  "operation": "install",
  "manager": { "name": "Winget", "displayName": "WinGet", "executableFriendlyName": "winget.exe" },
  "source": { "name": "winget", "url": "https://cdn.winget.microsoft.com/cache", "isVirtualManager": false },
  "package": { "id": "Microsoft.VisualStudioCode", "name": "Microsoft Visual Studio Code" },
  "options": {
    "scope": "machine",
    "architecture": "x64",
    "interactive": false,
    "runAsAdministrator": true,
    "skipHashCheck": false,
    "preRelease": false,
    "customParameters": [],
    "killBeforeOperation": []
  },
  "broker": { "requestedElevation": "elevated", "effectiveUser": "CONTOSO\\alice", "clientVersion": "3.2.0" }
}
```

The broker must check that `UniGetUI-Request-Id` matches `requestId`. The broker should reject duplicate request ids with conflicting bodies and may return a cached decision for exact duplicate bodies within a short replay window.

## Response Body

The response envelope always contains a policy decision. A denied response must have `wouldExecute: false` and an empty command array.

```json
{
  "responseVersion": "1.0.0",
  "responseType": "packageBrokerResponse",
  "broker": {
    "name": "UniGetUI Package Broker",
    "protocolVersion": "1.0",
    "transport": "http-named-pipe",
    "pipeName": "\\\\.\\pipe\\UniGetUI.PackageBroker.v1",
    "elevatedSimulation": false
  },
  "auditId": "audit-20260505-000001",
  "requestId": "req-winget-vscode-install",
  "receivedAt": "2026-05-05T12:00:01Z",
  "completedAt": "2026-05-05T12:00:01Z",
  "manager": "Winget",
  "source": "winget",
  "packageId": "Microsoft.VisualStudioCode",
  "operation": "install",
  "decision": "allow",
  "ruleId": "allow.winget.vscode",
  "reason": "Visual Studio Code is approved for managed workstations.",
  "wouldExecute": true,
  "policy": {
    "id": "contoso.desktop.standard-allowlist",
    "revision": 4,
    "policyVersion": "1.0.0"
  },
  "execution": {
    "mode": "elevated",
    "command": ["winget.exe", "install", "--id", "Microsoft.VisualStudioCode", "--exact", "--source", "winget", "--scope", "machine", "--silent", "--architecture", "x64"],
    "note": "Command was constructed by the broker from validated request fields."
  }
}
```

## Versioning

The wire protocol has three related version values:

| Version | Location | Purpose |
| --- | --- | --- |
| Pipe version | Pipe name suffix, such as `.v1` | Breaking transport changes |
| Protocol version | `UniGetUI-Protocol-Version` header and `broker.protocolVersion` | HTTP route/header semantics |
| Document versions | Request, response, and policy schema versions | JSON body contracts |

Compatible schema additions require a new schema id and version but do not necessarily require a new pipe name. Breaking route, framing, or authentication changes should use a new pipe name.

## Named Pipe To HTTP Mapping

A client writes the HTTP request exactly as it would over a TCP stream, except the stream is a named pipe handle. The broker reads until headers are complete, reads exactly `Content-Length` body bytes, evaluates the request, writes an HTTP response, and either waits for the next request on the same pipe or closes the pipe.

The response should be generated from server-observed state wherever possible:

| Response field | Source of truth |
| --- | --- |
| `auditId` | Broker-generated |
| `receivedAt`, `completedAt` | Broker-generated |
| `policy` | Active validated policy |
| `decision`, `ruleId`, `reason` | Policy evaluator |
| `execution.command` | Broker command builder |
| `broker.pipeName`, client identity | Named pipe server APIs |

## Compatibility Alias In The Simulator

The C# simulator also accepts `POST /requests` over loopback HTTP as a compatibility alias for earlier samples. New clients should use `POST /v1/package-operations/evaluate`.