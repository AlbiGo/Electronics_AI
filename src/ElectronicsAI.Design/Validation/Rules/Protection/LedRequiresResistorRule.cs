using static ElectronicsAI.Design.PatternChecks;

namespace ElectronicsAI.Design;

public static class LedRequiresResistorRule
{
    private static readonly PartQuery LedPart = new(["led"], PartText.TypeNameNote, ["resistor", "ohm", "ω", "Ω", "transistor", "npn", "pnp", "bjt", "mosfet", "nmos", "2n2222", "2n3904"], SketchPartKind.Led);

    private static readonly PartQuery TypeResistor = new(["resistor"]);

    private static readonly End Anode = new(LedPart, ["a"], ["anode"]);

    public static SketchRule Present(SketchBoard board) =>
        board.Sketch.Parts.Any(part => SketchPartKinds.Of(part) == SketchPartKind.Resistor)
            ? new SketchRule("Resistor present", "A resistor is in the circuit.", "Pass")
            : new SketchRule("Resistor present", "The LED has no series resistor.", "Fail");

    public static IReadOnlyList<SketchRule> Evaluate(SketchBoard board) =>
        Check(
            "LED series resistor",
            "A resistor is in series with the indicator LED.",
            "The indicator LED has no series resistor.",
            new Touches(TypeResistor, Anode),
            only: LedPart).Run(board, null).ToArray();
}
