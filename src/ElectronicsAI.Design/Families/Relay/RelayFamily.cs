using static ElectronicsAI.Design.PatternChecks;

namespace ElectronicsAI.Design;

public static class RelayFamily
{
    private static readonly PartQuery RelayPart = new(["relay"]);

    private static readonly PartQuery TypeResistor = new(["resistor"]);

    private static readonly PartQuery RelaySwitch = new(["transistor", "npn", "bjt", "mosfet"]);

    private static readonly End Coil = new(RelayPart, ["A1", "A2"], ["coil"]);

    private static readonly End RelayBase = new(RelaySwitch, ["b"], ["base"]);

    private static readonly End RelayEmitter = new(RelaySwitch, ["e"], ["emitter"]);

    public static CircuitPattern Pattern { get; } = new()
    {
        Category = CircuitCategory.Relay,
        Reason = "Relay driver checked without ngspice.",
        When = static board => board.Has(RelayPart),
        Checks =
        [
            Check("Base resistor", "A resistor sits between the GPIO and the transistor base.", "No resistor joins a GPIO pin to the transistor base.",
                new ThroughPart(TypeResistor, RelayBase, new End(Mark: Mark.Gpio)),
                RelaySwitch, "The sketch has no transistor for the relay coil."),
            Check("Emitter grounded", "The transistor emitter returns to ground.", "The transistor emitter is not connected to ground.",
                new SameNet(RelayEmitter, new End(Mark: Mark.Ground)),
                RelaySwitch, "The sketch has no transistor."),
            new BatchCheck(static (board, _) => FlybackDiodeRule.AcrossRelay(board)),
            new BatchCheck(static (board, _) => CommonGroundRule.Relay(board)),
            Check("Coil supply", "The coil is fed from the supply, not from a GPIO pin.", "The relay coil is not connected to a supply.",
                new Every(
                    new SameNet(Coil, new End(Mark: Mark.RelaySupply)),
                    new Absent(new SameNet(Coil, new End(Mark: Mark.Gpio)))),
                failOf: static board => new SameNet(Coil, new End(Mark: Mark.Gpio)).Holds(board)
                    ? "A GPIO pin is tied to the relay coil."
                    : "The relay coil is not connected to a supply."),
            new BatchCheck(static (board, _) => LedRequiresResistorRule.Evaluate(board)),
        ],
    };
}

public sealed class RelayDriverRules : IRelayValidator, ISketchRuleSet
{
    public static readonly RelayDriverRules Shared = new();

    public CircuitCategory Category => CircuitCategory.Relay;

    public string Reason => RelayFamily.Pattern.Reason;

    public static bool Applies(SchematicSketch sketch) => RelayFamily.Pattern.Applies(sketch, null);

    public static IReadOnlyList<SketchRule> Evaluate(SchematicSketch sketch) => RelayDriverRule.Evaluate(sketch);

    bool ISketchValidator.Applies(SchematicSketch sketch) => Applies(sketch);

    IReadOnlyList<SketchRule> ISketchValidator.Evaluate(SchematicSketch sketch) => Evaluate(sketch);

    bool ISketchRuleSet.Applies(SchematicSketch sketch, string? request) => RelayFamily.Pattern.Applies(sketch, request);

    SketchRuleResult ISketchRuleSet.Evaluate(SchematicSketch sketch, string? request) => RelayFamily.Pattern.Evaluate(sketch, request);
}
