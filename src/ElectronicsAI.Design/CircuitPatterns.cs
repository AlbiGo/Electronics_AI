namespace ElectronicsAI.Design;

public static class CircuitPatterns
{
    private static readonly SwitchTerm[] SwitchWords =
    [
        SwitchTerm.Transistor,
        SwitchTerm.Npn,
        SwitchTerm.Pnp,
        SwitchTerm.Bjt,
        SwitchTerm.Mosfet,
        SwitchTerm.Nmos,
        SwitchTerm.TwoN2222,
        SwitchTerm.TwoN3904,
    ];

    private static readonly PartQuery RelayPart = new(["relay"]);

    private static readonly PartQuery BridgePart = new(["tb6612", "h-bridge", "hbridge", "drv8833", "l298", "l293", "bts7960"], PartText.TypeName);

    private static readonly PartQuery MotorPart = new(["motor"], PartText.TypeName);

    private static readonly PartQuery DiodePart = new(["diode"]);

    private static readonly PartQuery Controller = new(["mcu", "esp32"], PartText.TypeName);

    private static readonly PartQuery MosfetApply = new(["mosfet", "nmos"], PartText.TypeName);

    private static readonly PartQuery PowerSwitch = new(["mosfet", "nmos", "transistor", "npn", "bjt"]);

    private static readonly PartQuery RelaySwitch = new(["transistor", "npn", "bjt", "mosfet"]);

    private static readonly PartQuery TypeResistor = new(["resistor"]);

    private static readonly PartQuery AnyResistor = new(["resistor", "ohm", "ω", "Ω"], PartText.TypeNameNote, Kind: SketchPartKind.Resistor);

    private static readonly PartQuery SwitchPart = new(Words(SwitchWords), PartText.TypeNameNote);

    private static readonly PartQuery FieldPart = new(["mosfet", "nmos"], PartText.TypeNameNote);

    private static readonly PartQuery LedPart = new(["led"], PartText.TypeNameNote, ["resistor", "ohm", "ω", "Ω", ..Words(SwitchWords)], SketchPartKind.Led);

    private static readonly PartQuery SensorWord = new(["sensor", "max30102", "dht", "oled"], PartText.TypeName);

    private static readonly End Coil = new(RelayPart, ["A1", "A2"], ["coil"]);

    private static readonly End RelayBase = new(RelaySwitch, ["b"], ["base"]);

    private static readonly End RelayEmitter = new(RelaySwitch, ["e"], ["emitter"]);

    private static readonly End Anode = new(LedPart, ["a"], ["anode"]);

    private static readonly End Gate = new(PowerSwitch, ["g", "gate", "b", "base"]);

    private static readonly End Drain = new(PowerSwitch, ["d", "drain", "c", "collector"]);

    private static readonly End Source = new(PowerSwitch, ["s", "source", "e", "emitter"]);

    private static readonly End Control = new(SwitchPart, ["b", "g"], ["base", "gate"]);

    private static readonly End Base = new(SwitchPart, ["b"], ["base"]);

    private static readonly End Emitter = new(SwitchPart, ["e"], ["emitter"]);

    private static readonly End Collector = new(SwitchPart, ["c"], ["collector"]);

    private static readonly End FetGate = new(SwitchPart, ["g"], ["gate"]);

    private static readonly End FetSource = new(SwitchPart, ["s"], ["source"]);

    private static readonly End FetDrain = new(SwitchPart, ["d"], ["drain"]);

    public static CircuitPattern BareLed { get; } = new()
    {
        Category = CircuitCategory.Led,
        Reason = "Missing current limiting resistor.",
        FailOnly = true,
        Checks = [new BatchCheck(static (board, _) => MissingSeries(board) is { } rule ? [rule] : [])],
    };

    public static CircuitPattern OpenDivider { get; } = new()
    {
        Category = CircuitCategory.Divider,
        Reason = "Missing the bottom resistor.",
        FailOnly = true,
        Checks = [new BatchCheck(static (board, _) => MissingBottom(board) is { } rule ? [rule] : [])],
    };

    public static CircuitPattern Relay { get; } = new()
    {
        Category = CircuitCategory.Relay,
        Reason = "Relay driver checked without ngspice.",
        When = static board => board.Has(RelayPart),
        Checks =
        [
            Check("Base resistor", "A resistor sits between the GPIO and the transistor base.", "No resistor joins a GPIO pin to the transistor base.",
                new ThroughPart(TypeResistor, RelayBase, new End(Mark: Mark.Gpio)),
                RelaySwitch, "The sketch has no transistor for the relay coil."),
            Check("Emitter grounded", "The transistor emitter returns to ground.", "The transistor emitter is not connected to ground.",
                new SameNet(RelayEmitter, new End(Mark: Mark.Ground)),
                RelaySwitch, "The sketch has no transistor."),
            Check("Flyback diode", "The diode is connected across the relay coil.", "The diode is not connected across the coil.",
                new SpansNets(DiodePart, Coil, 2),
                DiodePart, "No diode is across the relay coil."),
            Check("Common ground", "The controller ground joins the coil ground.", "The controller ground is not tied to the coil supply ground.",
                new SameNet(new End(Controller, Mark: Mark.Ground), new End(Mark: Mark.Ground)),
                Controller, "The sketch has no controller."),
            Check("Coil supply", "The coil is fed from the supply, not from a GPIO pin.", "The relay coil is not connected to a supply.",
                new Every(
                    new SameNet(Coil, new End(Mark: Mark.RelaySupply)),
                    new Absent(new SameNet(Coil, new End(Mark: Mark.Gpio)))),
                failOf: static board => new SameNet(Coil, new End(Mark: Mark.Gpio)).Holds(board)
                    ? "A GPIO pin is tied to the relay coil."
                    : "The relay coil is not connected to a supply."),
            Check("LED series resistor", "A resistor is in series with the indicator LED.", "The indicator LED has no series resistor.",
                new Touches(TypeResistor, Anode),
                only: LedPart),
        ],
    };

    public static CircuitPattern MotorBridge { get; } = new()
    {
        Category = CircuitCategory.MotorDriver,
        Reason = "Motor driver checked without ngspice.",
        When = static board => !board.Has(RelayPart) && board.Has(BridgePart),
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

    public static CircuitPattern MotorDiscrete { get; } = new()
    {
        Category = CircuitCategory.MotorDriver,
        Reason = "Motor driver checked without ngspice.",
        When = static board => !board.Has(RelayPart) && !board.Has(BridgePart)
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

    public static CircuitPattern TransistorLed { get; } = new()
    {
        Category = CircuitCategory.TransistorLed,
        Reason = "Transistor LED switch checked without ngspice.",
        When = static board => !Relay.When(board) && !MotorBridge.When(board) && !MotorDiscrete.When(board) && board.Has(SwitchPart) && board.Has(LedPart),
        Checks =
        [
            Check("Transistor", "{name} switches the LED.", "The sketch has no transistor.", new HasPart(SwitchPart), SwitchPart),
            Check("LED series resistor", "A resistor sits between the supply and the LED.", "The LED has no series resistor from the supply.",
                new ThroughPart(AnyResistor, new End(LedPart), new End(Mark: Mark.TransistorSupply), Avoid: Control)),
            Check("Base resistor", "A resistor sits between the drive and the base.", "No resistor joins a drive to the base.",
                new Some(
                    new Every(new Absent(new PinsUsed(SwitchPart)), new ThroughPart(AnyResistor, new End(SwitchPart), new End(SwitchPart), Note: ["base"])),
                    new Every(new PinsUsed(SwitchPart), new ThroughPart(AnyResistor, Base, new End(Mark: Mark.Drive), Distinct: true, BWithout: new End(LedPart)))),
                SwitchPart, "The sketch has no transistor.", unless: FieldPart),
            Check("Gate resistor", "A resistor sits between the drive and the gate.", "No resistor joins a drive to the gate.",
                new Some(
                    new Every(new Absent(new PinsUsed(SwitchPart)), new ThroughPart(AnyResistor, new End(SwitchPart), new End(SwitchPart), Note: ["gate"])),
                    new Every(new PinsUsed(SwitchPart), new ThroughPart(AnyResistor, FetGate, new End(Mark: Mark.Drive), Distinct: true, BWithout: new End(LedPart)))),
                SwitchPart, "The sketch has no transistor.", only: FieldPart),
            Check("Emitter grounded", "The emitter returns to ground.", "The emitter is not connected to ground.",
                new Some(
                    new SameNet(Emitter, new End(Mark: Mark.Ground)),
                    new Every(new Absent(new HasNet(Emitter)), new SameNet(new End(SwitchPart), new End(Mark: Mark.Ground)))),
                SwitchPart, "The sketch has no transistor.", unless: FieldPart),
            Check("Source grounded", "The source returns to ground.", "The source is not connected to ground.",
                new Some(
                    new SameNet(FetSource, new End(Mark: Mark.Ground)),
                    new Every(new Absent(new HasNet(FetSource)), new SameNet(new End(SwitchPart), new End(Mark: Mark.Ground)))),
                SwitchPart, "The sketch has no transistor.", only: FieldPart),
            Check("Low-side switch", "The transistor collector sinks the LED to ground.", "The transistor does not switch the LED on its low side.",
                new Some(
                    new Every(new Absent(new PinsUsed(SwitchPart)), new SameNet(new End(SwitchPart), new End(LedPart)), new SameNet(new End(SwitchPart), new End(Mark: Mark.Ground))),
                    new Every(new PinsUsed(SwitchPart), new SameNet(Collector, new End(LedPart), new End(Mark: Mark.TransistorSupply)))),
                SwitchPart, "The sketch has no transistor.", unless: FieldPart),
            Check("Low-side switch", "The transistor collector sinks the LED to ground.", "The transistor does not switch the LED on its low side.",
                new Some(
                    new Every(new Absent(new PinsUsed(SwitchPart)), new SameNet(new End(SwitchPart), new End(LedPart)), new SameNet(new End(SwitchPart), new End(Mark: Mark.Ground))),
                    new Every(new PinsUsed(SwitchPart), new SameNet(FetDrain, new End(LedPart), new End(Mark: Mark.TransistorSupply)))),
                SwitchPart, "The sketch has no transistor.", only: FieldPart),
        ],
    };

    public static CircuitPattern Sensor { get; } = new()
    {
        Category = CircuitCategory.Sensor,
        Reason = "Module wiring checked without ngspice.",
        When = static board => !Relay.When(board) && !MotorBridge.When(board) && !MotorDiscrete.When(board) && !TransistorLed.When(board) && board.Has(SensorWord),
        Checks = [new BatchCheck(static (board, _) => ModuleRules(board))],
    };

    public static CircuitPattern Filter { get; } = new()
    {
        Category = CircuitCategory.Filter,
        Reason = "Cutoff checked from the resistor and capacitor.",
        When = static board => RcLowPassRules.Applies(board.Sketch),
        AttachNetlist = true,
        Assume = RcLowPassRules.Assumptions,
        Checks = [new BatchCheck(static (board, request) => RcLowPassRules.Evaluate(board.Sketch, request))],
    };

    public static IReadOnlyList<ISketchRuleSet> All { get; } =
    [
        new RequiredPartRuleSet(
            CircuitCategory.TransistorLed,
            [SwitchTerm.Transistor, SwitchTerm.Npn, SwitchTerm.Pnp, SwitchTerm.Mosfet, SwitchTerm.Bjt],
            SwitchWords,
            "Transistor",
            "The request asks for a transistor, and the sketch has none."),
        BareLed,
        OpenDivider,
        Relay,
        MotorBridge,
        MotorDiscrete,
        TransistorLed,
        Sensor,
        Filter,
    ];

    private static string[] Words(SwitchTerm[] terms) => terms.Select(static term => term.Text()).ToArray();

    private static ClauseCheck Check(
        string name,
        string pass,
        string fail,
        Clause body,
        PartQuery? subject = null,
        string? absent = null,
        PartQuery? only = null,
        PartQuery? unless = null,
        Func<SketchBoard, string>? failOf = null) => new()
    {
        Name = name,
        Pass = pass,
        Fail = fail,
        Body = body,
        Subject = subject,
        Absent = absent,
        Only = only,
        Unless = unless,
        FailOf = failOf,
    };

    private static SketchRule? MissingSeries(SketchBoard board)
    {
        foreach (var led in board.Sketch.Parts.Where(part => SketchPartKinds.Of(part) == SketchPartKind.Led))
        {
            var anode = board.Nets.FirstOrDefault(net => net.Any(end => IsLedPin(end, led.Id, "anode", "a")));
            var cathode = board.Nets.FirstOrDefault(net => net.Any(end => IsLedPin(end, led.Id, "cathode", "k")));
            if (anode is null || cathode is null)
            {
                continue;
            }

            var supplyOnAnode = anode.Any(IsWireSupply) && cathode.Any(IsWireGround);
            var supplyOnCathode = cathode.Any(IsWireSupply) && anode.Any(IsWireGround);
            if (supplyOnAnode || supplyOnCathode)
            {
                return new SketchRule("Current limiting resistor", "Missing current limiting resistor.", "Fail");
            }
        }

        return null;
    }

    private static SketchRule? MissingBottom(SketchBoard board)
    {
        if (board.Sketch.Parts.Any(part => SketchPartKinds.Of(part) == SketchPartKind.Led))
        {
            return null;
        }

        var resistors = board.Sketch.Parts.Where(part => SketchPartKinds.Of(part) == SketchPartKind.Resistor).ToList();
        if (resistors.Count == 0)
        {
            return null;
        }

        var spansRails = resistors.Any(resistor =>
        {
            var touched = board.Nets.Where(net => net.Any(end => SketchBoard.Belongs(end, resistor))).ToList();
            return touched.Any(net => net.Any(IsWireSupply)) && touched.Any(net => net.Any(IsWireGround));
        });
        var midpoint = board.Nets.Any(net =>
            !net.Any(IsWireSupply)
            && !net.Any(IsWireGround)
            && resistors.Count(resistor => net.Any(end => SketchBoard.Belongs(end, resistor))) >= 2);
        return spansRails && !midpoint
            ? new SketchRule("Divider", "Missing the bottom resistor.", "Fail")
            : null;
    }

    private static IReadOnlyList<SketchRule> ModuleRules(SketchBoard board)
    {
        var modules = board.Sketch.Parts.Where(SketchBoard.IsModule).ToList();
        var controllers = modules.Where(SketchBoard.IsController).ToList();
        var peripherals = modules.Where(part => !SketchBoard.IsController(part)).ToList();
        var rules = new List<SketchRule>
        {
            Shared(board, controllers, peripherals, Mark.ModulePower, "Power connected", "3V3 or VIN is shared."),
            Shared(board, controllers, peripherals, Mark.ModuleGround, "Ground connected", "GND is shared."),
            Voltage(board),
        };

        if (board.UsesI2C())
        {
            rules.Add(Shared(board, controllers, peripherals, Mark.Sda, "SDA present", "SDA joins the controller and the module."));
            rules.Add(Shared(board, controllers, peripherals, Mark.Scl, "SCL present", "SCL joins the controller and the module."));
            rules.Add(PullUp(board, Mark.Sda, "SDA pull-up"));
            rules.Add(PullUp(board, Mark.Scl, "SCL pull-up"));
        }

        return rules;
    }

    private static SketchRule Shared(
        SketchBoard board,
        IReadOnlyList<SketchPart> controllers,
        IReadOnlyList<SketchPart> peripherals,
        Mark pin,
        string name,
        string passDetail)
    {
        if (controllers.Count == 0 || peripherals.Count == 0)
        {
            return new SketchRule(name, "The sketch needs a controller and a module.", "Fail");
        }

        var missing = peripherals.Where(peripheral => !board.Nets.Any(net =>
            net.Any(end => controllers.Any(part => SketchBoard.Belongs(end, part)) && board.Marked(end, pin))
            && net.Any(end => SketchBoard.Belongs(end, peripheral) && board.Marked(end, pin)))).ToList();
        return missing.Count == 0
            ? new SketchRule(name, passDetail, "Pass")
            : new SketchRule(name, $"Missing on {string.Join(", ", missing.Select(part => part.Name))}.", "Fail");
    }

    private static SketchRule Voltage(SketchBoard board)
    {
        foreach (var net in board.Nets)
        {
            var low = net.Any(end => board.Marked(end, Mark.LowVoltage));
            var high = net.Any(end => board.Marked(end, Mark.HighVoltage));
            if (low && high)
            {
                return new SketchRule("Voltage compatible", "5 V and 3.3 V are tied together.", "Fail");
            }
        }

        return new SketchRule("Voltage compatible", "The shared supply pins use one voltage.", "Pass");
    }

    private static SketchRule PullUp(SketchBoard board, Mark bus, string name)
    {
        var busNet = board.Nets.FirstOrDefault(net => net.Any(end => board.Marked(end, bus)));
        var powerNet = board.Nets.FirstOrDefault(net => net.Any(end =>
            board.Marked(end, Mark.ModulePower) && board.Sketch.Parts.Any(part => SketchBoard.IsModule(part) && SketchBoard.Belongs(end, part))));
        if (busNet is null || powerNet is null)
        {
            return new SketchRule(name, "The bus pin is not connected.", "Fail");
        }

        var pulled = board.Sketch.Parts.Where(part => SketchPartKinds.Of(part) == SketchPartKind.Resistor).Any(resistor =>
            busNet.Any(end => SketchBoard.Belongs(end, resistor)) && powerNet.Any(end => SketchBoard.Belongs(end, resistor)));
        return pulled
            ? new SketchRule(name, "A resistor ties the bus pin to the supply.", "Pass")
            : new SketchRule(name, "No pull-up resistor ties this pin to the supply.", "Fail");
    }

    private static bool IsLedPin(string end, string partId, string name, string shortName)
    {
        if (!end.StartsWith(partId + ".", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var pin = end[(partId.Length + 1)..];
        return pin.Equals(shortName, StringComparison.OrdinalIgnoreCase)
            || pin.Contains(name, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWireSupply(string end) =>
        end.Equals("VCC", StringComparison.OrdinalIgnoreCase)
        || end.Equals("VDD", StringComparison.OrdinalIgnoreCase)
        || end.Equals("5V", StringComparison.OrdinalIgnoreCase)
        || end.Equals("+5V", StringComparison.OrdinalIgnoreCase);

    private static bool IsWireGround(string end) =>
        end.Equals("GND", StringComparison.OrdinalIgnoreCase)
        || end.Equals("0", StringComparison.OrdinalIgnoreCase)
        || end.Equals("VSS", StringComparison.OrdinalIgnoreCase);
}
