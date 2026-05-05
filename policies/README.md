# UniGetUI Package Broker Policies

This document describes a proposed package policy format for UniGetUI. The goal is to let IT admins publish allow-list or deny-list policy files that an elevated UniGetUI broker service can evaluate before running package manager operations requested by the regular unelevated UniGetUI process.

The initial format covers WinGet and PowerShell Gallery requests. It is intentionally shaped around UniGetUI package operation data: package identity, manager, source, operation, scope, architecture, elevation, integrity options, prerelease, install location, and custom parameters.

## Files

| File | Purpose |
| --- | --- |
| `schemas/unigetui.package-policy.schema.1.0.json` | JSON Schema for admin-authored policy files |
| `schemas/unigetui.package-request.schema.1.0.json` | JSON Schema for canonical unelevated-to-broker package requests |
| `samples/corporate-allowlist.policy.json` | Fail-closed WinGet allow-list sample |
| `samples/corporate-allowlist.policy.yaml` | YAML form of a fail-closed WinGet allow-list sample |
| `samples/deny-risky-options.policy.json` | Default-allow policy that denies risky request options |
| `samples/powershell-current-user.policy.json` | PowerShell Gallery CurrentUser-only sample |
| `samples/scenario-coverage.policy.json` | Focused policy for precedence, version, and constraint scenarios |
| `samples/requests/winget-vscode-install.request.yaml` | YAML form of a canonical WinGet request sample |
| `samples/scenarios/*.scenarios.json` | Data-driven scenario manifests with expected decisions and rule ids |
| `samples/invalid/` | Invalid policy and request fixtures for fail-closed validation scenarios |
| `scripts/Invoke-UniGetUIPolicySimulation.ps1` | Runs one policy against one or more request files |
| `scripts/Test-UniGetUIPolicySamples.ps1` | Runs the bundled end-to-end sample cases |

## Trust Boundary

The unelevated UniGetUI process should not directly choose an elevated command line. Instead, it should send a canonical package operation request to the elevated broker. The broker validates the request shape, validates the policy, evaluates policy rules, and only then constructs or runs the elevated package manager operation.

The policy decision must be made over the request data, not only the package id. A package id that is approved for silent WinGet install from the `winget` source might still be denied if the request asks for `--ignore-security-hash`, a custom `--override`, a different source, a prerelease package, or a pre/post operation command.

Recommended production behavior is fail closed:

1. If the policy cannot be loaded, deny.
2. If the request cannot be validated, deny.
3. If no rule matches and `defaultDecision` is `deny`, deny.
4. If two matching rules share a priority, deny wins.

## Broker Flow

1. The unelevated UniGetUI process resolves the selected package and options into a canonical request document.
2. The elevated broker validates the request against `unigetui.package-request.schema.1.0.json`.
3. The broker loads the admin policy and validates it against `unigetui.package-policy.schema.1.0.json`.
4. Disabled rules are ignored.
5. Every enabled rule is matched against the canonical request.
6. Matching rules are sorted by `priority`, where the lowest number wins.
7. If matching rules share the same priority, a `deny` rule wins over an `allow` rule.
8. If no rule matches, `enforcement.defaultDecision` is used.
9. If the final decision is `allow`, the broker builds the package manager command from trusted request fields and the selected UniGetUI manager helper semantics.
10. If the final decision is `deny`, the broker returns the policy reason to the client and does not run the package manager.

## Policy Format

A policy file can be authored as JSON or YAML, following WinGet's practical authoring model. The JSON Schema files remain the authoritative contract; YAML documents are parsed and normalized to canonical JSON before schema validation and rule evaluation.

Supported file extensions are `.json`, `.yaml`, and `.yml`. The simulation scripts use `ConvertFrom-Yaml` from the `powershell-yaml` module when available. When running under Windows PowerShell without that command, they can fall back to an installed `pwsh` that can import `powershell-yaml`.

JSON policies start with version and type fields inspired by WinGet manifest conventions:

```json
{
  "$schema": "https://aka.ms/unigetui/package-policy.schema.1.0.json",
  "policyVersion": "1.0.0",
  "policyType": "packageBrokerPolicy",
  "metadata": {
    "id": "contoso.desktop.standard-allowlist",
    "publisher": "Contoso IT",
    "revision": 4,
    "publishedAt": "2026-05-05T00:00:00Z"
  },
  "enforcement": {
    "defaultDecision": "deny",
    "failureDecision": "deny",
    "rulePrecedence": "priorityThenDeny"
  },
  "rules": []
}
```

The same policy shape can be written as YAML:

```yaml
"$schema": https://aka.ms/unigetui/package-policy.schema.1.0.json
policyVersion: 1.0.0
policyType: packageBrokerPolicy
metadata:
  id: contoso.desktop.standard-allowlist-yaml
  publisher: Contoso IT
  revision: 1
  publishedAt: "2026-05-05T00:00:00Z"
enforcement:
  defaultDecision: deny
  failureDecision: deny
  rulePrecedence: priorityThenDeny
rules: []
```

Rules contain four core fields:

| Field | Description |
| --- | --- |
| `id` | Stable rule id for audit logs |
| `priority` | Lower number wins |
| `decision` | `allow` or `deny` |
| `match` | Selectors that must all match the request |

Optional `constraints` let an allow rule reject risky variants of an otherwise approved package. For example, the allow-list sample permits `Microsoft.VisualStudioCode` but rejects custom parameters, integrity bypasses, prerelease packages, pre/post commands, and kill-before-operation process lists.

```json
{
  "id": "allow.winget.vscode",
  "priority": 100,
  "decision": "allow",
  "reason": "Visual Studio Code is approved for managed workstations.",
  "match": {
    "operations": [ "install", "update" ],
    "managers": [ "Winget" ],
    "sources": [ "winget" ],
    "packageIdentifiers": [ "Microsoft.VisualStudioCode" ],
    "scopes": [ "user", "machine" ],
    "architectures": [ "x64", "arm64" ]
  },
  "constraints": {
    "allowInteractive": false,
    "allowSkipHashCheck": false,
    "allowPreRelease": false,
    "allowCustomParameters": false,
    "allowPrePostCommands": false,
    "allowKillBeforeOperation": false
  }
}
```

Selectors are case-insensitive for wildcard string fields. The `packageIdentifiers`, `packageNames`, and `sources` selectors accept exact values or `*` wildcards, such as `Microsoft.*` or `PS*`.

## Request Format

A request file models what the unelevated executable asks the elevated broker to do. It mirrors UniGetUI's package operation inputs rather than a raw shell command.

```json
{
  "$schema": "https://aka.ms/unigetui/package-request.schema.1.0.json",
  "requestVersion": "1.0.0",
  "requestType": "packageOperation",
  "requestId": "req-winget-vscode-install",
  "createdAt": "2026-05-05T12:00:00Z",
  "operation": "install",
  "manager": {
    "name": "Winget",
    "displayName": "WinGet",
    "executableFriendlyName": "winget.exe"
  },
  "source": {
    "name": "winget",
    "url": "https://cdn.winget.microsoft.com/cache"
  },
  "package": {
    "id": "Microsoft.VisualStudioCode",
    "name": "Microsoft Visual Studio Code"
  },
  "options": {
    "scope": "machine",
    "architecture": "x64",
    "interactive": false,
    "runAsAdministrator": true,
    "skipHashCheck": false,
    "preRelease": false,
    "customParameters": []
  },
  "broker": {
    "requestedElevation": "elevated",
    "effectiveUser": "CONTOSO\\alice",
    "clientVersion": "3.2.0"
  }
}
```

The `broker.effectiveCommand` field can appear in samples for audit readability, but a runtime broker should build the final command from validated request fields and UniGetUI manager helpers instead of trusting a client-supplied command string.

## Manager Mapping

### WinGet

WinGet requests map to the current UniGetUI WinGet operation helper semantics:

| Request field | WinGet command meaning |
| --- | --- |
| `package.id` | `--id <id> --exact` |
| `source.name` | `--source <source>` |
| `options.scope` | `--scope user` or `--scope machine` |
| `options.version` | `--version <version>` on install |
| `options.interactive` | `--interactive` when true, otherwise `--silent` |
| `options.architecture` | `--architecture x86`, `x64`, or `arm64` |
| `options.skipHashCheck` | `--ignore-security-hash` when true |
| `options.customInstallLocation` | `--location <path>` |
| `options.customParameters` | Additional operation parameters, if policy allows them |

Known WinGet sources in UniGetUI include `winget`, `winget-fonts`, and `msstore`. The sample policies treat source as part of the trusted identity.

### PowerShell Gallery

PowerShell requests map to the current UniGetUI PowerShell operation helper semantics:

| Request field | PowerShell command meaning |
| --- | --- |
| `package.id` | `-Name <id>` |
| `options.scope` | `CurrentUser` for `user`, `AllUsers` for `machine` |
| `options.version` | `-RequiredVersion <version>` on install |
| `options.preRelease` | `-AllowPrerelease` when true |
| `options.skipHashCheck` | `-SkipPublisherCheck` when true |
| `options.customParameters` | Additional operation parameters, if policy allows them |

Known PowerShell sources in UniGetUI include `PSGallery` and `PoshTestGallery`. The `powershell-current-user.policy.json` sample uses source, package id, scope, and elevation as decision inputs.

## Scenario Outcomes

Bundled scenarios are data-driven through manifest files under `samples/scenarios/`. Each scenario declares an id, policy fixture, request fixture, expected decision, expected rule id, and tags. The current manifests cover these categories:

| Category | Coverage |
| --- | --- |
| Baseline allow-list | Approved WinGet and PowerShell requests, unknown-package default deny, and PowerShell machine-scope deny |
| Baseline deny-list | Default-allow behavior with explicit denials for hash/publisher bypass, custom parameters, and unapproved WinGet sources |
| JSON/YAML parity | JSON policy with YAML request, YAML policy with JSON request, and YAML policy with YAML request |
| Rule precedence | Disabled rules are ignored and deny wins when allow and deny rules match at the same priority |
| Version matching | Allowed WinGet version range, out-of-range default deny, and prerelease version default deny |
| Constraints | Allowed and denied custom install locations and package-manager parameters |
| Risky options | Denials for interactive installs, pre/post commands, kill-before-operation, prerelease modules, and publisher-check bypass |
| Validation | Invalid request and policy fixtures fail closed with a deny decision |

## Running The Simulation

Run all bundled sample cases:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File .\policies\scripts\Test-UniGetUIPolicySamples.ps1
```

List scenarios without running them:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File .\policies\scripts\Test-UniGetUIPolicySamples.ps1 -List
```

Run a tagged subset:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File .\policies\scripts\Test-UniGetUIPolicySamples.ps1 -Tag winget
```

Run a specific scenario manifest:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File .\policies\scripts\Test-UniGetUIPolicySamples.ps1 `
  -ScenarioPath .\policies\samples\scenarios\extended.scenarios.json
```

Run a single policy against one or more requests:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File .\policies\scripts\Invoke-UniGetUIPolicySimulation.ps1 `
  -PolicyPath .\policies\samples\corporate-allowlist.policy.json `
  -RequestPath .\policies\samples\requests\winget-vscode-*.request.json
```

Run a YAML policy against a YAML request:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File .\policies\scripts\Invoke-UniGetUIPolicySimulation.ps1 `
  -PolicyPath .\policies\samples\corporate-allowlist.policy.yaml `
  -RequestPath .\policies\samples\requests\winget-vscode-install.request.yaml
```

The simulator validates JSON or YAML syntax, normalizes YAML to JSON, optionally uses `Test-Json` for JSON Schema validation when available, performs semantic validation, evaluates rules, and prints the selected decision, rule id, and reason. The sample test runner loads scenario manifests and also asserts expected rule ids when a scenario declares one. It never runs a real package manager.

## Runtime Integration Notes

This artifact set designs and exercises the policy format; it does not add the actual elevated service. A runtime implementation should add signed or admin-protected policy locations, C# model classes, broker API contracts, audit logging, and unit tests around the same request and policy semantics. The elevated service should also reject stale or unsigned policy files before evaluating requests.