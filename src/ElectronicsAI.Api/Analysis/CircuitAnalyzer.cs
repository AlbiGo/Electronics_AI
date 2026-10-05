using ElectronicsAI.Design;
using ElectronicsAI.Domain;
using ElectronicsAI.Simulation;
using ElectronicsAI.Validation;

namespace ElectronicsAI.Api;

public sealed record DescribeRequest(string Description, string? Accept = null);

public sealed record AnalyzeRequest(
    double InputVolts,
    double OutputVolts,
    double LoadCurrentAmps,
    string? Description,
    string? Family = null,
    int Steps = 0,
    double ClockHertz = 0,
    double ForwardVolts = 0);

public sealed record ErrorResponse(string Error);

public sealed record ComponentView(
    string Id,
    string Reference,
    string Kind,
    IReadOnlyDictionary<string, string> Attributes);

public sealed record CircuitView(
    string Name,
    string InputNet,
    string OutputNet,
    string GroundNet,
    IReadOnlyList<ComponentView> Components,
    IReadOnlyList<Net> Nets);

public sealed record AnalysisResponse(
    CircuitView Circuit,
    string Netlist,
    SimulationResult Simulation,
    IReadOnlyList<Check> Checks);

public sealed class CircuitAnalyzer(
    ICircuitProposer proposer,
    SpiceNetlistWriter netlistWriter,
    NgspiceRunner ngspice,
    CircuitValidator validator)
{
    public AnalysisResponse Analyze(CircuitRequirement requirement)
    {
        var circuit = proposer.Propose(requirement);
        var netlist = netlistWriter.Write(circuit);
        var simulation = ngspice.Run(netlist, circuit);
        var checks = simulation.OperatingPoint is { } operatingPoint
            ? validator.Validate(circuit, requirement, operatingPoint)
            : validator.Unavailable(circuit);

        return new AnalysisResponse(ToView(circuit), netlist, simulation, checks);
    }

    private static CircuitView ToView(Circuit circuit) =>
        new(
            circuit.Name,
            circuit.InputNet,
            circuit.OutputNet,
            circuit.GroundNet,
            circuit.Components.Select(ToView).ToArray(),
            circuit.Nets);

    private static ComponentView ToView(Component component) =>
        new(component.Id, component.Reference, component.Kind.ToString(), Attributes(component));

    private static IReadOnlyDictionary<string, string> Attributes(Component component) =>
        component switch
        {
            VoltageSource source => new Dictionary<string, string>
            {
                ["volts"] = source.Volts.ToString(System.Globalization.CultureInfo.InvariantCulture),
            },
            Resistor resistor => new Dictionary<string, string>
            {
                ["ohms"] = resistor.Ohms.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["powerRatingWatts"] = resistor.PowerRatingWatts.ToString(System.Globalization.CultureInfo.InvariantCulture),
            },
            Capacitor capacitor => new Dictionary<string, string>
            {
                ["farads"] = capacitor.Farads.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["voltageRatingVolts"] = capacitor.VoltageRatingVolts.ToString(System.Globalization.CultureInfo.InvariantCulture),
            },
            LinearRegulator regulator => new Dictionary<string, string>
            {
                ["partNumber"] = regulator.PartNumber,
                ["nominalOutputVolts"] = regulator.NominalOutputVolts.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["maxOutputCurrentAmps"] = regulator.MaxOutputCurrentAmps.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["thermalWarningWatts"] = regulator.ThermalWarningWatts.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["hasHeatsink"] = regulator.HasHeatsink ? "true" : "false",
                ["outputSeriesOhms"] = regulator.OutputSeriesOhms.ToString(System.Globalization.CultureInfo.InvariantCulture),
            },
            Clock clock => new Dictionary<string, string>
            {
                ["hertz"] = clock.Hertz.ToString(System.Globalization.CultureInfo.InvariantCulture),
            },
            Counter counter => new Dictionary<string, string>
            {
                ["steps"] = counter.Steps.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["activeStep"] = counter.ActiveStep.ToString(System.Globalization.CultureInfo.InvariantCulture),
            },
            Led led => new Dictionary<string, string>
            {
                ["forwardVolts"] = led.ForwardVolts.ToString(System.Globalization.CultureInfo.InvariantCulture),
            },
            _ => new Dictionary<string, string>(),
        };
}
