using System.Text.Json;

namespace ElectronicsAI.Api;

public sealed record SavedMatch(string Request, string Content);

public sealed class SavedReplyCatalog(string path)
{
    public SavedMatch? Find(string message)
    {
        if (string.IsNullOrWhiteSpace(message) || !File.Exists(path))
        {
            return null;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        SavedMatch? best = null;
        foreach (var item in document.RootElement.EnumerateArray())
        {
            var content = ReplyText(item);
            if (content is null)
            {
                continue;
            }

            foreach (var phrase in Phrases(item))
            {
                if (phrase.Length == 0 || !message.Contains(phrase, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (best is null || phrase.Length > best.Request.Length)
                {
                    best = new SavedMatch(phrase, content);
                }
            }
        }

        return best;
    }

    private static IEnumerable<string> Phrases(JsonElement item)
    {
        if (item.TryGetProperty("requests", out var requests) && requests.ValueKind == JsonValueKind.Array)
        {
            foreach (var request in requests.EnumerateArray())
            {
                if (request.ValueKind == JsonValueKind.String && request.GetString() is { } phrase)
                {
                    yield return phrase.Trim();
                }
            }
        }

        if (item.TryGetProperty("request", out var single)
            && single.ValueKind == JsonValueKind.String
            && single.GetString() is { } text)
        {
            yield return text.Trim();
        }
    }

    private static string? ReplyText(JsonElement item)
    {
        if (!item.TryGetProperty("reply", out var reply))
        {
            return null;
        }

        return reply.ValueKind switch
        {
            JsonValueKind.String => reply.GetString(),
            JsonValueKind.Object or JsonValueKind.Array => reply.GetRawText(),
            _ => null,
        };
    }
}
