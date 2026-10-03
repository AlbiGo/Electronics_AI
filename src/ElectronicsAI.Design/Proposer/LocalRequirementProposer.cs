using System.Globalization;
using System.Text.RegularExpressions;
using ElectronicsAI.Domain;

namespace ElectronicsAI.Design;

public sealed partial class LocalRequirementProposer : IRequirementProposer
{
    private static readonly string[] OtherCircuits =
    [
        "buck", "boost", "flyback", "sepic", "switching", "mosfet", "op-amp", "opamp", "amplifier",
    ];

    public Task<RequirementProposal> ProposeAsync(string description, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return Task.FromResult<RequirementProposal>(new RefusedRequirement(
                "Describe the input voltage, the output voltage, and the load."));
        }

        var text = description.ToLowerInvariant();
        if (OtherCircuits.Any(text.Contains))
        {
            return Task.FromResult<RequirementProposal>(new RefusedRequirement(
                "This version does not build that circuit. It can build a 12 V to 5 V linear supply at about 200 mA, a 12 V to 3.3 V divider with negligible load, or a counter from 1 to 8."));
        }

        if (text.Contains("counter"))
        {
            return Task.FromResult(ProposeCounter(description));
        }

        var volts = VoltPattern().Matches(description)
            .Select(match => double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture))
            .ToArray();
        if (volts.Length < 2)
        {
            return Task.FromResult<RequirementProposal>(Refuse());
        }

        var input = volts.Max();
        var output = volts.Where(value => value < input - 0.2).DefaultIfEmpty(volts.Min()).Min();
        var load = ReadLoad(description);

        if (Near(input, 12, 0.5) && Near(output, 5, 0.2))
        {
            var current = load ?? 0.2;
            if (current is < 0.15 or > 0.25)
            {
                return Task.FromResult<RequirementProposal>(new RefusedRequirement(
                    "The 5 V supply in this version is about 200 mA."));
            }

            return Task.FromResult<RequirementProposal>(new SupportedRequirement(
                new(input, output, current, description.Trim())));
        }

        if (Near(input, 12, 0.5) && Near(output, 3.3, 0.15))
        {
            if (load is > 0.001)
            {
                return Task.FromResult<RequirementProposal>(new RefusedRequirement(
                    "The 3.3 V circuit in this version is a divider for a negligible load."));
            }

            return Task.FromResult<RequirementProposal>(new SupportedRequirement(
                new(input, output, 0, description.Trim())));
        }

        return Task.FromResult<RequirementProposal>(Refuse());
    }

    private static bool Near(double value, double target, double tolerance) =>
        Math.Abs(value - target) <= tolerance;

    private static double? ReadLoad(string description)
    {
        var milli = MilliampPattern().Match(description);
        if (milli.Success)
        {
            return double.Parse(milli.Groups[1].Value, CultureInfo.InvariantCulture) / 1000;
        }

        var amps = AmpPattern().Match(description);
        if (amps.Success)
        {
            return double.Parse(amps.Groups[1].Value, CultureInfo.InvariantCulture);
        }

        return null;
    }

    private static RequirementProposal ProposeCounter(string description)
    {
        var range = RangePattern().Match(description);
        if (range.Success)
        {
            var start = int.Parse(range.Groups[1].Value, CultureInfo.InvariantCulture);
            var end = int.Parse(range.Groups[2].Value, CultureInfo.InvariantCulture);
            if (start != 1 || end != 8)
            {
                return new RefusedRequirement("This version builds a counter from 1 to 8.");
            }
        }

        var volts = VoltPattern().Matches(description)
            .Select(match => double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture))
            .ToArray();
        var supply = volts.Length == 0 ? 5 : volts.Max();
        var forward = volts.Length >= 2 ? volts.Min() : 2;
        if (forward >= supply)
        {
            forward = 2;
        }

        var load = ReadLoad(description) ?? 0.01;
        var clockMatch = HertzPattern().Match(description);
        var clock = clockMatch.Success
            ? double.Parse(clockMatch.Groups[1].Value, CultureInfo.InvariantCulture)
            : 1;

        return new SupportedRequirement(new(
            supply,
            forward,
            load,
            description.Trim(),
            CircuitFamily.Counter,
            Steps: 8,
            clock,
            forward));
    }

    private static RefusedRequirement Refuse() => new(
        "This version analyzes a 12 V to 3.3 V divider with negligible load, a 12 V to 5 V supply at about 200 mA, or a counter from 1 to 8.");

    [GeneratedRegex(@"(\d+(?:\.\d+)?)\s*v(?:olts?)?\b", RegexOptions.IgnoreCase)]
    private static partial Regex VoltPattern();

    [GeneratedRegex(@"(\d+(?:\.\d+)?)\s*ma\b", RegexOptions.IgnoreCase)]
    private static partial Regex MilliampPattern();

    [GeneratedRegex(@"(\d+(?:\.\d+)?)\s*A\b")]
    private static partial Regex AmpPattern();

    [GeneratedRegex(@"(\d+)\s*(?:to|-)\s*(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex RangePattern();

    [GeneratedRegex(@"(\d+(?:\.\d+)?)\s*hz\b", RegexOptions.IgnoreCase)]
    private static partial Regex HertzPattern();
}
