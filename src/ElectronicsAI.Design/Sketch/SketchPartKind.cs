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
}

public static class SketchPartKinds
{
    public static SketchPartKind Of(SketchPart part)
    {
        var type = $"{part.Type} {part.Name}";
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

        if (Has(type, "node") || Has(type, "junction") || Has(type, "net"))
        {
            return SketchPartKind.Node;
        }

        if (Has(type, "vsource") || Has(type, "source") || Has(type, "supply") || Has(type, "battery"))
        {
            return SketchPartKind.Supply;
        }

        return SketchPartKind.Unknown;
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
}
