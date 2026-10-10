namespace ElectronicsAI.Design;

public sealed record LayoutPin(string Name, double X, double Y, string Side);

public sealed record LayoutPart(string Id, double X, double Y, double Width, double Height, bool Upright, IReadOnlyList<LayoutPin> Pins);

public sealed record LayoutPoint(double X, double Y);

public sealed record LayoutWire(string From, string To, string Role, IReadOnlyList<LayoutPoint> Points);

public sealed record SchematicLayout(IReadOnlyList<LayoutPart> Parts, IReadOnlyList<LayoutWire> Wires);

public static class SchematicRouter
{
    private const int Step = 8;

    public static SchematicLayout Place(SchematicSketch sketch)
    {
        var keepRails = SeriesSpine(sketch) is not null || LowSideColumn(sketch) is not null;
        var drawn = sketch with { Parts = sketch.Parts.Where(part => keepRails || !IsRailPart(part)).ToList() };
        var wires = Split(drawn);
        var places = Columns(drawn);
        var boxes = Boxes(drawn, places);
        var anchors = Anchors(drawn, wires, boxes, sketch.Parts);
        var nets = Nets(wires);
        var used = new Dictionary<(int X, int Y), int>();
        var blocked = Interiors(boxes);
        var routed = new List<LayoutWire>();
        var rail = new Dictionary<int, (int X, int Y)>();
        foreach (var wire in wires.OrderBy(wire => RoleRank(RoleOf(wire, sketch.Parts))))
        {
            var role = RoleOf(wire, sketch.Parts);
            var fromAnchored = anchors.ContainsKey(wire.From);
            var toAnchored = anchors.ContainsKey(wire.To);
            if (!fromAnchored && !toAnchored)
            {
                continue;
            }

            var startEnd = fromAnchored ? wire.From : wire.To;
            var start = anchors[startEnd];
            var goal = Goal(wire, anchors, role, nets, rail, boxes);
            var net = NetId(nets, startEnd, wire);
            var lowSide = LowSideColumn(drawn) is not null;
            var path = lowSide && GateWire(wire, anchors, boxes, drawn)
                ? GatePath(start, goal, boxes)
                : lowSide && ControllerSupplyWire(wire, role, anchors, boxes)
                    ? TopPath((start.X, start.Y), goal, boxes)
                    : lowSide && ControllerGroundWire(wire, role, anchors, boxes)
                        ? SidePath((start.X, start.Y), goal, boxes, left: true)
                        : Route(start, goal, net, blocked, used, boxes);
            path = Rise(path, wire, anchors, role, boxes);
            foreach (var point in path)
            {
                used[(point.X, point.Y)] = net;
            }

            if (role is "power" or "ground")
            {
                rail[net] = path[^1];
            }

            routed.Add(new LayoutWire(wire.From, wire.To, role, path.Select(point => new LayoutPoint(point.X, point.Y)).ToList()));
        }

        var parts = boxes.Select(box => new LayoutPart(
            box.Part.Id,
            box.Cx,
            box.Cy,
            box.Width,
            box.Height,
            box.Upright,
            anchors.Values.Where(pin => pin.Owner == box.Part.Id).Select(pin => new LayoutPin(pin.Name, pin.X, pin.Y, pin.Side)).ToList())).ToList();
        return new SchematicLayout(parts, routed);
    }

    private static (int X, int Y) Goal(
        SketchWire wire,
        Dictionary<string, Anchor> anchors,
        string role,
        Dictionary<string, int> nets,
        Dictionary<int, (int X, int Y)> rail,
        List<Box> boxes)
    {
        var fromKey = Key(wire.From, wire.To, anchors);
        var toKey = Key(wire.To, wire.From, anchors);
        var fromHas = anchors.ContainsKey(fromKey);
        var toHas = anchors.ContainsKey(toKey);
        if (fromHas && toHas)
        {
            return (anchors[toKey].X, anchors[toKey].Y);
        }

        var pin = anchors[fromHas ? fromKey : toKey];
        var net = NetId(nets, wire.From, wire);
        if (rail.TryGetValue(net, out var join))
        {
            return join;
        }

        var y = role == "ground"
            ? boxes.Max(box => box.Cy + box.Height / 2) + 48
            : boxes.Min(box => box.Cy - box.Height / 2) - 40;
        return Snap(pin.X, y);
    }

    private static List<(int X, int Y)> Rise(
        List<(int X, int Y)> path,
        SketchWire wire,
        Dictionary<string, Anchor> anchors,
        string role,
        List<Box> boxes)
    {
        if (role != "power" || boxes.Count == 0)
        {
            return path;
        }

        var pin = SupplyPin(wire, anchors);
        if (pin is null)
        {
            return path;
        }

        var railY = Snap(pin.X, boxes.Min(box => box.Top) - 40).Y;
        var stub = new List<(int X, int Y)> { (pin.X, railY), (pin.X, pin.Y) };
        if (path.Count == 0)
        {
            return stub;
        }

        if (path[0].X == pin.X && path[0].Y == pin.Y)
        {
            return stub.Concat(path.Skip(1)).ToList();
        }

        if (path[^1].X == pin.X && path[^1].Y == pin.Y)
        {
            return path.Concat(stub.AsEnumerable().Reverse().Skip(1)).ToList();
        }

        return path;
    }

    private static Anchor? SupplyPin(SketchWire wire, Dictionary<string, Anchor> anchors)
    {
        foreach (var end in new[] { wire.From, wire.To })
        {
            if (anchors.TryGetValue(end, out var pin) && IsSupplyName(pin.Name))
            {
                return pin;
            }
        }

        return null;
    }

    private static List<(int X, int Y)> Route(
        Anchor start,
        (int X, int Y) goal,
        int net,
        HashSet<(int X, int Y)> blocked,
        Dictionary<(int X, int Y), int> used,
        List<Box> boxes)
    {
        var origin = (start.X, start.Y);
        if (origin == goal)
        {
            return [origin];
        }

        var open = new PriorityQueue<(int X, int Y), int>();
        var cost = new Dictionary<(int X, int Y), int> { [origin] = 0 };
        var came = new Dictionary<(int X, int Y), (int X, int Y)>();
        open.Enqueue(origin, Distance(origin, goal));
        var guard = 0;
        while (open.Count > 0 && guard++ < 5000)
        {
            var node = open.Dequeue();
            if (node == goal)
            {
                return Walk(came, node);
            }

            foreach (var next in new (int X, int Y)[] { (node.X + Step, node.Y), (node.X - Step, node.Y), (node.X, node.Y + Step), (node.X, node.Y - Step) })
            {
                if (Math.Abs(next.X) > 4000 || Math.Abs(next.Y) > 4000)
                {
                    continue;
                }

                if (next != goal && (blocked.Contains(next) || (used.TryGetValue(next, out var owner) && owner != net)))
                {
                    continue;
                }

                var stepCost = cost[node] + Step;
                if (cost.TryGetValue(next, out var known) && known <= stepCost)
                {
                    continue;
                }

                cost[next] = stepCost;
                came[next] = node;
                open.Enqueue(next, stepCost + Distance(next, goal));
            }
        }

        if (boxes.Count == 0)
        {
            return [origin, (origin.X, goal.Y), goal];
        }

        return SidePath(origin, goal, boxes, left: false);
    }

    private static bool GateWire(SketchWire wire, Dictionary<string, Anchor> anchors, List<Box> boxes, SchematicSketch sketch)
    {
        return EndsOn(wire, anchors, pin => boxes.Any(box => box.Part.Id.Equals(pin.Owner, StringComparison.OrdinalIgnoreCase) && IsGateResistor(box.Part, sketch)));
    }

    private static bool ControllerGroundWire(SketchWire wire, string role, Dictionary<string, Anchor> anchors, List<Box> boxes)
    {
        return role == "ground" && EndsOn(wire, anchors, pin => boxes.Any(box => box.Part.Id.Equals(pin.Owner, StringComparison.OrdinalIgnoreCase) && SketchBoard.IsController(box.Part)));
    }

    private static bool ControllerSupplyWire(SketchWire wire, string role, Dictionary<string, Anchor> anchors, List<Box> boxes)
    {
        return role == "power" && EndsOn(wire, anchors, pin => boxes.Any(box => box.Part.Id.Equals(pin.Owner, StringComparison.OrdinalIgnoreCase) && SketchBoard.IsController(box.Part)));
    }

    private static bool EndsOn(SketchWire wire, Dictionary<string, Anchor> anchors, Func<Anchor, bool> match)
    {
        foreach (var end in new[] { wire.From, wire.To })
        {
            if (anchors.TryGetValue(end, out var pin) && match(pin))
            {
                return true;
            }
        }

        return false;
    }

    private static List<(int X, int Y)> GatePath(Anchor start, (int X, int Y) goal, List<Box> boxes)
    {
        var origin = (start.X, start.Y);
        if (origin.X == goal.X || origin.Y == goal.Y)
        {
            return [origin, goal];
        }

        return SidePath(origin, goal, boxes, left: true);
    }

    private static List<(int X, int Y)> TopPath((int X, int Y) start, (int X, int Y) goal, List<Box> boxes)
    {
        var rail = Snap(0, boxes.Min(box => box.Top) - 32).Y;
        return Compact([start, (start.X, rail), (goal.X, rail), goal]);
    }

    private static List<(int X, int Y)> SidePath((int X, int Y) start, (int X, int Y) goal, List<Box> boxes, bool left)
    {
        var channel = left
            ? Snap(boxes.Min(box => box.Left) - 24, 0).X
            : Snap(boxes.Max(box => box.Right) + 24, 0).X;
        return Compact([start, (channel, start.Y), (channel, goal.Y), goal]);
    }

    private static List<(int X, int Y)> Compact(List<(int X, int Y)> path)
    {
        var kept = new List<(int X, int Y)>();
        foreach (var point in path)
        {
            if (kept.Count == 0 || kept[^1] != point)
            {
                kept.Add(point);
            }
        }

        if (kept.Count < 3)
        {
            return kept;
        }

        var compact = new List<(int X, int Y)> { kept[0] };
        for (var index = 1; index < kept.Count - 1; index++)
        {
            var previous = compact[^1];
            var current = kept[index];
            var next = kept[index + 1];
            if ((previous.X == current.X && current.X == next.X) || (previous.Y == current.Y && current.Y == next.Y))
            {
                continue;
            }

            compact.Add(current);
        }

        compact.Add(kept[^1]);
        return compact;
    }

    private static List<(int X, int Y)> Walk(Dictionary<(int X, int Y), (int X, int Y)> came, (int X, int Y) node)
    {
        var path = new List<(int X, int Y)> { node };
        while (came.TryGetValue(node, out var previous))
        {
            node = previous;
            path.Add(node);
        }

        path.Reverse();
        var compact = new List<(int X, int Y)> { path[0] };
        for (var index = 1; index < path.Count - 1; index++)
        {
            var previous = compact[^1];
            var current = path[index];
            var next = path[index + 1];
            if ((previous.X == current.X && current.X == next.X) || (previous.Y == current.Y && current.Y == next.Y))
            {
                continue;
            }

            compact.Add(current);
        }

        compact.Add(path[^1]);
        return compact;
    }

    private static int NetId(Dictionary<string, int> nets, string end, SketchWire wire)
    {
        if (nets.TryGetValue(Norm(end), out var id) || nets.TryGetValue(Norm(wire.From), out id) || nets.TryGetValue(Norm(wire.To), out id))
        {
            return id;
        }

        return 0;
    }

    private static int Distance((int X, int Y) from, (int X, int Y) to) => Math.Abs(from.X - to.X) + Math.Abs(from.Y - to.Y);

    private static Dictionary<string, Anchor> Anchors(SchematicSketch sketch, List<SketchWire> wires, List<Box> boxes, IReadOnlyList<SketchPart> parts)
    {
        var anchors = new Dictionary<string, Anchor>(StringComparer.OrdinalIgnoreCase);
        var used = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var lowSide = LowSideColumn(sketch) is not null;
        foreach (var box in boxes.Where(box => SketchBoard.IsController(box.Part)))
        {
            var pins = wires.SelectMany(wire => new[] { wire.From, wire.To })
                .Where(end => OwnerOf(end).Equals(box.Part.Id, StringComparison.OrdinalIgnoreCase) && end.Contains('.'))
                .Select(end => end[(end.IndexOf('.') + 1)..])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var top = pins.Where(pin => IsSupplyName(pin)).ToList();
            var bottom = pins.Where(pin => IsGroundName(pin)).ToList();
            var side = pins.Except(top, StringComparer.OrdinalIgnoreCase).Except(bottom, StringComparer.OrdinalIgnoreCase).ToList();
            PlaceRow(box, top, "top", anchors);
            PlaceRow(box, lowSide ? side : bottom, "bottom", anchors);
            PlaceRow(box, lowSide ? bottom : side, lowSide ? "left" : "right", anchors);
        }

        if (lowSide)
        {
            foreach (var box in boxes.Where(box => IsTransistor(box.Part)))
            {
                PlaceTransistor(box, wires, anchors);
            }

            foreach (var box in boxes.Where(box => IsGateResistor(box.Part, sketch)))
            {
                PlaceGateResistor(box, wires, boxes, anchors);
            }
        }

        foreach (var wire in wires.OrderBy(wire => RoleRank(RoleOf(wire, parts))))
        {
            Claim(wire.From, wire.To, boxes, anchors, used);
            Claim(wire.To, wire.From, boxes, anchors, used);
        }

        return anchors;
    }

    private static void PlaceRow(Box box, List<string> pins, string side, Dictionary<string, Anchor> anchors)
    {
        for (var index = 0; index < pins.Count; index++)
        {
            var along = pins.Count == 1 ? 0.5 : (index + 1.0) / (pins.Count + 1);
            var point = side switch
            {
                "top" => Snap(box.Left + box.Width * along, box.Top),
                "bottom" => Snap(box.Left + box.Width * along, box.Bottom),
                "left" => Snap(box.Left, box.Top + box.Height * along),
                _ => Snap(box.Right, box.Top + box.Height * along),
            };
            var name = pins[index];
            anchors[$"{box.Part.Id}.{name}"] = new Anchor(box.Part.Id, name, point.X, point.Y, side);
        }
    }

    private static void Claim(string end, string other, List<Box> boxes, Dictionary<string, Anchor> anchors, Dictionary<string, int> used)
    {
        var owner = OwnerOf(end);
        var box = boxes.Find(item => item.Part.Id.Equals(owner, StringComparison.OrdinalIgnoreCase));
        if (box is null)
        {
            return;
        }

        if (anchors.ContainsKey(end))
        {
            return;
        }

        var lead = NextLead(box, other, anchors, used, boxes);
        anchors[end] = anchors[$"{box.Part.Id}.{lead}"];
    }

    private static string NextLead(Box box, string other, Dictionary<string, Anchor> anchors, Dictionary<string, int> used, List<Box> boxes)
    {
        var id = box.Part.Id;
        used.TryGetValue(id, out var count);
        var names = LeadNames(box.Part);
        var name = names[Math.Min(count, names.Length - 1)];
        if (anchors.ContainsKey($"{id}.{name}") && count == 0)
        {
            name = names[^1];
        }

        var side = SideFor(box, other, count, anchors, boxes);
        var point = side switch
        {
            "top" => Snap(box.Cx, box.Top),
            "bottom" => Snap(box.Cx, box.Bottom),
            "left" => Snap(box.Kind == SketchPartKind.Led ? box.Cx - 28 : box.Left, box.Cy),
            _ => Snap(box.Kind == SketchPartKind.Led ? box.Cx + 24 : box.Right, box.Cy),
        };
        if (side is "left" or "right" && box.Kind == SketchPartKind.Led)
        {
            name = side == "left" ? "Anode" : "Cathode";
        }

        if (side is "top" or "bottom" && box.Kind == SketchPartKind.Led)
        {
            var facing = side == "top" ? "Anode" : "Cathode";
            if (!anchors.ContainsKey($"{id}.{facing}"))
            {
                name = facing;
            }
        }

        anchors[$"{id}.{name}"] = new Anchor(id, name, point.X, point.Y, side);
        used[id] = count + 1;
        return name;
    }

    private static string SideFor(Box box, string other, int count, Dictionary<string, Anchor> anchors, List<Box> boxes)
    {
        bool Taken(string side) => anchors.Values.Any(pin => pin.Owner.Equals(box.Part.Id, StringComparison.OrdinalIgnoreCase) && pin.Side == side);
        var otherBox = boxes.Find(item => item.Part.Id.Equals(OwnerOf(other), StringComparison.OrdinalIgnoreCase));
        var railLink = otherBox is not null
            && box.Kind is SketchPartKind.Supply or SketchPartKind.Ground
            && otherBox.Kind is SketchPartKind.Supply or SketchPartKind.Ground;
        if (railLink)
        {
            return "left";
        }

        if (otherBox is not null && Math.Abs(otherBox.Cx - box.Cx) <= Step && Math.Abs(otherBox.Cy - box.Cy) > Step)
        {
            var facing = otherBox.Cy < box.Cy ? "top" : "bottom";
            if (!Taken(facing))
            {
                return facing;
            }
        }

        if (box.Kind == SketchPartKind.Supply && (IsGroundName(other) || otherBox?.Kind == SketchPartKind.Ground))
        {
            return "left";
        }

        if (box.Upright)
        {
            if (IsGroundName(other)) return "bottom";
            if (IsSupplyName(other)) return "top";
            if (count == 0) return "top";
            if (Taken("top") && !Taken("bottom")) return "bottom";
            if (Taken("bottom") && !Taken("top")) return "top";
            return "bottom";
        }

        if (IsGroundName(other) || IsSupplyName(other))
        {
            return "right";
        }

        if (count == 0)
        {
            return "left";
        }

        return Taken("right") ? "left" : "right";
    }

    private static string[] LeadNames(SketchPart part) =>
        SketchPartKinds.Of(part) == SketchPartKind.Led ? ["Anode", "Cathode"] : ["1", "2"];

    private static string Key(string end, string other, Dictionary<string, Anchor> anchors)
    {
        if (anchors.ContainsKey(end))
        {
            return end;
        }

        var owner = OwnerOf(end);
        return anchors.Keys.FirstOrDefault(key => key.StartsWith(owner + ".", StringComparison.OrdinalIgnoreCase) && anchors[key].Owner.Equals(owner, StringComparison.OrdinalIgnoreCase)) ?? end;
    }

    private static List<Box> Boxes(SchematicSketch sketch, Dictionary<string, (double Column, double Row, bool Upright)> places)
    {
        var boxes = new List<Box>();
        foreach (var part in sketch.Parts)
        {
            if (!places.TryGetValue(part.Id, out var place))
            {
                continue;
            }

            var kind = SketchPartKinds.Of(part);
            var block = SketchBoard.IsController(part);
            var ecu = IsEcu(part);
            var rail = kind is SketchPartKind.Supply or SketchPartKind.Ground or SketchPartKind.Node;
            var transistor = IsTransistor(part) && LowSideColumn(sketch) is not null;
            var span = transistor ? 64 : rail ? 32 : ecu ? 140 : block ? 112 : 56;
            var across = transistor ? 48 : rail ? 24 : ecu ? 180 : block ? 56 : 28;
            var width = place.Upright ? across : span;
            var height = place.Upright ? span : across;
            var cx = 96 + place.Column * 168;
            var pitch = LowSideColumn(sketch) is not null ? 200 : 120;
            var cy = 96 + (int)Math.Round(place.Row * pitch);
            boxes.Add(new Box(part, kind, Snap(cx, cy).X, Snap(cx, cy).Y, width, height, place.Upright));
        }

        return boxes;
    }

    private static Dictionary<string, (double Column, double Row, bool Upright)> Columns(SchematicSketch sketch)
    {
        var gpio = GpioRows(sketch);
        if (gpio is not null)
        {
            return gpio;
        }

        var ecu = EcuStack(sketch);
        if (ecu is not null)
        {
            return ecu;
        }

        var lowSide = LowSideColumn(sketch);
        if (lowSide is not null)
        {
            return lowSide;
        }

        var spine = SeriesSpine(sketch);
        if (spine is not null)
        {
            return spine;
        }

        var parts = sketch.Parts.Where(part => SketchPartKinds.Of(part) is not (SketchPartKind.Supply or SketchPartKind.Ground)).ToList();
        var places = new Dictionary<string, (double Column, double Row, bool Upright)>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < parts.Count; index++)
        {
            var upright = parts.Count > 1 && parts.All(part => IsLead(part));
            places[parts[index].Id] = (0, index, upright);
        }

        return places;
    }

    private static Dictionary<string, (double Column, double Row, bool Upright)>? LowSideColumn(SchematicSketch sketch)
    {
        var transistors = sketch.Parts.Where(IsTransistor).ToList();
        if (transistors.Count != 1 || !sketch.Parts.Any(part => SketchPartKinds.Of(part) == SketchPartKind.Led))
        {
            return null;
        }

        if (sketch.Parts.Count(SketchBoard.IsController) > 1)
        {
            return null;
        }

        if (sketch.Parts.Any(part =>
            {
                var text = TextOf(part);
                return text.Contains("solenoid", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("motor", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("pump", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("relay", StringComparison.OrdinalIgnoreCase);
            }))
        {
            return null;
        }

        var aside = sketch.Parts.Where(part => IsGateResistor(part, sketch) || SketchBoard.IsController(part)).ToList();
        var spine = sketch.Parts
            .Where(part => aside.All(item => !item.Id.Equals(part.Id, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(LowSideWeight)
            .ToList();
        var places = new Dictionary<string, (double Column, double Row, bool Upright)>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < spine.Count; index++)
        {
            places[spine[index].Id] = (0, index, IsLead(spine[index]));
        }

        var row = places[transistors[0].Id].Row;
        const double side = -1.45;
        var controllers = aside.Where(SketchBoard.IsController).ToList();
        for (var index = 0; index < controllers.Count; index++)
        {
            places[controllers[index].Id] = (side, index, false);
        }

        var gate = aside.Where(part => IsGateResistor(part, sketch)).ToList();
        for (var index = 0; index < gate.Count; index++)
        {
            places[gate[index].Id] = (side, row + index * 0.7, true);
        }

        return places;
    }

    private static int LowSideWeight(SketchPart part)
    {
        if (SketchPartKinds.Of(part) == SketchPartKind.Supply)
        {
            return 1;
        }

        if (SketchBoard.IsController(part))
        {
            return 2;
        }

        if (SketchPartKinds.Of(part) is SketchPartKind.Resistor or SketchPartKind.Capacitor)
        {
            return 3;
        }

        if (SketchPartKinds.Of(part) == SketchPartKind.Led)
        {
            return 4;
        }

        if (IsTransistor(part))
        {
            return 5;
        }

        if (SketchPartKinds.Of(part) == SketchPartKind.Ground)
        {
            return 6;
        }

        return 7;
    }

    private static bool IsTransistor(SketchPart part)
    {
        var text = $"{part.Type} {part.Name}";
        return text.Contains("mosfet", StringComparison.OrdinalIgnoreCase)
            || text.Contains("nmos", StringComparison.OrdinalIgnoreCase)
            || text.Contains("transistor", StringComparison.OrdinalIgnoreCase)
            || text.Contains("npn", StringComparison.OrdinalIgnoreCase)
            || text.Contains("bjt", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsGateResistor(SketchPart part, SchematicSketch sketch)
    {
        if (SketchPartKinds.Of(part) != SketchPartKind.Resistor)
        {
            return false;
        }

        if (TextOf(part).Contains("gate", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var neighbors = NeighborIds(part, sketch);
        var transistor = neighbors.Any(id => sketch.Parts.Any(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase) && IsTransistor(item)));
        var led = neighbors.Any(id => sketch.Parts.Any(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase) && SketchPartKinds.Of(item) == SketchPartKind.Led));
        var controller = neighbors.Any(id => sketch.Parts.Any(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase) && SketchBoard.IsController(item)));
        return transistor && controller && !led;
    }

    private static List<string> NeighborIds(SketchPart part, SchematicSketch sketch)
    {
        var ids = new List<string>();
        foreach (var wire in sketch.Wires)
        {
            var left = OwnerOf(wire.From);
            var right = OwnerOf(wire.To);
            if (left.Equals(part.Id, StringComparison.OrdinalIgnoreCase) && !right.Equals(part.Id, StringComparison.OrdinalIgnoreCase))
            {
                ids.Add(right);
            }
            else if (right.Equals(part.Id, StringComparison.OrdinalIgnoreCase) && !left.Equals(part.Id, StringComparison.OrdinalIgnoreCase))
            {
                ids.Add(left);
            }
        }

        return ids;
    }

    private static void PlaceTransistor(Box box, List<SketchWire> wires, Dictionary<string, Anchor> anchors)
    {
        foreach (var wire in wires)
        {
            var end = On(wire.From, box.Part.Id) ? wire.From : On(wire.To, box.Part.Id) ? wire.To : null;
            if (end is null || anchors.ContainsKey(end))
            {
                continue;
            }

            var pin = end.Contains('.') ? end[(end.IndexOf('.') + 1)..] : "gate";
            var side = pin.Equals("s", StringComparison.OrdinalIgnoreCase) || pin.Equals("source", StringComparison.OrdinalIgnoreCase) || pin.Equals("emitter", StringComparison.OrdinalIgnoreCase)
                ? "bottom"
                : pin.Equals("d", StringComparison.OrdinalIgnoreCase) || pin.Equals("drain", StringComparison.OrdinalIgnoreCase) || pin.Equals("collector", StringComparison.OrdinalIgnoreCase)
                    ? "top"
                    : "left";
            var point = side switch
            {
                "top" => Snap(box.Cx, box.Top),
                "bottom" => Snap(box.Cx, box.Bottom),
                _ => Snap(box.Left, box.Cy),
            };
            var name = side switch { "top" => "Drain", "bottom" => "Source", _ => "Gate" };
            anchors[end] = new Anchor(box.Part.Id, name, point.X, point.Y, side);
        }
    }

    private static void PlaceGateResistor(Box box, List<SketchWire> wires, List<Box> boxes, Dictionary<string, Anchor> anchors)
    {
        foreach (var wire in wires)
        {
            var end = On(wire.From, box.Part.Id) ? wire.From : On(wire.To, box.Part.Id) ? wire.To : null;
            if (end is null || anchors.ContainsKey(end))
            {
                continue;
            }

            var other = end.Equals(wire.From, StringComparison.OrdinalIgnoreCase) ? wire.To : wire.From;
            var otherBox = boxes.Find(item => item.Part.Id.Equals(OwnerOf(other), StringComparison.OrdinalIgnoreCase));
            var side = otherBox is not null && IsTransistor(otherBox.Part)
                ? otherBox.Cx >= box.Cx ? "right" : "left"
                : "top";
            var point = side switch
            {
                "top" => Snap(box.Cx, box.Top),
                "bottom" => Snap(box.Cx, box.Bottom),
                "left" => Snap(box.Left, box.Cy),
                _ => Snap(box.Right, box.Cy),
            };
            anchors[end] = new Anchor(box.Part.Id, side == "right" ? "2" : "1", point.X, point.Y, side);
        }
    }

    private static Dictionary<string, (double Column, double Row, bool Upright)>? SeriesSpine(SchematicSketch sketch)
    {
        if (sketch.Parts.Any(SketchBoard.IsController) || sketch.Parts.Any(IsEcu))
        {
            return null;
        }

        if (sketch.Parts.Count < 2)
        {
            return null;
        }

        if (sketch.Parts.Any(part => SketchPartKinds.Of(part) is not (SketchPartKind.Supply or SketchPartKind.Ground or SketchPartKind.Resistor or SketchPartKind.Led or SketchPartKind.Switch or SketchPartKind.Capacitor or SketchPartKind.Node)))
        {
            return null;
        }

        var neighbors = sketch.Parts.ToDictionary(part => part.Id, _ => new List<string>(), StringComparer.OrdinalIgnoreCase);
        foreach (var wire in sketch.Wires)
        {
            var left = OwnerOf(wire.From);
            var right = OwnerOf(wire.To);
            if (!neighbors.ContainsKey(left) || !neighbors.ContainsKey(right) || left.Equals(right, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            neighbors[left].Add(right);
            neighbors[right].Add(left);
        }

        if (!IsSinglePath(sketch.Parts, neighbors))
        {
            return null;
        }

        var start = sketch.Parts.OrderBy(SpineWeight).First();
        var ordered = new List<SketchPart>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var current = start.Id;
        while (!string.IsNullOrEmpty(current) && seen.Add(current))
        {
            var part = sketch.Parts.First(item => item.Id.Equals(current, StringComparison.OrdinalIgnoreCase));
            ordered.Add(part);
            current = neighbors[current]
                .Where(id => !seen.Contains(id))
                .OrderBy(id => SpineWeight(sketch.Parts.First(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase))))
                .FirstOrDefault() ?? "";
        }

        foreach (var part in sketch.Parts.OrderBy(SpineWeight))
        {
            if (seen.Add(part.Id))
            {
                ordered.Add(part);
            }
        }

        var places = new Dictionary<string, (double Column, double Row, bool Upright)>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < ordered.Count; index++)
        {
            var upright = SketchPartKinds.Of(ordered[index]) is SketchPartKind.Resistor or SketchPartKind.Led or SketchPartKind.Switch or SketchPartKind.Capacitor;
            places[ordered[index].Id] = (0, index, upright);
        }

        return places;
    }

    private static int SpineWeight(SketchPart part) => SketchPartKinds.Of(part) switch
    {
        SketchPartKind.Supply => 1,
        SketchPartKind.Resistor or SketchPartKind.Capacitor => 2,
        SketchPartKind.Node => 3,
        SketchPartKind.Switch => 4,
        SketchPartKind.Led => 5,
        SketchPartKind.Ground => 6,
        _ => 7,
    };

    private static bool IsSinglePath(IReadOnlyList<SketchPart> parts, Dictionary<string, List<string>> neighbors)
    {
        bool Rail(string id) => parts.FirstOrDefault(part => part.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) is { } part
            && SketchPartKinds.Of(part) is SketchPartKind.Supply or SketchPartKind.Ground;
        var ends = 0;
        foreach (var part in parts)
        {
            var links = neighbors[part.Id]
                .Where(id => !(Rail(part.Id) && Rail(id)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            if (links > 2)
            {
                return false;
            }

            if (links == 1)
            {
                ends++;
            }

            if (links == 0)
            {
                return false;
            }
        }

        return ends == 2;
    }

    private static Dictionary<string, (double Column, double Row, bool Upright)>? EcuStack(SchematicSketch sketch)
    {
        var ecu = sketch.Parts.FirstOrDefault(IsEcu);
        if (ecu is null)
        {
            return null;
        }

        var rest = sketch.Parts.Where(part => !part.Id.Equals(ecu.Id, StringComparison.OrdinalIgnoreCase) && !IsRailPart(part)).ToList();
        var stacked = rest.Where(part => !IsClampDiode(part)).Select((part, index) => (part, index)).OrderBy(item => StackRank(item.part)).ThenBy(item => item.index).Select(item => item.part).ToList();
        var diodes = rest.Where(IsClampDiode).ToList();
        var places = new Dictionary<string, (double Column, double Row, bool Upright)>(StringComparer.OrdinalIgnoreCase)
        {
            [ecu.Id] = (0, Math.Max(0, stacked.Count - 1) / 2.0, false),
        };
        for (var index = 0; index < stacked.Count; index++)
        {
            places[stacked[index].Id] = (1, index, false);
        }

        var solenoid = stacked.FirstOrDefault(part => TextOf(part).Contains("solenoid", StringComparison.OrdinalIgnoreCase));
        var hostRow = solenoid is null ? Math.Max(0, stacked.Count - 1) : places[solenoid.Id].Row;
        for (var index = 0; index < diodes.Count; index++)
        {
            places[diodes[index].Id] = (1 + 80.0 / 168, hostRow - 40.0 / 120 - index * 0.5, false);
        }

        return places;
    }

    private static int StackRank(SketchPart part)
    {
        var text = TextOf(part);
        if (text.Contains("temp", StringComparison.OrdinalIgnoreCase) || text.Contains("thermistor", StringComparison.OrdinalIgnoreCase) || text.Contains("ntc", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (text.Contains("switch", StringComparison.OrdinalIgnoreCase) || text.Contains("button", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        if (text.Contains("solenoid", StringComparison.OrdinalIgnoreCase))
        {
            return 3;
        }

        return 2;
    }

    private static bool IsEcu(SketchPart part) =>
        TextOf(part).Contains("ecu", StringComparison.OrdinalIgnoreCase) || part.Id.Equals("ecu1", StringComparison.OrdinalIgnoreCase);

    private static bool IsClampDiode(SketchPart part)
    {
        var text = TextOf(part);
        return text.Contains("diode", StringComparison.OrdinalIgnoreCase) && !text.Contains("led", StringComparison.OrdinalIgnoreCase);
    }

    private static string TextOf(SketchPart part) => $"{part.Type} {part.Name} {part.Note} {part.Id}";

    private static Dictionary<string, (double Column, double Row, bool Upright)>? GpioRows(SchematicSketch sketch)
    {
        var controllers = sketch.Parts.Where(SketchBoard.IsController).ToList();
        if (controllers.Count != 1 || sketch.Parts.Any(part => part.Id != controllers[0].Id && !IsLead(part)))
        {
            return null;
        }

        var host = controllers[0].Id;
        var neighbors = sketch.Parts.Where(IsLead).ToDictionary(part => part.Id, _ => new List<string>(), StringComparer.OrdinalIgnoreCase);
        var outside = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var wire in sketch.Wires)
        {
            var left = OwnerOf(wire.From);
            var right = OwnerOf(wire.To);
            if (neighbors.ContainsKey(left) && neighbors.ContainsKey(right) && !left.Equals(right, StringComparison.OrdinalIgnoreCase))
            {
                neighbors[left].Add(right);
                neighbors[right].Add(left);
            }
            else if (neighbors.ContainsKey(left))
            {
                (outside.TryGetValue(left, out var list) ? list : outside[left] = []).Add(wire.To);
            }
            else if (neighbors.ContainsKey(right))
            {
                (outside.TryGetValue(right, out var list) ? list : outside[right] = []).Add(wire.From);
            }
        }

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rows = new List<List<string>>();
        foreach (var start in neighbors.Keys)
        {
            if (used.Contains(start) || !(outside.TryGetValue(start, out var tokens) && tokens.Any(token => OwnerOf(token).Equals(host, StringComparison.OrdinalIgnoreCase) && IsSignal(token))))
            {
                continue;
            }

            var order = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var current = start;
            while (!string.IsNullOrEmpty(current) && neighbors.ContainsKey(current) && seen.Add(current))
            {
                used.Add(current);
                order.Add(current);
                current = neighbors[current].FirstOrDefault(item => neighbors.ContainsKey(item) && !seen.Contains(item)) ?? "";
            }

            rows.Add(order);
        }

        if (rows.Count == 0 || neighbors.Keys.Any(id => !used.Contains(id)))
        {
            return null;
        }

        var places = new Dictionary<string, (double Column, double Row, bool Upright)>(StringComparer.OrdinalIgnoreCase)
        {
            [host] = (0, (rows.Count - 1) / 2.0, false),
        };
        for (var row = 0; row < rows.Count; row++)
        {
            for (var column = 0; column < rows[row].Count; column++)
            {
                places[rows[row][column]] = (column + 1, row, false);
            }
        }

        return places;
    }

    private static bool IsSignal(string end)
    {
        var pin = end.Contains('.') ? end[(end.IndexOf('.') + 1)..] : end;
        return pin.StartsWith("GPIO", StringComparison.OrdinalIgnoreCase)
            || System.Text.RegularExpressions.Regex.IsMatch(pin, @"^(IO|D)\d+$", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    }

    private static HashSet<(int X, int Y)> Interiors(List<Box> boxes)
    {
        var cells = new HashSet<(int X, int Y)>();
        foreach (var box in boxes)
        {
            for (var x = box.Left + Step; x < box.Right; x += Step)
            {
                for (var y = box.Top + Step; y < box.Bottom; y += Step)
                {
                    cells.Add(Snap(x, y));
                }
            }
        }

        return cells;
    }

    private static Dictionary<string, int> Nets(List<SketchWire> wires)
    {
        var parent = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string Find(string end)
        {
            var name = Norm(end);
            if (!parent.ContainsKey(name))
            {
                parent[name] = name;
            }

            var root = name;
            while (!parent[root].Equals(root, StringComparison.OrdinalIgnoreCase))
            {
                root = parent[root];
            }

            parent[name] = root;
            return root;
        }

        foreach (var wire in wires)
        {
            var left = Find(wire.From);
            var right = Find(wire.To);
            if (!left.Equals(right, StringComparison.OrdinalIgnoreCase))
            {
                parent[left] = right;
            }
        }

        var ids = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var next = 0;
        foreach (var end in parent.Keys.ToList())
        {
            var root = Find(end);
            if (!ids.ContainsKey(root))
            {
                ids[root] = next++;
            }

            ids[end] = ids[root];
        }

        return ids;
    }

    private static List<SketchWire> Split(SchematicSketch sketch)
    {
        var wires = sketch.Wires.Select(wire => new SketchWire(wire.From.Trim(), wire.To.Trim())).ToList();
        foreach (var part in sketch.Parts.Where(part => IsLead(part) || SketchPartKinds.Of(part) is SketchPartKind.Supply or SketchPartKind.Ground or SketchPartKind.Node))
        {
            var hits = wires.Select((wire, index) => (wire, index)).Where(item => On(item.wire.From, part.Id) || On(item.wire.To, part.Id)).Select(item => item.index).ToList();
            if (hits.Count != 2)
            {
                continue;
            }

            var pins = hits.Select(index => On(wires[index].From, part.Id) ? wires[index].From : wires[index].To).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (pins.Count >= 2)
            {
                continue;
            }

            for (var slot = 0; slot < hits.Count; slot++)
            {
                var wire = wires[hits[slot]];
                var lead = $"{part.Id}.{(slot == 0 ? "a" : "b")}";
                wires[hits[slot]] = new SketchWire(
                    wire.From.Equals(part.Id, StringComparison.OrdinalIgnoreCase) ? lead : wire.From,
                    wire.To.Equals(part.Id, StringComparison.OrdinalIgnoreCase) ? lead : wire.To);
            }
        }

        foreach (var part in sketch.Parts.Where(IsTransistor))
        {
            var hits = wires.Select((wire, index) => (wire, index)).Where(item => On(item.wire.From, part.Id) || On(item.wire.To, part.Id)).Select(item => item.index).ToList();
            var groups = hits.GroupBy(index => On(wires[index].From, part.Id) ? wires[index].From : wires[index].To, StringComparer.OrdinalIgnoreCase);
            foreach (var group in groups.Where(group => group.Count() > 1))
            {
                var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var index in group)
                {
                    var wire = wires[index];
                    var onFrom = On(wire.From, part.Id);
                    var other = onFrom ? wire.To : wire.From;
                    var role = TransistorRole(other, sketch);
                    var name = role;
                    var suffix = 2;
                    while (!used.Add(name))
                    {
                        name = role + suffix++;
                    }

                    var lead = $"{part.Id}.{name}";
                    wires[index] = new SketchWire(onFrom ? lead : wire.From, onFrom ? wire.To : lead);
                }
            }
        }

        return wires;
    }

    private static string TransistorRole(string other, SchematicSketch sketch)
    {
        if (IsGroundName(other))
        {
            return "source";
        }

        var owner = sketch.Parts.FirstOrDefault(part => On(other, part.Id));
        if (owner is null)
        {
            return "drain";
        }

        if (SketchPartKinds.Of(owner) == SketchPartKind.Ground)
        {
            return "source";
        }

        if (SketchBoard.IsController(owner) || IsGateResistor(owner, sketch))
        {
            return "gate";
        }

        return "drain";
    }

    private static string RoleOf(SketchWire wire, IReadOnlyList<SketchPart> parts)
    {
        if (IsGroundName(wire.From) || IsGroundName(wire.To) || RailRole(wire.From, parts) == "ground" || RailRole(wire.To, parts) == "ground")
        {
            return "ground";
        }

        if (IsSupplyName(wire.From) || IsSupplyName(wire.To) || RailRole(wire.From, parts) == "power" || RailRole(wire.To, parts) == "power")
        {
            return "power";
        }

        return "signal";
    }

    private static string RailRole(string end, IReadOnlyList<SketchPart> parts)
    {
        var part = parts.FirstOrDefault(item => On(end, item.Id) && IsRailPart(item));
        if (part is null)
        {
            return "";
        }

        return IsGroundName(part.Id) || IsGroundName(part.Name) ? "ground" : "power";
    }

    private static bool IsRailPart(SketchPart part)
    {
        if (SketchBoard.IsController(part) || IsLead(part))
        {
            return false;
        }

        return IsSupplyName(part.Id) || IsGroundName(part.Id) || IsSupplyName(part.Name) || IsGroundName(part.Name);
    }

    private static int RoleRank(string role) => role switch { "ground" => 0, "power" => 1, _ => 2 };

    private static bool IsSupplyName(string end)
    {
        var pin = end.Contains('.') ? end[(end.IndexOf('.') + 1)..] : end;
        return pin.Equals("3V3", StringComparison.OrdinalIgnoreCase)
            || pin.Equals("3.3V", StringComparison.OrdinalIgnoreCase)
            || pin.Equals("+3V3", StringComparison.OrdinalIgnoreCase)
            || pin.Equals("+3.3V", StringComparison.OrdinalIgnoreCase)
            || pin.Equals("VCC", StringComparison.OrdinalIgnoreCase)
            || pin.Equals("VDD", StringComparison.OrdinalIgnoreCase)
            || pin.Equals("VIN", StringComparison.OrdinalIgnoreCase)
            || pin.Equals("5V", StringComparison.OrdinalIgnoreCase)
            || pin.Equals("+5V", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsGroundName(string end)
    {
        var pin = end.Contains('.') ? end[(end.IndexOf('.') + 1)..] : end;
        return pin.Equals("GND", StringComparison.OrdinalIgnoreCase)
            || pin.Equals("VSS", StringComparison.OrdinalIgnoreCase)
            || pin.Equals("AGND", StringComparison.OrdinalIgnoreCase)
            || pin.Equals("0", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLead(SketchPart part) =>
        SketchPartKinds.Of(part) is SketchPartKind.Resistor or SketchPartKind.Led or SketchPartKind.Switch or SketchPartKind.Capacitor;

    private static bool On(string end, string id) =>
        end.Equals(id, StringComparison.OrdinalIgnoreCase) || end.StartsWith(id + ".", StringComparison.OrdinalIgnoreCase);

    private static string OwnerOf(string end) => end.Split('.')[0];

    private static string Norm(string end) => end.Trim().ToLowerInvariant();

    private static (int X, int Y) Snap(double x, double y) => ((int)Math.Round(x / Step) * Step, (int)Math.Round(y / Step) * Step);

    private sealed class Box(SketchPart part, SketchPartKind kind, int cx, int cy, int width, int height, bool upright)
    {
        public SketchPart Part { get; } = part;

        public SketchPartKind Kind { get; } = kind;

        public int Cx { get; } = cx;

        public int Cy { get; } = cy;

        public int Width { get; } = width;

        public int Height { get; } = height;

        public bool Upright { get; } = upright;

        public int Left => Cx - Width / 2;

        public int Right => Cx + Width / 2;

        public int Top => Cy - Height / 2;

        public int Bottom => Cy + Height / 2;
    }

    private sealed record Anchor(string Owner, string Name, int X, int Y, string Side);
}
