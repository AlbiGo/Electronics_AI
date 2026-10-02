using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ElectronicsAI.Design;

namespace ElectronicsAI.Api;

public sealed class SchematicSketchService(HttpClient http, LanguageModelOptions options, SavedReplyCatalog? replies = null)
{
    public const string Instructions =
        """
        Describe the circuit the user asked for. Reply with one short JSON object and nothing else.
        Ripple counter: {"kind":"ripple-counter","bits":8,"title":"8-bit ripple counter","summary":"Each T input is tied high so the flip-flop toggles on every clock edge."}
        ESP32 and MAX30102: {"kind":"heart-rate","title":"Heart rate monitor","summary":"The ESP32 reads the MAX30102 over I2C. SDA is GPIO21 and SCL is GPIO22."}
        Any other circuit: {"title":"...","summary":"...","parts":[{"id":"r1","name":"R1","type":"block","note":"10k"}],"wires":[{"from":"r1","to":"r2"}]}
        If the user asks for a transistor, include that transistor in parts. A transistor and an LED need the transistor, the LED, a series resistor, and a base resistor.
        Do not add a parts list to a ripple counter or a heart-rate reply.
        """;

    public async Task<SchematicSketch> SketchAsync(
        string message,
        CancellationToken cancellationToken,
        Func<string, Task>? report = null)
    {
        Task Say(string text) => report?.Invoke(text) ?? Task.CompletedTask;

        if (replies?.Find(message) is { } saved)
        {
            await Say($"Using saved reply \"{saved.Request}\".");
            await Say($"Model reply: {Preview(saved.Content)}");
            return await Build(saved.Content, message, Say);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, options.Endpoint);
        if (!string.IsNullOrWhiteSpace(options.ApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        }

        request.Content = JsonContent.Create(ChatCompletion.Body(options.Model, Instructions, message, stream: true));

        await Say($"Calling {options.Model} at {options.Endpoint}. Waiting up to 5 minutes.");
        var watch = Stopwatch.StartNew();
        using var waiting = new CancellationTokenSource();
        var heartbeat = Heartbeat(Say, waiting.Token);
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken);
        }
        finally
        {
            waiting.Cancel();
            await heartbeat;
        }

        using (response)
        {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        await Say($"Model replied {(int)response.StatusCode} in {watch.ElapsedMilliseconds} ms");
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Model returned {(int)response.StatusCode}: {Preview(body)}");
        }

        using var document = JsonDocument.Parse(body);
        var content = document.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? "";
        await Say($"Model reply: {Preview(content)}");
        return await Build(content, message, Say);
        }
    }

    private static async Task<SchematicSketch> Build(string content, string message, Func<string, Task> say)
    {
        var notes = new List<string>();
        var sketch = SketchReplyParser.Parse(content, message, notes.Add);
        foreach (var note in notes)
        {
            await say(note);
        }

        var flipFlops = sketch.Parts.Count(part => part.Type == "flipflop");
        await say(flipFlops > 0
            ? $"Schema built: {sketch.Title}, {flipFlops} flip-flops, {sketch.Wires.Count} wires"
            : sketch.Parts.Count > 0
                ? $"Schema built: {sketch.Title}, {sketch.Parts.Count} parts, {sketch.Wires.Count} wires"
                : $"Schema built from the model text: {sketch.Title}");
        return sketch;
    }

    private static async Task Heartbeat(Func<string, Task> say, CancellationToken cancellationToken)
    {
        var elapsed = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(15), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            elapsed += 15;
            await say($"Still waiting for the model, {elapsed} seconds");
        }
    }

    private static string Preview(string text)
    {
        var flat = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return flat.Length <= 500 ? flat : flat[..500];
    }
}
