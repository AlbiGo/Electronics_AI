using static ElectronicsAI.Design.PatternChecks;

namespace ElectronicsAI.Design;

public static class TransistorLedFamily
{
    private static readonly SwitchTerm[] SwitchWords =
    [
        SwitchTerm.Transistor,
        SwitchTerm.Npn,
        SwitchTerm.Pnp,
        SwitchTerm.Bjt,
        SwitchTerm.Mosfet,
        SwitchTerm.Nmos,
        SwitchTerm.TwoN2222,
        SwitchTerm.TwoN3904,
    ];

    private static readonly PartQuery AnyResistor = new(["resistor", "ohm", "ω", "Ω"], PartText.TypeNameNote, Kind: SketchPartKind.Resistor);

    private static readonly PartQuery SwitchPart = new(Words(SwitchWords), PartText.TypeNameNote);

    private static readonly PartQuery FieldPart = new(["mosfet", "nmos"], PartText.TypeNameNote);

    private static readonly PartQuery LedPart = new(["led"], PartText.TypeNameNote, ["resistor", "ohm", "ω", "Ω", ..Words(SwitchWords)], SketchPartKind.Led);

    private static readonly End Control = new(SwitchPart, ["b", "g"], ["base", "gate"]);

    private static readonly End Base = new(SwitchPart, ["b"], ["base"]);

    private static readonly End Emitter = new(SwitchPart, ["e"], ["emitter"]);

    private static readonly End Collector = new(SwitchPart, ["c"], ["collector"]);

    private static readonly End FetGate = new(SwitchPart, ["g"], ["gate"]);

    private static readonly End FetSource = new(SwitchPart, ["s"], ["source"]);

    private static readonly End FetDrain = new(SwitchPart, ["d"], ["drain"]);

    public static ISketchRuleSet MissingTransistor { get; } = new RequiredPartRuleSet(
        CircuitCategory.TransistorLed,
        [SwitchTerm.Transistor, SwitchTerm.Npn, SwitchTerm.Pnp, SwitchTerm.Mosfet, SwitchTerm.Bjt],
        SwitchWords,
        "Transistor",
        "The request asks for a transistor, and the sketch has none.");

    public static CircuitPattern Pattern { get; } = new()
    {
        Category = CircuitCategory.TransistorLed,
        Reason = "Transistor LED switch checked without ngspice.",
        When = static board => !RelayFamily.Pattern.When(board) && !MotorFamily.Bridge.When(board) && !MotorFamily.Discrete.When(board) && board.Has(SwitchPart) && board.Has(LedPart),
        Checks =
        [
            Check("Transistor", "{name} switches the LED.", "The sketch has no transistor.", new HasPart(SwitchPart), SwitchPart),
            Check("LED series resistor", "A resistor sits between the supply and the LED.", "The LED has no series resistor from the supply.",
                new ThroughPart(AnyResistor, new End(LedPart), new End(Mark: Mark.TransistorSupply), Avoid: Control)),
            Check("Base resistor", "A resistor sits between the drive and the base.", "No resistor joins a drive to the base.",
                new Some(
                    new Every(new Absent(new PinsUsed(SwitchPart)), new ThroughPart(AnyResistor, new End(SwitchPart), new End(SwitchPart), Note: ["base"])),
                    new Every(new PinsUsed(SwitchPart), new ThroughPart(AnyResistor, Base, new End(Mark: Mark.Drive), Distinct: true, BWithout: new End(LedPart)))),
                SwitchPart, "The sketch has no transistor.", unless: FieldPart),
            Check("Gate resistor", "A resistor sits between the drive and the gate.", "No resistor joins a drive to the gate.",
                new Some(
                    new Every(new Absent(new PinsUsed(SwitchPart)), new ThroughPart(AnyResistor, new End(SwitchPart), new End(SwitchPart), Note: ["gate"])),
                    new Every(new PinsUsed(SwitchPart), new ThroughPart(AnyResistor, FetGate, new End(Mark: Mark.Drive), Distinct: true, BWithout: new End(LedPart)))),
                SwitchPart, "The sketch has no transistor.", only: FieldPart),
            Check("Emitter grounded", "The emitter returns to ground.", "The emitter is not connected to ground.",
                new Some(
                    new SameNet(Emitter, new End(Mark: Mark.Ground)),
                    new Every(new Absent(new HasNet(Emitter)), new SameNet(new End(SwitchPart), new End(Mark: Mark.Ground)))),
                SwitchPart, "The sketch has no transistor.", unless: FieldPart),
            Check("Source grounded", "The source returns to ground.", "The source is not connected to ground.",
                new Some(
                    new SameNet(FetSource, new End(Mark: Mark.Ground)),
                    new Every(new Absent(new HasNet(FetSource)), new SameNet(new End(SwitchPart), new End(Mark: Mark.Ground)))),
                SwitchPart, "The sketch has no transistor.", only: FieldPart),
            Check("Low-side switch", "The transistor collector sinks the LED to ground.", "The transistor does not switch the LED on its low side.",
                new Some(
                    new Every(new Absent(new PinsUsed(SwitchPart)), new SameNet(new End(SwitchPart), new End(LedPart)), new SameNet(new End(SwitchPart), new End(Mark: Mark.Ground))),
                    new Every(new PinsUsed(SwitchPart), new SameNet(Collector, new End(LedPart), new End(Mark: Mark.TransistorSupply)))),
                SwitchPart, "The sketch has no transistor.", unless: FieldPart),
            Check("Low-side switch", "The transistor collector sinks the LED to ground.", "The transistor does not switch the LED on its low side.",
                new Some(
                    new Every(new Absent(new PinsUsed(SwitchPart)), new SameNet(new End(SwitchPart), new End(LedPart)), new SameNet(new End(SwitchPart), new End(Mark: Mark.Ground))),
                    new Every(new PinsUsed(SwitchPart), new SameNet(FetDrain, new End(LedPart), new End(Mark: Mark.TransistorSupply)))),
                SwitchPart, "The sketch has no transistor.", only: FieldPart),
        ],
    };

    private static string[] Words(SwitchTerm[] terms) => terms.Select(static term => term.Text()).ToArray();
}

public static class TransistorLedRules
{
    public static bool Applies(SchematicSketch sketch) => TransistorLedFamily.Pattern.Applies(sketch, null);

    public static IReadOnlyList<SketchRule> Evaluate(SchematicSketch sketch) => TransistorLedFamily.Pattern.Evaluate(sketch, null).Rules;
}
