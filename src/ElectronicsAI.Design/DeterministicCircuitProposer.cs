using ElectronicsAI.Domain;

namespace ElectronicsAI.Design;

public interface ICircuitProposer
{
    Circuit Propose(CircuitRequirement requirement);
}

public sealed class DeterministicCircuitProposer : ICircuitProposer
{
    public Circuit Propose(CircuitRequirement requirement)
    {
        if (requirement.Family == CircuitFamily.Counter)
        {
            return BuildCounter(requirement);
        }

        if (requirement.Family is CircuitFamily.Inferred or CircuitFamily.LinearRegulator && IsRegulatorSupply(requirement))
        {
            return BuildRegulatorSupply(requirement);
        }

        if (requirement.Family is CircuitFamily.Inferred or CircuitFamily.Divider && IsDivider(requirement))
        {
            return BuildDivider(requirement);
        }

        throw new UnsupportedRequirementException();
    }

    public static bool IsDivider(CircuitRequirement requirement) =>
        Near(requirement.InputVolts, 12, 0.5)
        && Near(requirement.OutputVolts, 3.3, 0.15)
        && requirement.LoadCurrentAmps <= 0.001;

    public static bool IsRegulatorSupply(CircuitRequirement requirement) =>
        Near(requirement.InputVolts, 12, 0.5)
        && Near(requirement.OutputVolts, 5, 0.2)
        && requirement.LoadCurrentAmps is >= 0.15 and <= 0.25;

    private static Circuit BuildDivider(CircuitRequirement requirement)
    {
        VoltageSource source = new()
        {
            Id = "vin",
            Reference = "V1",
            Volts = requirement.InputVolts,
        };
        Resistor upper = new()
        {
            Id = "r1",
            Reference = "R1",
            Ohms = 26_100,
        };
        Resistor lower = new()
        {
            Id = "r2",
            Reference = "R2",
            Ohms = 10_000,
        };

        return new Circuit(
            "12 V to 3.3 V divider",
            InputNet: "VIN",
            OutputNet: "VOUT",
            GroundNet: "GND",
            ProbeComponentId: lower.Id,
            Components: [source, upper, lower],
            Nets:
            [
                new("VIN", [new(source.Id, "positive"), new(upper.Id, "a")]),
                new("VOUT", [new(upper.Id, "b"), new(lower.Id, "a")]),
                new("GND", [new(source.Id, "negative"), new(lower.Id, "b")]),
            ]);
    }

    private static Circuit BuildRegulatorSupply(CircuitRequirement requirement)
    {
        var loadOhms = requirement.OutputVolts / requirement.LoadCurrentAmps;

        VoltageSource source = new()
        {
            Id = "vin",
            Reference = "V1",
            Volts = requirement.InputVolts,
        };
        Capacitor inputCap = new()
        {
            Id = "c1",
            Reference = "C1",
            Farads = 100e-6,
            VoltageRatingVolts = 25,
        };
        LinearRegulator regulator = new()
        {
            Id = "u1",
            Reference = "U1",
        };
        Capacitor outputCap = new()
        {
            Id = "c2",
            Reference = "C2",
            Farads = 10e-6,
            VoltageRatingVolts = 16,
        };
        Resistor load = new()
        {
            Id = "rload",
            Reference = "RLOAD",
            Ohms = loadOhms,
            PowerRatingWatts = 1,
        };

        return new Circuit(
            "12 V to 5 V supply",
            InputNet: "VIN",
            OutputNet: "VOUT",
            GroundNet: "GND",
            ProbeComponentId: load.Id,
            Components: [source, inputCap, regulator, outputCap, load],
            Nets:
            [
                new("VIN",
                [
                    new(source.Id, "positive"),
                    new(inputCap.Id, "a"),
                    new(regulator.Id, "in"),
                ]),
                new("VOUT",
                [
                    new(regulator.Id, "out"),
                    new(outputCap.Id, "a"),
                    new(load.Id, "a"),
                ]),
                new("GND",
                [
                    new(source.Id, "negative"),
                    new(inputCap.Id, "b"),
                    new(regulator.Id, "gnd"),
                    new(outputCap.Id, "b"),
                    new(load.Id, "b"),
                ]),
            ]);
    }

    private static Circuit BuildCounter(CircuitRequirement requirement)
    {
        if (requirement.Steps != 8)
        {
            throw new UnsupportedRequirementException("This version builds a counter from 1 to 8.");
        }

        if (requirement.InputVolts is < 3 or > 15)
        {
            throw new UnsupportedRequirementException("The counter supply in this version is 3 V to 15 V.");
        }

        if (requirement.LoadCurrentAmps is < 0.001 or > 0.02)
        {
            throw new UnsupportedRequirementException("Each LED in this version is about 1 mA to 20 mA.");
        }

        if (requirement.ClockHertz is <= 0 or > 10_000)
        {
            throw new UnsupportedRequirementException("The counter clock in this version is between a fraction of a hertz and 10 kHz.");
        }

        var forward = requirement.ForwardVolts;
        if (requirement.InputVolts - forward < 0.5)
        {
            throw new UnsupportedRequirementException("The LED forward voltage has to sit below the supply.");
        }

        var ohms = (requirement.InputVolts - forward) / requirement.LoadCurrentAmps;
        var dissipation = requirement.LoadCurrentAmps * requirement.LoadCurrentAmps * ohms;
        if (dissipation > 0.5)
        {
            throw new UnsupportedRequirementException("The LED resistor would dissipate more than 0.5 W.");
        }

        VoltageSource supply = new()
        {
            Id = "vcc",
            Reference = "V1",
            Volts = requirement.InputVolts,
        };
        Clock clock = new()
        {
            Id = "clk",
            Reference = "CLK",
            Hertz = requirement.ClockHertz,
        };
        Counter counter = new()
        {
            Id = "u1",
            Reference = "U1",
            Steps = requirement.Steps,
            ActiveStep = 1,
        };

        var components = new List<Component> { supply, clock, counter };
        var nets = new List<Net>
        {
            new("CLK", [new(clock.Id, "out"), new(counter.Id, "clock")]),
            new("VCC", [new(supply.Id, "positive"), new(counter.Id, "vcc")]),
        };
        var ground = new List<Connection> { new(supply.Id, "negative"), new(counter.Id, "gnd") };

        for (var step = 1; step <= requirement.Steps; step++)
        {
            Resistor resistor = new()
            {
                Id = $"r{step}",
                Reference = $"R{step}",
                Ohms = ohms,
                PowerRatingWatts = dissipation <= 0.25 ? 0.25 : 0.5,
            };
            Led led = new()
            {
                Id = $"led{step}",
                Reference = $"LED{step}",
                ForwardVolts = forward,
            };
            components.Add(resistor);
            components.Add(led);
            nets.Add(new($"Q{step}", [new(counter.Id, $"q{step}"), new(resistor.Id, "a")]));
            nets.Add(new($"LED{step}", [new(resistor.Id, "b"), new(led.Id, "anode")]));
            ground.Add(new(led.Id, "cathode"));
        }

        nets.Add(new("GND", ground));
        return new Circuit(
            "Counter from 1 to 8",
            InputNet: "Q1",
            OutputNet: "LED1",
            GroundNet: "GND",
            ProbeComponentId: "r1",
            Components: components,
            Nets: nets);
    }

    private static bool Near(double value, double target, double tolerance) =>
        Math.Abs(value - target) <= tolerance;
}
