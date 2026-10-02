namespace ElectronicsAI.Design;

public sealed record SketchRule(string Name, string Detail, string Result);

public static class ModuleSketchRules
{
    public static bool Applies(SchematicSketch sketch)
    {
        var board = SketchBoard.Of(sketch);
        return !MotorDriverRules.Applies(sketch)
            && !RelayDriverRules.Applies(sketch)
            && board.Sketch.Parts.Any(SketchBoard.IsController)
            && board.Sketch.Parts.Any(part => SketchBoard.IsModule(part) && !SketchBoard.IsController(part));
    }

    public static IReadOnlyList<SketchRule> Evaluate(SchematicSketch sketch) =>
        CircuitPatterns.Sensor.Evaluate(sketch, null).Rules;
}
