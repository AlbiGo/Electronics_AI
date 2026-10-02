namespace ElectronicsAI.Design;

public static class HeartRateMonitorSketch
{
    public static SchematicSketch? TryCreate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var value = text.ToLowerInvariant();
        if (!value.Contains("esp32") || !value.Contains("max30102"))
        {
            return null;
        }

        return Create();
    }

    public static SchematicSketch Create(string? title = null, string? summary = null)
    {
        var parts = new List<SketchPart>
        {
            new("esp32", "ESP32", "mcu", null, ["3V3", "GND", "GPIO21", "GPIO22"]),
            new("max30102", "MAX30102", "sensor", null, ["VIN", "GND", "SDA", "SCL"]),
            new("rsda", "R4.7k", "resistor", "4.7k", ["1", "2"]),
            new("rscl", "R4.7k", "resistor", "4.7k", ["1", "2"]),
        };
        var wires = new List<SketchWire>
        {
            new("esp32.3V3", "max30102.VIN"),
            new("esp32.GND", "max30102.GND"),
            new("esp32.GPIO21", "max30102.SDA"),
            new("esp32.GPIO22", "max30102.SCL"),
            new("esp32.3V3", "rsda.1"),
            new("rsda.2", "max30102.SDA"),
            new("esp32.3V3", "rscl.1"),
            new("rscl.2", "max30102.SCL"),
        };

        return new SchematicSketch(
            string.IsNullOrWhiteSpace(title) ? "Heart rate monitor" : title,
            string.IsNullOrWhiteSpace(summary)
                ? "The ESP32 reads the MAX30102 over I2C at 3.3 V. SDA is GPIO21 and SCL is GPIO22, each with a 4.7k pull-up."
                : summary,
            parts,
            wires,
            null);
    }
}
