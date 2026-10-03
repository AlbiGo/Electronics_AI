using ElectronicsAI.Design;
using ElectronicsAI.Simulation;
using ElectronicsAI.Validation;

namespace ElectronicsAI.Api;

public sealed record SketchCheckView(string Name, string Detail, string Result);

public sealed record SketchSimulationView(
    bool Available,
    string? Reason,
    string? Netlist,
    double? InputVolts,
    double? OutputVolts,
    double? LoadCurrentAmps,
    IReadOnlyList<SketchCheckView> Checks,
    IReadOnlyList<string> Assumptions);

public sealed class SketchSpice(ICircuitCompiler compiler, SpiceNetlistWriter writer, NgspiceRunner ngspice, SketchRuleCatalog rules)
{
    public SketchSimulationView Simulate(SchematicSketch sketch, string? request = null)
    {
        if (rules.Match(sketch, request) is { } ruleSet)
        {
            var outcome = ruleSet.Evaluate(sketch, request);
            return new SketchSimulationView(
                false,
                outcome.Reason,
                outcome.AttachNetlist ? TryNetlist(sketch) : null,
                null,
                null,
                null,
                outcome.Rules.Select(rule => new SketchCheckView(rule.Name, rule.Detail, rule.Result)).ToArray(),
                outcome.Assumptions);
        }

        try
        {
            var circuit = compiler.Compile(sketch);
            var netlist = writer.Write(circuit);
            var result = ngspice.Run(netlist, circuit);
            var point = result.OperatingPoint;
            var checks = SketchCircuitChecks.Evaluate(circuit, point, result.Reason)
                .Select(check => new SketchCheckView(check.Name, check.Detail, check.Result.ToString()))
                .ToList();
            if (SketchVoltageNote.Misstated(sketch, point?.OutputVolts) is { } stated)
            {
                checks.Add(new SketchCheckView(stated.Name, stated.Detail, stated.Result));
            }

            return new SketchSimulationView(
                result.Available,
                result.Reason,
                netlist,
                point?.InputVolts,
                point?.OutputVolts,
                point?.LoadCurrentAmps,
                checks,
                SketchCircuitChecks.Assumptions(circuit));
        }
        catch (CircuitCompileException exception)
        {
            return new SketchSimulationView(
                false,
                exception.Message,
                null,
                null,
                null,
                null,
                [new SketchCheckView("Operating point", exception.Message, "Unavailable")],
                []);
        }
    }

    private string? TryNetlist(SchematicSketch sketch)
    {
        try
        {
            return writer.Write(compiler.Compile(sketch));
        }
        catch (CircuitCompileException)
        {
            return null;
        }
    }
}
