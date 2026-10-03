using System.Globalization;
using System.Text.RegularExpressions;

namespace ElectronicsAI.Design;

public static partial class SketchVoltageNote
{
    public static SketchRule? Misstated(SchematicSketch sketch, double? actual)
    {
        if (actual is not double volts)
        {
            return null;
        }

        foreach (var part in sketch.Parts)
        {
            var kind = $"{part.Type} {part.Name}";
            if (kind.Contains("gnd", StringComparison.OrdinalIgnoreCase)
                || kind.Contains("ground", StringComparison.OrdinalIgnoreCase)
                || kind.Contains("supply", StringComparison.OrdinalIgnoreCase)
                || kind.Contains("source", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!kind.Contains("vout", StringComparison.OrdinalIgnoreCase)
                && !kind.Contains("node", StringComparison.OrdinalIgnoreCase)
                && !kind.Contains("junction", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var match = VoltsPattern.Match(part.Note ?? "");
            if (!match.Success
                || !double.TryParse(match.Groups["n"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var stated))
            {
                continue;
            }

            var limit = Math.Max(0.05, Math.Abs(volts) * 0.05);
            if (Math.Abs(stated - volts) <= limit)
            {
                return null;
            }

            return new SketchRule(
                "Stated voltage",
                Invariant($"{part.Name} says {stated:0.00} V. The output is {volts:0.00} V."),
                "Fail");
        }

        return null;
    }

    private static string Invariant(FormattableString message) => message.ToString(CultureInfo.InvariantCulture);

    [GeneratedRegex(@"(?<n>\d+(?:\.\d+)?)\s*v", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VoltsPattern { get; }
}
