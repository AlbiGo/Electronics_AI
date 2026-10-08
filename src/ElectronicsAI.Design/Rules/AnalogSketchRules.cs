namespace ElectronicsAI.Design;

public static class AnalogSketchRules
{
    public static SketchRule? MissingBottomResistor(SchematicSketch sketch) =>
        DividerFamily.OpenDivider.Evaluate(sketch, null).Rules.FirstOrDefault();
}
