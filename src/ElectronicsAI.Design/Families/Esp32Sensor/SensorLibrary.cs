namespace ElectronicsAI.Design;

public enum SensorInterface
{
    Unknown,
    Analog,
    I2C,
    Spi,
    Uart,
    Data,
}

public sealed record SensorPart(string Name, SensorInterface Interface, string Voltage, string[] Pins);

public static class SensorLibrary
{
    public static IReadOnlyList<SensorPart> All { get; } = ComponentLibrary.In("sensor").Select(ToSensor).ToList();

    public static SensorPart? Find(SketchPart part)
    {
        var record = ComponentLibrary.Find(part);
        return record is not null && record.Category.Equals("sensor", StringComparison.OrdinalIgnoreCase)
            ? ToSensor(record)
            : null;
    }

    private static SensorPart ToSensor(ComponentRecord record) =>
        new(record.Name, Parse(record.Interface), record.Voltage, record.Pins);

    private static SensorInterface Parse(string value) => value.ToUpperInvariant() switch
    {
        "ANALOG" => SensorInterface.Analog,
        "I2C" => SensorInterface.I2C,
        "SPI" => SensorInterface.Spi,
        "UART" => SensorInterface.Uart,
        "DATA" => SensorInterface.Data,
        _ => SensorInterface.Unknown,
    };
}
