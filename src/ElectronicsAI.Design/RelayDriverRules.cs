namespace ElectronicsAI.Design;

public sealed class RelayDriverRules : IRelayValidator, ISketchRuleSet
{
    public static readonly RelayDriverRules Shared = new();

    public CircuitCategory Category => CircuitCategory.Relay;

    public string Reason => CircuitPatterns.Relay.Reason;

    public static bool Applies(SchematicSketch sketch) => CircuitPatterns.Relay.Applies(sketch, null);

    public static IReadOnlyList<SketchRule> Evaluate(SchematicSketch sketch) => CircuitPatterns.Relay.Evaluate(sketch, null).Rules;

    bool ISketchValidator.Applies(SchematicSketch sketch) => Applies(sketch);

    IReadOnlyList<SketchRule> ISketchValidator.Evaluate(SchematicSketch sketch) => Evaluate(sketch);

    bool ISketchRuleSet.Applies(SchematicSketch sketch, string? request) => CircuitPatterns.Relay.Applies(sketch, request);

    SketchRuleResult ISketchRuleSet.Evaluate(SchematicSketch sketch, string? request) => CircuitPatterns.Relay.Evaluate(sketch, request);
}
