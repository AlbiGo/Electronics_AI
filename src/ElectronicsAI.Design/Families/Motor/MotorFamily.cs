using static ElectronicsAI.Design.PatternChecks;

namespace ElectronicsAI.Design;

public static class MotorFamily
{
    private static readonly PartQuery BridgePart = new(["tb6612", "h-bridge", "hbridge", "drv8833", "l298", "l293", "bts7960"], PartText.TypeName);

    private static readonly PartQuery MotorPart = new(["motor"], PartText.TypeName);

    private static readonly PartQuery DiodePart = new(["diode"]);

    private static readonly PartQuery Controller = new(["mcu", "esp32"], PartText.TypeName);

    private static readonly PartQuery MosfetApply = new(["mosfet", "nmos"], PartText.TypeName);

    private static readonly PartQuery PowerSwitch = new(["mosfet", "nmos", "transistor", "npn", "bjt"]);

    private static readonly PartQuery TypeResistor = new(["resistor"]);

    private static readonly End Gate = new(PowerSwitch, ["g", "gate", "b", "base"]);

    private static readonly End Drain = new(PowerSwitch, ["d", "drain", "c", "collector"]);

    private static readonly End Source = new(PowerSwitch, ["s", "source", "e", "emitter"]);

    public static CircuitPattern Bridge { get; } = new()
    {
        Category = CircuitCategory.MotorDriver,
        Reason = "Motor driver checked without ngspice.",
        When = static board => !RelayFamily.Pattern.When(board) && board.Has(BridgePart),
        Checks =
        [
            Check("H-bridge driver", "{name} drives the motor.", "The sketch has no H-bridge.", new HasPart(BridgePart), BridgePart),
            Check("Motor outputs", "Both bridge outputs connect to the motor.", "The motor is not wired across the bridge outputs.",
                new Every(
                    new SameNet(new End(BridgePart, ["AO1", "BO1", "OUT1"]), new End(MotorPart)),
                    new SameNet(new End(BridgePart, ["AO2", "BO2", "OUT2"]), new End(MotorPart)))),
            Check("Separate motor supply", "VM is separate from the logic rail.", "The motor supply is missing or tied to the logic rail.",
                new OnPinNet(new End(BridgePart, ["VM", "VMOT", "VBB"]), new End(Mark: Mark.MotorFlybackSupply), new End(BridgePart, ["VCC", "VDD", "3V3"]))),
            Check("Common ground", "The controller ground joins the driver ground.", "The controller and the driver do not share a ground.",
                new SameNet(new End(Controller, Mark: Mark.Ground), new End(BridgePart, ["GND"]))),
            Check("PWM input", "A GPIO pin drives PWMA.", "No GPIO pin drives the PWM input.",
                new SameNet(new End(BridgePart, ["PWMA", "PWMB", "PWM"]), new End(Mark: Mark.PwmOrGpio))),
            Check("Direction pins", "AIN1 and AIN2 are driven by the controller.", "The direction inputs are not both driven.",
                new Some(
                    new Every(new SameNet(new End(BridgePart, ["AIN1"]), new End(Mark: Mark.PwmOrGpio)), new SameNet(new End(BridgePart, ["AIN2"]), new End(Mark: Mark.PwmOrGpio))),
                    new Every(new SameNet(new End(BridgePart, ["IN1"]), new End(Mark: Mark.PwmOrGpio)), new SameNet(new End(BridgePart, ["IN2"]), new End(Mark: Mark.PwmOrGpio))))),
            Check("Standby pulled high", "STBY is held high, so the driver is enabled.", "STBY is floating or held low.",
                new Some(
                    new SameNet(new End(BridgePart, ["STBY", "STANDBY"]), new End(Mark: Mark.LogicHigh)),
                    new ThroughPart(TypeResistor, new End(BridgePart, ["STBY", "STANDBY"]), new End(Mark: Mark.LogicHigh), Distinct: true))),
        ],
    };

    public static CircuitPattern Discrete { get; } = new()
    {
        Category = CircuitCategory.MotorDriver,
        Reason = "Motor driver checked without ngspice.",
        When = static board => !RelayFamily.Pattern.When(board) && !Bridge.When(board)
            && (board.Has(MotorPart) || (board.Has(MosfetApply) && board.Has(DiodePart) && board.Has(Controller))),
        Checks =
        [
            Check("MOSFET or transistor", "{name} switches the motor.", "The sketch has no MOSFET or transistor.", new HasPart(PowerSwitch), PowerSwitch),
            Check("Flyback diode", "The diode is across the motor and the switch.", "The diode is not connected across the motor.",
                new Every(new SameNet(Drain, new End(DiodePart)), new SameNet(new End(DiodePart), new End(Mark: Mark.MotorFlybackSupply))),
                DiodePart, "No diode is across the motor."),
            Check("Separate motor supply", "The motor has its own supply, separate from the GPIO pin.", "The motor supply is missing or tied to a GPIO pin.",
                new NetState(new End(Mark: Mark.MotorDiscreteSupply), new End(Mark: Mark.PwmOrGpio))),
            Check("Common ground", "The controller ground joins the motor ground.", "The controller and the motor do not share a ground.",
                new SameNet(new End(Controller, Mark: Mark.Ground), Source)),
            Check("Gate resistor", "A resistor sits between the PWM pin and the gate.", "No resistor joins the PWM pin to the gate.",
                new ThroughPart(TypeResistor, Gate, new End(Mark: Mark.PwmOrGpio)),
                PowerSwitch, "There is no gate or base to drive."),
            Check("Pull-down resistor", "A resistor holds the gate low when the GPIO is off.", "No pull-down resistor holds the gate low.",
                new ThroughPart(TypeResistor, Gate, new End(Mark: Mark.Ground), Distinct: true),
                PowerSwitch, "There is no gate or base for a pull-down."),
            Check("PWM source", "An ESP32 GPIO pin drives the gate.", "No GPIO or PWM pin drives the switch.",
                new NetState(new End(Mark: Mark.AnyPwm))),
        ],
    };
}

public static class MotorDriverRules
{
    public static bool Applies(SchematicSketch sketch) =>
        MotorFamily.Bridge.Applies(sketch, null) || MotorFamily.Discrete.Applies(sketch, null);

    public static IReadOnlyList<SketchRule> Evaluate(SchematicSketch sketch)
    {
        var board = SketchBoard.Of(sketch);
        var pattern = board.Has(new PartQuery(["tb6612", "h-bridge", "hbridge", "drv8833", "l298", "l293", "bts7960"], PartText.TypeName))
            ? MotorFamily.Bridge
            : MotorFamily.Discrete;
        return pattern.Evaluate(sketch, null).Rules;
    }
}
