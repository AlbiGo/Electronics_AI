namespace ElectronicsAI.Domain;

public enum ComponentKind
{
    VoltageSource,
    Resistor,
    Capacitor,
    LinearRegulator,
    Clock,
    Counter,
    Led,
}

public abstract record Component
{
    public required string Id { get; init; }

    public required string Reference { get; init; }

    public abstract ComponentKind Kind { get; }

    public abstract IReadOnlyList<string> Pins { get; }
}

public sealed record VoltageSource : Component
{
    public required double Volts { get; init; }

    public override ComponentKind Kind => ComponentKind.VoltageSource;

    public override IReadOnlyList<string> Pins => ["positive", "negative"];
}

public sealed record Resistor : Component
{
    public required double Ohms { get; init; }

    public double PowerRatingWatts { get; init; } = 0.25;

    public override ComponentKind Kind => ComponentKind.Resistor;

    public override IReadOnlyList<string> Pins => ["a", "b"];
}

public sealed record Capacitor : Component
{
    public required double Farads { get; init; }

    public required double VoltageRatingVolts { get; init; }

    public override ComponentKind Kind => ComponentKind.Capacitor;

    public override IReadOnlyList<string> Pins => ["a", "b"];
}

public sealed record LinearRegulator : Component
{
    public string PartNumber { get; init; } = "LM7805";

    public double NominalOutputVolts { get; init; } = 5;

    public double MaxOutputCurrentAmps { get; init; } = 1;

    public double ThermalWarningWatts { get; init; } = 1.2;

    public bool HasHeatsink { get; init; }

    /// <summary>
    /// Series resistance after the ideal regulator. 0.2 ohm drops a 200 mA load to about 4.96 V.
    /// </summary>
    public double OutputSeriesOhms { get; init; } = 0.2;

    public override ComponentKind Kind => ComponentKind.LinearRegulator;

    public override IReadOnlyList<string> Pins => ["in", "out", "gnd"];
}

public sealed record Clock : Component
{
    public required double Hertz { get; init; }

    public override ComponentKind Kind => ComponentKind.Clock;

    public override IReadOnlyList<string> Pins => ["out"];
}

public sealed record Counter : Component
{
    public int Steps { get; init; } = 8;

    public int ActiveStep { get; init; } = 1;

    public override ComponentKind Kind => ComponentKind.Counter;

    public override IReadOnlyList<string> Pins
    {
        get
        {
            var pins = new List<string> { "clock", "vcc", "gnd" };
            for (var step = 1; step <= Steps; step++)
            {
                pins.Add($"q{step}");
            }

            return pins;
        }
    }
}

public sealed record Led : Component
{
    public required double ForwardVolts { get; init; }

    public override ComponentKind Kind => ComponentKind.Led;

    public override IReadOnlyList<string> Pins => ["anode", "cathode"];
}

public sealed record Connection(string ComponentId, string Pin);

public sealed record Net(string Name, IReadOnlyList<Connection> Connections);

public sealed record Circuit(
    string Name,
    string InputNet,
    string OutputNet,
    string GroundNet,
    string? ProbeComponentId,
    IReadOnlyList<Component> Components,
    IReadOnlyList<Net> Nets);
