using System.Globalization;
using System.Text.RegularExpressions;
using ElectronicsAI.Domain;

namespace ElectronicsAI.Design;

public sealed partial class CircuitCompiler
{
    public const double DefaultSupplyVolts = 5;
    public const double DefaultLedForwardVolts = 2;

    public Circuit Compile(SchematicSketch sketch)
    {
        sketch = CloseSwitches(sketch);
        if (sketch.Parts.Count == 0)
        {
            throw new CircuitCompileException("This schematic has no ngspice model yet.");
        }

        var kinds = sketch.Parts.ToDictionary(part => part.Id, SketchPartKinds.Of, StringComparer.OrdinalIgnoreCase);
        if (kinds.Values.Any(kind => kind == SketchPartKind.Unknown))
        {
            throw new CircuitCompileException("This schematic has no ngspice model yet.");
        }
        var resistors = new List<Resistor>();
        var capacitors = new List<Capacitor>();
        var leds = new List<Led>();
        foreach (var part in sketch.Parts)
        {
            var kind = kinds[part.Id];
            if (kind == SketchPartKind.Resistor)
            {
                if (!TryOhms(part.Note, out var ohms) && !TryOhms(part.Name, out ohms, requireUnit: true))
                {
                    throw new CircuitCompileException($"Resistor {part.Id} has no resistance. Put the ohms in its note, such as 330 ohm.");
                }

                resistors.Add(new Resistor { Id = part.Id, Reference = Reference(part.Id, "R"), Ohms = ohms });
            }
            else if (kind == SketchPartKind.Capacitor)
            {
                if (!TryFarads(part.Note, out var farads))
                {
                    throw new CircuitCompileException($"Capacitor {part.Id} has no capacitance.");
                }

                capacitors.Add(new Capacitor
                {
                    Id = part.Id,
                    Reference = Reference(part.Id, "C"),
                    Farads = farads,
                    VoltageRatingVolts = 16,
                });
            }
            else if (kind == SketchPartKind.Led)
            {
                var forward = TryVolts(part.Note, out var volts) ? volts : DefaultLedForwardVolts;
                leds.Add(new Led { Id = part.Id, Reference = Reference(part.Id, "LED"), ForwardVolts = forward });
            }
        }

        if (resistors.Count == 0)
        {
            throw new CircuitCompileException("This schematic has no ngspice model yet.");
        }

        var endpoints = new Dictionary<string, Endpoint>(StringComparer.OrdinalIgnoreCase);
        var groups = new UnionFind();
        foreach (var wire in NormalizeWires(sketch, kinds))
        {
            var from = Resolve(wire.From, sketch.Parts, kinds);
            var to = Resolve(wire.To, sketch.Parts, kinds);
            endpoints[from.Key] = from;
            endpoints[to.Key] = to;
            groups.Union(from.Key, to.Key);
        }

        if (!endpoints.Values.Any(endpoint => endpoint.External is not null && IsSupply(endpoint.External)))
        {
            throw new CircuitCompileException("The sketch has no VCC supply to simulate.");
        }

        if (!endpoints.Values.Any(endpoint => endpoint.External is not null && IsGround(endpoint.External)))
        {
            throw new CircuitCompileException("The sketch has no GND net to simulate.");
        }

        var supplyLabel = endpoints.Values
            .Select(endpoint => endpoint.External)
            .First(label => label is not null && IsSupply(label) && !IsGround(label))!;
        var supplyPart = sketch.Parts.FirstOrDefault(part => kinds[part.Id] == SketchPartKind.Supply);
        var supplyVolts = supplyPart is not null && TryVolts(supplyPart.Note, out var noted)
            ? noted
            : TryVolts(supplyLabel, out var labeled) ? labeled : DefaultSupplyVolts;
        VoltageSource supply = new()
        {
            Id = "vcc",
            Reference = "V1",
            Volts = supplyVolts,
        };

        var nets = new List<Net>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var anonymous = 0;
        foreach (var key in endpoints.Keys)
        {
            var root = groups.Find(key);
            if (!seen.Add(root))
            {
                continue;
            }

            var members = endpoints.Where(pair => groups.Find(pair.Key).Equals(root, StringComparison.OrdinalIgnoreCase)).Select(pair => pair.Value).ToList();
            var ground = members.Any(member => member.External is not null && IsGround(member.External));
            var supplyNet = members.Select(member => member.External).FirstOrDefault(label => label is not null && IsSupply(label) && !IsGround(label));
            if (ground && supplyNet is not null)
            {
                throw new CircuitCompileException("VCC is shorted to GND.");
            }

            var name = ground ? "GND" : supplyNet ?? $"n{++anonymous}";
            var connections = members
                .Where(member => member.PartId is not null && member.SpicePin is not null)
                .Select(member => new Connection(member.PartId!, member.SpicePin!))
                .ToList();
            if (supplyNet is not null)
            {
                connections.Add(new Connection(supply.Id, "positive"));
            }

            if (ground)
            {
                connections.Add(new Connection(supply.Id, "negative"));
            }

            nets.Add(new Net(name, connections));
        }

        var outputIndex = nets.FindIndex(net =>
            net.Connections.Any(connection => leds.Any(led => led.Id.Equals(connection.ComponentId, StringComparison.OrdinalIgnoreCase) && connection.Pin == "anode")));
        if (outputIndex < 0)
        {
            outputIndex = nets.FindIndex(net =>
                !net.Name.Equals("GND", StringComparison.OrdinalIgnoreCase)
                && !net.Name.Equals(supplyLabel, StringComparison.OrdinalIgnoreCase));
        }

        if (outputIndex < 0)
        {
            outputIndex = nets.FindIndex(net => net.Name.Equals(supplyLabel, StringComparison.OrdinalIgnoreCase));
        }

        if (nets[outputIndex].Name.StartsWith('n'))
        {
            nets[outputIndex] = nets[outputIndex] with { Name = "out" };
        }

        var output = nets[outputIndex];

        var probe = Probe(resistors, leds, nets);
        var components = new List<Component> { supply };
        components.AddRange(resistors);
        components.AddRange(capacitors);
        components.AddRange(leds);
        return new Circuit(
            sketch.Title,
            supplyLabel.Equals("VCC", StringComparison.OrdinalIgnoreCase) ? "VCC" : supplyLabel,
            output.Name,
            "GND",
            probe.Id,
            components,
            nets);
    }

    private static SchematicSketch CloseSwitches(SchematicSketch sketch)
    {
        var switches = sketch.Parts.Where(part => SketchPartKinds.Of(part) == SketchPartKind.Switch).ToList();
        if (switches.Count == 0)
        {
            return sketch;
        }

        var wires = sketch.Wires.ToList();
        foreach (var part in switches)
        {
            var attached = wires.Where(wire => EndsOn(wire.From, part.Id) || EndsOn(wire.To, part.Id)).ToList();
            if (attached.Count != 2)
            {
                throw new CircuitCompileException($"Switch {part.Name} needs two wires to test the closed circuit.");
            }

            wires.Remove(attached[0]);
            wires.Remove(attached[1]);
            wires.Add(new SketchWire(Other(attached[0], part.Id), Other(attached[1], part.Id)));
        }

        return sketch with
        {
            Parts = sketch.Parts.Where(part => SketchPartKinds.Of(part) != SketchPartKind.Switch).ToList(),
            Wires = wires,
        };
    }

    private static bool EndsOn(string end, string partId)
    {
        var text = end.Trim();
        var id = text.Split('.')[0];
        return id.Equals(partId, StringComparison.OrdinalIgnoreCase);
    }

    private static string Other(SketchWire wire, string partId) =>
        EndsOn(wire.From, partId) ? wire.To : wire.From;

    private static Resistor Probe(IReadOnlyList<Resistor> resistors, IReadOnlyList<Led> leds, IReadOnlyList<Net> nets)
    {
        foreach (var led in leds)
        {
            var anode = nets.FirstOrDefault(net => net.Connections.Any(connection => connection.ComponentId == led.Id && connection.Pin == "anode"));
            var resistor = anode?.Connections
                .Select(connection => resistors.FirstOrDefault(item => item.Id.Equals(connection.ComponentId, StringComparison.OrdinalIgnoreCase)))
                .FirstOrDefault(item => item is not null);
            if (resistor is not null)
            {
                return resistor;
            }
        }

        return resistors[0];
    }

    private static Endpoint Resolve(string raw, IReadOnlyList<SketchPart> parts, IReadOnlyDictionary<string, SketchPartKind> kinds)
    {
        var text = raw.Trim();
        var dot = text.IndexOf('.');
        if (dot < 0)
        {
            var whole = parts.FirstOrDefault(part => part.Id.Equals(text, StringComparison.OrdinalIgnoreCase));
            if (whole is not null)
            {
                var wholeKind = kinds[whole.Id];
                if (wholeKind == SketchPartKind.Ground)
                {
                    return new Endpoint("ext:GND", null, null, "GND");
                }

                if (wholeKind == SketchPartKind.Node)
                {
                    return new Endpoint("ext:" + whole.Id, null, null, whole.Id);
                }

                throw new CircuitCompileException($"Wire end {text} needs a pin.");
            }

            return new Endpoint("ext:" + text.ToUpperInvariant(), null, null, text);
        }

        var part = parts.FirstOrDefault(item => item.Id.Equals(text[..dot], StringComparison.OrdinalIgnoreCase))
            ?? throw new CircuitCompileException($"Wire end {text} does not name a part.");
        var pinName = text[(dot + 1)..];
        if (kinds[part.Id] == SketchPartKind.Supply)
        {
            return pinName.Contains("neg", StringComparison.OrdinalIgnoreCase)
                ? new Endpoint("ext:GND", null, null, "GND")
                : new Endpoint("ext:VCC", null, null, "VCC");
        }

        var pin = SpicePin(part, kinds[part.Id], pinName);
        return new Endpoint(part.Id + ":" + pin, part.Id, pin, null);
    }

    private static List<SketchWire> NormalizeWires(SchematicSketch sketch, IReadOnlyDictionary<string, SketchPartKind> kinds)
    {
        var wires = sketch.Wires.ToList();
        string? Bare(string end)
        {
            var text = end.Trim();
            if (text.Contains('.'))
            {
                return null;
            }

            return sketch.Parts.FirstOrDefault(part => part.Id.Equals(text, StringComparison.OrdinalIgnoreCase))?.Id;
        }

        SketchPartKind Kind(string end)
        {
            var id = Bare(end);
            return id is null ? SketchPartKind.Unknown : kinds[id];
        }

        for (var index = 0; index < wires.Count; index++)
        {
            var wire = wires[index];
            wires[index] = new SketchWire(MapSupply(wire.From, wire.To), MapSupply(wire.To, wire.From));
        }

        string MapSupply(string end, string other)
        {
            if (Kind(end) != SketchPartKind.Supply)
            {
                return end.Trim();
            }

            var id = Bare(end);
            return Kind(other) == SketchPartKind.Ground || IsGround(other.Trim()) ? id + ".negative" : id + ".positive";
        }

        foreach (var part in sketch.Parts)
        {
            var kind = kinds[part.Id];
            if (kind is not (SketchPartKind.Resistor or SketchPartKind.Led or SketchPartKind.Capacitor))
            {
                continue;
            }

            var hits = new List<(int Index, bool From)>();
            for (var index = 0; index < wires.Count; index++)
            {
                if (Bare(wires[index].From)?.Equals(part.Id, StringComparison.OrdinalIgnoreCase) == true)
                {
                    hits.Add((index, true));
                }

                if (Bare(wires[index].To)?.Equals(part.Id, StringComparison.OrdinalIgnoreCase) == true)
                {
                    hits.Add((index, false));
                }
            }

            if (hits.Count == 0)
            {
                continue;
            }

            if (hits.Count > 2)
            {
                throw new CircuitCompileException($"Part {part.Id} has more than two connections.");
            }

            var pins = kind == SketchPartKind.Led ? new[] { "anode", "cathode" } : new[] { "a", "b" };
            for (var pin = 0; pin < hits.Count; pin++)
            {
                var (index, from) = hits[pin];
                var wire = wires[index];
                var rewritten = part.Id + "." + pins[pin];
                wires[index] = from ? wire with { From = rewritten } : wire with { To = rewritten };
            }
        }

        return wires;
    }

    private static string SpicePin(SketchPart part, SketchPartKind kind, string pin)
    {
        var index = part.Pins.ToList().FindIndex(item => item.Equals(pin, StringComparison.OrdinalIgnoreCase));
        if (kind is SketchPartKind.Resistor or SketchPartKind.Capacitor)
        {
            if (pin.Equals("a", StringComparison.OrdinalIgnoreCase) || pin == "1" || index == 0)
            {
                return "a";
            }

            if (pin.Equals("b", StringComparison.OrdinalIgnoreCase) || pin == "2" || index == 1)
            {
                return "b";
            }
        }

        if (kind == SketchPartKind.Led)
        {
            if (pin.Contains("anode", StringComparison.OrdinalIgnoreCase) || pin.Equals("a", StringComparison.OrdinalIgnoreCase) || index == 0)
            {
                return "anode";
            }

            if (pin.Contains("cathode", StringComparison.OrdinalIgnoreCase) || pin.Equals("k", StringComparison.OrdinalIgnoreCase) || index == 1)
            {
                return "cathode";
            }
        }

        throw new CircuitCompileException($"Pin {pin} on {part.Id} cannot be simulated.");
    }

    private static string Reference(string id, string prefix)
    {
        var digits = new string(id.Where(char.IsDigit).ToArray());
        return prefix + (digits.Length == 0 ? "1" : digits);
    }

    private static bool IsGround(string label) =>
        label.Equals("GND", StringComparison.OrdinalIgnoreCase)
        || label.Equals("VSS", StringComparison.OrdinalIgnoreCase)
        || label.Equals("AGND", StringComparison.OrdinalIgnoreCase)
        || label == "0";

    private static bool IsSupply(string label) =>
        label.Equals("VCC", StringComparison.OrdinalIgnoreCase)
        || label.Equals("VDD", StringComparison.OrdinalIgnoreCase)
        || label.Equals("VIN", StringComparison.OrdinalIgnoreCase)
        || TryVolts(label, out _);

    private static bool TryOhms(string? text, out double ohms, bool requireUnit = false)
    {
        ohms = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var match = OhmsPattern().Match(text);
        if (!match.Success || (requireUnit && match.Groups["u"].Length == 0) || !double.TryParse(match.Groups["n"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return false;
        }

        ohms = match.Groups["u"].Value.ToLowerInvariant() switch
        {
            "k" => number * 1_000,
            "meg" => number * 1_000_000,
            _ => number,
        };
        return ohms > 0;
    }

    private static bool TryFarads(string? text, out double farads)
    {
        farads = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var match = FaradsPattern().Match(text);
        if (!match.Success || !double.TryParse(match.Groups["n"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return false;
        }

        farads = match.Groups["u"].Value.ToLowerInvariant() switch
        {
            "pf" or "p" => number * 1e-12,
            "nf" or "n" => number * 1e-9,
            "uf" or "µf" or "μf" or "u" or "µ" or "μ" => number * 1e-6,
            "mf" or "m" => number * 1e-3,
            _ => number,
        };
        return farads > 0;
    }

    private static bool TryVolts(string? text, out double volts)
    {
        volts = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var match = VoltsPattern().Match(text);
        if (!match.Success || !double.TryParse(match.Groups["n"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out volts))
        {
            return false;
        }

        return volts > 0;
    }

    [GeneratedRegex(@"(?<n>\d+(?:\.\d+)?)\s*(?<u>meg|k|ohm|ohms|ω|Ω)?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OhmsPattern();

    [GeneratedRegex(@"(?<n>\d+(?:\.\d+)?)\s*v\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VoltsPattern();

    [GeneratedRegex(@"(?<n>\d+(?:\.\d+)?)\s*(?<u>pf|nf|uf|µf|μf|mf|f|p|n|u|µ|μ|m)?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FaradsPattern();

    private sealed record Endpoint(string Key, string? PartId, string? SpicePin, string? External);

    private sealed class UnionFind
    {
        private readonly Dictionary<string, string> parent = new(StringComparer.OrdinalIgnoreCase);

        public string Find(string key)
        {
            if (!parent.TryGetValue(key, out var current))
            {
                parent[key] = key;
                return key;
            }

            var root = current;
            while (!parent[root].Equals(root, StringComparison.OrdinalIgnoreCase))
            {
                root = parent[root];
            }

            parent[key] = root;
            return root;
        }

        public void Union(string left, string right)
        {
            var leftRoot = Find(left);
            var rightRoot = Find(right);
            if (!leftRoot.Equals(rightRoot, StringComparison.OrdinalIgnoreCase))
            {
                parent[leftRoot] = rightRoot;
            }
        }
    }
}
