using System.Globalization;
using System.Text;
using ElectronicsAI.Domain;

namespace ElectronicsAI.Simulation;

public sealed class SpiceNetlistWriter
{
    public string Write(Circuit circuit)
    {
        var builder = new StringBuilder();
        builder.Append("* ElectronicsAI ").AppendLine(circuit.Name);

        var counter = circuit.Components.OfType<Counter>().SingleOrDefault();
        foreach (var component in circuit.Components)
        {
            if (!Include(component, counter))
            {
                continue;
            }

            switch (component)
            {
                case VoltageSource source:
                    builder.Append(source.Reference)
                        .Append(' ')
                        .Append(Node(circuit, source.Id, "positive"))
                        .Append(' ')
                        .Append(Node(circuit, source.Id, "negative"))
                        .Append(" DC ")
                        .AppendLine(FormatValue(source.Volts));
                    break;
                case Resistor resistor:
                    builder.Append(resistor.Reference)
                        .Append(' ')
                        .Append(Node(circuit, resistor.Id, "a"))
                        .Append(' ')
                        .Append(Node(circuit, resistor.Id, "b"))
                        .Append(' ')
                        .AppendLine(FormatValue(resistor.Ohms));
                    break;
                case Capacitor capacitor:
                    builder.Append(capacitor.Reference)
                        .Append(' ')
                        .Append(Node(circuit, capacitor.Id, "a"))
                        .Append(' ')
                        .Append(Node(circuit, capacitor.Id, "b"))
                        .Append(' ')
                        .AppendLine(FormatValue(capacitor.Farads));
                    break;
                case LinearRegulator regulator:
                    builder.Append('X').Append(regulator.Reference)
                        .Append(' ')
                        .Append(Node(circuit, regulator.Id, "in"))
                        .Append(' ')
                        .Append(Node(circuit, regulator.Id, "out"))
                        .Append(' ')
                        .Append(Node(circuit, regulator.Id, "gnd"))
                        .Append(' ')
                        .AppendLine(SubcircuitName(regulator));
                    break;
                case Counter active:
                    var supply = circuit.Components.OfType<VoltageSource>().Single();
                    builder.Append("VQ").Append(active.ActiveStep)
                        .Append(' ')
                        .Append(Node(circuit, active.Id, $"q{active.ActiveStep}"))
                        .Append(" 0 DC ")
                        .AppendLine(FormatValue(supply.Volts));
                    break;
                case Led led:
                    builder.Append('V').Append(led.Reference)
                        .Append(' ')
                        .Append(Node(circuit, led.Id, "anode"))
                        .Append(' ')
                        .Append(Node(circuit, led.Id, "cathode"))
                        .Append(" DC ")
                        .AppendLine(FormatValue(led.ForwardVolts));
                    break;
                case Clock:
                    break;
            }
        }

        foreach (var regulator in circuit.Components.OfType<LinearRegulator>())
        {
            var name = SubcircuitName(regulator);
            builder.Append(".subckt ").Append(name).AppendLine(" in out gnd");
            builder.AppendLine("* Ideal 5 V source. Load current is supplied here; dissipation is computed from the operating point.");
            builder.AppendLine("Rbias in gnd 1G");
            builder.Append("Videal mid gnd DC ").AppendLine(FormatValue(regulator.NominalOutputVolts));
            builder.Append("Rseries mid out ").AppendLine(FormatValue(regulator.OutputSeriesOhms));
            builder.AppendLine(".ends");
        }

        builder.AppendLine(".op");
        builder.Append(".print op v(").Append(NodeName(circuit.OutputNet)).Append(") v(").Append(NodeName(circuit.InputNet)).Append(')');
        if (circuit.ProbeComponentId is not null)
        {
            var probe = circuit.Components.Single(component => component.Id == circuit.ProbeComponentId);
            builder.Append(" i(").Append(probe.Reference).Append(')');
        }

        builder.AppendLine();
        builder.AppendLine(".end");
        return builder.ToString();
    }

    public static string FormatValue(double value)
    {
        var abs = Math.Abs(value);
        if (abs == 0)
        {
            return "0";
        }

        (double Scale, string Suffix)[] units =
        [
            (1e12, "T"),
            (1e9, "G"),
            (1e6, "Meg"),
            (1e3, "k"),
            (1, ""),
            (1e-3, "m"),
            (1e-6, "u"),
            (1e-9, "n"),
            (1e-12, "p"),
        ];

        foreach (var (scale, suffix) in units)
        {
            if (abs + 1e-15 >= scale)
            {
                var scaled = value / scale;
                return scaled.ToString("0.###", CultureInfo.InvariantCulture) + suffix;
            }
        }

        return value.ToString("0.###e0", CultureInfo.InvariantCulture);
    }

    private static bool Include(Component component, Counter? counter)
    {
        if (counter is null || component is Counter or VoltageSource or Clock)
        {
            return true;
        }

        return component.Id == $"r{counter.ActiveStep}" || component.Id == $"led{counter.ActiveStep}";
    }

    private static string SubcircuitName(LinearRegulator regulator) =>
        regulator.PartNumber.ToLowerInvariant();

    private static string Node(Circuit circuit, string componentId, string pin)
    {
        var net = circuit.Nets.Single(candidate =>
            candidate.Connections.Any(connection => connection.ComponentId == componentId && connection.Pin == pin));
        return NodeName(net.Name);
    }

    private static string NodeName(string net) =>
        net.Equals("GND", StringComparison.OrdinalIgnoreCase) || net == "0"
            ? "0"
            : net.ToLowerInvariant();
}
