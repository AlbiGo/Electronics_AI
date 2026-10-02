using System.Globalization;
using System.Text.Json;
using ElectronicsAI.Domain;

namespace ElectronicsAI.Design;

public static class ModelReplyParser
{
    public static RequirementProposal Parse(string content, string description)
    {
        var start = content.IndexOf('{');
        var end = content.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return new RefusedRequirement("The language model did not return a circuit requirement.");
        }

        try
        {
            using var document = JsonDocument.Parse(content[start..(end + 1)]);
            var root = document.RootElement;
            if (!ReadBool(root, "supported"))
            {
                return new RefusedRequirement(ReadString(root, "reason")
                    ?? "This version analyzes a 12 V to 3.3 V divider with negligible load, a 12 V to 5 V supply at about 200 mA, or a counter from 1 to 8.");
            }

            var family = ReadString(root, "family");
            if (family is not null && family.Equals("counter", StringComparison.OrdinalIgnoreCase))
            {
                var steps = TryReadDouble(root, "steps", out var parsedSteps) ? (int)parsedSteps : 8;
                if (steps != 8)
                {
                    return new RefusedRequirement("This version builds a counter from 1 to 8.");
                }

                var input = TryReadDouble(root, "inputVolts", out var parsedInput) ? parsedInput : 5;
                var forward = TryReadDouble(root, "forwardVolts", out var parsedForward) ? parsedForward : 2;
                var load = TryReadDouble(root, "loadCurrentAmps", out var parsedLoad) ? parsedLoad : 0.01;
                var clock = TryReadDouble(root, "clockHertz", out var parsedClock) ? parsedClock : 1;
                return new SupportedRequirement(new(
                    input,
                    forward,
                    load,
                    description.Trim(),
                    CircuitFamily.Counter,
                    steps,
                    clock,
                    forward));
            }

            if (!TryReadDouble(root, "inputVolts", out var inputVolts)
                || !TryReadDouble(root, "outputVolts", out var output)
                || !TryReadDouble(root, "loadCurrentAmps", out var loadCurrent))
            {
                return new RefusedRequirement("The language model did not return a circuit requirement.");
            }

            return new SupportedRequirement(new(inputVolts, output, loadCurrent, description.Trim()));
        }
        catch (JsonException)
        {
            return new RefusedRequirement("The language model did not return a circuit requirement.");
        }
    }

    private static bool ReadBool(JsonElement root, string name)
    {
        var property = Find(root, name);
        return property is { ValueKind: JsonValueKind.True };
    }

    private static string? ReadString(JsonElement root, string name)
    {
        var property = Find(root, name);
        return property?.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
    }

    private static bool TryReadDouble(JsonElement root, string name, out double value)
    {
        value = 0;
        var property = Find(root, name);
        if (property is null)
        {
            return false;
        }

        if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetDouble(out value))
        {
            return true;
        }

        return property.Value.ValueKind == JsonValueKind.String
            && double.TryParse(property.Value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static JsonElement? Find(JsonElement root, string name)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }

        return null;
    }
}
