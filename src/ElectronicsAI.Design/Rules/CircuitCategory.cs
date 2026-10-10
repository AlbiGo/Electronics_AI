namespace ElectronicsAI.Design;

public enum CircuitCategory
{
    Unknown,
    Esp32Sensor,
    Relay,
    MotorDriver,
    Divider,
    Logic,
    Buck,
    Solenoid,
    LowSideLed,
    Embedded,
}

public interface ISketchValidator
{
    CircuitCategory Category { get; }

    string Reason { get; }

    bool Applies(SchematicSketch sketch);

    IReadOnlyList<SketchRule> Evaluate(SchematicSketch sketch);
}

public interface IMotorDriverValidator : ISketchValidator;

public interface IRelayValidator : ISketchValidator;

public interface ISensorValidator : ISketchValidator;

public static class SketchValidators
{
    private static readonly ISketchValidator[] Known =
    [
        MotorDriverValidator.Shared,
        RelayDriverValidator.Shared,
        SensorCircuitValidator.Shared,
    ];

    public static ISketchValidator? Select(SchematicSketch sketch) =>
        Known.FirstOrDefault(validator => validator.Applies(sketch));

    public static IReadOnlyList<ISketchValidator> All => Known;
}

public static class CircuitCategories
{
    public static CircuitCategory Of(SchematicSketch sketch)
    {
        if (RelayDriverRules.Applies(sketch))
        {
            return CircuitCategory.Relay;
        }

        if (SolenoidFamily.Pattern.Applies(sketch, null))
        {
            return CircuitCategory.Solenoid;
        }

        if (LowSideLedFamily.Pattern.Applies(sketch, null))
        {
            return CircuitCategory.LowSideLed;
        }

        if (MotorDriverRules.Applies(sketch))
        {
            return CircuitCategory.MotorDriver;
        }

        if (BuckConverterRules.Applies(sketch, null))
        {
            return CircuitCategory.Buck;
        }

        if (Esp32SensorFamily.Matches(sketch))
        {
            return CircuitCategory.Esp32Sensor;
        }

        if (DividerFamily.Matches(sketch))
        {
            return CircuitCategory.Divider;
        }

        if (LogicFamily.Matches(sketch))
        {
            return CircuitCategory.Logic;
        }

        if (ControllerIoValidator.Applies(sketch))
        {
            return CircuitCategory.Embedded;
        }

        return CircuitCategory.Unknown;
    }
}
