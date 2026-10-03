namespace ElectronicsAI.Design;

public static class CircuitPatterns
{
    public static IReadOnlyList<ISketchRuleSet> All { get; } =
    [
        new BuckRuleSet(),
        TransistorLedFamily.MissingTransistor,
        LedFamily.BareLed,
        DividerFamily.OpenDivider,
        RelayFamily.Pattern,
        MotorFamily.Bridge,
        MotorFamily.Discrete,
        TransistorLedFamily.Pattern,
        ..Esp32SensorFamily.Patterns,
        FilterFamily.Pattern,
    ];
}
