using ElectronicsAI.Design;
using ElectronicsAI.Domain;
using ElectronicsAI.Simulation;
using ElectronicsAI.Validation;

namespace ElectronicsAI.Tests;

public class CircuitEngineTests
{
    private readonly DeterministicCircuitProposer _proposer = new();
    private readonly SpiceNetlistWriter _netlist = new();
    private readonly CircuitValidator _validator = new();

    [Fact]
    public void Divider_uses_26_1k_and_10k()
    {
        var requirement = new CircuitRequirement(12, 3.3, 0);
        var circuit = _proposer.Propose(requirement);

        var upper = Assert.IsType<Resistor>(circuit.Components.Single(component => component.Id == "r1"));
        var lower = Assert.IsType<Resistor>(circuit.Components.Single(component => component.Id == "r2"));
        Assert.Equal(26_100, upper.Ohms);
        Assert.Equal(10_000, lower.Ohms);

        var midpoint = requirement.InputVolts * lower.Ohms / (upper.Ohms + lower.Ohms);
        Assert.InRange(midpoint, 3.30, 3.35);
        AssertEveryPinIsConnected(circuit);

        var netlist = _netlist.Write(circuit);
        Assert.Contains("26.1k", netlist, StringComparison.Ordinal);
        Assert.Contains("10k", netlist, StringComparison.Ordinal);
        Assert.Contains("v(vout)", netlist, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Regulator_dissipation_warns_without_a_heatsink()
    {
        var requirement = new CircuitRequirement(12, 5, 0.2);
        var circuit = _proposer.Propose(requirement);
        var load = Assert.IsType<Resistor>(circuit.Components.Single(component => component.Id == "rload"));
        Assert.Equal(25, load.Ohms);
        AssertEveryPinIsConnected(circuit);

        var power = CircuitValidator.DissipationWatts(12, 4.96, 0.2);
        Assert.InRange(power, 1.39, 1.42);

        var operatingPoint = new OperatingPoint(12, 4.96, 0.2);
        var checks = _validator.Validate(circuit, requirement, operatingPoint);
        Assert.Equal(CheckStatus.Pass, checks.Single(check => check.Name == "Output voltage").Result);
        Assert.Equal(CheckStatus.Pass, checks.Single(check => check.Name == "C1 voltage rating").Result);
        Assert.Equal(CheckStatus.Pass, checks.Single(check => check.Name == "C2 voltage rating").Result);
        Assert.Equal(CheckStatus.Warning, checks.Single(check => check.Name == "Regulator dissipation").Result);

        var cooled = circuit with
        {
            Components = circuit.Components
                .Select(component => component is LinearRegulator regulator ? regulator with { HasHeatsink = true } : component)
                .ToArray(),
        };
        var cooledChecks = _validator.Validate(cooled, requirement, operatingPoint);
        Assert.Equal(CheckStatus.Pass, cooledChecks.Single(check => check.Name == "Regulator dissipation").Result);

        var underrated = circuit with
        {
            Components = circuit.Components
                .Select(component => component is Capacitor capacitor && capacitor.Id == "c1"
                    ? capacitor with { VoltageRatingVolts = 10 }
                    : component)
                .ToArray(),
        };
        var failed = _validator.Validate(underrated, requirement, operatingPoint);
        Assert.Equal(CheckStatus.Fail, failed.Single(check => check.Name == "C1 voltage rating").Result);
    }

    [Fact]
    public void Supply_netlist_models_the_7805()
    {
        var circuit = _proposer.Propose(new CircuitRequirement(12, 5, 0.2));
        var netlist = _netlist.Write(circuit);

        Assert.Contains("XU1 vin vout 0 lm7805", netlist, StringComparison.Ordinal);
        Assert.Contains("RLOAD vout 0 25", netlist, StringComparison.Ordinal);
        Assert.Contains("C1 vin 0 100u", netlist, StringComparison.Ordinal);
        Assert.Contains("C2 vout 0 10u", netlist, StringComparison.Ordinal);
        Assert.Contains("Videal mid gnd DC 5", netlist, StringComparison.Ordinal);
        Assert.Contains("Rseries mid out 200m", netlist, StringComparison.Ordinal);
    }

    [Fact]
    public void Parser_reads_an_ngspice_operating_point()
    {
        const string output = """
            No. of Data Rows : 1
            v(vout) = 4.960317e+00
            v(vin) = 1.200000e+01
            i(rload) = -1.984127e-01
            """;

        var operatingPoint = NgspiceOutputParser.Parse(output, "VIN", "VOUT", "RLOAD");

        Assert.Equal(12, operatingPoint.InputVolts, 3);
        Assert.Equal(4.960317, operatingPoint.OutputVolts, 5);
        Assert.Equal(0.1984127, operatingPoint.LoadCurrentAmps, 5);
    }

    [Fact]
    public void Parser_reads_the_ngspice_operating_point_table()
    {
        const string output = """
            Node                                  Voltage
            ----                                  -------
            vout                             4.960317e+00
            vin                              1.200000e+01

            Resistor: Simple linear resistor
                 device                 rload
                  model                     R
             resistance                    25
                      i              0.198413
            """;

        var operatingPoint = NgspiceOutputParser.Parse(output, "VIN", "VOUT", "RLOAD");

        Assert.Equal(12, operatingPoint.InputVolts, 3);
        Assert.Equal(4.960317, operatingPoint.OutputVolts, 5);
        Assert.Equal(0.198413, operatingPoint.LoadCurrentAmps, 5);
    }

    [Fact]
    public void Missing_ngspice_does_not_invent_an_operating_point()
    {
        var circuit = _proposer.Propose(new CircuitRequirement(12, 5, 0.2));
        var result = new NgspiceRunner().Run(_netlist.Write(circuit), circuit);

        if (!NgspiceRunner.IsAvailable())
        {
            Assert.False(result.Available);
            Assert.Null(result.OperatingPoint);
            Assert.Contains("ngspice", result.Reason, StringComparison.OrdinalIgnoreCase);
            return;
        }

        Assert.NotNull(result.OperatingPoint);
        Assert.InRange(result.OperatingPoint.OutputVolts, 4.9, 5.05);
        Assert.InRange(result.OperatingPoint.LoadCurrentAmps, 0.15, 0.22);

        var checks = _validator.Validate(circuit, new CircuitRequirement(12, 5, 0.2), result.OperatingPoint);
        Assert.Equal(CheckStatus.Warning, checks.Single(check => check.Name == "Regulator dissipation").Result);
    }

    [Fact]
    public void Counter_builds_eight_led_branches()
    {
        var requirement = new CircuitRequirement(5, 2, 0.01, Family: CircuitFamily.Counter, Steps: 8, ClockHertz: 1, ForwardVolts: 2);
        var circuit = _proposer.Propose(requirement);

        Assert.Equal(8, circuit.Components.OfType<Led>().Count());
        Assert.Equal(8, circuit.Components.OfType<Resistor>().Count());
        var resistor = Assert.IsType<Resistor>(circuit.Components.Single(component => component.Id == "r1"));
        Assert.Equal(300, resistor.Ohms);
        AssertEveryPinIsConnected(circuit);

        var netlist = _netlist.Write(circuit);
        Assert.Contains("VQ1 q1 0 DC 5", netlist, StringComparison.Ordinal);
        Assert.Contains("R1 q1 led1 300", netlist, StringComparison.Ordinal);
        Assert.Contains("VLED1 led1 0 DC 2", netlist, StringComparison.Ordinal);
        Assert.DoesNotContain("R2", netlist, StringComparison.Ordinal);

        var checks = _validator.Validate(circuit, requirement, new OperatingPoint(5, 2, 0.01));
        Assert.Equal(CheckStatus.Pass, checks.Single(check => check.Name == "Count sequence").Result);
        Assert.Equal(CheckStatus.Pass, checks.Single(check => check.Name == "LED current").Result);
        Assert.Equal(CheckStatus.Pass, checks.Single(check => check.Name == "Resistor power").Result);
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8, 1], Enumerable.Range(0, 9).Select(clock => CircuitValidator.ActiveStep(8, clock)));
    }

    [Fact]
    public void Counter_led_current_matches_ngspice_when_installed()
    {
        var requirement = new CircuitRequirement(5, 2, 0.01, Family: CircuitFamily.Counter, ForwardVolts: 2);
        var circuit = _proposer.Propose(requirement);
        var result = new NgspiceRunner().Run(_netlist.Write(circuit), circuit);
        if (!NgspiceRunner.IsAvailable())
        {
            Assert.False(result.Available);
            return;
        }

        Assert.NotNull(result.OperatingPoint);
        Assert.InRange(result.OperatingPoint.LoadCurrentAmps, 0.009, 0.011);
        var checks = _validator.Validate(circuit, requirement, result.OperatingPoint);
        Assert.Equal(CheckStatus.Pass, checks.Single(check => check.Name == "LED current").Result);
    }

    [Fact]
    public void Unsupported_requirement_is_rejected()
    {
        var exception = Assert.Throws<UnsupportedRequirementException>(() =>
            _proposer.Propose(new CircuitRequirement(9, 3.3, 1)));
        Assert.Contains("12 V to 5 V", exception.Message, StringComparison.Ordinal);
    }

    private static void AssertEveryPinIsConnected(Circuit circuit)
    {
        foreach (var component in circuit.Components)
        {
            foreach (var pin in component.Pins)
            {
                var count = circuit.Nets
                    .SelectMany(net => net.Connections)
                    .Count(connection => connection.ComponentId == component.Id && connection.Pin == pin);
                Assert.Equal(1, count);
            }
        }
    }
}
