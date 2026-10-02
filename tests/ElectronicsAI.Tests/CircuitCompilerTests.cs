using ElectronicsAI.Design;
using ElectronicsAI.Simulation;
using ElectronicsAI.Validation;

namespace ElectronicsAI.Tests;

public class CircuitCompilerTests
{
    private readonly CircuitCompiler compiler = new();
    private readonly SpiceNetlistWriter writer = new();

    [Fact]
    public void Led_tied_directly_to_5v_fails_for_a_missing_resistor()
    {
        const string reply = """
            {"parts":[{"id":"led1","type":"LED","pins":["Anode","Cathode"]}],"wires":[{"from":"VCC","to":"led1.Anode"},{"from":"led1.Cathode","to":"GND"}]}
            """;

        var rule = AnalogSketchRules.MissingSeriesResistor(SketchReplyParser.Parse(reply, "LED connected directly to 5V"));

        Assert.NotNull(rule);
        Assert.Equal("Fail", rule.Result);
        Assert.Equal("Missing current limiting resistor.", rule.Detail);
    }

    [Fact]
    public void Led_with_a_series_resistor_is_not_flagged()
    {
        const string reply = """
            {"parts":[{"id":"r1","type":"resistor","value":"330 ohm","pins":["1","2"]},{"id":"led1","type":"LED","pins":["Anode","Cathode"]}],"wires":[{"from":"VCC","to":"r1.1"},{"from":"r1.2","to":"led1.Anode"},{"from":"led1.Cathode","to":"GND"}]}
            """;

        Assert.Null(AnalogSketchRules.MissingSeriesResistor(SketchReplyParser.Parse(reply, "5 V LED")));
    }

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
    public void Adder_sketch_has_no_spice_model()
    {
        const string reply = """
            {"parts":[{"id":"fa0","type":"1-bit full adder","pins":["A","B","Cin","Sum","Cout"]}],"wires":[]}
            """;

        var error = Assert.Throws<CircuitCompileException>(() => compiler.Compile(SketchReplyParser.Parse(reply, "1-bit full adder")));

        Assert.Equal("This schematic has no ngspice model yet.", error.Message);
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
        var sketch = HeartRateMonitorSketch.Create();
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
    }

    [Fact]
    public void Rc_low_pass_reports_the_cutoff_near_1_kilohertz()
    {
        const string reply = """
            {"title":"Low-pass","parts":[{"id":"vin","name":"Vin","type":"vsource","note":"5 V"},{"id":"r1","name":"R1","type":"resistor","note":"1.6k"},{"id":"c1","name":"C1","type":"capacitor","note":"100nF"},{"id":"vout","name":"Vout","type":"node"},{"id":"gnd","name":"GND","type":"ground"}],"wires":[{"from":"vin","to":"r1"},{"from":"r1","to":"vout"},{"from":"vout","to":"c1"},{"from":"c1","to":"gnd"},{"from":"vin","to":"gnd"}]}
            """;

        var sketch = SketchReplyParser.Parse(reply, "RC low-pass filter at 1 kHz");
        var rules = RcLowPassRules.Evaluate(sketch, "RC low-pass filter at 1 kHz");
        var cutoff = rules.Single(rule => rule.Name == "Cutoff frequency");

        Assert.True(RcLowPassRules.Applies(sketch));
        Assert.All(rules, rule => Assert.Equal("Pass", rule.Result));
        Assert.Contains("994.7 Hz calculated, 1000 Hz expected", cutoff.Detail);
        Assert.Contains("100n", writer.Write(compiler.Compile(sketch)), StringComparison.Ordinal);
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
        Assert.False(RcLowPassRules.Applies(sketch));
        Assert.All(rules, rule => Assert.Equal("Pass", rule.Result));
        Assert.Contains(rules, rule => rule.Name == "Standby pulled high");
        Assert.DoesNotContain(rules, rule => rule.Name == "Cutoff frequency");
    }

    [Fact]
    public void Transistor_led_switch_is_checked_without_ngspice()
    {
        const string reply = """
            {"title":"Transistor-switched LED","summary":"NPN low-side switch","parts":[{"id":"vcc","name":"VCC","type":"supply","note":"5V"},{"id":"gnd","name":"GND","type":"net"},{"id":"vin","name":"VIN","type":"supply"},{"id":"q1","name":"Q1 2N3904","type":"npn"},{"id":"d1","name":"D1 LED","type":"led"},{"id":"r_led","name":"R1","type":"resistor","note":"330Ω"},{"id":"r_base","name":"R2","type":"resistor","note":"10k"}],"wires":[{"from":"vcc","to":"r_led.1"},{"from":"r_led.2","to":"d1.A"},{"from":"d1.K","to":"q1.C"},{"from":"q1.E","to":"gnd"},{"from":"vin","to":"r_base.1"},{"from":"r_base.2","to":"q1.B"}]}
            """;

        var sketch = SketchReplyParser.Parse(reply, "generate a simple circuit with a transistor and a led");
        var rules = TransistorLedRules.Evaluate(sketch);

        Assert.Equal(CircuitCategory.TransistorLed, CircuitCategories.Of(sketch));
        Assert.All(rules, rule => Assert.Equal("Pass", rule.Result));
        Assert.Contains(rules, rule => rule.Name == "LED series resistor");
        Assert.Contains(rules, rule => rule.Name == "Base resistor");
    }

    [Fact]
    public void Block_typed_transistor_led_is_read_from_the_notes()
    {
        const string reply = """
            {"title":"Transistor LED switch","summary":"An NPN transistor (2N2222) acts as a low-side switch.","parts":[{"id":"vcc","name":"VCC","type":"block","note":"+5V supply"},{"id":"gnd","name":"GND","type":"block"},{"id":"in","name":"IN","type":"block","note":"control signal"},{"id":"r1","name":"R1","type":"block","note":"10k base resistor"},{"id":"q1","name":"Q1","type":"block","note":"NPN 2N2222"},{"id":"d1","name":"D1","type":"block","note":"LED"},{"id":"r2","name":"R2","type":"block","note":"330Ω LED resistor"}],"wires":[{"from":"vcc","to":"r2"},{"from":"r2","to":"d1"},{"from":"d1","to":"q1"},{"from":"q1","to":"gnd"},{"from":"in","to":"r1"},{"from":"r1","to":"q1"}]}
            """;

        var sketch = SketchReplyParser.Parse(reply, "generate a simple circuit with a transistor");
        var rules = TransistorLedRules.Evaluate(sketch);

        Assert.Equal(CircuitCategory.TransistorLed, CircuitCategories.Of(sketch));
        Assert.All(rules, rule => Assert.Equal("Pass", rule.Result));
    }

    [Fact]
    public void A_led_sketch_fails_when_the_request_asked_for_a_transistor()
    {
        const string reply = """
            {"title":"a simple transistor and a led circuit","parts":[{"id":"r1","name":"r1","type":"resistor","note":"330 ohm"},{"id":"led1","name":"led1","type":"LED","pins":["Anode","Cathode"]}],"wires":[{"from":"VCC","to":"r1.1"},{"from":"r1.2","to":"led1.Anode"},{"from":"led1.Cathode","to":"GND"}]}
            """;

        var sketch = SketchReplyParser.Parse(reply, "an LED and a 330 ohm resistor");
        var catalog = new SketchRuleCatalog(SketchRules.All);
        var missing = catalog.Match(sketch, "a simple transistor and a led circuit")?.Evaluate(sketch, "a simple transistor and a led circuit").Rules.Single();
        var plain = catalog.Match(sketch, "an LED and a 330 ohm resistor");

        Assert.NotNull(missing);
        Assert.Equal("Transistor", missing.Name);
        Assert.Equal("Fail", missing.Result);
        Assert.Null(plain);
    }

    [Fact]
    public void A_reply_without_a_transistor_is_completed_as_an_npn_led_switch()
    {
        const string reply = """
            {"title":"generate a simple transistor and led circuit","parts":[{"id":"r1","name":"r1","type":"resistor","note":"330 ohm"},{"id":"led1","name":"led1","type":"LED","pins":["Anode","Cathode"]}],"wires":[{"from":"VCC","to":"r1.1"},{"from":"r1.2","to":"led1.Anode"},{"from":"led1.Cathode","to":"GND"}]}
            """;

        var sketch = SketchReplyParser.Parse(reply, "generate a simple transistor and led circuit");
        var rules = TransistorLedRules.Evaluate(sketch);

        Assert.Contains(sketch.Parts, part => part.Type == "npn");
        Assert.Equal(CircuitCategory.TransistorLed, CircuitCategories.Of(sketch));
        Assert.All(rules, rule => Assert.Equal("Pass", rule.Result));
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
    public void The_rule_catalog_selects_the_matching_family()
    {
        var catalog = new SketchRuleCatalog(SketchRules.All);
        const string motor = """
            {"title":"Motor","parts":[{"id":"u1","name":"ESP32","type":"mcu"},{"id":"q1","name":"N-MOSFET","type":"mosfet"},{"id":"m1","name":"Motor","type":"motor"},{"id":"d1","name":"Flyback","type":"diode"},{"id":"rg","name":"Gate","type":"resistor"},{"id":"gnd","name":"GND","type":"ground"}],"wires":[{"from":"u1.GPIO18","to":"rg.1"},{"from":"rg.2","to":"q1.G"},{"from":"q1.S","to":"gnd"},{"from":"q1.D","to":"m1.2"},{"from":"d1.A","to":"q1.D"},{"from":"d1.K","to":"m1.1"}]}
            """;
        const string filter = """
            {"title":"Low-pass","parts":[{"id":"r1","name":"R1","type":"resistor","note":"1.6k"},{"id":"c1","name":"C1","type":"capacitor","note":"100nF"},{"id":"gnd","name":"GND","type":"ground"}],"wires":[{"from":"r1","to":"c1"},{"from":"c1","to":"gnd"}]}
            """;

        var motorMatch = catalog.Match(SketchReplyParser.Parse(motor, "ESP32 motor driver"), "ESP32 motor driver");
        var filterMatch = catalog.Match(SketchReplyParser.Parse(filter, "RC low-pass filter at 1 kHz"), "RC low-pass filter at 1 kHz");

        Assert.Equal(CircuitCategory.MotorDriver, motorMatch!.Category);
        Assert.Equal(CircuitCategory.Filter, filterMatch!.Category);
        Assert.Contains(catalog.Sets, set => set.Category == CircuitCategory.Relay);
        Assert.Contains(catalog.Sets, set => set.Category == CircuitCategory.Sensor);
    }
}
