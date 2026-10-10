using ElectronicsAI.Design;
using ElectronicsAI.Domain;
using ElectronicsAI.Simulation;
using ElectronicsAI.Validation;

namespace ElectronicsAI.Tests;

public class CircuitCompilerTests
{
    private readonly CircuitCompiler compiler = new();
    private readonly SpiceNetlistWriter writer = new();

    [Fact]
    public void Led_sketch_compiles_to_a_5_volt_netlist()
    {
        const string reply = """
            {"parts":[{"id":"r1","type":"resistor","value":"330 ohm","pins":["1","2"]},{"id":"led1","type":"LED","pins":["Anode","Cathode"]}],"wires":[{"from":"VCC","to":"r1.1"},{"from":"r1.2","to":"led1.Anode"},{"from":"led1.Cathode","to":"GND"}]}
            """;

        var circuit = compiler.Compile(SketchReplyParser.Parse(reply, "make a 5 V LED circuit"));
        var netlist = writer.Write(circuit);

        Assert.Contains("V1 vcc 0 DC 5", netlist, StringComparison.Ordinal);
        Assert.Contains("R1 vcc out 330", netlist, StringComparison.Ordinal);
        Assert.Contains("VLED1 out 0 DC 2", netlist, StringComparison.Ordinal);
        Assert.Contains("i(R1)", netlist, StringComparison.Ordinal);
    }

    [Fact]
    public void Esp32_push_button_led_is_checked_without_ngspice()
    {
        const string reply = """
            {"parts":[{"id":"u1","name":"ESP32","type":"block"},{"id":"sw1","name":"SW1","type":"block","note":"Push Button"},{"id":"r1","name":"R1","type":"block","note":"330 ohm"},{"id":"r2","name":"R2","type":"block","note":"10k ohm"},{"id":"d1","name":"D1","type":"block","note":"LED"}],"wires":[{"from":"u1.3V3","to":"3V3"},{"from":"u1.GND","to":"GND"},{"from":"u1.GPIO19","to":"r1"},{"from":"r1","to":"d1"},{"from":"d1","to":"GND"},{"from":"u1.GPIO18","to":"sw1"},{"from":"sw1","to":"GND"},{"from":"u1.GPIO18","to":"r2"},{"from":"r2","to":"3V3"}]}
            """;

        var sketch = SketchReplyParser.Parse(reply, "ESP32 push button LED");
        var match = new SketchRuleCatalog(SketchRules.All).Match(sketch, null);
        var result = match!.Evaluate(sketch, null);

        Assert.Equal(CircuitCategory.Embedded, match.Category);
        Assert.False(result.Simulate);
        Assert.All(result.Rules, rule => Assert.Equal("Pass", rule.Result));
        var layout = SchematicRouter.Place(sketch);
        Assert.False(SharesTrack(layout, "signal", "ground"));
        var pull = layout.Parts.Single(part => part.Id.Equals("r2", StringComparison.OrdinalIgnoreCase));
        var pullPins = pull.Pins.GroupBy(pin => pin.Name, StringComparer.OrdinalIgnoreCase).Select(group => group.First()).ToList();
        var supply = layout.Wires.Single(wire => wire.Role == "power" && (wire.From.StartsWith("r2.", StringComparison.OrdinalIgnoreCase) || wire.To.StartsWith("r2.", StringComparison.OrdinalIgnoreCase) || wire.From.Equals("r2", StringComparison.OrdinalIgnoreCase) || wire.To.Equals("r2", StringComparison.OrdinalIgnoreCase)));
        var supplyEnd = supply.Points[0];
        Assert.Contains(pullPins, pin => Math.Abs(pin.X - supplyEnd.X) < 1 && Math.Abs(pin.Y - supplyEnd.Y) < 1);
        Assert.Contains(pullPins, pin => Math.Abs(pin.X - supplyEnd.X) > 1 || Math.Abs(pin.Y - supplyEnd.Y) > 1);
        Assert.True(supply.Points.Min(point => point.Y) < pull.Y - pull.Height / 2);
        var led = layout.Parts.Single(part => part.Id.Equals("d1", StringComparison.OrdinalIgnoreCase));
        var cathode = led.Pins.First(pin => pin.Name.Equals("Cathode", StringComparison.OrdinalIgnoreCase));
        var ledGround = layout.Wires.Single(wire => wire.Role == "ground" && (wire.From.StartsWith("d1", StringComparison.OrdinalIgnoreCase) || wire.To.StartsWith("d1", StringComparison.OrdinalIgnoreCase)));
        Assert.True(Math.Abs(ledGround.Points[0].X - cathode.X) < 1 && Math.Abs(ledGround.Points[0].Y - cathode.Y) < 1);
    }

    [Fact]
    public void Esp32_lowercase_ground_token_still_routes()
    {
        const string reply = """
            {"parts":[{"id":"esp32","name":"ESP32","type":"ESP32 DevKit","pins":["3V3","GND","GPIO18","GPIO19"]},{"id":"button1","type":"push button","pins":["1","2"]},{"id":"r1","type":"resistor","value":"10k ohm","pins":["1","2"]},{"id":"led1","type":"LED","pins":["Anode","Cathode"]},{"id":"r2","type":"resistor","value":"330 ohm","pins":["1","2"]}],"wires":[{"from":"esp32.GPIO18","to":"button1.1"},{"from":"button1.2","to":"gnd"},{"from":"esp32.GPIO18","to":"r1.1"},{"from":"r1.2","to":"3V3"},{"from":"esp32.GPIO19","to":"r2.1"},{"from":"r2.2","to":"led1.Anode"},{"from":"led1.Cathode","to":"gnd"},{"from":"esp32.3V3","to":"3V3"},{"from":"esp32.GND","to":"gnd"},{"from":"3V3","to":"gnd"}]}
            """;

        var sketch = SketchReplyParser.Parse(reply, "Create an ESP32 connected to a push button and LED");
        var layout = SchematicRouter.Place(sketch);
        Assert.NotEmpty(layout.Wires);
    }

    [Fact]
    public void Esp32_pull_down_on_the_controller_pins_passes()
    {
        const string reply = """
            {"parts":[{"id":"esp32","name":"ESP32","type":"block"},{"id":"button1","name":"SW1","type":"block","note":"Push Button"},{"id":"r1","name":"Rpull","type":"block","note":"10k ohm"},{"id":"r2","name":"Rseries","type":"block","note":"330 ohm"},{"id":"led1","name":"LED1","type":"block","note":"LED"}],"wires":[{"from":"esp32.GPIO18","to":"button1"},{"from":"button1","to":"esp32.3V3"},{"from":"esp32.GPIO18","to":"r1"},{"from":"r1","to":"esp32.GND"},{"from":"esp32.GPIO19","to":"r2"},{"from":"r2","to":"led1"},{"from":"led1","to":"esp32.GND"}]}
            """;

        var sketch = SketchReplyParser.Parse(reply, "Create an ESP32 connected to a push button and LED");
        var match = new SketchRuleCatalog(SketchRules.All).Match(sketch, null);
        var result = match!.Evaluate(sketch, null);

        Assert.Equal(CircuitCategory.Embedded, match.Category);
        Assert.All(result.Rules, rule => Assert.True(rule.Result == "Pass", $"{rule.Name}: {rule.Detail}"));
        Assert.False(SharesTrack(SchematicRouter.Place(sketch), "signal", "ground"));
        Assert.False(SharesTrack(SchematicRouter.Place(sketch), "power", "ground"));
        var y = SchematicRouter.Place(sketch).Parts.ToDictionary(part => part.Id, part => part.Y, StringComparer.OrdinalIgnoreCase);
        Assert.NotEqual(y["button1"], y["r1"]);
        Assert.NotEqual(y["r1"], y["r2"]);
    }

    [Fact]
    public void Esp32_button_pull_down_and_series_led_are_separate_branches()
    {
        const string reply = """
            {"parts":[{"id":"esp32","name":"ESP32","type":"block"},{"id":"button1","name":"SW1","type":"block","note":"Push Button"},{"id":"r1","name":"Rpull","type":"block","note":"10k ohm"},{"id":"r2","name":"Rseries","type":"block","note":"330 ohm"},{"id":"led1","name":"LED1","type":"block","note":"LED"},{"id":"rail3","name":"3V3","type":"block"}],"wires":[{"from":"esp32.3V3","to":"3V3"},{"from":"esp32.GND","to":"GND"},{"from":"esp32.GPIO18","to":"button1"},{"from":"button1","to":"3V3"},{"from":"esp32.GPIO18","to":"r1"},{"from":"r1","to":"GND"},{"from":"esp32.GPIO18","to":"r2"},{"from":"r2","to":"led1"},{"from":"led1","to":"GND"}]}
            """;

        var sketch = SketchReplyParser.Parse(reply, "Create an ESP32 connected to a push button and LED");
        var result = new SketchRuleCatalog(SketchRules.All).Match(sketch, null)!.Evaluate(sketch, null);
        Assert.All(result.Rules, rule => Assert.True(rule.Result == "Pass", $"{rule.Name}: {rule.Detail}"));
        var layout = SchematicRouter.Place(sketch);
        Assert.DoesNotContain(layout.Parts, part => part.Id.Equals("rail3", StringComparison.OrdinalIgnoreCase));
        var y = layout.Parts.ToDictionary(part => part.Id, part => part.Y, StringComparer.OrdinalIgnoreCase);
        Assert.NotEqual(y["button1"], y["r1"]);
        Assert.NotEqual(y["r1"], y["r2"]);
        Assert.Equal(y["r2"], y["led1"]);
    }

    [Fact]
    public void Esp32_series_chain_does_not_pass_as_a_button_and_led()
    {
        const string reply = """
            {"parts":[{"id":"esp32","name":"ESP32","type":"block","pins":["3V3","GND","GPIO5","GPIO18"]},{"id":"sw","name":"Push Button","type":"block","note":"Push Button","pins":["1","2"]},{"id":"led","name":"LED","type":"block","note":"LED","pins":["A","K"]},{"id":"r330","name":"Series Resistor","type":"block","note":"330 ohm","pins":["1","2"]},{"id":"r10","name":"Pull Resistor","type":"block","note":"10k ohm","pins":["1","2"]}],"wires":[{"from":"esp32.3V3","to":"3V3"},{"from":"esp32.GND","to":"GND"},{"from":"esp32.GPIO18","to":"sw.1"},{"from":"sw.2","to":"led.A"},{"from":"sw.2","to":"GND"},{"from":"led.K","to":"GND"},{"from":"led.K","to":"r330.1"},{"from":"r330.2","to":"r10.1"},{"from":"r10.2","to":"3V3"},{"from":"esp32.GPIO5","to":"r330.1"}]}
            """;

        var sketch = SketchReplyParser.Parse(reply, "Create an ESP32 connected to a push button and LED");
        var result = new SketchRuleCatalog(SketchRules.All).Match(sketch, null)!.Evaluate(sketch, null);

        Assert.Contains(result.Rules, rule => rule.Name == "GPIO input detected" && rule.Result == "Fail");
        Assert.Contains(result.Rules, rule => rule.Name == "GPIO output detected" && rule.Result == "Fail");
        Assert.Contains(result.Rules, rule => rule.Name == "Pull resistor present" && rule.Result == "Fail");
    }

    [Fact]
    public void Pinless_push_button_led_is_in_series_from_5v_to_ground()
    {
        const string reply = """
            {"parts":[{"id":"sw1","name":"SW1","type":"block","note":"Push Button"},{"id":"r1","name":"R1","type":"block","note":"330 ohm"},{"id":"d1","name":"D1","type":"block","note":"LED"}],"wires":[{"from":"+5V","to":"sw1"},{"from":"sw1","to":"r1"},{"from":"r1","to":"d1"},{"from":"d1","to":"GND"}]}
            """;

        var result = new LedCircuitValidator().Evaluate(SketchReplyParser.Parse(reply, "push button LED"), null);

        Assert.True(result.Simulate);
        Assert.All(result.Rules, rule => Assert.Equal("Pass", rule.Result));
        var layout = SchematicRouter.Place(SketchReplyParser.Parse(reply, "push button LED"));
        Assert.Contains(layout.Wires, wire => wire.Role == "power" && (wire.From.Equals("+5V", StringComparison.OrdinalIgnoreCase) || wire.To.Equals("+5V", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void Block_typed_push_button_led_compiles_as_a_closed_switch()
    {
        const string reply = """
            {"title":"Push Button + LED","parts":[{"id":"v1","name":"V1","type":"block","note":"5V supply"},{"id":"s1","name":"S1","type":"block","note":"normally-open push button"},{"id":"r1","name":"R1","type":"block","note":"330 ohm"},{"id":"d1","name":"D1","type":"block","note":"LED anode"}],"wires":[{"from":"v1","to":"s1"},{"from":"s1","to":"r1"},{"from":"r1","to":"d1"},{"from":"d1","to":"GND"}]}
            """;

        var circuit = new CircuitEngine(compiler).Compile(SketchReplyParser.Parse(reply, "Push Button + LED"));

        Assert.Contains(circuit.Components, component => component is Led);
        Assert.Contains(circuit.Components, component => component is Resistor);
        Assert.DoesNotContain(circuit.Components, component => component.Id == "s1");
    }

    [Fact]
    public void Series_led_passes_validation_before_simulation()
    {
        const string reply = """
            {"parts":[{"id":"r1","type":"resistor","value":"330 ohm","pins":["1","2"]},{"id":"led1","type":"LED","pins":["Anode","Cathode"]}],"wires":[{"from":"VCC","to":"r1.1"},{"from":"r1.2","to":"led1.Anode"},{"from":"led1.Cathode","to":"GND"}]}
            """;

        var sketch = SketchReplyParser.Parse(reply, "5 V LED");
        var result = new LedCircuitValidator().Evaluate(sketch, null);

        Assert.True(result.Simulate);
        Assert.All(result.Rules, rule => Assert.Equal("Pass", rule.Result));
        Assert.Contains(result.Rules, rule => rule.Name == "Resistor present");
        Assert.Contains(result.Rules, rule => rule.Name == "Correct wiring");
        Assert.Contains(result.Rules, rule => rule.Name == "Power source present");
        Assert.Contains(result.Rules, rule => rule.Name == "Ground present");
    }

    [Fact]
    public void Led_without_a_resistor_fails_validation()
    {
        const string reply = """
            {"parts":[{"id":"led1","type":"LED","pins":["Anode","Cathode"]}],"wires":[{"from":"VCC","to":"led1.Anode"},{"from":"led1.Cathode","to":"GND"}]}
            """;

        var rules = new LedCircuitValidator().Evaluate(SketchReplyParser.Parse(reply, "LED on 5 V"), null).Rules;

        Assert.Equal("Fail", rules.Single(rule => rule.Name == "Resistor present").Result);
        Assert.Equal("Fail", rules.Single(rule => rule.Name == "Correct wiring").Result);
        Assert.Equal("Pass", rules.Single(rule => rule.Name == "Power source present").Result);
        Assert.Equal("Pass", rules.Single(rule => rule.Name == "Ground present").Result);
    }

    [Fact]
    public void Adder_sketch_has_no_spice_model()
    {
        const string reply = """
            {"parts":[{"id":"fa0","type":"1-bit full adder","pins":["A","B","Cin","Sum","Cout"]}],"wires":[]}
            """;

        var error = Assert.Throws<CircuitCompileException>(() => compiler.Compile(SketchReplyParser.Parse(reply, "1-bit full adder")));

        Assert.StartsWith("This schematic has no ngspice model yet.", error.Message);
    }

    [Fact]
    public void Ngspice_checks_the_led_current_and_resistor_power()
    {
        const string reply = """
            {"parts":[{"id":"r1","type":"resistor","value":"330 ohm","pins":["1","2"]},{"id":"led1","type":"LED","pins":["Anode","Cathode"]}],"wires":[{"from":"VCC","to":"r1.1"},{"from":"r1.2","to":"led1.Anode"},{"from":"led1.Cathode","to":"GND"}]}
            """;
        var circuit = compiler.Compile(SketchReplyParser.Parse(reply, "5 V LED"));
        var result = new NgspiceRunner().Run(writer.Write(circuit), circuit);
        if (!NgspiceRunner.IsAvailable())
        {
            Assert.False(result.Available);
            return;
        }

        Assert.True(result.Available, result.Reason);
        var expected = (5d - 2d) / 330d;
        Assert.InRange(result.OperatingPoint!.LoadCurrentAmps, expected * 0.9, expected * 1.1);
        var checks = SketchCircuitChecks.Evaluate(circuit, result.OperatingPoint, null);
        Assert.Contains(checks, check => check.Name == "LED current" && check.Result == CheckStatus.Pass);
        Assert.Contains(checks, check => check.Name == "Resistor power" && check.Result == CheckStatus.Pass);
    }

    [Fact]
    public void Heart_rate_board_passes_wiring_rules_without_spice()
    {
        var sketch = new SchematicSketch(
            "Heart rate monitor",
            "The ESP32 reads the MAX30102 over I2C at 3.3 V.",
            [
                new SketchPart("esp32", "ESP32", "mcu", null, ["3V3", "GND", "GPIO21", "GPIO22"]),
                new SketchPart("max30102", "MAX30102", "sensor", null, ["VIN", "GND", "SDA", "SCL"]),
                new SketchPart("rsda", "R4.7k", "resistor", "4.7k", ["1", "2"]),
                new SketchPart("rscl", "R4.7k", "resistor", "4.7k", ["1", "2"]),
            ],
            [
                new SketchWire("esp32.3V3", "max30102.VIN"),
                new SketchWire("esp32.GND", "max30102.GND"),
                new SketchWire("esp32.GPIO21", "max30102.SDA"),
                new SketchWire("esp32.GPIO22", "max30102.SCL"),
                new SketchWire("esp32.3V3", "rsda.1"),
                new SketchWire("rsda.2", "max30102.SDA"),
                new SketchWire("esp32.3V3", "rscl.1"),
                new SketchWire("rscl.2", "max30102.SCL"),
            ],
            null);
        var rules = ModuleSketchRules.Evaluate(sketch);

        Assert.True(ModuleSketchRules.Applies(sketch));
        Assert.All(rules, rule => Assert.Equal("Pass", rule.Result));
        Assert.Contains(rules, rule => rule.Name == "SDA pull-up");
        Assert.Contains(rules, rule => rule.Name == "Voltage compatible");
    }

    [Fact]
    public void Five_volts_tied_to_3v3_fails_the_voltage_rule()
    {
        var sketch = new SchematicSketch(
            "Board",
            "ESP32 and a sensor.",
            [
                new SketchPart("esp32", "ESP32", "mcu", null, ["5V", "GND", "GPIO21", "GPIO22"]),
                new SketchPart("max30102", "MAX30102", "sensor", null, ["3V3", "GND", "SDA", "SCL"]),
            ],
            [
                new SketchWire("esp32.5V", "max30102.3V3"),
                new SketchWire("esp32.GND", "max30102.GND"),
                new SketchWire("esp32.GPIO21", "max30102.SDA"),
                new SketchWire("esp32.GPIO22", "max30102.SCL"),
            ],
            null);

        var voltage = ModuleSketchRules.Evaluate(sketch).Single(rule => rule.Name == "Voltage compatible");

        Assert.Equal("Fail", voltage.Result);
    }

    [Fact]
    public void Divider_sketch_compiles_to_a_midpoint_netlist()
    {
        const string reply = """
            {"parts":[{"id":"r1","type":"resistor","value":"10k","pins":["1","2"]},{"id":"r2","type":"resistor","value":"10k","pins":["1","2"]}],"wires":[{"from":"VCC","to":"r1.1"},{"from":"r1.2","to":"r2.1"},{"from":"r2.2","to":"GND"}]}
            """;

        var circuit = compiler.Compile(SketchReplyParser.Parse(reply, "voltage divider"));
        var netlist = writer.Write(circuit);

        Assert.Contains("V1 vcc 0 DC 5", netlist, StringComparison.Ordinal);
        Assert.Contains("R1 vcc out 10k", netlist, StringComparison.Ordinal);
        Assert.Contains("R2 out 0 10k", netlist, StringComparison.Ordinal);
        Assert.Contains("v(out)", netlist, StringComparison.Ordinal);
    }

    [Fact]
    public void Ngspice_checks_the_divider_voltage()
    {
        const string reply = """
            {"parts":[{"id":"r1","type":"resistor","value":"10k","pins":["1","2"]},{"id":"r2","type":"resistor","value":"10k","pins":["1","2"]}],"wires":[{"from":"VCC","to":"r1.1"},{"from":"r1.2","to":"r2.1"},{"from":"r2.2","to":"GND"}]}
            """;
        var circuit = compiler.Compile(SketchReplyParser.Parse(reply, "voltage divider"));
        var result = new NgspiceRunner().Run(writer.Write(circuit), circuit);
        if (!NgspiceRunner.IsAvailable())
        {
            Assert.False(result.Available);
            return;
        }

        Assert.True(result.Available, result.Reason);
        Assert.InRange(result.OperatingPoint!.OutputVolts, 2.25, 2.75);
        var checks = SketchCircuitChecks.Evaluate(circuit, result.OperatingPoint, null);
        Assert.Contains(checks, check => check.Name == "Divider voltage" && check.Result == CheckStatus.Pass);
    }

    [Fact]
    public void Divider_missing_the_bottom_resistor_fails()
    {
        const string reply = """
            {"parts":[{"id":"r1","type":"resistor","value":"10k","pins":["1","2"]}],"wires":[{"from":"VCC","to":"r1.1"},{"from":"r1.2","to":"GND"}]}
            """;

        var rule = AnalogSketchRules.MissingBottomResistor(SketchReplyParser.Parse(reply, "divider with the bottom resistor missing"));

        Assert.NotNull(rule);
        Assert.Equal("Fail", rule.Result);
        Assert.Equal("Missing the bottom resistor.", rule.Detail);
    }

    [Fact]
    public void Divider_with_both_resistors_is_not_flagged()
    {
        const string reply = """
            {"parts":[{"id":"r1","type":"resistor","value":"10k","pins":["1","2"]},{"id":"r2","type":"resistor","value":"10k","pins":["1","2"]}],"wires":[{"from":"VCC","to":"r1.1"},{"from":"r1.2","to":"r2.1"},{"from":"r2.2","to":"GND"}]}
            """;

        Assert.Null(AnalogSketchRules.MissingBottomResistor(SketchReplyParser.Parse(reply, "voltage divider")));
    }

    [Fact]
    public void Five_volts_on_the_max30102_fails_voltage_compatible()
    {
        const string reply = """
            {"parts":[{"id":"esp32","name":"ESP32","type":"mcu","pins":["5V","GND","GPIO21","GPIO22"]},{"id":"max30102","name":"MAX30102","type":"sensor","pins":["3V3","GND","SDA","SCL"]},{"id":"rsda","type":"resistor","value":"4.7k","pins":["1","2"]},{"id":"rscl","type":"resistor","value":"4.7k","pins":["1","2"]}],"wires":[{"from":"esp32.5V","to":"max30102.3V3"},{"from":"esp32.GND","to":"max30102.GND"},{"from":"esp32.GPIO21","to":"max30102.SDA"},{"from":"esp32.GPIO22","to":"max30102.SCL"},{"from":"esp32.5V","to":"rsda.1"},{"from":"rsda.2","to":"max30102.SDA"},{"from":"esp32.5V","to":"rscl.1"},{"from":"rscl.2","to":"max30102.SCL"}]}
            """;

        var rules = ModuleSketchRules.Evaluate(SketchReplyParser.Parse(reply, "5 V tied to the MAX30102"));
        var voltage = rules.Single(rule => rule.Name == "Voltage compatible");

        Assert.Equal("Fail", voltage.Result);
        Assert.Equal("5 V and 3.3 V are tied together.", voltage.Detail);
        Assert.All(rules.Where(rule => rule.Name != "Voltage compatible"), rule => Assert.Equal("Pass", rule.Result));
    }

    [Fact]
    public void Sensor_supply_above_its_max_voltage_fails()
    {
        const string reply = """
            {"parts":[{"id":"esp32","name":"ESP32","type":"mcu","pins":["5V","GND","GPIO21","GPIO22"]},{"id":"max30102","name":"MAX30102","type":"sensor","pins":["VIN","GND","SDA","SCL"]}],"wires":[{"from":"esp32.5V","to":"max30102.VIN"},{"from":"esp32.GND","to":"max30102.GND"},{"from":"esp32.GPIO21","to":"max30102.SDA"},{"from":"esp32.GPIO22","to":"max30102.SCL"}]}
            """;

        var voltage = ModuleSketchRules.Evaluate(SketchReplyParser.Parse(reply, "5 V on the MAX30102")).Single(rule => rule.Name == "Voltage compatible");

        Assert.Equal("Fail", voltage.Result);
        Assert.Equal("MAX30102 allows 3.3 V.", voltage.Detail);
    }

    [Fact]
    public void Source_node_and_ground_parts_compile_as_a_divider()
    {
        const string reply = """
            {"title":"5V to 1.67V divider","parts":[{"id":"v1","name":"V1","type":"vsource","note":"5 V"},{"id":"r1","name":"R1","type":"resistor","note":"20 kΩ (top)"},{"id":"r2","name":"R2","type":"resistor","note":"10 kΩ (bottom)"},{"id":"gnd","name":"GND","type":"ground"},{"id":"vout","name":"Vout","type":"node","note":"≈1.67 V"}],"wires":[{"from":"v1","to":"r1"},{"from":"r1","to":"vout"},{"from":"vout","to":"r2"},{"from":"r2","to":"gnd"},{"from":"v1","to":"gnd"}]}
            """;

        var sketch = SketchReplyParser.Parse(reply, "20k over 10k from 5 volts");
        var circuit = new CircuitEngine(compiler).Compile(sketch);
        var netlist = writer.Write(circuit);
        var assumptions = SketchCircuitChecks.Assumptions(circuit);

        Assert.Contains("V1 vcc 0 DC 5", netlist, StringComparison.Ordinal);
        Assert.Contains("R1 vcc out 20k", netlist, StringComparison.Ordinal);
        Assert.Contains("R2 out 0 10k", netlist, StringComparison.Ordinal);
        Assert.Contains("Source = 5 V", assumptions);
        Assert.Contains("Top resistor = 20 kΩ", assumptions);
        Assert.Contains("Bottom resistor = 10 kΩ", assumptions);
        Assert.Contains("Expected Vout = 1.67 V", assumptions);
        var layout = SchematicRouter.Place(sketch);
        var placed = layout.Parts.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(placed["v1"].X, placed["r1"].X);
        Assert.Equal(placed["r1"].X, placed["vout"].X);
        Assert.Equal(placed["vout"].X, placed["r2"].X);
        Assert.Equal(placed["r2"].X, placed["gnd"].X);
        Assert.True(placed["v1"].Y < placed["r1"].Y && placed["r1"].Y < placed["vout"].Y && placed["vout"].Y < placed["r2"].Y && placed["r2"].Y < placed["gnd"].Y);
        foreach (var wire in layout.Wires.Where(item => !Touches(item, "v1") || !Touches(item, "gnd")))
        {
            Assert.All(wire.Points, point => Assert.Equal(placed["r1"].X, point.X));
        }
    }

    [Fact]
    public void Relay_driver_checks_pass_for_a_gpio_transistor_and_flyback_diode()
    {
        const string reply = """
            {"title":"ESP32-controlled relay driver","parts":[{"id":"u1","name":"ESP32","type":"mcu"},{"id":"q1","name":"2N2222","type":"transistor"},{"id":"k1","name":"Relay","type":"relay"},{"id":"d1","name":"1N4148","type":"diode"},{"id":"r1","name":"Base resistor","type":"resistor","note":"1 kΩ"},{"id":"r3","name":"LED series","type":"resistor","note":"1 kΩ"},{"id":"led1","name":"Indicator","type":"led"},{"id":"v5","name":"+5V","type":"supply"},{"id":"gnd","name":"GND","type":"ground"}],"wires":[{"from":"u1.GPIO23","to":"r1.1"},{"from":"r1.2","to":"q1.B"},{"from":"q1.E","to":"gnd"},{"from":"v5","to":"k1.A1"},{"from":"k1.A2","to":"q1.C"},{"from":"d1.K","to":"k1.A1"},{"from":"d1.A","to":"k1.A2"},{"from":"v5","to":"r3.1"},{"from":"r3.2","to":"led1.A"},{"from":"led1.K","to":"q1.C"},{"from":"u1.GND","to":"gnd"}]}
            """;

        var rules = RelayDriverRules.Evaluate(SketchReplyParser.Parse(reply, "Create a relay driver controlled by an ESP32"));

        Assert.All(rules, rule => Assert.Equal("Pass", rule.Result));
        Assert.Contains(rules, rule => rule.Name == "Flyback diode");
        Assert.Contains(rules, rule => rule.Name == "Base resistor");
    }

    [Fact]
    public void Motor_driver_is_not_judged_as_a_sensor_board()
    {
        const string reply = """
            {"title":"ESP32 low-side motor driver","parts":[{"id":"u1","name":"ESP32","type":"mcu"},{"id":"q1","name":"N-MOSFET","type":"mosfet"},{"id":"m1","name":"Motor","type":"motor"},{"id":"d1","name":"Flyback","type":"diode"},{"id":"rg","name":"Gate resistor","type":"resistor","note":"100 Ω"},{"id":"rpd","name":"Gate pulldown","type":"resistor","note":"100 kΩ"},{"id":"vmot","name":"Motor supply","type":"supply","note":"12 V"},{"id":"gnd","name":"GND","type":"ground"}],"wires":[{"from":"u1.GPIO18","to":"rg.1"},{"from":"rg.2","to":"q1.G"},{"from":"q1.G","to":"rpd.1"},{"from":"rpd.2","to":"gnd"},{"from":"q1.S","to":"gnd"},{"from":"vmot","to":"m1.1"},{"from":"m1.2","to":"q1.D"},{"from":"d1.K","to":"m1.1"},{"from":"d1.A","to":"q1.D"},{"from":"u1.GND","to":"gnd"}]}
            """;

        var sketch = SketchReplyParser.Parse(reply, "ESP32 PWM motor driver");
        var rules = MotorDriverRules.Evaluate(sketch);

        Assert.Equal(CircuitCategory.MotorDriver, CircuitCategories.Of(sketch));
        Assert.False(ModuleSketchRules.Applies(sketch));
        Assert.All(rules, rule => Assert.Equal("Pass", rule.Result));
        Assert.DoesNotContain(rules, rule => rule.Detail.Contains("controller and a module", StringComparison.Ordinal));
    }

    [Fact]
    public void Tb6612_motor_driver_is_not_scored_as_a_low_pass_filter()
    {
        const string reply = """
            {"title":"ESP32 DC motor driver (TB6612FNG)","parts":[{"id":"u1","name":"ESP32","type":"mcu"},{"id":"u2","name":"TB6612FNG","type":"driver"},{"id":"m1","name":"DC Motor","type":"motor"},{"id":"rstby","name":"R_STBY","type":"resistor","note":"10 kΩ"},{"id":"cdec","name":"Cdec","type":"capacitor","note":"100 nF"},{"id":"cbulk","name":"Cbulk","type":"capacitor","note":"470 uF"},{"id":"v3","name":"3V3","type":"supply"},{"id":"vm","name":"VM","type":"supply"},{"id":"gnd","name":"GND","type":"ground"}],"wires":[{"from":"v3","to":"u2.VCC"},{"from":"v3","to":"rstby.1"},{"from":"rstby.2","to":"u2.STBY"},{"from":"u1.GPIO18","to":"u2.PWMA"},{"from":"u1.GPIO19","to":"u2.AIN1"},{"from":"u1.GPIO21","to":"u2.AIN2"},{"from":"vm","to":"u2.VM"},{"from":"u2.AO1","to":"m1.1"},{"from":"u2.AO2","to":"m1.2"},{"from":"u2.GND","to":"gnd"},{"from":"u1.GND","to":"gnd"},{"from":"cdec.1","to":"u2.VCC"},{"from":"cdec.2","to":"gnd"},{"from":"cbulk.1","to":"u2.VM"},{"from":"cbulk.2","to":"gnd"}]}
            """;

        var sketch = SketchReplyParser.Parse(reply, "ESP32 DC motor driver with a TB6612FNG");
        var rules = MotorDriverRules.Evaluate(sketch);

        Assert.Equal(CircuitCategory.MotorDriver, CircuitCategories.Of(sketch));
        Assert.All(rules, rule => Assert.Equal("Pass", rule.Result));
        Assert.Contains(rules, rule => rule.Name == "Standby pulled high");
        Assert.DoesNotContain(rules, rule => rule.Name == "Cutoff frequency");
    }

    [Fact]
    public void A_reply_without_a_transistor_keeps_the_model_sketch()
    {
        const string reply = """
            {"title":"generate a simple transistor and led circuit","parts":[{"id":"r1","name":"r1","type":"resistor","note":"330 ohm"},{"id":"led1","name":"led1","type":"LED","pins":["Anode","Cathode"]}],"wires":[{"from":"VCC","to":"r1.1"},{"from":"r1.2","to":"led1.Anode"},{"from":"led1.Cathode","to":"GND"}]}
            """;

        var sketch = SketchReplyParser.Parse(reply, "generate a simple transistor and led circuit");

        Assert.DoesNotContain(sketch.Parts, part => part.Type == "npn");
        Assert.Contains(sketch.Parts, part => part.Id == "led1");
    }

    [Fact]
    public void A_divider_node_that_says_3_33_volts_fails_against_1_67()
    {
        const string reply = """
            {"title":"Divider","parts":[{"id":"vout","name":"Vout","type":"node","note":"~3.33 V"},{"id":"r1","name":"R1","type":"resistor","note":"20 kΩ"}],"wires":[]}
            """;

        var rule = SketchVoltageNote.Misstated(SketchReplyParser.Parse(reply, "20k over 10k from 5 volts"), 1.67);

        Assert.NotNull(rule);
        Assert.Equal("Fail", rule.Result);
        Assert.Equal("Vout says 3.33 V. The output is 1.67 V.", rule.Detail);
    }

    [Fact]
    public void Low_side_led_driver_is_not_scored_as_a_gpio_led()
    {
        const string reply = """
            {"title":"Low-side LED","parts":[{"id":"u1","name":"ESP32","type":"mcu","pins":["3V3","GND","GPIO18"]},{"id":"rled","name":"Series resistor","type":"resistor","note":"330 ohm"},{"id":"d1","name":"LED","type":"led","pins":["Anode","Cathode"]},{"id":"q1","name":"N-MOSFET","type":"mosfet","pins":["Gate","Drain","Source"]},{"id":"rg","name":"Gate resistor","type":"resistor","note":"100 ohm"}],"wires":[{"from":"u1.3V3","to":"rled.1"},{"from":"rled.2","to":"d1.Anode"},{"from":"d1.Cathode","to":"q1.Drain"},{"from":"q1.Source","to":"GND"},{"from":"u1.GND","to":"GND"},{"from":"u1.GPIO18","to":"rg.1"},{"from":"rg.2","to":"q1.Gate"}]}
            """;

        var sketch = SketchReplyParser.Parse(reply, "low-side LED driver");
        var match = new SketchRuleCatalog(SketchRules.All).Match(sketch, null)!;
        var result = match.Evaluate(sketch, null);

        Assert.Equal(CircuitCategory.LowSideLed, CircuitCategories.Of(sketch));
        Assert.Equal(CircuitCategory.LowSideLed, match.Category);
        Assert.DoesNotContain(result.Rules, rule => rule.Name == "GPIO output detected");
        Assert.All(result.Rules, rule => Assert.True(rule.Result == "Pass", $"{rule.Name}: {rule.Detail}"));
    }

    [Fact]
    public void Controller_and_unnamed_mosfet_leads_stay_a_low_side_led_driver()
    {
        const string reply = """
            {"title":"Low-side LED driver","parts":[{"id":"u1","name":"controller","type":"controller"},{"id":"q1","name":"Q1","type":"n-mosfet"},{"id":"d1","name":"LED","type":"led"},{"id":"r1","name":"Rseries","type":"resistor","note":"330 ohm"},{"id":"r2","name":"Gate Resistor","type":"resistor","note":"100 ohm"}],"wires":[{"from":"u1.3V3","to":"r1.1"},{"from":"r1.2","to":"d1.A"},{"from":"d1.K","to":"q1"},{"from":"q1","to":"GND"},{"from":"u1.GPIO","to":"r2.1"},{"from":"r2.2","to":"q1"},{"from":"u1.GND","to":"GND"}]}
            """;

        var sketch = SketchReplyParser.Parse(reply, "low-side LED driver");
        var match = new SketchRuleCatalog(SketchRules.All).Match(sketch, null)!;

        Assert.Equal(CircuitCategory.LowSideLed, match.Category);
        Assert.False(LogicFamily.Matches(sketch));
        Assert.All(match.Evaluate(sketch, null).Rules, rule => Assert.True(rule.Result == "Pass", $"{rule.Name}: {rule.Detail}"));
    }

    [Fact]
    public void Low_side_driver_accepts_an_unnamed_gpio_a_voltage_source_and_a_separate_ground()
    {
        const string reply = """
            {"title":"Low-side LED driver (gpio-transistor)","parts":[{"id":"u1","name":"ESP32","type":"controller"},{"id":"r1","name":"Rseries","type":"resistor","note":"330 ohm"},{"id":"d1","name":"LED","type":"led"},{"id":"q1","name":"Q1","type":"n-mosfet"},{"id":"r2","name":"Rgate","type":"resistor","note":"gate resistor"},{"id":"v1","name":"3.3V","type":"voltage","note":"3.3V"},{"id":"gnd","name":"GND","type":"ground"}],"wires":[{"from":"v1","to":"r1"},{"from":"r1","to":"d1.anode"},{"from":"d1.cathode","to":"q1.drain"},{"from":"q1.source","to":"GND"},{"from":"u1","to":"r2"},{"from":"r2","to":"q1.gate"},{"from":"u1.GND","to":"gnd"},{"from":"u1.3.3V","to":"v1"}]}
            """;

        var rules = LowSideLedFamily.Pattern.Evaluate(SketchReplyParser.Parse(reply, "low-side LED driver"), null).Rules;

        Assert.All(rules, rule => Assert.True(rule.Result == "Pass", $"{rule.Name}: {rule.Detail}"));
    }

    [Fact]
    public void Low_side_driver_accepts_an_out_pin_and_a_five_volt_rail()
    {
        const string reply = """
            {"title":"Low-side LED driver","parts":[{"id":"u1","name":"Controller","type":"controller"},{"id":"q1","name":"Q1","type":"n-mosfet"},{"id":"d1","name":"LED1","type":"led"},{"id":"r1","name":"Rseries","type":"resistor","note":"330 ohm"},{"id":"r2","name":"Rgate","type":"resistor","note":"100 ohm"}],"wires":[{"from":"+5V","to":"r1"},{"from":"r1","to":"d1"},{"from":"d1","to":"q1.D"},{"from":"q1.S","to":"GND"},{"from":"u1.OUT","to":"r2"},{"from":"r2","to":"q1.G"},{"from":"u1.VSS","to":"GND"}]}
            """;

        var rules = LowSideLedFamily.Pattern.Evaluate(SketchReplyParser.Parse(reply, "low-side LED driver"), null).Rules;

        Assert.All(rules, rule => Assert.True(rule.Result == "Pass", $"{rule.Name}: {rule.Detail}"));
    }

    [Fact]
    public void Low_side_led_keeps_the_load_on_the_center_line_and_the_gate_trace_on_the_left()
    {
        const string reply = """
            {"title":"Low-side LED driver","parts":[{"id":"q1","name":"Q1","type":"n-mosfet"},{"id":"d1","name":"LED","type":"led"},{"id":"r2","name":"Rgate","type":"resistor","note":"gate resistor"},{"id":"u1","name":"ESP32","type":"controller"},{"id":"r1","name":"Rseries","type":"resistor","note":"330 ohm"},{"id":"gnd","name":"GND","type":"ground"}],"wires":[{"from":"u1.3V3","to":"r1"},{"from":"r1","to":"d1.anode"},{"from":"d1.cathode","to":"q1.drain"},{"from":"q1.source","to":"gnd"},{"from":"u1.GPIO","to":"r2"},{"from":"r2","to":"q1.gate"},{"from":"u1.GND","to":"gnd"}]}
            """;

        var layout = SchematicRouter.Place(SketchReplyParser.Parse(reply, "low-side LED driver"));
        var part = layout.Parts.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);

        Assert.Equal(part["r1"].X, part["d1"].X);
        Assert.Equal(part["d1"].X, part["q1"].X);
        Assert.Equal(part["q1"].X, part["gnd"].X);
        Assert.True(part["r1"].X - part["r1"].Width / 2 - (part["u1"].X + part["u1"].Width / 2) >= 140);
        Assert.True(part["r1"].Y < part["d1"].Y && part["d1"].Y < part["q1"].Y && part["q1"].Y < part["gnd"].Y);
        Assert.True(part["r2"].X < part["q1"].X - part["q1"].Width / 2);
        Assert.True(part["u1"].Y < part["r2"].Y);
        Assert.Equal("left", part["q1"].Pins.Single(pin => pin.Name == "Gate").Side);
        Assert.Equal("bottom", part["u1"].Pins.Single(pin => pin.Name.Equals("GPIO", StringComparison.OrdinalIgnoreCase)).Side);
        Assert.Equal(part["q1"].X, part["q1"].Pins.Single(pin => pin.Name == "Drain").X);
        Assert.Equal(part["q1"].X, part["q1"].Pins.Single(pin => pin.Name == "Source").X);

        foreach (var wire in layout.Wires.Where(item => Touches(item, "r2") || Touches(item, "u1")))
        {
            Assert.False(Crosses(wire, part["d1"]));
            Assert.False(Crosses(wire, part["r1"]));
            Assert.False(Crosses(wire, part["q1"]));
        }

        foreach (var wire in layout.Wires.Where(item => (Touches(item, "d1") && Touches(item, "q1")) || (Touches(item, "q1") && Touches(item, "gnd")) || (Touches(item, "r1") && Touches(item, "d1"))))
        {
            Assert.All(wire.Points, point => Assert.Equal(part["r1"].X, point.X));
        }
    }

    [Fact]
    public void Led_wired_to_a_gpio_fails_the_low_side_family()
    {
        const string reply = """
            {"parts":[{"id":"u1","name":"ESP32","type":"mcu","pins":["3V3","GND","GPIO18"]},{"id":"r1","name":"R1","type":"resistor","note":"330 ohm"},{"id":"d1","name":"LED","type":"led"},{"id":"q1","name":"N-MOSFET","type":"mosfet","pins":["Gate","Drain","Source"]}],"wires":[{"from":"u1.GPIO18","to":"r1.1"},{"from":"r1.2","to":"d1.Anode"},{"from":"d1.Cathode","to":"GND"},{"from":"u1.GND","to":"GND"},{"from":"u1.3V3","to":"3V3"}]}
            """;

        var sketch = SketchReplyParser.Parse(reply, "ESP32 LED");
        var rules = LowSideLedFamily.Pattern.Evaluate(sketch, null).Rules;

        Assert.Equal(CircuitCategory.LowSideLed, new SketchRuleCatalog(SketchRules.All).Match(sketch, null)!.Category);
        Assert.Equal("Fail", rules.Single(rule => rule.Name == "Gate drive").Result);
        Assert.Equal("Fail", rules.Single(rule => rule.Name == "LED off the GPIO").Result);
        Assert.Contains("gate", rules.Single(rule => rule.Name == "LED off the GPIO").Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_rule_catalog_selects_the_matching_family()
    {
        var catalog = new SketchRuleCatalog(SketchRules.All);
        const string motor = """
            {"title":"Motor","parts":[{"id":"u1","name":"ESP32","type":"mcu"},{"id":"q1","name":"N-MOSFET","type":"mosfet"},{"id":"m1","name":"Motor","type":"motor"},{"id":"d1","name":"Flyback","type":"diode"},{"id":"rg","name":"Gate","type":"resistor"},{"id":"gnd","name":"GND","type":"ground"}],"wires":[{"from":"u1.GPIO18","to":"rg.1"},{"from":"rg.2","to":"q1.G"},{"from":"q1.S","to":"gnd"},{"from":"q1.D","to":"m1.2"},{"from":"d1.A","to":"q1.D"},{"from":"d1.K","to":"m1.1"}]}
            """;
        var motorMatch = catalog.Match(SketchReplyParser.Parse(motor, "ESP32 motor driver"), "ESP32 motor driver");

        Assert.Equal(CircuitCategory.MotorDriver, motorMatch!.Category);
        Assert.Contains(catalog.Sets, set => set.Category == CircuitCategory.Relay);
        Assert.Contains(catalog.Sets, set => set.Category == CircuitCategory.Esp32Sensor);
        Assert.Contains(catalog.Sets, set => set.Category == CircuitCategory.Buck);
        Assert.Contains(catalog.Sets, set => set.Category == CircuitCategory.Solenoid);
        Assert.Contains(catalog.Sets, set => set.Category == CircuitCategory.Divider);
    }

    [Fact]
    public void A_buck_reply_missing_the_inductor_stays_incomplete()
    {
        const string reply = """
            {"title":"Buck","summary":"Steps 12 V down to 5 V.","parts":[{"id":"u1","name":"LM2596","type":"regulator"},{"id":"cin","name":"Input capacitor","type":"capacitor","value":"100uF"},{"id":"cout","name":"Output capacitor","type":"capacitor","value":"220uF"},{"id":"sw","name":"Switch node","type":"node"},{"id":"gnd","name":"GND","type":"ground"},{"id":"vin","name":"VIN","type":"supply","note":"12V"},{"id":"vout","name":"Vout","type":"node","note":"5V"}],"wires":[{"from":"vin","to":"u1.VIN"},{"from":"u1.OUT","to":"sw"},{"from":"sw","to":"vout"}]}
            """;

        var sketch = SketchReplyParser.Parse(reply, "Create a buck converter from 12V to 5V");
        Assert.DoesNotContain(sketch.Parts, part => part.Type == "inductor");
        Assert.DoesNotContain(sketch.Parts, part => part.Type == "schottky");

        var rules = BuckConverterRules.Evaluate(sketch);
        Assert.Equal("Fail", rules.Single(rule => rule.Name == "Inductor").Result);
        Assert.Equal("Fail", rules.Single(rule => rule.Name == "Schottky diode").Result);
    }

    [Fact]
    public void A_buck_with_the_output_above_the_input_fails()
    {
        const string reply = """
            {"title":"Buck","summary":"","parts":[{"id":"vin","name":"VIN","type":"supply","note":"5V","category":"power"},{"id":"u1","name":"LM2596","type":"regulator","category":"power"},{"id":"L1","type":"inductor","category":"passive","value":"33uH"},{"id":"d1","name":"Schottky","type":"schottky","note":"1N5822","category":"protection"},{"id":"cin","name":"Input capacitor","type":"capacitor","value":"100uF","category":"passive"},{"id":"cout","name":"Output capacitor","type":"capacitor","value":"220uF","category":"passive"},{"id":"vout","name":"Vout","type":"node","note":"12V","category":"power"}]}
            """;

        var sketch = SketchReplyParser.Parse(reply, "Create a buck converter from 5V to 12V");
        var inductor = sketch.Parts.Single(part => part.Id == "L1");
        Assert.Equal(PartCategory.Passive, inductor.Category);
        Assert.Equal("33uH", inductor.Note);

        var rule = BuckConverterRules.Evaluate(sketch).Single(item => item.Name == "Input above output");
        Assert.Equal("Fail", rule.Result);
        Assert.Equal("5 V in is not above 12 V out.", rule.Detail);
    }

    [Fact]
    public void A_failed_sketch_offers_a_correction_from_the_checks()
    {
        var offer = CircuitSuggestions.Find(
        [
            ("Common ground", "The controller and the motor do not share a ground.", "Fail"),
            ("Gate resistor", "No resistor joins the PWM pin to the gate.", "Fail"),
            ("Pull-down resistor", "A resistor holds the gate low.", "Pass"),
        ]);

        Assert.NotNull(offer);
        Assert.Contains("do not share a ground", offer.Text);
        Assert.Contains("No resistor joins the PWM pin", offer.Text);
        Assert.DoesNotContain("holds the gate low", offer.Text);

        var unavailable = CircuitSuggestions.Find([("Operating point", "This schematic has no ngspice model yet.", "Unavailable")]);
        Assert.NotNull(unavailable);
        Assert.Contains("no ngspice model", unavailable.Text);
        Assert.Null(CircuitSuggestions.Find([("Inductor", "An inductor carries the current into the output.", "Pass")]));
    }

    [Fact]
    public void An_ecu_solenoid_reply_is_scored_on_the_sensor_switch_and_driver()
    {
        const string reply = """
            {"title":"ECU Solenoid Control","parts":[{"id":"ecu1","type":"ecu"},{"id":"temp1","type":"temperature_sensor"},{"id":"sw1","type":"switch"},{"id":"q1","type":"nmosfet"},{"id":"sol1","type":"solenoid"},{"id":"d1","type":"flyback_diode"}],"wires":[{"from":"temp1.Output","to":"ecu1.TempIn"},{"from":"sw1.2","to":"ecu1.SwitchIn"},{"from":"ecu1.SolenoidOut","to":"q1.Gate"},{"from":"q1.Drain","to":"sol1.Control"},{"from":"sol1.VCC","to":"+12V"},{"from":"q1.Source","to":"GND"}]}
            """;

        var sketch = SketchReplyParser.Parse(reply, "Create a circuit with a ECU, temperature sensor, solenoid, and a switch");
        var rules = SolenoidFamily.Pattern.Evaluate(sketch, null).Rules;

        Assert.Equal(CircuitCategory.Solenoid, new SketchRuleCatalog(SketchRules.All).Match(sketch, null)!.Category);
        Assert.Equal("mosfet", sketch.Parts.Single(part => part.Id == "q1").Type);
        Assert.Equal("diode", sketch.Parts.Single(part => part.Id == "d1").Type);
        Assert.Equal("Pass", rules.Single(rule => rule.Name == "ECU present").Result);
        Assert.Equal("Pass", rules.Single(rule => rule.Name == "Temperature sensor connected").Result);
        Assert.Equal("Pass", rules.Single(rule => rule.Name == "Switch connected").Result);
        Assert.Equal("Pass", rules.Single(rule => rule.Name == "Solenoid connected through driver").Result);
        Assert.Equal("Fail", rules.Single(rule => rule.Name == "Flyback diode present").Result);
        Assert.Contains("flyback diode", rules.Single(rule => rule.Name == "Flyback diode present").Detail, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Fail", rules.Single(rule => rule.Name == "Common ground present").Result);
        var layout = SchematicRouter.Place(sketch);
        var ecu = layout.Parts.Single(part => part.Id == "ecu1");
        var solenoid = layout.Parts.Single(part => part.Id == "sol1");
        var diode = layout.Parts.Single(part => part.Id == "d1");
        Assert.True(solenoid.X > ecu.X);
        Assert.True(diode.X > solenoid.X);
        Assert.True(diode.Y < solenoid.Y);
        Assert.Equal(140, ecu.Width);
        Assert.Equal(180, ecu.Height);
        Assert.Equal("Pass", rules.Single(rule => rule.Name == "Solenoid not driven directly from ECU pin").Result);
        Assert.Equal("Pass", rules.Single(rule => rule.Name == "External supply provided").Result);
    }

    [Fact]
    public void Pinless_ecu_solenoid_wiring_passes()
    {
        const string reply = """
            {"parts":[{"id":"ecu1","name":"ECU","type":"block","pins":["TempIn","SwitchIn","SolenoidOut","GND"]},{"id":"temp1","name":"Temperature sensor","type":"block"},{"id":"sw1","name":"Switch","type":"block"},{"id":"sol1","name":"Solenoid","type":"block","pins":["1","2"]},{"id":"q1","name":"N-MOSFET","type":"block","pins":["Gate","Drain","Source"]},{"id":"rg","name":"Gate resistor","type":"resistor","note":"100 ohm","pins":["1","2"]},{"id":"d1","name":"Flyback diode","type":"diode","pins":["Anode","Cathode"]}],"wires":[{"from":"temp1","to":"ecu1.TempIn"},{"from":"temp1","to":"GND"},{"from":"temp1","to":"5V"},{"from":"sw1","to":"ecu1.SwitchIn"},{"from":"sw1","to":"GND"},{"from":"ecu1.SolenoidOut","to":"rg.1"},{"from":"rg.2","to":"q1.Gate"},{"from":"q1.Drain","to":"sol1.2"},{"from":"sol1.1","to":"12V"},{"from":"q1.Source","to":"GND"},{"from":"ecu1.GND","to":"GND"},{"from":"d1.Cathode","to":"12V"},{"from":"d1.Anode","to":"q1.Drain"}]}
            """;

        var rules = SolenoidFamily.Pattern.Evaluate(SketchReplyParser.Parse(reply, "ECU temperature sensor switch solenoid"), null).Rules;

        Assert.All(rules, rule => Assert.True(rule.Result == "Pass", $"{rule.Name}: {rule.Detail}"));
    }

    [Fact]
    public void Swapped_mosfet_series_diode_and_bypassed_gate_resistor_fail()
    {
        const string reply = """
            {"parts":[{"id":"ecu1","name":"ECU","type":"ecu","pins":["TempIn","SwitchIn","SolenoidOut","GND"]},{"id":"temp1","name":"Temperature sensor","type":"temperature_sensor"},{"id":"sw1","name":"Switch","type":"switch"},{"id":"sol1","name":"Solenoid","type":"solenoid","pins":["POS","NEG"]},{"id":"q1","name":"N-MOSFET","type":"mosfet","pins":["Gate","Drain","Source"]},{"id":"rg","name":"Gate resistor","type":"resistor","note":"100 ohm","pins":["1","2"]},{"id":"d1","name":"Flyback","type":"diode","pins":["Anode","Cathode"]}],"wires":[{"from":"temp1.OUT","to":"ecu1.TempIn"},{"from":"sw1.1","to":"ecu1.SwitchIn"},{"from":"ecu1.SolenoidOut","to":"rg.1"},{"from":"rg.1","to":"rg.2"},{"from":"rg.2","to":"d1.Anode"},{"from":"d1.Cathode","to":"q1.Drain"},{"from":"q1.Gate","to":"sol1.NEG"},{"from":"sol1.POS","to":"12V"},{"from":"q1.Source","to":"GND"},{"from":"ecu1.GND","to":"GND"}]}
            """;

        var rules = SolenoidFamily.Pattern.Evaluate(SketchReplyParser.Parse(reply, "ECU solenoid"), null).Rules;

        Assert.Equal("Fail", rules.Single(rule => rule.Name == "MOSFET terminals").Result);
        Assert.Contains("swapped", rules.Single(rule => rule.Name == "MOSFET terminals").Detail, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Fail", rules.Single(rule => rule.Name == "Gate resistor").Result);
        Assert.Contains("bypassed", rules.Single(rule => rule.Name == "Gate resistor").Detail, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Fail", rules.Single(rule => rule.Name == "Flyback diode present").Result);
        Assert.Contains("series", rules.Single(rule => rule.Name == "Flyback diode present").Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Ecu_temperature_switch_and_solenoid_is_not_scored_as_a_motor()
    {
        const string reply = """
            {"parts":[{"id":"ecu1","name":"ECU","type":"block"},{"id":"temp1","name":"Temperature sensor","type":"block"},{"id":"sw1","name":"Switch","type":"block"},{"id":"sol1","name":"Solenoid","type":"block"},{"id":"q1","name":"N-MOSFET","type":"mosfet"},{"id":"d1","name":"Flyback","type":"diode"}],"wires":[{"from":"temp1.Output","to":"ecu1.TempIn"},{"from":"sw1.1","to":"ecu1.SwitchIn"},{"from":"ecu1.SolenoidOut","to":"q1.Gate"},{"from":"q1.Drain","to":"sol1.1"},{"from":"sol1.2","to":"+12V"},{"from":"q1.Source","to":"GND"},{"from":"ecu1.GND","to":"GND"},{"from":"d1.K","to":"+12V"},{"from":"d1.A","to":"q1.Drain"}]}
            """;

        var sketch = SketchReplyParser.Parse(reply, "ECU with a temperature sensor, a switch, and a solenoid");
        var match = new SketchRuleCatalog(SketchRules.All).Match(sketch, "ECU with a temperature sensor, a switch, and a solenoid");
        var rules = match!.Evaluate(sketch, null).Rules;

        Assert.Equal(CircuitCategory.Solenoid, match.Category);
        Assert.DoesNotContain(rules, rule => rule.Detail.Contains("across the motor", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(rules, rule => rule.Detail.Contains("PWM pin", StringComparison.OrdinalIgnoreCase));
        var layout = SchematicRouter.Place(sketch);
        var ecu = layout.Parts.Single(part => part.Id == "ecu1");
        var sensor = layout.Parts.Single(part => part.Id == "temp1");
        var toggle = layout.Parts.Single(part => part.Id == "sw1");
        var solenoid = layout.Parts.Single(part => part.Id == "sol1");
        Assert.True(sensor.X > ecu.X && toggle.X > ecu.X && solenoid.X > ecu.X);
        Assert.True(sensor.Y < toggle.Y && toggle.Y < solenoid.Y);
    }

    [Fact]
    public void A_solenoid_wired_to_an_ecu_pin_fails()
    {
        const string reply = """
            {"title":"ECU Solenoid Control","parts":[{"id":"ecu","name":"ECU","type":"ecu"},{"id":"temp1","name":"Temperature sensor","type":"temperature_sensor"},{"id":"sw1","name":"Switch","type":"switch"},{"id":"q1","name":"N-MOSFET","type":"mosfet"},{"id":"sol1","name":"Solenoid","type":"solenoid"},{"id":"d1","name":"Flyback","type":"diode"},{"id":"gnd","name":"GND","type":"ground"}],"wires":[{"from":"temp1.Output","to":"ecu.TempIn"},{"from":"sw1.2","to":"ecu.SwitchIn"},{"from":"ecu.SolenoidOut","to":"q1.Gate"},{"from":"q1.Drain","to":"sol1.Control"},{"from":"sol1.VCC","to":"+12V"},{"from":"q1.Source","to":"gnd"},{"from":"ecu.GND","to":"gnd"},{"from":"d1.K","to":"sol1.VCC"},{"from":"d1.A","to":"q1.Drain"},{"from":"sol1.Control","to":"ecu.SolenoidOut"}]}
            """;

        var rule = SolenoidFamily.Pattern.Evaluate(
            SketchReplyParser.Parse(reply, "ECU solenoid"),
            null).Rules.Single(item => item.Name == "Solenoid not driven directly from ECU pin");

        Assert.Equal("Fail", rule.Result);
        Assert.Equal("The solenoid is wired directly to an ECU pin.", rule.Detail);
    }

    [Fact]
    public void Component_database_stores_each_family()
    {
        var max = ComponentLibrary.All.Single(item => item.Name == "MAX30102");

        Assert.Equal("sensor", max.Category);
        Assert.Equal("I2C", max.Interface);
        Assert.Equal("3.3V", max.Voltage);
        Assert.Equal(3.3, max.MaxVoltage);
        Assert.Equal(["VIN", "GND", "SDA", "SCL"], max.Pins);
        Assert.Contains(ComponentLibrary.All, item => item.Category == "power" && item.Name == "LM2596");
        Assert.Contains(ComponentLibrary.All, item => item.Category == "controller" && item.Name == "ESP32");
        Assert.Contains(ComponentLibrary.All, item => item.Category == "actuator" && item.Name == "Solenoid");
        Assert.Contains(ComponentLibrary.All, item => item.Category == "driver" && item.Name == "TB6612");
        Assert.Contains(ComponentLibrary.All, item => item.Category == "digital logic" && item.Name == "Full Adder");
        Assert.Contains(ComponentLibrary.All, item => item.Category == "passive" && item.Name == "Resistor");
        Assert.Contains(ComponentLibrary.All, item => item.Category == "protection" && item.Name == "Flyback Diode");
        Assert.All(ComponentLibrary.All, item => Assert.Contains(item.Category, ComponentCategories.All));
        Assert.Equal(SensorInterface.I2C, SensorLibrary.Find(new SketchPart("max30102", "MAX30102", "sensor", null))!.Interface);
    }

    [Fact]
    public void Temperature_sensor_reaches_the_ecu_and_still_needs_a_shared_ground()
    {
        const string reply = """
            {"parts":[{"id":"ecu1","type":"ecu"},{"id":"temp1","type":"temperature_sensor"}],"wires":[{"from":"temp1.Output","to":"ecu1.TempIn"},{"from":"temp1.VCC","to":"5V"},{"from":"temp1.GND","to":"GND"}]}
            """;

        var sketch = SketchReplyParser.Parse(reply, "temperature sensor and ECU");
        var result = Esp32SensorFamily.Analog.Evaluate(sketch, null);

        Assert.Equal(CircuitCategory.Esp32Sensor, CircuitCategories.Of(sketch));
        Assert.Equal(CircuitCategory.Esp32Sensor, new SketchRuleCatalog(SketchRules.All).Match(sketch, null)!.Category);
        Assert.Same(Esp32SensorFamily.Analog, Esp32SensorFamily.Patterns.First(pattern => pattern.Applies(sketch, null)));
        Assert.Equal("Pass", result.Rules.Single(rule => rule.Name == "Sensor powered").Result);
        Assert.Equal("Fail", result.Rules.Single(rule => rule.Name == "ECU powered").Result);
        Assert.Equal("Pass", result.Rules.Single(rule => rule.Name == "Sensor grounded").Result);
        Assert.Equal("Pass", result.Rules.Single(rule => rule.Name == "Sensor output connected").Result);
        Assert.Equal("Pass", result.Rules.Single(rule => rule.Name == "ECU input connected").Result);
        Assert.Equal("Fail", result.Rules.Single(rule => rule.Name == "Common ground present").Result);
    }

    [Fact]
    public void Temperature_sensor_and_ecu_share_ground()
    {
        const string reply = """
            {"parts":[{"id":"ecu1","type":"ecu"},{"id":"temp1","type":"temperature_sensor"}],"wires":[{"from":"temp1.Output","to":"ecu1.TempIn"},{"from":"temp1.VCC","to":"5V"},{"from":"ecu1.VCC","to":"5V"},{"from":"temp1.GND","to":"GND"},{"from":"ecu1.GND","to":"GND"}]}
            """;

        var rules = Esp32SensorFamily.Analog.Evaluate(SketchReplyParser.Parse(reply, "temperature sensor and ECU"), null).Rules;

        Assert.All(rules, rule => Assert.Equal("Pass", rule.Result));
    }

    [Fact]
    public void Closed_push_button_led_matches_the_series_led_current()
    {
        const string reply = """
            {"title":"Push Button + LED","parts":[{"id":"v1","name":"+5V","type":"vsource","note":"DC 5V","pins":["positive","negative"]},{"id":"s1","name":"S1","type":"pushbutton","note":"NO momentary","pins":["1","2"]},{"id":"r1","name":"R1","type":"resistor","note":"330 ohm","pins":["1","2"]},{"id":"d1","name":"D1","type":"led","pins":["Anode","Cathode"]}],"wires":[{"from":"v1.positive","to":"s1.1"},{"from":"s1.2","to":"r1.1"},{"from":"r1.2","to":"d1.Anode"},{"from":"d1.Cathode","to":"GND"}]}
            """;
        var circuit = new CircuitEngine(compiler).Compile(SketchReplyParser.Parse(reply, "Push Button + LED"));
        var result = new NgspiceRunner().Run(writer.Write(circuit), circuit);
        if (!NgspiceRunner.IsAvailable())
        {
            Assert.False(result.Available);
            return;
        }

        Assert.True(result.Available, result.Reason);
        var expected = (5d - 2d) / 330d;
        Assert.InRange(result.OperatingPoint!.LoadCurrentAmps, expected * 0.9, expected * 1.1);
        var checks = SketchCircuitChecks.Evaluate(circuit, result.OperatingPoint, null);
        Assert.Contains(checks, check => check.Name == "LED current" && check.Result == CheckStatus.Pass);
        Assert.Contains(checks, check => check.Name == "Resistor power" && check.Result == CheckStatus.Pass);
    }

    [Fact]
    public void Push_button_led_without_a_resistor_value_reports_the_missing_resistance()
    {
        const string reply = """
            {"title":"Push Button + LED","parts":[{"id":"v1","name":"+5V","type":"vsource","note":"DC 5V"},{"id":"s1","name":"S1","type":"pushbutton","note":"NO momentary"},{"id":"r1","name":"R1","type":"resistor","note":"Current limit"},{"id":"d1","name":"D1","type":"led"}],"wires":[{"from":"v1.positive","to":"s1.1"},{"from":"s1.2","to":"d1.Anode"},{"from":"d1.Cathode","to":"r1.1"},{"from":"r1.2","to":"GND"}]}
            """;

        var error = Assert.Throws<CircuitCompileException>(() => new CircuitEngine(compiler).Compile(SketchReplyParser.Parse(reply, "Push Button + LED")));

        Assert.Equal("Resistor r1 has no resistance. Put the ohms in its note, such as 330 ohm.", error.Message);
    }

    [Fact]
    public void Led_resistor_and_battery_keep_the_led_when_its_note_mentions_the_resistor()
    {
        const string reply = """
            {"title":"LED + Resistor + Battery","parts":[{"id":"d1","name":"LED","type":"led","note":"series resistor"},{"id":"r1","name":"R1","type":"resistor","note":"330 ohm"},{"id":"b1","name":"Battery","type":"battery","note":"9V"}],"wires":[{"from":"b1","to":"r1"},{"from":"r1","to":"d1"},{"from":"d1","to":"GND"},{"from":"b1","to":"GND"}]}
            """;

        var circuit = new CircuitEngine(compiler).Compile(SketchReplyParser.Parse(reply, "LED + Resistor + Battery"));

        Assert.Contains(circuit.Components.OfType<Led>(), led => led.Id == "d1");
        Assert.Contains(circuit.Components.OfType<Resistor>(), resistor => resistor.Id == "r1" && resistor.Ohms == 330);
        Assert.Equal(9, circuit.Components.OfType<VoltageSource>().Single().Volts);
    }

    [Fact]
    public void Low_side_led_driver_offers_three_choices_until_one_is_chosen()
    {
        const string series = """
            {"title":"Low-Side Controlled LED Driver Loop","parts":[{"id":"d1","name":"LED","type":"led"},{"id":"r1","name":"R1","type":"resistor","note":"330 ohm"},{"id":"sw","name":"Push Button","type":"switch"}],"wires":[]}
            """;
        const string transistor = """
            {"parts":[{"id":"d1","name":"LED","type":"led"},{"id":"q1","name":"N-MOSFET","type":"mosfet"},{"id":"u1","name":"ESP32","type":"mcu"}],"wires":[]}
            """;
        const string plain = """
            {"parts":[{"id":"d1","name":"LED","type":"led"},{"id":"r1","name":"R1","type":"resistor","note":"330 ohm"}],"wires":[]}
            """;

        var asked = LedDriverChoices.Find(SketchReplyParser.Parse(series, "Low-Side Controlled LED Driver Loop"), "Low-Side Controlled LED Driver Loop");
        Assert.Equal(["series-switch", "gpio-transistor", "transistor-only"], asked!.Select(choice => choice.Id));

        var withTransistor = LedDriverChoices.Find(SketchReplyParser.Parse(transistor, "blink an LED"), "blink an LED");
        Assert.Equal(3, withTransistor!.Count);

        Assert.Null(LedDriverChoices.Find(SketchReplyParser.Parse(plain, "LED + Resistor + Battery"), "LED + Resistor + Battery"));
        Assert.Null(LedDriverChoices.Find(
            SketchReplyParser.Parse(series, "Low-Side Controlled LED Driver Loop"),
            "Low-Side Controlled LED Driver Loop\n\nChosen LED driver: series-switch."));
    }

    [Fact]
    public void Led_switch_and_battery_stack_on_one_vertical_axis()
    {
        const string reply = """
            {"title":"LED + Resistor + Switch + Battery","parts":[{"id":"b1","name":"Battery","type":"battery","note":"9V"},{"id":"r1","name":"R1","type":"resistor","note":"330 ohm"},{"id":"sw1","name":"Push Button","type":"switch","note":"SPST"},{"id":"d1","name":"LED","type":"led"},{"id":"gnd1","name":"GND","type":"ground"}],"wires":[{"from":"b1","to":"r1"},{"from":"r1","to":"sw1"},{"from":"sw1","to":"d1"},{"from":"d1","to":"gnd1"},{"from":"b1","to":"gnd1"}]}
            """;

        var layout = SchematicRouter.Place(SketchReplyParser.Parse(reply, "LED resistor switch battery"));
        var part = layout.Parts.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);

        Assert.Equal(part["b1"].X, part["r1"].X);
        Assert.Equal(part["r1"].X, part["sw1"].X);
        Assert.Equal(part["sw1"].X, part["d1"].X);
        Assert.Equal(part["d1"].X, part["gnd1"].X);
        Assert.True(part["b1"].Y < part["r1"].Y && part["r1"].Y < part["sw1"].Y && part["sw1"].Y < part["d1"].Y && part["d1"].Y < part["gnd1"].Y);
        Assert.True(part["sw1"].Upright);
        Assert.True(part["b1"].Upright == false && part["gnd1"].Upright == false);

        foreach (var wire in layout.Wires.Where(item => !Touches(item, "b1") || !Touches(item, "gnd1")))
        {
            Assert.True(wire.Points.Count >= 2);
            Assert.All(wire.Points, point => Assert.Equal(part["r1"].X, point.X));
        }
    }

    private static bool Crosses(LayoutWire wire, LayoutPart part)
    {
        var left = part.X - part.Width / 2;
        var right = part.X + part.Width / 2;
        var top = part.Y - part.Height / 2;
        var bottom = part.Y + part.Height / 2;
        for (var index = 1; index < wire.Points.Count; index++)
        {
            var from = wire.Points[index - 1];
            var to = wire.Points[index];
            var steps = (int)(Math.Max(Math.Abs(to.X - from.X), Math.Abs(to.Y - from.Y)) / 4) + 1;
            for (var step = 0; step <= steps; step++)
            {
                var x = from.X + (to.X - from.X) * step / steps;
                var y = from.Y + (to.Y - from.Y) * step / steps;
                if (x > left + 1 && x < right - 1 && y > top + 1 && y < bottom - 1)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool Touches(LayoutWire wire, string id) =>
        wire.From.Equals(id, StringComparison.OrdinalIgnoreCase)
        || wire.To.Equals(id, StringComparison.OrdinalIgnoreCase)
        || wire.From.StartsWith(id + ".", StringComparison.OrdinalIgnoreCase)
        || wire.To.StartsWith(id + ".", StringComparison.OrdinalIgnoreCase);

    private static bool SharesTrack(SchematicLayout layout, string leftRole, string rightRole)
    {
        var left = layout.Wires.Where(wire => wire.Role == leftRole).ToList();
        var right = layout.Wires.Where(wire => wire.Role == rightRole).ToList();
        foreach (var a in left)
        {
            foreach (var b in right)
            {
                for (var i = 1; i < a.Points.Count; i++)
                {
                    for (var j = 1; j < b.Points.Count; j++)
                    {
                        if (Collinear(a.Points[i - 1], a.Points[i], b.Points[j - 1], b.Points[j]))
                        {
                            return true;
                        }
                    }
                }
            }
        }

        return false;
    }

    private static bool Collinear(LayoutPoint a1, LayoutPoint a2, LayoutPoint b1, LayoutPoint b2)
    {
        var aVertical = Math.Abs(a1.X - a2.X) < 1;
        var bVertical = Math.Abs(b1.X - b2.X) < 1;
        if (aVertical && bVertical && Math.Abs(a1.X - b1.X) < 1)
        {
            return RangesOverlap(a1.Y, a2.Y, b1.Y, b2.Y);
        }

        var aHorizontal = Math.Abs(a1.Y - a2.Y) < 1;
        var bHorizontal = Math.Abs(b1.Y - b2.Y) < 1;
        return aHorizontal && bHorizontal && Math.Abs(a1.Y - b1.Y) < 1 && RangesOverlap(a1.X, a2.X, b1.X, b2.X);
    }

    private static bool RangesOverlap(double a1, double a2, double b1, double b2)
    {
        var start = Math.Max(Math.Min(a1, a2), Math.Min(b1, b2));
        var end = Math.Min(Math.Max(a1, a2), Math.Max(b1, b2));
        return end - start > 1;
    }
}
