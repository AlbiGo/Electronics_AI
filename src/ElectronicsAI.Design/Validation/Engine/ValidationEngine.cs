namespace ElectronicsAI.Design;

public static class ValidationEngine
{
    public static IReadOnlyList<ISketchRuleSet> Sets { get; } =
    [
        ..PowerSupplyValidator.Sets,
        RelayDriverValidator.Shared,
        new SolenoidDriverValidator(),
        MotorDriverValidator.Shared,
        SensorCircuitValidator.Shared,
        new DigitalLogicValidator(),
        new LedCircuitValidator(),
    ];

    public static ISketchRuleSet? Select(SchematicSketch sketch, string? request) =>
        Sets.FirstOrDefault(set => set.Applies(sketch, request));
}
