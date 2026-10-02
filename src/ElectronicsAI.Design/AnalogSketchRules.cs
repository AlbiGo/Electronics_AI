namespace ElectronicsAI.Design;

public static class AnalogSketchRules
{
    public static SketchRule? MissingSeriesResistor(SchematicSketch sketch) =>
        CircuitPatterns.BareLed.Evaluate(sketch, null).Rules.FirstOrDefault();

    public static SketchRule? MissingBottomResistor(SchematicSketch sketch) =>
        CircuitPatterns.OpenDivider.Evaluate(sketch, null).Rules.FirstOrDefault();
}
