namespace ElectronicsAI.Design;

public enum PartCategory
{
    Unknown,
    Power,
    Passive,
    Logic,
    Sensor,
    Actuator,
    Protection,
}

public sealed record SketchPart(
    string Id,
    string Name,
    string Type,
    string? Note,
    IReadOnlyList<string>? Pins = null,
    PartCategory Category = PartCategory.Unknown)
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
