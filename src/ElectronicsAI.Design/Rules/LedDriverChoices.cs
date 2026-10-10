namespace ElectronicsAI.Design;

public sealed record LedDriverChoice(string Id, string Label, string Detail, string Prompt);

public static class LedDriverChoices
{
    private const string Chosen = "Chosen LED driver:";

    public static IReadOnlyList<LedDriverChoice>? Find(SchematicSketch sketch, string? request)
    {
        if (request?.Contains(Chosen, StringComparison.OrdinalIgnoreCase) == true)
        {
            return null;
        }

        var led = sketch.Parts.Any(part => SketchPartKinds.Of(part) == SketchPartKind.Led);
        var transistor = sketch.Parts.Any(IsTransistor);
        var asked = Asks(request) || Asks(sketch.Title) || Asks(sketch.Summary);
        if (!asked && !(led && transistor))
        {
            return null;
        }

        return
        [
            new(
                "series-switch",
                "Push-button in series",
                "Battery, resistor, switch, and LED in one vertical chain. The closed switch is used for the current.",
                $"{Chosen} series-switch. Draw a battery, a 330 ohm resistor, a push-button switch, and an LED in series. Wire the battery positive through the resistor and the switch to the LED anode, and the LED cathode to GND, with the battery negative on GND. Do not use a transistor or a controller."),
            new(
                "gpio-transistor",
                "ESP32 low-side transistor",
                "A GPIO pin drives the transistor gate. The LED current goes through the drain to ground.",
                $"{Chosen} gpio-transistor. Draw an ESP32, a 330 ohm resistor, an LED, an N-MOSFET, and a gate resistor. Name the ESP32 pins GPIO, 3V3, and GND, and the MOSFET pins Gate, Drain, and Source. Wire 3V3 through the 330 ohm resistor to the LED anode, the LED cathode to Drain, and Source to GND. Wire GPIO through the gate resistor to Gate, and wire ESP32 GND to GND. Do not use a push button."),
            new(
                "transistor-only",
                "Transistor, no controller",
                "The transistor switches the LED. There is no microcontroller.",
                $"{Chosen} transistor-only. Draw a 9V battery, a 330 ohm resistor, an LED, and an N-MOSFET. Wire the battery positive through the resistor to the LED anode, the LED cathode to the drain, and the source to GND. Leave the gate as its own lead. Do not add a controller or a push button."),
        ];
    }

    private static bool Asks(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var led = text.Contains("led", StringComparison.OrdinalIgnoreCase);
        var lowSide = text.Contains("low-side", StringComparison.OrdinalIgnoreCase)
            || text.Contains("low side", StringComparison.OrdinalIgnoreCase)
            || text.Contains("lowside", StringComparison.OrdinalIgnoreCase);
        var driver = text.Contains("driver", StringComparison.OrdinalIgnoreCase);
        return led && (lowSide || driver);
    }

    private static bool IsTransistor(SketchPart part)
    {
        var text = $"{part.Type} {part.Name} {part.Note}";
        return text.Contains("mosfet", StringComparison.OrdinalIgnoreCase)
            || text.Contains("nmos", StringComparison.OrdinalIgnoreCase)
            || text.Contains("transistor", StringComparison.OrdinalIgnoreCase)
            || text.Contains("npn", StringComparison.OrdinalIgnoreCase)
            || text.Contains("pnp", StringComparison.OrdinalIgnoreCase)
            || text.Contains("bjt", StringComparison.OrdinalIgnoreCase);
    }
}
