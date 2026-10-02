using System.Text.RegularExpressions;

namespace ElectronicsAI.Design;

public static partial class RippleCounterSketch
{
    public static SchematicSketch? TryCreate(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return null;
        }

        var text = description.ToLowerInvariant();
        if (!text.Contains("counter") && !text.Contains("ripple") && !text.Contains("flip-flop") && !text.Contains("flipflop"))
        {
            return null;
        }

        var match = BitPattern().Match(description);
        if (!match.Success)
        {
            return null;
        }

        var bits = int.Parse(match.Groups[1].Value);
        if (bits is < 1 or > 16)
        {
            return null;
        }

        return Create(bits);
    }

    public static SchematicSketch Create(int bits, string? title = null, string? summary = null)
    {
        var parts = new List<SketchPart> { new("clk", "CLK", "input", null) };
        var wires = new List<SketchWire>();
        for (var bit = 0; bit < bits; bit++)
        {
            parts.Add(new($"tff{bit}", $"TFF{bit}", "flipflop", "T=1"));
            parts.Add(new($"q{bit}", $"Q{bit}", "output", null));
            wires.Add(bit == 0
                ? new SketchWire("clk", $"tff{bit}")
                : new SketchWire($"tff{bit - 1}", $"tff{bit}"));
            wires.Add(new($"tff{bit}", $"q{bit}"));
        }

        return new SchematicSketch(
            title ?? $"{bits}-bit ripple counter",
            summary ?? $"A ripple counter of {bits} T flip-flops. Each T input is tied high, so the stage toggles on every clock edge.",
            parts,
            wires,
            null);
    }

    [GeneratedRegex(@"(\d+)\s*-?\s*bit", RegexOptions.IgnoreCase)]
    private static partial Regex BitPattern();
}
