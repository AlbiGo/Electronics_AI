using System.Globalization;
using ElectronicsAI.Domain;

namespace ElectronicsAI.Validation;

public static class SketchCircuitChecks
{
    public static IReadOnlyList<string> Assumptions(Circuit circuit)
    {
        var supply = circuit.Components.OfType<VoltageSource>().SingleOrDefault();
        var resistors = circuit.Components.OfType<Resistor>().ToList();
        var lines = new List<string>();
        if (supply is not null)
        {
            lines.Add(Invariant($"Source = {supply.Volts:0.##} V"));
        }

        if (TrySeriesDivider(circuit, resistors, out var bottom, out var expectedVolts))
        {
            var top = resistors.Single(item => item.Id != bottom.Id);
            lines.Add($"Top resistor = {Ohms(top.Ohms)}");
            lines.Add($"Bottom resistor = {Ohms(bottom.Ohms)}");
            lines.Add(Invariant($"Expected Vout = {expectedVolts:0.00} V"));
            return lines;
        }

        foreach (var resistor in resistors)
        {
            lines.Add($"{resistor.Reference} = {Ohms(resistor.Ohms)}");
        }

        var led = circuit.Components.OfType<Led>().SingleOrDefault();
        if (led is not null)
        {
            lines.Add(Invariant($"LED forward drop = {led.ForwardVolts:0.##} V"));
        }

        return lines;
    }

    public static IReadOnlyList<Check> Evaluate(Circuit circuit, OperatingPoint? point, string? reason)
    {
        var resistors = circuit.Components.OfType<Resistor>().ToList();
        var resistor = resistors.Single(item => item.Id == circuit.ProbeComponentId);
        var led = circuit.Components.OfType<Led>().SingleOrDefault();
        var supply = circuit.Components.OfType<VoltageSource>().Single();
        var divider = TrySeriesDivider(circuit, resistors, out var bottom, out var expectedVolts);
        var expectedCurrent = led is not null
            ? (supply.Volts - led.ForwardVolts) / resistor.Ohms
            : divider
                ? expectedVolts / bottom.Ohms
                : supply.Volts / resistor.Ohms;
        var currentName = led is null ? "Load current" : "LED current";
        if (point is null)
        {
            var detail = reason ?? "Simulation did not run, so this check has no operating point.";
            var pending = new List<Check>
            {
                new(currentName, detail, CheckStatus.Unavailable),
                new("Resistor power", detail, CheckStatus.Unavailable),
            };
            if (divider)
            {
                pending.Add(new Check("Divider voltage", detail, CheckStatus.Unavailable));
            }

            return pending;
        }

        var currentDelta = Math.Abs(point.LoadCurrentAmps - expectedCurrent);
        var currentLimit = Math.Max(0.0005, Math.Abs(expectedCurrent) * 0.1);
        var currentStatus = currentDelta <= currentLimit ? CheckStatus.Pass : CheckStatus.Fail;
        var assumed = led is null ? "" : Invariant($" LED forward drop assumed {led.ForwardVolts:0.##} V.");
        var currentDetail = Invariant($"{point.LoadCurrentAmps * 1000:0.0} mA simulated, {expectedCurrent * 1000:0.0} mA expected.{assumed}");

        var power = point.LoadCurrentAmps * point.LoadCurrentAmps * resistor.Ohms;
        var powerStatus = power <= resistor.PowerRatingWatts ? CheckStatus.Pass : CheckStatus.Fail;
        var powerDetail = Invariant($"{power:0.000} W in {resistor.Reference}, {resistor.PowerRatingWatts:0.##} W rating");
        var checks = new List<Check>
        {
            new(currentName, currentDetail, currentStatus),
            new("Resistor power", powerDetail, powerStatus),
        };
        if (divider)
        {
            var delta = Math.Abs(point.OutputVolts - expectedVolts);
            var limit = Math.Max(0.05, Math.Abs(expectedVolts) * 0.1);
            var status = delta <= limit ? CheckStatus.Pass : CheckStatus.Fail;
            checks.Add(new Check(
                "Divider voltage",
                Invariant($"{point.OutputVolts:0.00} V simulated, {expectedVolts:0.00} V expected."),
                status));
        }

        return checks;
    }

    private static bool TrySeriesDivider(Circuit circuit, IReadOnlyList<Resistor> resistors, out Resistor bottom, out double expectedVolts)
    {
        bottom = null!;
        expectedVolts = 0;
        if (resistors.Count != 2 || circuit.Components.OfType<Led>().Any())
        {
            return false;
        }

        var top = resistors.FirstOrDefault(resistor =>
            On(circuit, resistor, circuit.InputNet, out var other) && Same(other, circuit.OutputNet));
        var lower = resistors.FirstOrDefault(resistor =>
            On(circuit, resistor, circuit.OutputNet, out var other) && Same(other, circuit.GroundNet));
        if (top is null || lower is null || top.Id == lower.Id)
        {
            return false;
        }

        var supply = circuit.Components.OfType<VoltageSource>().Single();
        bottom = lower;
        expectedVolts = supply.Volts * lower.Ohms / (top.Ohms + lower.Ohms);
        return expectedVolts >= 0;
    }

    private static bool On(Circuit circuit, Resistor resistor, string net, out string? other)
    {
        var a = NetName(circuit, resistor.Id, "a");
        var b = NetName(circuit, resistor.Id, "b");
        if (Same(a, net))
        {
            other = b;
            return true;
        }

        if (Same(b, net))
        {
            other = a;
            return true;
        }

        other = null;
        return false;
    }

    private static string? NetName(Circuit circuit, string componentId, string pin) =>
        circuit.Nets.FirstOrDefault(net => net.Connections.Any(connection =>
            connection.ComponentId.Equals(componentId, StringComparison.OrdinalIgnoreCase) && connection.Pin == pin))?.Name;

    private static bool Same(string? left, string? right) =>
        left is not null && right is not null && left.Equals(right, StringComparison.OrdinalIgnoreCase);

    private static string Ohms(double ohms)
    {
        if (ohms >= 1_000_000)
        {
            return Invariant($"{ohms / 1_000_000:0.##} MΩ");
        }

        if (ohms >= 1000)
        {
            return Invariant($"{ohms / 1000:0.##} kΩ");
        }

        return Invariant($"{ohms:0.##} Ω");
    }

    private static string Invariant(FormattableString value) =>
        value.ToString(CultureInfo.InvariantCulture);
}
