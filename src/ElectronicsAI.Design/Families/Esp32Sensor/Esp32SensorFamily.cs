namespace ElectronicsAI.Design;

public static class Esp32SensorFamily
{
    public static CircuitPattern I2C { get; } = new()
    {
        Category = CircuitCategory.Esp32Sensor,
        Reason = "I2C sensor checked without ngspice.",
        When = static board => OpenSensor(board) && Link(board) == SensorInterface.I2C,
        Checks = [new BatchCheck(static (board, _) => I2CRules(board))],
    };

    public static CircuitPattern Spi { get; } = new()
    {
        Category = CircuitCategory.Esp32Sensor,
        Reason = "SPI sensor checked without ngspice.",
        When = static board => OpenSensor(board) && Link(board) == SensorInterface.Spi,
        Checks = [new BatchCheck(static (board, _) => SpiRules(board))],
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
        Checks = [new BatchCheck(static (board, _) => DataRules(board))],
    };

    public static CircuitPattern Analog { get; } = new()
    {
        Category = CircuitCategory.Esp32Sensor,
        Reason = "Analog sensor checked without ngspice.",
        When = static board => OpenSensor(board) && Link(board) == SensorInterface.Analog,
        Assume = static (_, _) => ["The sensor output is an analog voltage."],
        Checks = [new BatchCheck(static (board, _) => AnalogSensorRule.Run(board).ToArray())],
    };

    public static CircuitPattern Sensor { get; } = new()
    {
        Category = CircuitCategory.Esp32Sensor,
        Reason = "Sensor checked without ngspice.",
        When = static board => OpenSensor(board) && Link(board) == SensorInterface.Unknown && board.Has(SensorWiring.NamedSensor),
        Checks = [new BatchCheck(static (board, _) => SharedRails(board))],
    };

    public static IReadOnlyList<CircuitPattern> Patterns { get; } = [I2C, Spi, Uart, Data, Analog, Sensor];

    public static bool Matches(SchematicSketch sketch) =>
        sketch.Parts.Any(SketchBoard.IsController)
        && sketch.Parts.Any(part => !SketchBoard.IsController(part) && (SensorWiring.NamedSensor.Matches(part) || SensorWiring.AnalogSensor.Matches(part) || SensorLibrary.Find(part) is not null || Is(part, "oled")));

    private static bool OpenSensor(SketchBoard board) =>
        !RelayFamily.Pattern.When(board) && !MotorFamily.Bridge.When(board) && !MotorFamily.Discrete.When(board) && !SolenoidFamily.Pattern.When(board)
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

        if (board.Has(SensorWiring.AnalogSensor) || Library(board, SensorInterface.Analog))
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

    private static IReadOnlyList<SketchRule> I2CRules(SketchBoard board) =>
        I2CSensorRule.Evaluate(board);

    private static IReadOnlyList<SketchRule> SpiRules(SketchBoard board)
    {
        var rules = SharedRails(board).ToList();
        rules.Add(SpiMosiConnectedRule.Evaluate(board));
        rules.Add(SpiMisoConnectedRule.Evaluate(board));
        rules.Add(SpiClockConnectedRule.Evaluate(board));
        rules.Add(SpiChipSelectRule.Evaluate(board));
        return rules;
    }

    private static IReadOnlyList<SketchRule> DataRules(SketchBoard board)
    {
        var rules = SharedRails(board).ToList();
        rules.Add(SensorDataRule.Evaluate(board));
        return rules;
    }

    private static IReadOnlyList<SketchRule> BusRules(SketchBoard board, params (string Name, string[] Pins)[] lines)
    {
        var rules = SharedRails(board).ToList();
        rules.AddRange(lines.Select(line => ModuleNet.Linked(board, line.Name, $"{line.Name.Split(' ')[0]} joins the controller and the sensor.", line.Pins)));
        return rules;
    }

    private static IReadOnlyList<SketchRule> SharedRails(SketchBoard board) =>
    [
        PowerSourceRule.Module(board),
        CommonGroundRule.Module(board),
        VoltageCompatibilityRule.Evaluate(board),
    ];

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
