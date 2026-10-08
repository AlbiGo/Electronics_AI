namespace ElectronicsAI.Design;

public sealed class LedCircuitValidator : ISketchRuleSet
{
    public CircuitCategory Category => CircuitCategory.Unknown;

    public bool Applies(SchematicSketch sketch, string? request)
    {
        var parts = sketch.Parts;
        return parts.Any(part => SketchPartKinds.Of(part) == SketchPartKind.Led)
            && parts.All(part => SketchPartKinds.Of(part) is SketchPartKind.Led or SketchPartKind.Resistor or SketchPartKind.Capacitor or SketchPartKind.Supply or SketchPartKind.Ground or SketchPartKind.Node or SketchPartKind.Switch);
    }

    public SketchRuleResult Evaluate(SchematicSketch sketch, string? request)
    {
        var board = SketchBoard.Of(sketch);
        var rules = new[]
        {
            LedRequiresResistorRule.Present(board),
            Wiring(board),
            Power(board),
            Ground(board),
        };
        return new SketchRuleResult("LED circuit checked before simulation.", rules, [], Simulate: rules.All(rule => rule.Result == "Pass"));
    }

    private static SketchRule Wiring(SketchBoard board) =>
        SeriesPath(board)
            ? new SketchRule("Correct wiring", "The LED is in series between the supply and ground.", "Pass")
            : new SketchRule("Correct wiring", "The LED is not wired from the supply to ground through a resistor.", "Fail");

    private static SketchRule Power(SketchBoard board)
    {
        var (supply, _) = Ends(board);
        return supply.Count > 0
            ? new SketchRule("Power source present", "The circuit has a supply.", "Pass")
            : new SketchRule("Power source present", "The circuit has no supply.", "Fail");
    }

    private static SketchRule Ground(SketchBoard board)
    {
        var (_, ground) = Ends(board);
        return ground.Count > 0
            ? new SketchRule("Ground present", "The circuit has a ground.", "Pass")
            : new SketchRule("Ground present", "The circuit has no ground.", "Fail");
    }

    private static bool SeriesPath(SketchBoard board)
    {
        var split = SketchBoard.Of(board.Sketch with { Wires = SplitLeads(board) });
        var nets = split.Nets;
        var edges = new List<(int From, int To, bool Led, bool Resistor)>();
        foreach (var part in board.Sketch.Parts)
        {
            var kind = SketchPartKinds.Of(part);
            if (kind is not (SketchPartKind.Led or SketchPartKind.Resistor or SketchPartKind.Switch))
            {
                continue;
            }

            var touched = new List<int>();
            for (var index = 0; index < nets.Count; index++)
            {
                if (nets[index].Any(end => SketchBoard.Belongs(end, part)))
                {
                    touched.Add(index);
                }
            }

            if (touched.Count >= 2)
            {
                edges.Add((touched[0], touched[1], kind == SketchPartKind.Led, kind == SketchPartKind.Resistor));
            }
        }

        var (supply, ground) = Ends(split);
        var pending = new Queue<(int Net, bool Led, bool Resistor)>();
        var seen = new HashSet<(int Net, bool Led, bool Resistor)>();
        foreach (var net in supply)
        {
            pending.Enqueue((net, false, false));
        }

        while (pending.Count > 0)
        {
            var state = pending.Dequeue();
            if (!seen.Add(state))
            {
                continue;
            }

            if (ground.Contains(state.Net) && state.Led && state.Resistor)
            {
                return true;
            }

            foreach (var edge in edges)
            {
                int? next = edge.From == state.Net ? edge.To : edge.To == state.Net ? edge.From : null;
                if (next is { } other)
                {
                    pending.Enqueue((other, state.Led || edge.Led, state.Resistor || edge.Resistor));
                }
            }
        }

        return false;
    }

    private static List<SketchWire> SplitLeads(SketchBoard board)
    {
        var wires = board.Sketch.Wires.Select(wire => new SketchWire(wire.From.Trim(), wire.To.Trim())).ToList();
        foreach (var part in board.Sketch.Parts)
        {
            var kind = SketchPartKinds.Of(part);
            if (kind is not (SketchPartKind.Led or SketchPartKind.Resistor or SketchPartKind.Switch))
            {
                continue;
            }

            var hits = new List<int>();
            for (var index = 0; index < wires.Count; index++)
            {
                if (On(wires[index].From, part.Id) || On(wires[index].To, part.Id))
                {
                    hits.Add(index);
                }
            }

            if (hits.Count != 2)
            {
                continue;
            }

            var pins = hits.Select(index => Lead(wires[index], part.Id)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (pins.Count >= 2)
            {
                continue;
            }

            for (var slot = 0; slot < hits.Count; slot++)
            {
                var wire = wires[hits[slot]];
                var lead = part.Id + "." + (slot == 0 ? "a" : "b");
                wires[hits[slot]] = new SketchWire(
                    wire.From.Equals(part.Id, StringComparison.OrdinalIgnoreCase) ? lead : wire.From,
                    wire.To.Equals(part.Id, StringComparison.OrdinalIgnoreCase) ? lead : wire.To);
            }
        }

        return wires;
    }

    private static bool On(string end, string id) =>
        end.Equals(id, StringComparison.OrdinalIgnoreCase)
        || end.StartsWith(id + ".", StringComparison.OrdinalIgnoreCase);

    private static string Lead(SketchWire wire, string id) =>
        On(wire.From, id) ? wire.From : wire.To;

    private static (HashSet<int> Supply, HashSet<int> Ground) Ends(SketchBoard board)
    {
        var supply = new HashSet<int>();
        var ground = new HashSet<int>();
        for (var index = 0; index < board.Nets.Count; index++)
        {
            var net = board.Nets[index];
            if (net.Any(WireEnds.IsSupply) || net.Any(end => board.Owner(end) is { } part && SketchPartKinds.Of(part) == SketchPartKind.Supply))
            {
                supply.Add(index);
            }

            if (net.Any(WireEnds.IsGround) || net.Any(end => board.Marked(end, Mark.Ground)))
            {
                ground.Add(index);
            }
        }

        return (supply, ground);
    }
}
