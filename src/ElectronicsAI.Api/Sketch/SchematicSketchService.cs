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
        Any other circuit: {"title":"...","summary":"...","parts":[{"id":"r1","name":"R1","type":"block","note":"10k"}],"wires":[{"from":"r1","to":"r2"}]}
        A buck converter needs the switching regulator, an inductor, a Schottky diode, an input capacitor, and an output capacitor. Set category to power, passive, logic, sensor, actuator, or protection.
        A temperature sensor and an ECU, with no solenoid requested, is only those two parts. Wire the sensor VCC and the ECU VCC to 5V, the sensor GND and the ECU GND to GND, and the sensor Output to the ECU TempIn. Do not add a solenoid, switch, MOSFET, or fan.
        An LED with a resistor and a battery is the LED, one resistor, and the battery. When the user also asks for a switch, add one push-button switch in series. Set the LED type to led. Put the resistance only in the resistor note, such as 330 ohm, and the voltage in the battery note, such as 9V. Wire the battery positive through the resistor and the switch to the LED anode, and the LED cathode to GND, with the battery negative also on GND.
        A low-side LED driver is a controller, an LED, a series resistor, an N-MOSFET, and a gate resistor. Name the controller pins GPIO, 3V3, and GND, and the MOSFET pins Gate, Drain, and Source. Wire 3V3 through the series resistor to the LED anode, the LED cathode to Drain, and Source to GND. Wire GPIO through the gate resistor to Gate, and wire the controller GND pin to GND. Do not wire the LED to GPIO.
        An ESP32, Arduino, STM32, or RP2040 with a push button and an LED is the controller, the button, the LED, one series resistor, and one pull resistor. Use notes that identify the push button, the resistor ohms, and the LED. Wire the controller 3V3 pin to 3V3 and its GND pin to GND. Wire one GPIO pin through the series resistor and the LED to GND. Wire the push button from that GPIO pin to 3V3, and a pull-down resistor from that same GPIO pin to GND. Do not wire the button, the LED, the series resistor, and the pull resistor in one chain.
        Add a solenoid only when the user asks for one. The parts are an ECU, a temperature sensor, a switch, a solenoid, an N-MOSFET, a gate resistor, and a flyback diode. Name them ECU, temperature sensor, switch, and solenoid. Wire the sensor output to TempIn and the switch to SwitchIn. Wire SolenoidOut through the gate resistor to the MOSFET gate, and a pull-down from that gate to GND. Wire the MOSFET source and the ECU GND to GND. Wire the solenoid from a coil supply such as +12V to the MOSFET drain, with the flyback diode across the solenoid and its cathode toward +12V. Do not use a motor, and do not wire the solenoid to an ECU pin.
        Do not add a parts list to a ripple counter.
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
