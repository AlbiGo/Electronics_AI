namespace ElectronicsAI.Design;

public static class AnalogSketchRules
{
    public static SketchRule? MissingSeriesResistor(SchematicSketch sketch) =>
        LedFamily.BareLed.Evaluate(sketch, null).Rules.FirstOrDefault();

    public static SketchRule? MissingBottomResistor(SchematicSketch sketch) =>
        DividerFamily.OpenDivider.Evaluate(sketch, null).Rules.FirstOrDefault();
}
