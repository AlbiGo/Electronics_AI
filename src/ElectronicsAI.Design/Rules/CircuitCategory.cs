namespace ElectronicsAI.Design;

public enum CircuitCategory
{
    Unknown,
    Esp32Sensor,
    Led,
    Relay,
    MotorDriver,
    TransistorLed,
    Filter,
    Divider,
    Logic,
    Buck,
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

public interface ITransistorLedValidator : ISketchValidator;

public static class SketchValidators
{
    private static readonly ISketchValidator[] Known =
    [
        new MotorDriverValidator(),
        RelayDriverRules.Shared,
        new TransistorLedValidator(),
        new SensorValidator(),
    ];

    public static ISketchValidator? Select(SchematicSketch sketch) =>
        Known.FirstOrDefault(validator => validator.Applies(sketch));

    public static IReadOnlyList<ISketchValidator> All => Known;

    private sealed class MotorDriverValidator : IMotorDriverValidator
    {
        public CircuitCategory Category => CircuitCategory.MotorDriver;

        public string Reason => "Motor driver checked without ngspice.";

        public bool Applies(SchematicSketch sketch) => MotorDriverRules.Applies(sketch);

        public IReadOnlyList<SketchRule> Evaluate(SchematicSketch sketch) => MotorDriverRules.Evaluate(sketch);
    }

    private sealed class TransistorLedValidator : ITransistorLedValidator
    {
        public CircuitCategory Category => CircuitCategory.TransistorLed;

        public string Reason => "Transistor LED switch checked without ngspice.";

        public bool Applies(SchematicSketch sketch) => TransistorLedRules.Applies(sketch);

        public IReadOnlyList<SketchRule> Evaluate(SchematicSketch sketch) => TransistorLedRules.Evaluate(sketch);
    }

    private sealed class SensorValidator : ISensorValidator
    {
        public CircuitCategory Category => CircuitCategory.Esp32Sensor;

        public string Reason => "ESP32 sensor checked without ngspice.";

        public bool Applies(SchematicSketch sketch) => CircuitCategories.Of(sketch) == CircuitCategory.Esp32Sensor;

        public IReadOnlyList<SketchRule> Evaluate(SchematicSketch sketch) => ModuleSketchRules.Evaluate(sketch);
    }
}

public static class CircuitCategories
{
    public static CircuitCategory Of(SchematicSketch sketch)
    {
        if (RelayDriverRules.Applies(sketch))
        {
            return CircuitCategory.Relay;
        }

        if (MotorDriverRules.Applies(sketch))
        {
            return CircuitCategory.MotorDriver;
        }

        if (TransistorLedRules.Applies(sketch))
        {
            return CircuitCategory.TransistorLed;
        }

        if (BuckConverterRules.Applies(sketch, null))
        {
            return CircuitCategory.Buck;
        }

        if (Esp32SensorFamily.Matches(sketch))
        {
            return CircuitCategory.Esp32Sensor;
        }

        if (RcLowPassRules.Applies(sketch))
        {
            return CircuitCategory.Filter;
        }

        if (LedFamily.Matches(sketch))
        {
            return CircuitCategory.Led;
        }

        if (DividerFamily.Matches(sketch))
        {
            return CircuitCategory.Divider;
        }

        if (LogicFamily.Matches(sketch))
        {
            return CircuitCategory.Logic;
        }

        return CircuitCategory.Unknown;
    }
}
