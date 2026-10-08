namespace ElectronicsAI.Design;

public static class MotorDriverRule
{
    public static IReadOnlyList<SketchRule> Evaluate(SchematicSketch sketch)
    {
        var board = SketchBoard.Of(sketch);
        var pattern = board.Has(new PartQuery(["tb6612", "h-bridge", "hbridge", "drv8833", "l298", "l293", "bts7960"], PartText.TypeName))
            ? MotorFamily.Bridge
            : MotorFamily.Discrete;
        return pattern.Evaluate(sketch, null).Rules;
    }
}
