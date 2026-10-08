namespace ElectronicsAI.Design;

public enum SketchPartKind
{
    Unknown,
    Resistor,
    Capacitor,
    Led,
    Supply,
    Ground,
    Node,
    Switch,
}

public static class SketchPartKinds
{
    public static SketchPartKind Of(SketchPart part)
    {
        var type = $"{part.Type} {part.Name} {part.Note}";
        if (Has(type, "resistor") || Has(type, "ohm"))
        {
            return SketchPartKind.Resistor;
        }

        if (Has(type, "led"))
        {
            return SketchPartKind.Led;
        }

        if (Has(type, "capacitor") || Has(type, "cap"))
        {
            return SketchPartKind.Capacitor;
        }

        if (Has(type, "ground") || Has(type, "gnd"))
        {
            return SketchPartKind.Ground;
        }

        if (Word(type, "node") || Word(type, "junction") || Word(type, "net"))
        {
            return SketchPartKind.Node;
        }

        if (Has(type, "vsource") || Has(type, "source") || Has(type, "supply") || Has(type, "battery"))
        {
            return SketchPartKind.Supply;
        }

        if (IsSwitch(type))
        {
            return SketchPartKind.Switch;
        }

        return SketchPartKind.Unknown;
    }

    private static bool IsSwitch(string type)
    {
        if (Has(type, "regulator") || Has(type, "buck") || Has(type, "lm2596") || Has(type, "lm2576"))
        {
            return false;
        }

        if (Has(type, "button") || Has(type, "momentary") || Has(type, "spst") || Has(type, "spdt") || Has(type, "normally-open") || Has(type, "normally open"))
        {
            return true;
        }

        return System.Text.RegularExpressions.Regex.IsMatch(
            type,
            @"(^|[^a-z])switch([^a-z]|$)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    }

    public static bool IsAnalog(SketchPartKind kind) =>
        kind is SketchPartKind.Resistor
            or SketchPartKind.Capacitor
            or SketchPartKind.Led
            or SketchPartKind.Supply
            or SketchPartKind.Ground
            or SketchPartKind.Node;

    private static bool Has(string text, string word) =>
        text.Contains(word, StringComparison.OrdinalIgnoreCase);

    private static bool Word(string text, string word) =>
        System.Text.RegularExpressions.Regex.IsMatch(
            text,
            $@"(^|[^a-z]){System.Text.RegularExpressions.Regex.Escape(word)}([^a-z]|$)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
}
