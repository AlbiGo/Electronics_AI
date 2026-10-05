using System.Text.Json;
using System.Text.RegularExpressions;

namespace ElectronicsAI.Design;

public static class SketchReplyParser
{
    public static SchematicSketch Parse(string content, string request, Action<string>? log = null)
    {
        void Note(string message) => log?.Invoke(message);

        var start = content.IndexOf('{');
        var end = content.LastIndexOf('}');
        if (start >= 0 && end > start)
        {
            Note("Found a JSON object in the model reply");
            try
            {
                using var document = JsonDocument.Parse(Clean(content[start..(end + 1)]));
                var sketch = Read(document.RootElement, request, Note);
                if (sketch.Parts.Count > 0 || !string.IsNullOrWhiteSpace(sketch.Diagram))
                {
                    Note($"JSON accepted: {sketch.Parts.Count} parts, {sketch.Wires.Count} wires");
                    return sketch;
                }

                Note("JSON had no parts to draw");
            }
            catch (JsonException exception)
            {
                Note($"JSON could not be read: {exception.Message}");
                var repaired = Repair(Clean(content));
                if (repaired is not null)
                {
                    try
                    {
                        using var document = JsonDocument.Parse(repaired);
                        Note("Closed the partial JSON");
                        return Read(document.RootElement, request, Note);
                    }
                    catch (JsonException)
                    {
                    }
                }

                var salvaged = Salvage(content, Note);
                if (salvaged is not null)
                {
                    return salvaged;
                }
            }
        }
        else if (start >= 0)
        {
            Note("JSON object was cut off");
            var salvaged = Salvage(content, Note);
            if (salvaged is not null)
            {
                return salvaged;
            }
        }
        else
        {
            Note("Model reply had no JSON object");
        }

        if (SketchTemplates.MatchText(content) is { } generated)
        {
            Note(generated.Note);
            return generated.Sketch;
        }

        Note("Using the model text as the diagram");
        var diagram = Fence(content) ?? content.Trim();
        return new SchematicSketch(
            Title(request),
            "The model returned this diagram.",
            [],
            [],
            diagram);
    }

    private static SchematicSketch Read(JsonElement root, string request, Action<string> note)
    {
        var title = Text(root, "title") ?? Title(request);
        var summary = Text(root, "summary") ?? "Schematic for the circuit you asked for.";
        var kind = Text(root, "kind")?.Trim().ToLowerInvariant();
        var bits = Int(root, "bits");
        note($"JSON fields: kind={kind ?? "none"}, bits={bits?.ToString() ?? "none"}, title={title}");
        if (SketchTemplates.MatchKind(kind, bits, title, summary, partial: false) is { } built)
        {
            note(built.Note);
            return built.Sketch;
        }

        var parts = new List<SketchPart>();
        if ((Array(root, "parts") ?? Array(root, "components")) is { } partArray)
        {
            foreach (var part in partArray.EnumerateArray())
            {
                var id = Text(part, "id");
                var name = Text(part, "name") ?? id;
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var type = Normalize(Text(part, "type"));
                var value = Text(part, "note") ?? Text(part, "value");
                var category = PartCategories.Read(Text(part, "category")) ?? PartCategories.Infer(type, name, value);
                parts.Add(new SketchPart(id, name, type, value, PinNames(part), category));
                if (parts.Count == 24)
                {
                    break;
                }
            }
        }

        var listedWires = Array(root, "wires") ?? Array(root, "connections");
        var wires = new List<SketchWire>();
        var skipped = 0;
        if (listedWires is { } wireArray)
        {
            foreach (var wire in wireArray.EnumerateArray())
            {
                var from = Endpoint(Text(wire, "from"), parts);
                var to = Endpoint(Text(wire, "to"), parts);
                if (from is null || to is null || from.Equals(to, StringComparison.OrdinalIgnoreCase))
                {
                    skipped++;
                    continue;
                }

                wires.Add(new SketchWire(from, to));
                if (wires.Count == 48)
                {
                    break;
                }
            }
        }

        note(skipped == 0
            ? $"Read {parts.Count} parts and {wires.Count} wires"
            : $"Read {parts.Count} parts and {wires.Count} wires. Skipped {skipped} wires that did not name a known pin");
        return new SchematicSketch(title, summary, WithPinsFromWires(parts, wires), wires, Text(root, "diagram"));
    }

    private static string Clean(string json) =>
        StrayWireField.Replace(json, "$1,$2");

    private static readonly Regex StrayWireField = new(
        """("(?:from|to)"\s*:\s*"[^"]+")\s*,\s*"[^"]+"\s*,\s*("(?:from|to)"\s*:)""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static List<SketchPart> WithPinsFromWires(List<SketchPart> parts, List<SketchWire> wires)
    {
        var seen = wires
            .SelectMany(wire => new[] { wire.From, wire.To })
            .Select(end =>
            {
                var dot = end.IndexOf('.');
                return dot < 0 ? (Id: "", Pin: "") : (Id: end[..dot], Pin: end[(dot + 1)..]);
            })
            .Where(item => item.Id.Length > 0 && item.Pin.Length > 0)
            .GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var group in seen)
        {
            var index = parts.FindIndex(part => part.Id.Equals(group.Key, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                continue;
            }

            var pins = parts[index].Pins.ToList();
            foreach (var pin in group.Select(item => item.Pin).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (pins.Any(item => item.Equals(pin, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                pins.Add(pin);
            }

            parts[index] = parts[index] with { Pins = pins };
        }

        return parts;
    }

    private static string? Repair(string content)
    {
        var start = content.IndexOf('{');
        if (start < 0)
        {
            return null;
        }

        var text = content[start..];
        var stack = new Stack<char>();
        var inString = false;
        var escape = false;
        var lastValueEnd = -1;
        var afterColon = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inString)
            {
                if (escape)
                {
                    escape = false;
                    continue;
                }

                if (c == '\\')
                {
                    escape = true;
                    continue;
                }

                if (c == '"')
                {
                    inString = false;
                    if (afterColon)
                    {
                        lastValueEnd = i;
                        afterColon = false;
                    }
                }

                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                continue;
            }

            if (c == '"')
            {
                inString = true;
                continue;
            }

            if (c is '{' or '[')
            {
                stack.Push(c);
                afterColon = false;
                continue;
            }

            if (c is '}' or ']')
            {
                if (stack.Count == 0)
                {
                    return text[..(i + 1)];
                }

                stack.Pop();
                lastValueEnd = i;
                afterColon = false;
                if (stack.Count == 0)
                {
                    return text[..(i + 1)];
                }

                continue;
            }

            if (c == ':')
            {
                afterColon = true;
                continue;
            }

            if (c == ',')
            {
                afterColon = false;
                continue;
            }

            if (afterColon)
            {
                var end = i;
                while (end < text.Length && !char.IsWhiteSpace(text[end]) && text[end] is not ',' and not '}' and not ']')
                {
                    end++;
                }

                if (end > i && end <= text.Length && (end == text.Length || text[end - 1] is '}' or ']' || char.IsDigit(text[end - 1]) || text[end - 1] is 'e' or 'l'))
                {
                    lastValueEnd = end - 1;
                    afterColon = false;
                    i = end - 1;
                }
            }
        }

        if (lastValueEnd < 0)
        {
            return null;
        }

        var body = text[..(lastValueEnd + 1)].TrimEnd().TrimEnd(',');
        stack.Clear();
        inString = false;
        escape = false;
        foreach (var c in body)
        {
            if (inString)
            {
                if (escape)
                {
                    escape = false;
                    continue;
                }

                if (c == '\\')
                {
                    escape = true;
                    continue;
                }

                if (c == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (c == '"')
            {
                inString = true;
                continue;
            }

            if (c is '{' or '[')
            {
                stack.Push(c);
            }
            else if (c is '}' or ']' && stack.Count > 0)
            {
                stack.Pop();
            }
        }

        while (stack.Count > 0)
        {
            body += stack.Pop() == '{' ? "}" : "]";
        }

        return body;
    }

    private static SchematicSketch? Salvage(string content, Action<string> note)
    {
        var kind = Field(content, "kind")?.Trim().ToLowerInvariant();
        var title = Field(content, "title");
        var summary = Field(content, "summary");
        int? bits = int.TryParse(Field(content, "bits"), out var parsed) ? parsed : null;
        note($"Partial JSON fields: kind={kind ?? "none"}, bits={bits?.ToString() ?? "none"}, title={title ?? "none"}");
        if (SketchTemplates.MatchKind(kind, bits, title, summary, partial: true) is { } built)
        {
            note(built.Note);
            return built.Sketch;
        }

        return null;
    }

    private static string? Field(string content, string name)
    {
        var match = Regex.Match(content, $"\"{name}\"\\s*:\\s*\"(?<value>[^\"]*)\"", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            return match.Groups["value"].Value;
        }

        match = Regex.Match(content, $"\"{name}\"\\s*:\\s*(?<value>\\d+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["value"].Value : null;
    }

    private static string Normalize(string? type)
    {
        var value = type?.Trim().ToLowerInvariant() ?? "block";
        return value switch
        {
            "tff" or "dff" or "flip-flop" or "flipflop" => "flipflop",
            "inductor" or "coil" => "inductor",
            "schottky" => "schottky",
            "diode" or "flyback_diode" or "flyback-diode" or "flyback" => "diode",
            "nmosfet" or "n-mosfet" or "n_mosfet" or "pmosfet" => "mosfet",
            "regulator" or "buck" => "regulator",
            "in" or "input" or "clock" => "input",
            "out" or "output" or "q" => "output",
            "and" or "or" or "not" or "nand" or "gate" => "gate",
            _ => value,
        };
    }

    private static string Title(string request)
    {
        var trimmed = request.Trim();
        return trimmed.Length <= 80 ? trimmed : trimmed[..80];
    }

    private static string? Fence(string content)
    {
        const string marker = "```";
        var start = content.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        start = content.IndexOf('\n', start);
        if (start < 0)
        {
            return null;
        }

        var end = content.IndexOf(marker, start + 1, StringComparison.Ordinal);
        if (end < 0)
        {
            return null;
        }

        return content[(start + 1)..end].Trim();
    }

    private static JsonElement? Array(JsonElement root, string name)
    {
        var property = Find(root, name);
        return property?.ValueKind == JsonValueKind.Array ? property : null;
    }

    private static int? Int(JsonElement root, string name)
    {
        var property = Find(root, name);
        if (property?.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out var number))
        {
            return number;
        }

        if (property?.ValueKind == JsonValueKind.String && int.TryParse(property.Value.GetString(), out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static IReadOnlyList<string> PinNames(JsonElement part)
    {
        var property = Find(part, "pins");
        if (property is null)
        {
            return [];
        }

        var pins = new List<string>();
        if (property.Value.ValueKind == JsonValueKind.Array)
        {
            foreach (var pin in property.Value.EnumerateArray())
            {
                if (pin.ValueKind == JsonValueKind.String && pin.GetString() is { } name && name.Trim() is { Length: > 0 } trimmed)
                {
                    pins.Add(trimmed);
                }

                if (pins.Count == 16)
                {
                    break;
                }
            }
        }
        else if (property.Value.ValueKind == JsonValueKind.Object)
        {
            foreach (var pin in property.Value.EnumerateObject())
            {
                if (pin.Name.Trim() is { Length: > 0 } trimmed)
                {
                    pins.Add(trimmed);
                }

                if (pins.Count == 16)
                {
                    break;
                }
            }
        }

        return pins;
    }

    private static string? Endpoint(string? raw, IReadOnlyList<SketchPart> parts)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var text = raw.Trim();
        var whole = parts.FirstOrDefault(part => part.Id.Equals(text, StringComparison.OrdinalIgnoreCase));
        if (whole is not null)
        {
            return whole.Id;
        }

        var dot = text.IndexOf('.');
        if (dot < 0)
        {
            return text;
        }

        if (dot == 0 || dot == text.Length - 1)
        {
            return null;
        }

        var part = parts.FirstOrDefault(item => item.Id.Equals(text[..dot], StringComparison.OrdinalIgnoreCase));
        if (part is null)
        {
            return null;
        }

        var pinText = text[(dot + 1)..];
        if (pinText.Length == 0)
        {
            return null;
        }

        if (part.Pins.Count > 0)
        {
            var pin = part.Pins.FirstOrDefault(item => item.Equals(pinText, StringComparison.OrdinalIgnoreCase));
            return pin is null ? null : $"{part.Id}.{pin}";
        }

        return $"{part.Id}.{pinText}";
    }

    private static string? Text(JsonElement root, string name)
    {
        var property = Find(root, name);
        return property?.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
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
