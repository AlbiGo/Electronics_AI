using static ElectronicsAI.Design.PatternChecks;

namespace ElectronicsAI.Design;

public static class SensorWiring
{
    public static readonly PartQuery NamedSensor = new(["sensor", "lm35", "tmp36", "max30102", "dht", "mpu6050"], PartText.TypeName);

    public static readonly PartQuery AnalogSensor = new(["temperature", "thermistor", "ntc", "lm35", "tmp36", "analog"], PartText.TypeName);

    public static readonly PartQuery Ecu = new(["ecu"], PartText.TypeName);

    public static readonly PartQuery Controller = new(["mcu", "esp32", "ecu", "arduino", "stm32", "rp2040"], PartText.TypeName);

    public static readonly End SensorPower = new(AnalogSensor, ["VCC", "VDD", "VIN", "Vs", "5V", "+5V"]);

    public static readonly End SensorGround = new(AnalogSensor, ["GND", "VSS", "AGND"]);

    public static readonly End SensorOut = new(AnalogSensor, ["OUT", "Output", "Vout", "AO", "A0", "SIG", "AN", "S", "DATA", "DAT"]);

    public static readonly End TempIn = new(Controller, ["TempIn", "TEMP_IN", "ECU_TEMP_IN", "adc1", "A0", "A1", "AN"], ["ADC", "AIN", "GPIO"]);

    public static readonly End ControllerGround = new(Controller, ["GND", "VSS", "AGND"]);

    public static readonly End EcuPower = new(Ecu, ["VCC", "VDD", "VIN", "5V", "+5V"]);

    public static readonly End SupplyRail = new(Mark: Mark.WireSupply);

    public static readonly End SupplyPart = new(Mark: Mark.MotorFlybackSupply);
}

public static class SensorMustBePoweredRule
{
    public static IEnumerable<SketchRule> Run(SketchBoard board) =>
        Check("Sensor powered", "The sensor is powered.", "The sensor has no supply.",
            new Some(new SameNet(SensorWiring.SensorPower, SensorWiring.SupplyRail), new SameNet(SensorWiring.SensorPower, SensorWiring.SupplyPart)))
            .Run(board, null);
}

public static class SensorGroundConnectedRule
{
    public static IEnumerable<SketchRule> Run(SketchBoard board) =>
        Check("Sensor grounded", "The sensor is grounded.", "The sensor is not grounded.",
            new SameNet(SensorWiring.SensorGround, new End(Mark: Mark.Ground)))
            .Run(board, null);
}

public static class SensorOutputConnectedRule
{
    public static IEnumerable<SketchRule> Run(SketchBoard board) =>
        Check("Sensor output connected", "The sensor output is wired.", "The sensor output is not connected.",
            new HasNet(SensorWiring.SensorOut))
            .Run(board, null);
}

public static class AnalogSensorRule
{
    public static IEnumerable<SketchRule> Run(SketchBoard board) =>
        SensorMustBePoweredRule.Run(board)
            .Concat(EcuPowered(board))
            .Concat(SensorGroundConnectedRule.Run(board))
            .Concat(SensorOutputConnectedRule.Run(board))
            .Concat(Check("ECU input connected", "The sensor output reaches an analog input.", "The sensor output does not reach an analog input.",
                new SameNet(SensorWiring.SensorOut, SensorWiring.TempIn)).Run(board, null))
            .Concat(CommonGroundRule.Analog(board));

    private static IEnumerable<SketchRule> EcuPowered(SketchBoard board) =>
        Check("ECU powered", "The ECU is powered.", "The ECU has no supply.",
            new Some(new SameNet(SensorWiring.EcuPower, SensorWiring.SupplyRail), new SameNet(SensorWiring.EcuPower, SensorWiring.SupplyPart)),
            only: SensorWiring.Ecu).Run(board, null);
}

public static class I2CSensorRule
{
    public static IReadOnlyList<SketchRule> Evaluate(SketchBoard board)
    {
        var rules = new List<SketchRule>
        {
            PowerSourceRule.Module(board),
            CommonGroundRule.Module(board),
            VoltageCompatibilityRule.Evaluate(board),
            I2CSdaConnectedRule.Evaluate(board),
            I2CSclConnectedRule.Evaluate(board),
        };
        rules.AddRange(I2CPullupRule.Evaluate(board));
        return rules;
    }
}

public static class I2CSdaConnectedRule
{
    public static SketchRule Evaluate(SketchBoard board) =>
        ModuleNet.Shared(board, Mark.Sda, "SDA present", "SDA joins the controller and the module.");
}

public static class I2CSclConnectedRule
{
    public static SketchRule Evaluate(SketchBoard board) =>
        ModuleNet.Shared(board, Mark.Scl, "SCL present", "SCL joins the controller and the module.");
}

public static class I2CPullupRule
{
    public static IReadOnlyList<SketchRule> Evaluate(SketchBoard board) =>
    [
        ModuleNet.PullUp(board, Mark.Sda, "SDA pull-up"),
        ModuleNet.PullUp(board, Mark.Scl, "SCL pull-up"),
    ];
}

public static class SpiMosiConnectedRule
{
    public static SketchRule Evaluate(SketchBoard board) =>
        ModuleNet.Linked(board, "MOSI connected", "MOSI joins the controller and the sensor.", ["MOSI"]);
}

public static class SpiMisoConnectedRule
{
    public static SketchRule Evaluate(SketchBoard board) =>
        ModuleNet.Linked(board, "MISO connected", "MISO joins the controller and the sensor.", ["MISO"]);
}

public static class SpiClockConnectedRule
{
    public static SketchRule Evaluate(SketchBoard board) =>
        ModuleNet.Linked(board, "SCK connected", "SCK joins the controller and the sensor.", ["SCK", "SCLK"]);
}

public static class SpiChipSelectRule
{
    public static SketchRule Evaluate(SketchBoard board) =>
        ModuleNet.Linked(board, "CS connected", "CS joins the controller and the sensor.", ["CS", "SS"]);
}

public static class SensorDataRule
{
    public static SketchRule Evaluate(SketchBoard board) =>
        ModuleNet.Shared(board, Mark.Data, "Data connected", "The data pin joins the controller and the sensor.");
}
