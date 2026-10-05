namespace ElectronicsAI.Design;

public static class PartCategories
{
    public static PartCategory? Read(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return Enum.TryParse<PartCategory>(text.Trim(), ignoreCase: true, out var category) && category != PartCategory.Unknown
            ? category
            : null;
    }

    public static PartCategory Infer(string? type, string? name, string? note)
    {
        var text = $"{type} {name} {note}";
        if (Has(text, "inductor", "coil", "capacitor", "resistor", "ohm", "µh", "μh"))
        {
            return PartCategory.Passive;
        }

        if (Has(text, "schottky", "diode", "tvs", "fuse"))
        {
            return PartCategory.Protection;
        }

        if (Has(text, "sensor", "max30102", "dht", "oled"))
        {
            return PartCategory.Sensor;
        }

        if (Has(text, "motor", "relay", "solenoid", "led"))
        {
            return PartCategory.Actuator;
        }

        if (Has(text, "mcu", "esp32", "ecu", "gate", "flip", "adder"))
        {
            return PartCategory.Logic;
        }

        if (Has(text, "lm2596", "lm2576", "regulator", "buck", "supply", "vsource", "battery"))
        {
            return PartCategory.Power;
        }

        return PartCategory.Unknown;
    }

    private static bool Has(string text, params string[] words) =>
        words.Any(word => text.Contains(word, StringComparison.OrdinalIgnoreCase));
}
