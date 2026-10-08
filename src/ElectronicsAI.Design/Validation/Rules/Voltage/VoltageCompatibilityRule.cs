using System.Globalization;

namespace ElectronicsAI.Design;

public static class VoltageCompatibilityRule
{
    public static SketchRule Evaluate(SketchBoard board)
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

        foreach (var part in board.Sketch.Parts)
        {
            var record = ComponentLibrary.Find(part);
            if (record?.MaxVoltage is not { } max || !record.Category.Equals("sensor", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var net in board.Nets)
            {
                var powered = net.Any(end =>
                    SketchBoard.Belongs(end, part)
                    && record.Pins.Any(pin => SketchBoard.PinOf(end).Equals(pin, StringComparison.OrdinalIgnoreCase))
                    && IsSupply(SketchBoard.PinOf(end)));
                if (!powered)
                {
                    continue;
                }

                var supply = net
                    .Where(end => !SketchBoard.Belongs(end, part))
                    .Select(Volts)
                    .Where(value => value is not null)
                    .Select(value => value!.Value)
                    .DefaultIfEmpty(0)
                    .Max();
                if (supply > max + 0.05)
                {
                    var shown = max.ToString("0.#", CultureInfo.InvariantCulture);
                    return new SketchRule("Voltage compatible", $"{record.Name} allows {shown} V.", "Fail");
                }
            }
        }

        return new SketchRule("Voltage compatible", "The shared supply pins use one voltage.", "Pass");
    }

    private static bool IsSupply(string pin) =>
        pin.Equals("VIN", StringComparison.OrdinalIgnoreCase)
        || pin.Equals("VCC", StringComparison.OrdinalIgnoreCase)
        || pin.Equals("VDD", StringComparison.OrdinalIgnoreCase)
        || pin.Equals("3V3", StringComparison.OrdinalIgnoreCase)
        || pin.Equals("3.3V", StringComparison.OrdinalIgnoreCase)
        || pin.Equals("5V", StringComparison.OrdinalIgnoreCase)
        || pin.Equals("+5V", StringComparison.OrdinalIgnoreCase);

    private static double? Volts(string end)
    {
        var pin = SketchBoard.PinOf(end);
        if (pin.Equals("12V", StringComparison.OrdinalIgnoreCase) || pin.Equals("+12V", StringComparison.OrdinalIgnoreCase))
        {
            return 12;
        }

        if (pin.Equals("5V", StringComparison.OrdinalIgnoreCase)
            || pin.Equals("+5V", StringComparison.OrdinalIgnoreCase)
            || pin.Equals("VBUS", StringComparison.OrdinalIgnoreCase))
        {
            return 5;
        }

        if (pin.Equals("3V3", StringComparison.OrdinalIgnoreCase) || pin.Equals("3.3V", StringComparison.OrdinalIgnoreCase))
        {
            return 3.3;
        }

        return null;
    }
}
