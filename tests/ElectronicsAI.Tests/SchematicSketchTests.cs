using System.Net;
using System.Text;
using System.Text.Json;
using ElectronicsAI.Api;
using ElectronicsAI.Design;

namespace ElectronicsAI.Tests;

public class SchematicSketchTests
{
    [Fact]
    public void Engine_builds_the_ripple_counter_from_the_model_reply()
    {
        const string reply = """
            {"kind":"ripple-counter","bits":8,"title":"8-bit ripple counter","summary":"Each T input is tied high so the flip-flop toggles on every clock edge."}
            """;

        var sketch = SketchReplyParser.Parse(reply, "Create an 8-bit counter circuit diagram");

        Assert.Equal("8-bit ripple counter", sketch.Title);
        Assert.Equal(8, sketch.Parts.Count(part => part.Type == "flipflop"));
        Assert.Contains(sketch.Wires, wire => wire.From == "clk" && wire.To == "tff0");
        Assert.Contains(sketch.Wires, wire => wire.From == "tff7" && wire.To == "q7");
    }

    [Fact]
    public void Engine_reads_a_ripple_counter_from_a_truncated_reply()
    {
        const string reply = """
            {"title":"8-bit Ripple Counter","summary":"Each T input is tied high so the flip-flop toggles on every clock edge.","kind":"ripple-counter","bits":8,"parts":[{"id":"r1","name":"R1","type":"block","note":"10k"},{"id":"r7","name":"R7","type":
            """;

        var sketch = SketchReplyParser.Parse(reply, "Create an 8-bit counter circuit diagram");

        Assert.Equal("8-bit Ripple Counter", sketch.Title);
        Assert.Equal(8, sketch.Parts.Count(part => part.Type == "flipflop"));
        Assert.Contains(sketch.Wires, wire => wire.From == "clk" && wire.To == "tff0");
    }

    [Fact]
    public void Engine_builds_the_heart_rate_monitor_from_the_model_reply()
    {
        const string reply = """
            {"kind":"heart-rate","title":"Heart rate monitor","summary":"The ESP32 reads the MAX30102 over I2C. SDA is GPIO21 and SCL is GPIO22."}
            """;

        var sketch = SketchReplyParser.Parse(reply, "Create a heart rate monitor using ESP32 and MAX30102.");

        Assert.Equal("Heart rate monitor", sketch.Title);
        Assert.Contains(sketch.Parts, part => part.Name == "ESP32" && part.Type == "mcu");
        Assert.Contains(sketch.Parts, part => part.Name == "MAX30102" && part.Type == "sensor");
        Assert.Contains(sketch.Wires, wire => wire.From == "esp32.3V3" && wire.To == "max30102.VIN");
        Assert.Contains(sketch.Wires, wire => wire.From == "esp32.GPIO21" && wire.To == "max30102.SDA");
        Assert.Contains(sketch.Wires, wire => wire.From == "esp32.GPIO22" && wire.To == "max30102.SCL");
    }

    [Fact]
    public void Prose_about_the_heart_sensor_still_draws_the_board()
    {
        const string reply = """
            Here's a simple implementation of a heart rate monitor using ESP32 and MAX30102:
            **Hardware Requirements:** * ESP32 board * MAX30102 heart rate sensor
            **Circuit Diagram:** * VCC: connected to 3.3V pin on the ESP32 * GND: connected to GND * SDA: connected to
            """;

        var sketch = SketchReplyParser.Parse(reply, "Create a heart rate monitor using ESP32 and MAX30102");

        Assert.Contains(sketch.Parts, part => part.Name == "ESP32" && part.Type == "mcu");
        Assert.Contains(sketch.Parts, part => part.Name == "MAX30102" && part.Type == "sensor");
        Assert.Contains(sketch.Wires, wire => wire.From == "esp32.GPIO21" && wire.To == "max30102.SDA");
        Assert.Contains(sketch.Wires, wire => wire.From == "esp32.GPIO22" && wire.To == "max30102.SCL");
    }

    [Fact]
    public async Task Engine_builds_the_heart_sensor_after_calling_the_model()
    {
        var handler = new ScriptedHandler("""{"kind":"heart-rate","title":"Heart rate monitor","summary":"The ESP32 reads the MAX30102 over I2C. SDA is GPIO21 and SCL is GPIO22."}""");
        using var http = new HttpClient(handler);
        var service = new SchematicSketchService(http, new LanguageModelOptions("http://model.test/v1/chat/completions", "gpt-5", ""));

        var sketch = await service.SketchAsync("Create a heart rate monitor using ESP32 and MAX30102.", CancellationToken.None);

        Assert.Equal("http://model.test/v1/chat/completions", handler.Request?.RequestUri?.ToString());
        Assert.Equal("Heart rate monitor", sketch.Title);
        Assert.Contains(sketch.Parts, part => part.Name == "ESP32" && part.Type == "mcu");
        Assert.Contains(sketch.Parts, part => part.Name == "MAX30102" && part.Type == "sensor");
        Assert.Contains("GPIO21", sketch.Summary);
        Assert.Contains("GPIO22", sketch.Summary);
    }

    [Fact]
    public void Engine_lays_out_the_parts_the_model_named()
    {
        const string reply = """
            {"title":"Divider","summary":"Two resistors.","parts":[{"id":"r1","name":"R1","type":"block","note":"10k"},{"id":"r2","name":"R2","type":"block"}],"wires":[{"from":"r1","to":"r2"}]}
            """;

        var sketch = SketchReplyParser.Parse(reply, "a divider");

        Assert.Equal(["r1", "r2"], sketch.Parts.Select(part => part.Id));
        Assert.Equal(new SketchWire("r1", "r2"), sketch.Wires[0]);
    }

    [Fact]
    public async Task Engine_calls_the_model_before_building_the_schema()
    {
        var handler = new ScriptedHandler("""{"kind":"ripple-counter","bits":8,"title":"8-bit ripple counter","summary":"T inputs tied high."}""");
        using var http = new HttpClient(handler);
        var service = new SchematicSketchService(http, new LanguageModelOptions("http://model.test/v1/chat/completions", "gpt-5", ""));

        var sketch = await service.SketchAsync("Create an 8-bit counter circuit diagram", CancellationToken.None);

        Assert.Equal("http://model.test/v1/chat/completions", handler.Request?.RequestUri?.ToString());
        Assert.Equal(8, sketch.Parts.Count(part => part.Type == "flipflop"));
    }

    [Fact]
    public void Saved_replies_keep_pins_and_named_supply_wires()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(RepliesFile()));
        var replies = document.RootElement.EnumerateArray().Select(item => item.GetProperty("reply").GetRawText()).ToArray();

        var oneBit = SketchReplyParser.Parse(replies[0], "1-bit full adder");
        Assert.Equal(["A", "B", "Cin", "Sum", "Cout"], oneBit.Parts[0].Pins);
        Assert.Empty(oneBit.Wires);

        var fourBit = SketchReplyParser.Parse(replies[1], "4-bit ripple-carry adder");
        Assert.Equal(
            [new SketchWire("fa0.Cout", "fa1.Cin"), new SketchWire("fa1.Cout", "fa2.Cin"), new SketchWire("fa2.Cout", "fa3.Cin")],
            fourBit.Wires);

        var led = SketchReplyParser.Parse(replies[2], "5 V LED");
        Assert.Equal("330 ohm", led.Parts.Single(part => part.Id == "r1").Note);
        Assert.Equal(["1", "2"], led.Parts.Single(part => part.Id == "r1").Pins);
        Assert.Equal(["Anode", "Cathode"], led.Parts.Single(part => part.Id == "led1").Pins);
        Assert.Equal(
            [new SketchWire("VCC", "r1.1"), new SketchWire("r1.2", "led1.Anode"), new SketchWire("led1.Cathode", "GND")],
            led.Wires);
    }

    [Fact]
    public void Unknown_pin_wires_are_skipped()
    {
        const string reply = """
            {"title":"Adder","summary":"One stage.","parts":[{"id":"fa0","name":"FA0","type":"full_adder","pins":["A","B"]}],"wires":[{"from":"fa0.A","to":"fa0.A"},{"from":"fa0.Nope","to":"fa0.B"}]}
            """;

        var sketch = SketchReplyParser.Parse(reply, "adder");

        Assert.Empty(sketch.Wires);
    }

    [Fact]
    public void Relay_driver_json_with_a_stray_wire_token_still_keeps_the_parts()
    {
        const string reply = """
            {"title":"ESP32-controlled relay driver","summary":"GPIO drives the coil.","parts":[{"id":"u1","name":"ESP32","type":"mcu"},{"id":"q1","name":"2N2222","type":"transistor"},{"id":"k1","name":"Relay","type":"relay"},{"id":"d1","name":"1N4148","type":"diode"},{"id":"r1","name":"Base resistor","type":"resistor","note":"1 kΩ"},{"id":"v5","name":"+5V","type":"supply"},{"id":"gnd","name":"GND","type":"ground"}],"wires":[{"from":"u1.GPIO23","to":"r1.1"},{"from":"r1.2","to":"q1.B"},{"from":"q1.E","to":"gnd"},{"from":"v5","+5V","to":"k1.A1"},{"from":"k1.A2","to":"q1.C"},{"from":"d1.K","to":"k1.A1"},{"from":"d1.A","to":"k1.A2"},{"from":"u1.GND","to":"gnd"}]}
            """;

        var sketch = SketchReplyParser.Parse(reply, "Create a relay driver controlled by an ESP32");

        Assert.Contains(sketch.Parts, part => part.Id == "u1");
        Assert.Contains(sketch.Wires, wire => wire.From == "u1.GPIO23" && wire.To == "r1.1");
        Assert.Contains(sketch.Wires, wire => wire.From == "v5" && wire.To == "k1.A1");
        Assert.Contains(sketch.Parts.Single(part => part.Id == "u1").Pins, pin => pin == "GPIO23");
    }

    [Fact]
    public async Task Saved_reply_is_used_instead_of_the_model()
    {
        var handler = new ScriptedHandler("should not be called");
        using var http = new HttpClient(handler);
        var service = new SchematicSketchService(
            http,
            new LanguageModelOptions("http://model.test/v1/chat/completions", "gpt-5", ""),
            new SavedReplyCatalog(RepliesFile()));

        var sketch = await service.SketchAsync("Can you make a 4-bit ripple-carry adder for me?", CancellationToken.None);

        Assert.Null(handler.Request);
        Assert.Equal(["fa0", "fa1", "fa2", "fa3"], sketch.Parts.Select(part => part.Id));
    }

    [Fact]
    public async Task Unmatched_sentence_still_calls_the_model()
    {
        var handler = new ScriptedHandler("""{"title":"Divider","summary":"Two resistors.","parts":[{"id":"r1","name":"R1","type":"block"}],"wires":[]}""");
        using var http = new HttpClient(handler);
        var service = new SchematicSketchService(
            http,
            new LanguageModelOptions("http://model.test/v1/chat/completions", "gpt-5", ""),
            new SavedReplyCatalog(RepliesFile()));

        var sketch = await service.SketchAsync("draw an unknown block", CancellationToken.None);

        Assert.Equal("http://model.test/v1/chat/completions", handler.Request?.RequestUri?.ToString());
        Assert.Equal("r1", sketch.Parts[0].Id);
        Assert.DoesNotContain("temperature", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Gpt5_request_omits_temperature()
    {
        var handler = new ScriptedHandler("""{"title":"Block","summary":"One block.","parts":[{"id":"r1","name":"R1","type":"block"}],"wires":[]}""");
        using var http = new HttpClient(handler);
        var service = new SchematicSketchService(
            http,
            new LanguageModelOptions("http://model.test/v1/chat/completions", "gpt-5", "sk-test"),
            new SavedReplyCatalog(RepliesFile()));

        await service.SketchAsync("draw an unknown block", CancellationToken.None);

        Assert.Contains("\"model\":\"gpt-5\"", handler.Body, StringComparison.Ordinal);
        Assert.Contains("\"reasoning_effort\":\"low\"", handler.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("temperature", handler.Body, StringComparison.Ordinal);
        Assert.Equal("Bearer", handler.Request?.Headers.Authorization?.Scheme);
        Assert.Equal("sk-test", handler.Request?.Headers.Authorization?.Parameter);
    }

    private static string RepliesFile()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "ElectronicsAI.Api", "model-replies.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException("model-replies.json");
    }

    private sealed class ScriptedHandler(string content) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            var body = JsonSerializer.Serialize(new
            {
                choices = new[] { new { message = new { content } } },
            });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
        }
    }
}
