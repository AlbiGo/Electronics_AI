using System.Globalization;
using System.Text.RegularExpressions;

namespace ElectronicsAI.Design;

public static partial class RcLowPassRules
{
    public static bool Applies(SchematicSketch sketch)
    {
        if (MotorDriverRules.Applies(sketch) || RelayDriverRules.Applies(sketch) || BuckConverterRules.Applies(sketch, null))
        {
            return false;
        }

        var hasResistor = sketch.Parts.Any(part => SketchPartKinds.Of(part) == SketchPartKind.Resistor);
        var hasCapacitor = sketch.Parts.Any(part => SketchPartKinds.Of(part) == SketchPartKind.Capacitor);
        var hasLed = sketch.Parts.Any(part => SketchPartKinds.Of(part) == SketchPartKind.Led);
        return hasResistor && hasCapacitor && !hasLed && CapacitorTouchesGround(sketch);
    }

    public static IReadOnlyList<SketchRule> Evaluate(SchematicSketch sketch, string? request)
    {
        var resistor = sketch.Parts.First(part => SketchPartKinds.Of(part) == SketchPartKind.Resistor);
        var capacitor = sketch.Parts.First(part => SketchPartKinds.Of(part) == SketchPartKind.Capacitor);
        var grounded = CapacitorTouchesGround(sketch);
        var output = HasOutput(sketch);
        var rules = new List<SketchRule>
        {
            new("Resistor present", $"{resistor.Name} is in the sketch.", "Pass"),
            new("Capacitor present", $"{capacitor.Name} is in the sketch.", "Pass"),
            new("Capacitor connected to ground", grounded ? $"{capacitor.Name} returns to ground." : $"{capacitor.Name} does not return to ground.", grounded ? "Pass" : "Fail"),
            new("Output node", output ? "The resistor and capacitor meet at the output." : "No output node joins the resistor and capacitor.", output ? "Pass" : "Fail"),
        };

        if (!TryOhms(resistor.Note, out var ohms) || !TryFarads(capacitor.Note, out var farads))
        {
            rules.Add(new SketchRule("Cutoff frequency", "The resistor or capacitor value could not be read.", "Fail"));
            return rules;
        }

        var calculated = 1 / (2 * Math.PI * ohms * farads);
        var expected = ExpectedHertz($"{request} {sketch.Title} {sketch.Summary}");
        if (expected is null)
        {
            rules.Add(new SketchRule("Cutoff frequency", Invariant($"{calculated:0.#} Hz calculated."), "Pass"));
            return rules;
        }

        var error = Math.Abs(calculated - expected.Value) / expected.Value * 100;
        var status = error <= 5 ? "Pass" : "Fail";
        rules.Add(new SketchRule(
            "Cutoff frequency",
            Invariant($"{calculated:0.#} Hz calculated, {expected.Value:0.#} Hz expected, {error:0.#}% error."),
            status));
        return rules;
    }

    public static IReadOnlyList<string> Assumptions(SchematicSketch sketch, string? request)
    {
        var lines = new List<string>();
        var resistor = sketch.Parts.FirstOrDefault(part => SketchPartKinds.Of(part) == SketchPartKind.Resistor);
        var capacitor = sketch.Parts.FirstOrDefault(part => SketchPartKinds.Of(part) == SketchPartKind.Capacitor);
        if (resistor is not null && TryOhms(resistor.Note, out var ohms))
        {
            lines.Add($"{resistor.Name} = {Ohms(ohms)}");
        }

        if (capacitor is not null && TryFarads(capacitor.Note, out var farads))
        {
            lines.Add($"{capacitor.Name} = {Farads(farads)}");
            if (resistor is not null && TryOhms(resistor.Note, out ohms))
            {
                var calculated = 1 / (2 * Math.PI * ohms * farads);
                lines.Add(Invariant($"Calculated fc = {calculated:0.#} Hz"));
            }
        }

        var expected = ExpectedHertz($"{request} {sketch.Title} {sketch.Summary}");
        if (expected is not null)
        {
            lines.Add(Invariant($"Expected fc = {expected.Value:0.#} Hz"));
        }

        return lines;
    }

    private static bool CapacitorTouchesGround(SchematicSketch sketch)
    {
        var capacitor = sketch.Parts.FirstOrDefault(part => SketchPartKinds.Of(part) == SketchPartKind.Capacitor);
        if (capacitor is null)
        {
            return false;
        }

        return Nets(sketch).Any(net =>
            net.Any(end => Belongs(end, capacitor))
            && net.Any(end => IsGround(end, sketch)));
    }

    private static bool HasOutput(SchematicSketch sketch)
    {
        if (sketch.Parts.Any(part => SketchPartKinds.Of(part) == SketchPartKind.Node))
        {
            return true;
        }

        var resistor = sketch.Parts.FirstOrDefault(part => SketchPartKinds.Of(part) == SketchPartKind.Resistor);
        var capacitor = sketch.Parts.FirstOrDefault(part => SketchPartKinds.Of(part) == SketchPartKind.Capacitor);
        if (resistor is null || capacitor is null)
        {
            return false;
        }

        return Nets(sketch).Any(net =>
            net.Any(end => Belongs(end, resistor))
            && net.Any(end => Belongs(end, capacitor))
            && !net.Any(end => IsGround(end, sketch)));
    }

    private static bool IsGround(string end, SchematicSketch sketch)
    {
        if (end.Equals("GND", StringComparison.OrdinalIgnoreCase)
            || end.Equals("VSS", StringComparison.OrdinalIgnoreCase)
            || end == "0")
        {
            return true;
        }

        var part = sketch.Parts.FirstOrDefault(item => Belongs(end, item));
        return part is not null && SketchPartKinds.Of(part) == SketchPartKind.Ground;
    }

    private static bool Belongs(string end, SketchPart part) =>
        end.Equals(part.Id, StringComparison.OrdinalIgnoreCase)
        || end.StartsWith(part.Id + ".", StringComparison.OrdinalIgnoreCase);

    private static List<HashSet<string>> Nets(SchematicSketch sketch)
    {
        var parent = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string Find(string key)
        {
            if (!parent.ContainsKey(key))
            {
                parent[key] = key;
            }

            var root = key;
            while (!parent[root].Equals(root, StringComparison.OrdinalIgnoreCase))
            {
                root = parent[root];
            }

            return parent[key] = root;
        }

        foreach (var wire in sketch.Wires)
        {
            var left = Find(wire.From.Trim());
            var right = Find(wire.To.Trim());
            if (!left.Equals(right, StringComparison.OrdinalIgnoreCase))
            {
                parent[left] = right;
            }
        }

        return parent.Keys
            .GroupBy(key => Find(key), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.ToHashSet(StringComparer.OrdinalIgnoreCase))
            .ToList();
    }

    private static double? ExpectedHertz(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var match = HertzPattern().Match(text);
        if (!match.Success || !double.TryParse(match.Groups["n"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return null;
        }

        var unit = match.Groups["u"].Value.ToLowerInvariant();
        return unit.StartsWith('k') ? number * 1000 : number;
    }

    private static bool TryOhms(string? text, out double ohms)
    {
        ohms = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var match = OhmsPattern().Match(text);
        if (!match.Success || !double.TryParse(match.Groups["n"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return false;
        }

        ohms = match.Groups["u"].Value.ToLowerInvariant() switch
        {
            "k" => number * 1_000,
            "meg" => number * 1_000_000,
            _ => number,
        };
        return ohms > 0;
    }

    private static bool TryFarads(string? text, out double farads)
    {
        farads = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var match = FaradsPattern().Match(text);
        if (!match.Success || !double.TryParse(match.Groups["n"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return false;
        }

        farads = match.Groups["u"].Value.ToLowerInvariant() switch
        {
            "pf" or "p" => number * 1e-12,
            "nf" or "n" => number * 1e-9,
            "uf" or "µf" or "μf" or "u" or "µ" or "μ" => number * 1e-6,
            _ => number,
        };
        return farads > 0;
    }

    private static string Ohms(double ohms) =>
        ohms >= 1000 ? Invariant($"{ohms / 1000:0.##} kΩ") : Invariant($"{ohms:0.##} Ω");

    private static string Farads(double farads)
    {
        if (farads >= 1e-6)
        {
            return Invariant($"{farads * 1e6:0.##} µF");
        }

        if (farads >= 1e-9)
        {
            return Invariant($"{farads * 1e9:0.##} nF");
        }

        return Invariant($"{farads * 1e12:0.##} pF");
    }

    private static string Invariant(FormattableString value) =>
        value.ToString(CultureInfo.InvariantCulture);

    [GeneratedRegex(@"(?<n>\d+(?:\.\d+)?)\s*(?<u>khz|kilohertz|hz)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HertzPattern();

    [GeneratedRegex(@"(?<n>\d+(?:\.\d+)?)\s*(?<u>meg|k|ohm|ohms|ω|Ω)?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OhmsPattern();

    [GeneratedRegex(@"(?<n>\d+(?:\.\d+)?)\s*(?<u>pf|nf|uf|µf|μf|f|p|n|u|µ|μ)?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FaradsPattern();
}
