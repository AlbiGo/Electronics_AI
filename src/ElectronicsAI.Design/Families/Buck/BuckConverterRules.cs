using System.Globalization;
using System.Text.RegularExpressions;

namespace ElectronicsAI.Design;

public static partial class BuckConverterRules
{
    public static bool Applies(SchematicSketch sketch, string? request)
    {
        if (RelayDriverRules.Applies(sketch) || MotorDriverRules.Applies(sketch))
        {
            return false;
        }

        var text = $"{request} {string.Join(" ", sketch.Parts.Select(part => $"{part.Type} {part.Name} {part.Note}"))}";
        return Has(text, "buck", "lm2596", "lm2576", "lm267", "mp1584", "mp2307", "step-down", "step down");
    }

    public static IReadOnlyList<SketchRule> Evaluate(SchematicSketch sketch) =>
    [
        Present(sketch, IsRegulator, "Switching regulator", "A switching regulator is in the sketch.", "No switching regulator, such as an LM2596, is in the sketch."),
        Present(sketch, IsInductor, "Inductor", "An inductor carries the current into the output.", "The inductor is missing."),
        Present(sketch, IsSchottky, "Schottky diode", "A Schottky diode is across the switch node.", "No Schottky diode is across the switch node."),
        Present(sketch, IsInputCapacitor, "Input capacitor", "An input capacitor is across the input.", "The input capacitor is missing."),
        Present(sketch, IsOutputCapacitor, "Output capacitor", "An output capacitor is across the output.", "The output capacitor is missing."),
        Voltages(sketch),
    ];

    public static bool IsInductor(SketchPart part)
    {
        var text = Text(part);
        return Has(text, "inductor", "coil") || Microhenry().IsMatch(text);
    }

    private static SketchRule Present(SchematicSketch sketch, Func<SketchPart, bool> match, string name, string pass, string fail)
    {
        var part = sketch.Parts.FirstOrDefault(match);
        return part is null
            ? new SketchRule(name, fail, "Fail")
            : new SketchRule(name, pass, "Pass");
    }

    private static SketchRule Voltages(SchematicSketch sketch)
    {
        var input = sketch.Parts.Select(part => (part, volts: ReadVolts(Text(part)))).FirstOrDefault(item => item.volts is not null && IsInput(item.part));
        var output = sketch.Parts.Select(part => (part, volts: ReadVolts(Text(part)))).FirstOrDefault(item => item.volts is not null && IsOutput(item.part));
        if (input.volts is not { } vin || output.volts is not { } vout)
        {
            return new SketchRule("Input above output", "The input and output voltages could not be read.", "Fail");
        }

        return vin > vout
            ? new SketchRule("Input above output", $"{Shown(vin)} V in is above {Shown(vout)} V out.", "Pass")
            : new SketchRule("Input above output", $"{Shown(vin)} V in is not above {Shown(vout)} V out.", "Fail");
    }

    private static string Shown(double volts) => volts.ToString("0.##", CultureInfo.InvariantCulture);

    private static bool IsRegulator(SketchPart part) => Has(Text(part), "lm2596", "lm2576", "lm267", "mp1584", "mp2307", "regulator", "buck", "switching");

    private static bool IsSchottky(SketchPart part) => Has(Text(part), "schottky", "ss34", "ss14", "1n581", "1n582", "mbr", "diode");

    private static bool IsInputCapacitor(SketchPart part)
    {
        var text = Text(part);
        return IsCapacitor(part) && Has(text, "input", "cin") && !Has(text, "output", "cout");
    }

    private static bool IsOutputCapacitor(SketchPart part)
    {
        var text = Text(part);
        return IsCapacitor(part) && Has(text, "output", "cout") && !Has(text, "input", "cin");
    }

    private static bool IsCapacitor(SketchPart part) => Has(Text(part), "capacitor", "cap");

    private static bool IsInput(SketchPart part) => Has(Text(part), "vin", "input", "supply", "vsource");

    private static bool IsOutput(SketchPart part) => Has(Text(part), "vout", "output") && !IsInput(part);

    private static string Text(SketchPart part) => $"{part.Type} {part.Name} {part.Note}";

    private static bool Has(string text, params string[] words) =>
        words.Any(word => text.Contains(word, StringComparison.OrdinalIgnoreCase));

    private static double? ReadVolts(string text)
    {
        var match = Volts().Match(text);
        return match.Success && double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var volts)
            ? volts
            : null;
    }

    [GeneratedRegex(@"(\d+(?:\.\d+)?)\s*v", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Volts();

    [GeneratedRegex(@"\d\s*[uµμ]h", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Microhenry();
}

public sealed class BuckRuleSet : ISketchRuleSet
{
    public CircuitCategory Category => CircuitCategory.Buck;

    public bool Applies(SchematicSketch sketch, string? request) => BuckConverterRules.Applies(sketch, request);

    public SketchRuleResult Evaluate(SchematicSketch sketch, string? request) =>
        new("Buck converter checked without ngspice.", BuckConverterRules.Evaluate(sketch), []);
}
