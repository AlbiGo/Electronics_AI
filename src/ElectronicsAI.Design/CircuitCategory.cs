namespace ElectronicsAI.Design;

public enum CircuitCategory
{
    Unknown,
    Sensor,
    Led,
    Relay,
    MotorDriver,
    TransistorLed,
    Filter,
    Divider,
    Logic,
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
        public CircuitCategory Category => CircuitCategory.Sensor;

        public string Reason => "Module wiring checked without ngspice.";

        public bool Applies(SchematicSketch sketch) => CircuitCategories.Of(sketch) == CircuitCategory.Sensor;

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

        if (sketch.Parts.Any(part => Is(part, "sensor", "max30102", "dht", "oled")))
        {
            return CircuitCategory.Sensor;
        }

        if (RcLowPassRules.Applies(sketch))
        {
            return CircuitCategory.Filter;
        }

        if (sketch.Parts.Any(part => SketchPartKinds.Of(part) == SketchPartKind.Led))
        {
            return CircuitCategory.Led;
        }

        var resistors = sketch.Parts.Count(part => SketchPartKinds.Of(part) == SketchPartKind.Resistor);
        if (resistors >= 2 && sketch.Parts.All(part => SketchPartKinds.IsAnalog(SketchPartKinds.Of(part))))
        {
            return CircuitCategory.Divider;
        }

        if (sketch.Parts.Any(part => Is(part, "adder", "gate", "flip")))
        {
            return CircuitCategory.Logic;
        }

        return CircuitCategory.Unknown;
    }

    private static bool Is(SketchPart part, params string[] words)
    {
        var text = $"{part.Type} {part.Name}";
        return words.Any(word => text.Contains(word, StringComparison.OrdinalIgnoreCase));
    }
}
