namespace ElectronicsAI.Design;

public sealed class SensorCircuitValidator : ISensorValidator, ISketchRuleSet
{
    public static readonly SensorCircuitValidator Shared = new();

    public CircuitCategory Category => CircuitCategory.Esp32Sensor;

    public string Reason => "Sensor checked without ngspice.";

    public static bool Applies(SchematicSketch sketch) =>
        Esp32SensorFamily.Patterns.Any(pattern => pattern.Applies(sketch, null));

    public static SketchRuleResult Evaluate(SchematicSketch sketch, string? request)
    {
        var pattern = Esp32SensorFamily.Patterns.First(item => item.Applies(sketch, request));
        return pattern.Evaluate(sketch, request);
    }

    bool ISketchValidator.Applies(SchematicSketch sketch) => Applies(sketch);

    IReadOnlyList<SketchRule> ISketchValidator.Evaluate(SchematicSketch sketch) => Evaluate(sketch, null).Rules;

    bool ISketchRuleSet.Applies(SchematicSketch sketch, string? request) => Applies(sketch);

    SketchRuleResult ISketchRuleSet.Evaluate(SchematicSketch sketch, string? request) => Evaluate(sketch, request);
}

public sealed class MotorDriverValidator : IMotorDriverValidator, ISketchRuleSet
{
    public static readonly MotorDriverValidator Shared = new();

    public CircuitCategory Category => CircuitCategory.MotorDriver;

    public string Reason => "Motor driver checked without ngspice.";

    public bool Applies(SchematicSketch sketch) => MotorDriverRules.Applies(sketch);

    public IReadOnlyList<SketchRule> Evaluate(SchematicSketch sketch) => MotorDriverRules.Evaluate(sketch);

    bool ISketchRuleSet.Applies(SchematicSketch sketch, string? request) => Applies(sketch);

    SketchRuleResult ISketchRuleSet.Evaluate(SchematicSketch sketch, string? request) =>
        new(Reason, Evaluate(sketch), []);
}

public sealed class RelayDriverValidator : IRelayValidator, ISketchRuleSet
{
    public static readonly RelayDriverValidator Shared = new();

    public CircuitCategory Category => CircuitCategory.Relay;

    public string Reason => RelayFamily.Pattern.Reason;

    public bool Applies(SchematicSketch sketch) => RelayDriverRules.Applies(sketch);

    public IReadOnlyList<SketchRule> Evaluate(SchematicSketch sketch) => RelayDriverRules.Evaluate(sketch);

    bool ISketchRuleSet.Applies(SchematicSketch sketch, string? request) => Applies(sketch);

    SketchRuleResult ISketchRuleSet.Evaluate(SchematicSketch sketch, string? request) =>
        RelayFamily.Pattern.Evaluate(sketch, request);
}

public static class PowerSupplyValidator
{
    public static IReadOnlyList<ISketchRuleSet> Sets { get; } = [new BuckRuleSet(), DividerFamily.OpenDivider];
}

public sealed class DigitalLogicValidator : ISketchRuleSet
{
    public CircuitCategory Category => CircuitCategory.Logic;

    public bool Applies(SchematicSketch sketch, string? request) =>
        LogicFamily.Matches(sketch)
        && !RelayDriverRules.Applies(sketch)
        && !SolenoidFamily.Pattern.Applies(sketch, request)
        && !MotorDriverRules.Applies(sketch)
        && !BuckConverterRules.Applies(sketch, request)
        && !Esp32SensorFamily.Matches(sketch);

    public SketchRuleResult Evaluate(SchematicSketch sketch, string? request) =>
        new(
            "Digital logic checked without ngspice.",
            [new SketchRule("Logic recognized", "The sketch is digital logic.", "Pass")],
            []);
}

public sealed class SolenoidDriverValidator : ISketchRuleSet
{
    public CircuitCategory Category => CircuitCategory.Solenoid;

    public bool Applies(SchematicSketch sketch, string? request) => SolenoidFamily.Pattern.Applies(sketch, request);

    public SketchRuleResult Evaluate(SchematicSketch sketch, string? request) => SolenoidFamily.Pattern.Evaluate(sketch, request);
}
