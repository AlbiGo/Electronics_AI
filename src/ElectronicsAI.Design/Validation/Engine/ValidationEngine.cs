namespace ElectronicsAI.Design;

public static class ValidationEngine
{
    public static IReadOnlyList<ISketchRuleSet> Sets { get; } =
    [
        ..PowerSupplyValidator.Sets,
        RelayDriverValidator.Shared,
        new SolenoidDriverValidator(),
        new LowSideLedValidator(),
        MotorDriverValidator.Shared,
        SensorCircuitValidator.Shared,
        new DigitalLogicValidator(),
        new ControllerIoValidator(),
        new LedCircuitValidator(),
    ];

    public static ISketchRuleSet? Select(SchematicSketch sketch, string? request) =>
        Sets.FirstOrDefault(set => set.Applies(sketch, request));
}
