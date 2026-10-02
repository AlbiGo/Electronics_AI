using System.Globalization;
using ElectronicsAI.Domain;

namespace ElectronicsAI.Validation;

public enum CheckStatus
{
    Pass,
    Warning,
    Fail,
    Unavailable,
}

public sealed record Check(string Name, string Detail, CheckStatus Result);

public sealed class CircuitValidator
{
    public const double ThermalWarningWatts = 1.2;

    public static double DissipationWatts(double inputVolts, double outputVolts, double loadCurrentAmps) =>
        (inputVolts - outputVolts) * loadCurrentAmps;

    public static int ActiveStep(int steps, int clocks)
    {
        var wrapped = clocks % steps;
        if (wrapped < 0)
        {
            wrapped += steps;
        }

        return wrapped + 1;
    }

    public IReadOnlyList<Check> Validate(Circuit circuit, CircuitRequirement requirement, OperatingPoint operatingPoint)
    {
        if (circuit.Components.OfType<Counter>().SingleOrDefault() is { } counter)
        {
            return
            [
                CountSequence(circuit, counter),
                LedCurrent(requirement, operatingPoint),
                ResistorPower(circuit, counter, operatingPoint),
            ];
        }

        var checks = new List<Check> { OutputVoltage(requirement, operatingPoint) };
        checks.AddRange(circuit.Components.OfType<Capacitor>().Select(capacitor => CapacitorRating(circuit, capacitor, operatingPoint)));
        checks.AddRange(circuit.Components.OfType<LinearRegulator>().Select(regulator => RegulatorHeat(regulator, operatingPoint)));
        return checks;
    }

    public IReadOnlyList<Check> Unavailable(Circuit circuit)
    {
        const string detail = "Simulation did not run, so this check has no operating point.";
        if (circuit.Components.OfType<Counter>().SingleOrDefault() is { } counter)
        {
            return
            [
                CountSequence(circuit, counter),
                new("LED current", detail, CheckStatus.Unavailable),
                new("Resistor power", detail, CheckStatus.Unavailable),
            ];
        }

        var checks = new List<Check> { new("Output voltage", detail, CheckStatus.Unavailable) };
        checks.AddRange(circuit.Components.OfType<Capacitor>().Select(capacitor =>
            new Check($"{capacitor.Reference} voltage rating", detail, CheckStatus.Unavailable)));
        if (circuit.Components.OfType<LinearRegulator>().Any())
        {
            checks.Add(new Check("Regulator dissipation", detail, CheckStatus.Unavailable));
        }

        return checks;
    }

    private static Check CountSequence(Circuit circuit, Counter counter)
    {
        for (var step = 1; step <= counter.Steps; step++)
        {
            if (!HasBranch(circuit, counter, step))
            {
                return new Check(
                    "Count sequence",
                    Invariant($"Step {step} is missing a resistor and an LED."),
                    CheckStatus.Fail);
            }
        }

        for (var clock = 0; clock <= counter.Steps; clock++)
        {
            var expected = (clock % counter.Steps) + 1;
            if (ActiveStep(counter.Steps, clock) != expected)
            {
                return new Check("Count sequence", "The outputs did not step from 1 through 8 and back to 1.", CheckStatus.Fail);
            }
        }

        return new Check("Count sequence", "Outputs step 1, 2, 3, 4, 5, 6, 7, 8, then 1.", CheckStatus.Pass);
    }

    private static bool HasBranch(Circuit circuit, Counter counter, int step)
    {
        var output = circuit.Nets.SingleOrDefault(net =>
            net.Connections.Any(connection => connection.ComponentId == counter.Id && connection.Pin == $"q{step}"));
        var resistor = output?.Connections.Select(connection => connection.ComponentId)
            .Select(id => circuit.Components.SingleOrDefault(component => component.Id == id))
            .OfType<Resistor>()
            .SingleOrDefault();
        if (resistor is null)
        {
            return false;
        }

        var ledNet = circuit.Nets.SingleOrDefault(net =>
            net.Connections.Any(connection => connection.ComponentId == resistor.Id && connection.Pin == "b"));
        var led = ledNet?.Connections.Select(connection => connection.ComponentId)
            .Select(id => circuit.Components.SingleOrDefault(component => component.Id == id))
            .OfType<Led>()
            .SingleOrDefault();
        return led is not null && circuit.Nets.Any(net =>
            net.Name.Equals(circuit.GroundNet, StringComparison.OrdinalIgnoreCase)
            && net.Connections.Any(connection => connection.ComponentId == led.Id && connection.Pin == "cathode"));
    }

    private static Check LedCurrent(CircuitRequirement requirement, OperatingPoint operatingPoint)
    {
        var delta = Math.Abs(operatingPoint.LoadCurrentAmps - requirement.LoadCurrentAmps);
        var limit = Math.Max(0.001, Math.Abs(requirement.LoadCurrentAmps) * 0.1);
        var status = delta <= limit ? CheckStatus.Pass : CheckStatus.Fail;
        var detail = Invariant(
            $"{operatingPoint.LoadCurrentAmps * 1000:0.#} mA simulated, {requirement.LoadCurrentAmps * 1000:0.#} mA expected");
        return new Check("LED current", detail, status);
    }

    private static Check ResistorPower(Circuit circuit, Counter counter, OperatingPoint operatingPoint)
    {
        var resistor = circuit.Components.OfType<Resistor>().Single(component => component.Id == $"r{counter.ActiveStep}");
        var power = operatingPoint.LoadCurrentAmps * operatingPoint.LoadCurrentAmps * resistor.Ohms;
        var status = power <= resistor.PowerRatingWatts ? CheckStatus.Pass : CheckStatus.Fail;
        var detail = Invariant($"{power:0.00} W in {resistor.Reference}, {resistor.PowerRatingWatts:0.##} W rating");
        return new Check("Resistor power", detail, status);
    }

    private static Check OutputVoltage(CircuitRequirement requirement, OperatingPoint operatingPoint)
    {
        var delta = Math.Abs(operatingPoint.OutputVolts - requirement.OutputVolts);
        var limit = Math.Max(0.05, Math.Abs(requirement.OutputVolts) * 0.02);
        var status = delta <= limit ? CheckStatus.Pass : CheckStatus.Fail;
        var detail = Invariant(
            $"{operatingPoint.OutputVolts:0.00} V simulated, {requirement.OutputVolts:0.00} V expected");
        return new Check("Output voltage", detail, status);
    }

    private static Check CapacitorRating(Circuit circuit, Capacitor capacitor, OperatingPoint operatingPoint)
    {
        var applied = AppliedVolts(circuit, capacitor, operatingPoint);
        var status = capacitor.VoltageRatingVolts < applied ? CheckStatus.Fail : CheckStatus.Pass;
        var detail = Invariant($"{capacitor.VoltageRatingVolts:0.##} V rating, {applied:0.00} V applied");
        return new Check($"{capacitor.Reference} voltage rating", detail, status);
    }

    private static Check RegulatorHeat(LinearRegulator regulator, OperatingPoint operatingPoint)
    {
        var power = DissipationWatts(operatingPoint.InputVolts, operatingPoint.OutputVolts, operatingPoint.LoadCurrentAmps);
        var hot = !regulator.HasHeatsink && power >= regulator.ThermalWarningWatts;
        var status = hot ? CheckStatus.Warning : CheckStatus.Pass;
        var cooling = regulator.HasHeatsink ? "with a heatsink" : "in free air, no heatsink";
        var detail = Invariant($"{power:0.00} W {cooling}");
        return new Check("Regulator dissipation", detail, status);
    }

    private static double AppliedVolts(Circuit circuit, Capacitor capacitor, OperatingPoint operatingPoint)
    {
        var applied = 0d;
        foreach (var net in circuit.Nets.Where(net => net.Connections.Any(connection => connection.ComponentId == capacitor.Id)))
        {
            if (net.Name.Equals(circuit.GroundNet, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (net.Name.Equals(circuit.InputNet, StringComparison.OrdinalIgnoreCase))
            {
                applied = Math.Max(applied, Math.Abs(operatingPoint.InputVolts));
            }
            else if (net.Name.Equals(circuit.OutputNet, StringComparison.OrdinalIgnoreCase))
            {
                applied = Math.Max(applied, Math.Abs(operatingPoint.OutputVolts));
            }
        }

        return applied;
    }

    private static string Invariant(FormattableString value) =>
        value.ToString(CultureInfo.InvariantCulture);
}
