namespace ElectronicsAI.Design;

public sealed record SketchCompletion(SchematicSketch Sketch, string Note);

public sealed class SketchTemplate
{
    public required string[][] RequestGroups { get; init; }

    public required string[] SkipWhenPartContains { get; init; }

    public required string Title { get; init; }

    public required string Summary { get; init; }

    public required string Note { get; init; }

    public required IReadOnlyList<SketchPart> Parts { get; init; }

    public required IReadOnlyList<SketchWire> Wires { get; init; }

    public bool Requested(string? request)
    {
        if (string.IsNullOrWhiteSpace(request))
        {
            return false;
        }

        return RequestGroups.All(group => group.Any(word => request.Contains(word, StringComparison.OrdinalIgnoreCase)));
    }

    public bool AlreadyPresent(SketchPart part)
    {
        var text = $"{part.Type} {part.Name} {part.Note}";
        return SkipWhenPartContains.Any(word => text.Contains(word, StringComparison.OrdinalIgnoreCase));
    }

    public SchematicSketch Create(string? title, string? summary) =>
        new(
            string.IsNullOrWhiteSpace(title) ? Title : title,
            string.IsNullOrWhiteSpace(summary) ? Summary : summary,
            Parts,
            Wires,
            null);
}

public static class SketchTemplates
{
    private static readonly SketchTemplate[] Known =
    [
        new()
        {
            RequestGroups = [["transistor", "npn", "pnp", "mosfet", "bjt"], ["led", "light"]],
            SkipWhenPartContains = ["transistor", "npn", "pnp", "bjt", "mosfet", "nmos", "2n2222", "2n3904"],
            Title = "NPN transistor LED switch",
            Summary = "An NPN low-side switch. The LED has a 330 ohm series resistor from 5 V, and a 10k resistor drives the base.",
            Note = "The model left out the transistor. Building an NPN LED switch.",
            Parts =
            [
                new("vcc", "VCC", "supply", "5V"),
                new("gnd", "GND", "ground", null),
                new("in", "IN", "node", "control signal"),
                new("rbase", "R_BASE", "resistor", "10k", ["1", "2"]),
                new("rled", "R_LED", "resistor", "330 ohm", ["1", "2"]),
                new("q1", "Q1", "npn", "2N2222", ["B", "C", "E"]),
                new("led1", "LED1", "led", null, ["A", "K"]),
            ],
            Wires =
            [
                new("vcc", "rled.1"),
                new("rled.2", "led1.A"),
                new("led1.K", "q1.C"),
                new("q1.E", "gnd"),
                new("in", "rbase.1"),
                new("rbase.2", "q1.B"),
            ],
        },
    ];

    public static SketchCompletion? TryComplete(
        IReadOnlyList<SketchPart> parts,
        string? request,
        string? title,
        string? summary)
    {
        var template = Known.FirstOrDefault(item => item.Requested(request) && !parts.Any(item.AlreadyPresent));
        return template is null ? null : new SketchCompletion(template.Create(title, summary), template.Note);
    }
}
