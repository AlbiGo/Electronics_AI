using static ElectronicsAI.Design.PatternChecks;

namespace ElectronicsAI.Design;

public static class Esp32SensorFamily
{
    private static readonly PartQuery NamedSensor = new(["sensor", "lm35", "tmp36", "max30102", "dht", "mpu6050"], PartText.TypeName);

    private static readonly PartQuery AnalogSensor = new(["temperature", "thermistor", "ntc", "lm35", "tmp36", "analog"], PartText.TypeName);

    private static readonly PartQuery Ecu = new(["ecu"], PartText.TypeName);

    private static readonly PartQuery Controller = new(["mcu", "esp32", "ecu", "arduino", "stm32", "rp2040"], PartText.TypeName);

    private static readonly End SensorPower = new(AnalogSensor, ["VCC", "VDD", "VIN", "Vs", "5V", "+5V"]);

    private static readonly End SensorGround = new(AnalogSensor, ["GND", "VSS", "AGND"]);

    private static readonly End SensorOut = new(AnalogSensor, ["OUT", "Output", "Vout", "AO", "A0", "SIG", "AN", "S", "DATA", "DAT"]);

    private static readonly End TempIn = new(Controller, ["TempIn", "TEMP_IN", "ECU_TEMP_IN", "adc1", "A0", "A1", "AN"], ["ADC", "AIN", "GPIO"]);

    private static readonly End EcuGround = new(Controller, ["GND", "VSS", "AGND"]);

    private static readonly End EcuPower = new(Ecu, ["VCC", "VDD", "VIN", "5V", "+5V"]);

    private static readonly End SupplyRail = new(Mark: Mark.WireSupply);

    private static readonly End SupplyPart = new(Mark: Mark.MotorFlybackSupply);

    public static CircuitPattern I2C { get; } = new()
    {
        Category = CircuitCategory.Esp32Sensor,
        Reason = "I2C sensor checked without ngspice.",
        When = static board => OpenSensor(board) && Link(board) == SensorInterface.I2C,
        Checks = [new BatchCheck(static (board, _) => ModuleRules(board, i2c: true, data: false))],
    };

    public static CircuitPattern Spi { get; } = new()
    {
        Category = CircuitCategory.Esp32Sensor,
        Reason = "SPI sensor checked without ngspice.",
        When = static board => OpenSensor(board) && Link(board) == SensorInterface.Spi,
        Checks = [new BatchCheck(static (board, _) => BusRules(board, ("MOSI connected", ["MOSI"]), ("MISO connected", ["MISO"]), ("SCK connected", ["SCK", "SCLK"]), ("CS connected", ["CS", "SS"])))],
    };

    public static CircuitPattern Uart { get; } = new()
    {
        Category = CircuitCategory.Esp32Sensor,
        Reason = "UART sensor checked without ngspice.",
        When = static board => OpenSensor(board) && Link(board) == SensorInterface.Uart,
        Checks = [new BatchCheck(static (board, _) => BusRules(board, ("RX connected", ["RX"]), ("TX connected", ["TX"])))],
    };

    public static CircuitPattern Data { get; } = new()
    {
        Category = CircuitCategory.Esp32Sensor,
        Reason = "Sensor checked without ngspice.",
        When = static board => OpenSensor(board) && Link(board) == SensorInterface.Data,
        Checks = [new BatchCheck(static (board, _) => ModuleRules(board, i2c: false, data: true))],
    };

    public static CircuitPattern Analog { get; } = new()
    {
        Category = CircuitCategory.Esp32Sensor,
        Reason = "Analog sensor checked without ngspice.",
        When = static board => OpenSensor(board) && Link(board) == SensorInterface.Analog,
        Assume = static (_, _) => ["The sensor output is an analog voltage."],
        Checks =
        [
            Check("Sensor powered", "The sensor is powered.", "The sensor has no supply.",
                new Some(new SameNet(SensorPower, SupplyRail), new SameNet(SensorPower, SupplyPart))),
            Check("ECU powered", "The ECU is powered.", "The ECU has no supply.",
                new Some(new SameNet(EcuPower, SupplyRail), new SameNet(EcuPower, SupplyPart)), only: Ecu),
            Check("Sensor grounded", "The sensor is grounded.", "The sensor is not grounded.",
                new SameNet(SensorGround, new End(Mark: Mark.Ground))),
            Check("Sensor output connected", "The sensor output is wired.", "The sensor output is not connected.",
                new HasNet(SensorOut)),
            Check("ECU input connected", "The sensor output reaches an analog input.", "The sensor output does not reach an analog input.",
                new SameNet(SensorOut, TempIn)),
            Check("Common ground present", "The controller and the sensor share a ground.", "The controller and the sensor do not share a ground.",
                new SameNet(EcuGround, SensorGround)),
        ],
    };

    public static CircuitPattern Sensor { get; } = new()
    {
        Category = CircuitCategory.Esp32Sensor,
        Reason = "Sensor checked without ngspice.",
        When = static board => OpenSensor(board) && Link(board) == SensorInterface.Unknown && board.Has(NamedSensor),
        Checks = [new BatchCheck(static (board, _) => ModuleRules(board, i2c: false, data: false))],
    };

    public static IReadOnlyList<CircuitPattern> Patterns { get; } = [I2C, Spi, Uart, Data, Analog, Sensor];

    public static bool Matches(SchematicSketch sketch) =>
        sketch.Parts.Any(SketchBoard.IsController)
        && sketch.Parts.Any(part => !SketchBoard.IsController(part) && (NamedSensor.Matches(part) || AnalogSensor.Matches(part) || SensorLibrary.Find(part) is not null || Is(part, "oled")));

    private static bool OpenSensor(SketchBoard board) =>
        !RelayFamily.Pattern.When(board) && !MotorFamily.Bridge.When(board) && !MotorFamily.Discrete.When(board) && !TransistorLedFamily.Pattern.When(board) && !SolenoidFamily.Pattern.When(board)
        && board.Sketch.Parts.Any(SketchBoard.IsController);

    private static SensorInterface Link(SketchBoard board)
    {
        if (Mentions(board, "MOSI", "MISO", "SCLK") || Library(board, SensorInterface.Spi))
        {
            return SensorInterface.Spi;
        }

        if (Mentions(board, "SDA", "SCL") || board.UsesI2C() || Library(board, SensorInterface.I2C))
        {
            return SensorInterface.I2C;
        }

        if (Mentions(board, "RX", "TX") || Library(board, SensorInterface.Uart))
        {
            return SensorInterface.Uart;
        }

        if (board.Has(AnalogSensor) || Library(board, SensorInterface.Analog))
        {
            return SensorInterface.Analog;
        }

        if (Mentions(board, "DATA", "DAT") || Library(board, SensorInterface.Data))
        {
            return SensorInterface.Data;
        }

        return SensorInterface.Unknown;
    }

    private static bool Library(SketchBoard board, SensorInterface link) =>
        board.Sketch.Parts.Any(part => SensorLibrary.Find(part)?.Interface == link);

    private static bool Mentions(SketchBoard board, params string[] pins)
    {
        var text = string.Join(" ", board.Sketch.Parts.SelectMany(part => part.Pins))
            + " " + string.Join(" ", board.Sketch.Wires.Select(wire => $"{wire.From} {wire.To}"));
        return pins.Any(pin => text.Contains(pin, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<SketchRule> BusRules(SketchBoard board, params (string Name, string[] Pins)[] lines)
    {
        var rules = ModuleRules(board, i2c: false, data: false).ToList();
        rules.AddRange(lines.Select(line => Linked(board, line.Name, $"{line.Name.Split(' ')[0]} joins the controller and the sensor.", line.Pins)));
        return rules;
    }

    private static SketchRule Linked(SketchBoard board, string name, string passDetail, string[] pins)
    {
        var modules = board.Sketch.Parts.Where(SketchBoard.IsModule).ToList();
        var controllers = modules.Where(SketchBoard.IsController).ToList();
        var peripherals = modules.Where(part => !SketchBoard.IsController(part)).ToList();
        if (controllers.Count == 0 || peripherals.Count == 0)
        {
            return new SketchRule(name, "The sketch needs a controller and a sensor.", "Fail");
        }

        var missing = peripherals.Where(peripheral => !board.Nets.Any(net =>
            net.Any(end => controllers.Any(part => SketchBoard.Belongs(end, part)))
            && net.Any(end => SketchBoard.Belongs(end, peripheral) && pins.Any(pin => SketchBoard.PinOf(end).Equals(pin, StringComparison.OrdinalIgnoreCase))))).ToList();
        return missing.Count == 0
            ? new SketchRule(name, passDetail, "Pass")
            : new SketchRule(name, $"Missing on {string.Join(", ", missing.Select(part => part.Name))}.", "Fail");
    }

    private static IReadOnlyList<SketchRule> ModuleRules(SketchBoard board, bool i2c, bool data)
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

        if (i2c)
        {
            rules.Add(Shared(board, controllers, peripherals, Mark.Sda, "SDA present", "SDA joins the controller and the module."));
            rules.Add(Shared(board, controllers, peripherals, Mark.Scl, "SCL present", "SCL joins the controller and the module."));
            rules.Add(PullUp(board, Mark.Sda, "SDA pull-up"));
            rules.Add(PullUp(board, Mark.Scl, "SCL pull-up"));
        }

        if (data)
        {
            rules.Add(Shared(board, controllers, peripherals, Mark.Data, "Data connected", "The data pin joins the controller and the sensor."));
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

    private static bool Is(SketchPart part, params string[] words)
    {
        var text = $"{part.Type} {part.Name}";
        return words.Any(word => text.Contains(word, StringComparison.OrdinalIgnoreCase));
    }
}

public static class ModuleSketchRules
{
    public static bool Applies(SchematicSketch sketch)
    {
        var board = SketchBoard.Of(sketch);
        return !MotorDriverRules.Applies(sketch)
            && !RelayDriverRules.Applies(sketch)
            && board.Sketch.Parts.Any(SketchBoard.IsController)
            && board.Sketch.Parts.Any(part => SketchBoard.IsModule(part) && !SketchBoard.IsController(part));
    }

    public static IReadOnlyList<SketchRule> Evaluate(SchematicSketch sketch)
    {
        var match = Esp32SensorFamily.Patterns.FirstOrDefault(pattern => pattern.Applies(sketch, null));
        return match is null ? [] : match.Evaluate(sketch, null).Rules;
    }
}
