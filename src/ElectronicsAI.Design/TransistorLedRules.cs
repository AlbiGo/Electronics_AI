namespace ElectronicsAI.Design;

public static class TransistorLedRules
{
    public static bool Applies(SchematicSketch sketch) => CircuitPatterns.TransistorLed.Applies(sketch, null);

    public static IReadOnlyList<SketchRule> Evaluate(SchematicSketch sketch) => CircuitPatterns.TransistorLed.Evaluate(sketch, null).Rules;
}
