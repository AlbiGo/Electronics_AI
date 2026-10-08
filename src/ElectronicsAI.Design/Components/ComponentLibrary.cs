using System.Text.Json;

namespace ElectronicsAI.Design;

public sealed class ComponentRecord
{
    public string Name { get; init; } = "";

    public string Category { get; init; } = "";

    public string Interface { get; init; } = "";

    public string Voltage { get; init; } = "";

    public double? MaxVoltage { get; init; }

    public string[] Pins { get; init; } = [];
}

public static class ComponentLibrary
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public static IReadOnlyList<ComponentRecord> All { get; } = Load();

    public static IReadOnlyList<ComponentRecord> In(string category) =>
        All.Where(item => item.Category.Equals(category, StringComparison.OrdinalIgnoreCase)).ToList();

    public static ComponentRecord? Find(SketchPart part)
    {
        var text = $"{part.Type} {part.Name}";
        return All
            .Where(item => text.Contains(item.Name, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.Name.Length)
            .FirstOrDefault();
    }

    private static IReadOnlyList<ComponentRecord> Load()
    {
        var assembly = typeof(ComponentLibrary).Assembly;
        var name = assembly.GetManifestResourceNames().Single(item => item.EndsWith("components.json", StringComparison.OrdinalIgnoreCase));
        using var stream = assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException("components.json is missing from the design assembly.");
        return JsonSerializer.Deserialize<List<ComponentRecord>>(stream, Json) ?? [];
    }
}
