namespace UniGetUI.PolicySimulator.Core;

public sealed class BrokerSimulator(PolicyDocument policy)
{
    private readonly PolicyEvaluator _evaluator = new();

    public BrokerEvaluationResponse Evaluate(PackageRequest request)
    {
        try
        {
            var decision = _evaluator.Evaluate(policy, request);
            var command = decision.Decision == "allow" ? CommandLineBuilder.Build(request) : [];
            return new BrokerEvaluationResponse(
                request.RequestId,
                request.Manager.Name,
                request.Source.Name,
                request.Package.Id,
                request.Operation,
                decision.Decision,
                decision.RuleId,
                decision.Reason,
                decision.Decision == "allow",
                command,
                "simulated-elevated");
        }
        catch (Exception exception) when (exception is PolicyValidationException or InvalidOperationException)
        {
            return new BrokerEvaluationResponse(
                request.RequestId,
                request.Manager.Name,
                request.Source.Name,
                request.Package.Id,
                request.Operation,
                "deny",
                "<validation-failure>",
                exception.Message,
                false,
                [],
                "simulated-elevated");
        }
    }
}