namespace ElectronicsAI.Design;

public static class Esp32SensorFamily
{
    private static readonly PartQuery HeartSensor = new(["max30102"], PartText.TypeName);

    private static readonly PartQuery DisplaySensor = new(["oled"], PartText.TypeName);

    private static readonly PartQuery ClimateSensor = new(["dht"], PartText.TypeName);

    private static readonly PartQuery NamedSensor = new(["sensor"], PartText.TypeName);

    public static SketchTemplate HeartBoard { get; } = new()
    {
        RequestGroups = [["esp32"], ["max30102"]],
        Kinds = ["heart-rate", "heartrate", "heart-rate-monitor"],
        ReplaceRequest = true,
        FillWhenUnwired = true,
        Title = "Heart rate monitor",
        Summary = "The ESP32 reads the MAX30102 over I2C at 3.3 V. SDA is GPIO21 and SCL is GPIO22, each with a 4.7k pull-up.",
        Note = "Building an ESP32 and MAX30102 heart rate monitor",
        Parts =
        [
            new("esp32", "ESP32", "mcu", null, ["3V3", "GND", "GPIO21", "GPIO22"]),
            new("max30102", "MAX30102", "sensor", null, ["VIN", "GND", "SDA", "SCL"]),
            new("rsda", "R4.7k", "resistor", "4.7k", ["1", "2"]),
            new("rscl", "R4.7k", "resistor", "4.7k", ["1", "2"]),
        ],
        Wires =
        [
            new("esp32.3V3", "max30102.VIN"),
            new("esp32.GND", "max30102.GND"),
            new("esp32.GPIO21", "max30102.SDA"),
            new("esp32.GPIO22", "max30102.SCL"),
            new("esp32.3V3", "rsda.1"),
            new("rsda.2", "max30102.SDA"),
            new("esp32.3V3", "rscl.1"),
            new("rscl.2", "max30102.SCL"),
        ],
    };

    public static CircuitPattern Heart { get; } = new()
    {
        Category = CircuitCategory.Esp32Sensor,
        Reason = "ESP32 heart-rate sensor checked without ngspice.",
        When = static board => OpenSensor(board) && board.Has(HeartSensor),
        Checks = [new BatchCheck(static (board, _) => ModuleRules(board, i2c: true, data: false))],
    };

    public static CircuitPattern Display { get; } = new()
    {
        Category = CircuitCategory.Esp32Sensor,
        Reason = "ESP32 display checked without ngspice.",
        When = static board => OpenSensor(board) && !board.Has(HeartSensor) && board.Has(DisplaySensor),
        Checks = [new BatchCheck(static (board, _) => ModuleRules(board, i2c: true, data: false))],
    };

    public static CircuitPattern Climate { get; } = new()
    {
        Category = CircuitCategory.Esp32Sensor,
        Reason = "ESP32 climate sensor checked without ngspice.",
        When = static board => OpenSensor(board) && !board.Has(HeartSensor) && !board.Has(DisplaySensor) && board.Has(ClimateSensor),
        Checks = [new BatchCheck(static (board, _) => ModuleRules(board, i2c: false, data: true))],
    };

    public static CircuitPattern Sensor { get; } = new()
    {
        Category = CircuitCategory.Esp32Sensor,
        Reason = "ESP32 sensor checked without ngspice.",
        When = static board => OpenSensor(board) && !board.Has(HeartSensor) && !board.Has(DisplaySensor) && !board.Has(ClimateSensor) && board.Has(NamedSensor),
        Checks = [new BatchCheck(static (board, _) => ModuleRules(board, board.UsesI2C(), data: false))],
    };

    public static IReadOnlyList<CircuitPattern> Patterns { get; } = [Heart, Display, Climate, Sensor];

    public static bool Matches(SchematicSketch sketch) =>
        sketch.Parts.Any(SketchBoard.IsController)
        && sketch.Parts.Any(part => !SketchBoard.IsController(part) && Is(part, "max30102", "oled", "dht", "sensor"));

    private static bool OpenSensor(SketchBoard board) =>
        !RelayFamily.Pattern.When(board) && !MotorFamily.Bridge.When(board) && !MotorFamily.Discrete.When(board) && !TransistorLedFamily.Pattern.When(board)
        && board.Sketch.Parts.Any(SketchBoard.IsController);

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
