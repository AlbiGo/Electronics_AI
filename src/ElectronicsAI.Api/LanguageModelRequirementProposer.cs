using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ElectronicsAI.Design;

namespace ElectronicsAI.Api;

public sealed record LanguageModelOptions(string Endpoint, string Model, string ApiKey);

public static class ChatCompletion
{
    public static object Body(string model, string system, string user, bool stream)
    {
        var messages = new object[]
        {
            new { role = "system", content = system },
            new { role = "user", content = user },
        };

        if (UsesDefaultTemperature(model))
        {
            return stream
                ? new { model, stream = false, reasoning_effort = "low", messages }
                : new { model, reasoning_effort = "low", messages };
        }

        return stream
            ? new { model, temperature = 0, stream = false, messages }
            : new { model, temperature = 0, messages };
    }

    public static bool UsesDefaultTemperature(string model)
    {
        var name = model.Trim();
        return name.StartsWith("gpt-5", StringComparison.OrdinalIgnoreCase)
            && !name.Contains("chat", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class LanguageModelRequirementProposer(
    HttpClient http,
    LanguageModelOptions options,
    LocalRequirementProposer fallback) : IRequirementProposer
{
    public const string Instructions =
        """
        Convert the user's electronics request into JSON only, with no markdown.
        This application can build:
        - a 12 V to 5 V linear supply at about 0.2 A
        - a 12 V to 3.3 V resistor divider with negligible load (loadCurrentAmps 0)
        - a counter from 1 to 8, with a clock and one LED on each step
        If the user asks for the counter, respond with {"supported":true,"family":"counter","steps":8,"inputVolts":5,"forwardVolts":2,"loadCurrentAmps":0.01,"clockHertz":1} and replace the supply, LED current, forward voltage, and clock with the numbers they gave.
        If the user asks for the supply or the divider, respond with {"supported":true,"inputVolts":12,"outputVolts":5,"loadCurrentAmps":0.2} using the voltages and current they asked for when those values still fit.
        If they ask for anything else, including a switching converter or a counter with a different count, respond with {"supported":false,"reason":"short reason"}.
        """;

    public async Task<RequirementProposal> ProposeAsync(string description, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, options.Endpoint);
        if (!string.IsNullOrWhiteSpace(options.ApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        }

        request.Content = JsonContent.Create(ChatCompletion.Body(options.Model, Instructions, description, stream: false));

        try
        {
            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return await fallback.ProposeAsync(description, cancellationToken);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var content = document.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            return ModelReplyParser.Parse(content ?? "", description);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or TaskCanceledException)
        {
            return await fallback.ProposeAsync(description, cancellationToken);
        }
    }
}
