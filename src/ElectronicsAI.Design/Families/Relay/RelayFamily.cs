using static ElectronicsAI.Design.PatternChecks;

namespace ElectronicsAI.Design;

public static class RelayFamily
{
    private static readonly PartQuery RelayPart = new(["relay"]);

    private static readonly PartQuery DiodePart = new(["diode"]);

    private static readonly PartQuery Controller = new(["mcu", "esp32"], PartText.TypeName);

    private static readonly PartQuery TypeResistor = new(["resistor"]);

    private static readonly PartQuery RelaySwitch = new(["transistor", "npn", "bjt", "mosfet"]);

    private static readonly PartQuery LedPart = new(["led"], PartText.TypeNameNote, ["resistor", "ohm", "ω", "Ω", "transistor", "npn", "pnp", "bjt", "mosfet", "nmos", "2n2222", "2n3904"], SketchPartKind.Led);

    private static readonly End Coil = new(RelayPart, ["A1", "A2"], ["coil"]);

    private static readonly End RelayBase = new(RelaySwitch, ["b"], ["base"]);

    private static readonly End RelayEmitter = new(RelaySwitch, ["e"], ["emitter"]);

    private static readonly End Anode = new(LedPart, ["a"], ["anode"]);

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
            Check("Flyback diode", "The diode is connected across the relay coil.", "The diode is not connected across the coil.",
                new SpansNets(DiodePart, Coil, 2),
                DiodePart, "No diode is across the relay coil."),
            Check("Common ground", "The controller ground joins the coil ground.", "The controller ground is not tied to the coil supply ground.",
                new SameNet(new End(Controller, Mark: Mark.Ground), new End(Mark: Mark.Ground)),
                Controller, "The sketch has no controller."),
            Check("Coil supply", "The coil is fed from the supply, not from a GPIO pin.", "The relay coil is not connected to a supply.",
                new Every(
                    new SameNet(Coil, new End(Mark: Mark.RelaySupply)),
                    new Absent(new SameNet(Coil, new End(Mark: Mark.Gpio)))),
                failOf: static board => new SameNet(Coil, new End(Mark: Mark.Gpio)).Holds(board)
                    ? "A GPIO pin is tied to the relay coil."
                    : "The relay coil is not connected to a supply."),
            Check("LED series resistor", "A resistor is in series with the indicator LED.", "The indicator LED has no series resistor.",
                new Touches(TypeResistor, Anode),
                only: LedPart),
        ],
    };
}

public sealed class RelayDriverRules : IRelayValidator, ISketchRuleSet
{
    public static readonly RelayDriverRules Shared = new();

    public CircuitCategory Category => CircuitCategory.Relay;

    public string Reason => RelayFamily.Pattern.Reason;

    public static bool Applies(SchematicSketch sketch) => RelayFamily.Pattern.Applies(sketch, null);

    public static IReadOnlyList<SketchRule> Evaluate(SchematicSketch sketch) => RelayFamily.Pattern.Evaluate(sketch, null).Rules;

    bool ISketchValidator.Applies(SchematicSketch sketch) => Applies(sketch);

    IReadOnlyList<SketchRule> ISketchValidator.Evaluate(SchematicSketch sketch) => Evaluate(sketch);

    bool ISketchRuleSet.Applies(SchematicSketch sketch, string? request) => RelayFamily.Pattern.Applies(sketch, request);

    SketchRuleResult ISketchRuleSet.Evaluate(SchematicSketch sketch, string? request) => RelayFamily.Pattern.Evaluate(sketch, request);
}
