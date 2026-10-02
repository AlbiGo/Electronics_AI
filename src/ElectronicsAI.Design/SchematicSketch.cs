namespace ElectronicsAI.Design;

public sealed record SketchPart(string Id, string Name, string Type, string? Note, IReadOnlyList<string>? Pins = null)
{
    public IReadOnlyList<string> Pins { get; init; } = Pins ?? [];
}

public sealed record SketchWire(string From, string To);

public sealed record SchematicSketch(
    string Title,
    string Summary,
    IReadOnlyList<SketchPart> Parts,
    IReadOnlyList<SketchWire> Wires,
    string? Diagram);
