namespace ElectronicsAI.Design;

public static class RelayDriverRule
{
    public static IReadOnlyList<SketchRule> Evaluate(SchematicSketch sketch) =>
        RelayFamily.Pattern.Evaluate(sketch, null).Rules;
}
