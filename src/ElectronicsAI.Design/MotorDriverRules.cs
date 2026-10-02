namespace ElectronicsAI.Design;

public static class MotorDriverRules
{
    public static bool Applies(SchematicSketch sketch) =>
        CircuitPatterns.MotorBridge.Applies(sketch, null) || CircuitPatterns.MotorDiscrete.Applies(sketch, null);

    public static IReadOnlyList<SketchRule> Evaluate(SchematicSketch sketch)
    {
        var board = SketchBoard.Of(sketch);
        var pattern = board.Has(new PartQuery(["tb6612", "h-bridge", "hbridge", "drv8833", "l298", "l293", "bts7960"], PartText.TypeName))
            ? CircuitPatterns.MotorBridge
            : CircuitPatterns.MotorDiscrete;
        return pattern.Evaluate(sketch, null).Rules;
    }
}
