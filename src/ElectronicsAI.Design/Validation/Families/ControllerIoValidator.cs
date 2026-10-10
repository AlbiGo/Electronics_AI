namespace ElectronicsAI.Design;

public sealed class ControllerIoValidator : ISketchRuleSet
{
    public CircuitCategory Category => CircuitCategory.Embedded;

    public static bool Applies(SchematicSketch sketch) =>
        sketch.Parts.Any(SketchBoard.IsController)
        && sketch.Parts.Any(part => SketchPartKinds.Of(part) is SketchPartKind.Led or SketchPartKind.Switch)
        && !Esp32SensorFamily.Matches(sketch)
        && !RelayDriverRules.Applies(sketch)
        && !MotorDriverRules.Applies(sketch)
        && !SolenoidFamily.Pattern.Applies(sketch, null)
        && !LowSideLedFamily.Pattern.Applies(sketch, null)
        && !BuckConverterRules.Applies(sketch, null);

    public bool Applies(SchematicSketch sketch, string? request) => Applies(sketch);

    public SketchRuleResult Evaluate(SchematicSketch sketch, string? request)
    {
        var board = SketchBoard.Of(sketch with { Wires = SplitLeads(sketch) });
        var rules = new List<SketchRule>
        {
            Powered(board),
            Grounded(board),
        };
        if (sketch.Parts.Any(part => SketchPartKinds.Of(part) == SketchPartKind.Led))
        {
            rules.Add(LedRequiresResistorRule.Present(board));
            rules.Add(GpioOutput(board));
            rules.Add(LedCurrent(board));
        }

        if (sketch.Parts.Any(part => SketchPartKinds.Of(part) == SketchPartKind.Switch))
        {
            rules.Add(GpioInput(board));
            rules.Add(PullResistor(board));
        }

        rules.Add(SupplyCompatible(board));
        rules.Add(CommonGround(board));
        return new SketchRuleResult("Controller IO checked without ngspice.", rules, []);
    }

    private static SketchRule Powered(SketchBoard board) =>
        board.Nets.Any(net => net.Any(end => IsControllerPowerPin(board, end)) && net.Any(end => IsSupplyEnd(board, end)))
            ? new SketchRule("Controller powered", "The controller power pin is connected to a supply.", "Pass")
            : new SketchRule("Controller powered", "The controller power pin is not connected to a supply.", "Fail");

    private static SketchRule Grounded(SketchBoard board) =>
        ControllerGround(board).Count > 0
            ? new SketchRule("Controller grounded", "The controller GND pin is connected to ground.", "Pass")
            : new SketchRule("Controller grounded", "The controller GND pin is not connected to ground.", "Fail");

    private static SketchRule SupplyCompatible(SketchBoard board)
    {
        var overloaded = board.Nets.Any(net =>
            net.Any(end => IsControllerPin(board, end, "3V3", "3.3V") || board.Marked(end, Mark.Gpio))
            && net.Any(end => end.Equals("5V", StringComparison.OrdinalIgnoreCase)
                || end.Equals("+5V", StringComparison.OrdinalIgnoreCase)
                || end.Equals("12V", StringComparison.OrdinalIgnoreCase)
                || end.Equals("+12V", StringComparison.OrdinalIgnoreCase)
                || IsControllerPin(board, end, "5V", "+5V", "12V", "+12V")));
        return overloaded
            ? new SketchRule("Supply voltage compatible", "A 3.3 V controller pin shares a higher-voltage rail.", "Fail")
            : new SketchRule("Supply voltage compatible", "The controller supply matches 3.3 V.", "Pass");
    }

    private static SketchRule GpioOutput(SketchBoard board)
    {
        var outputs = GpioPins(board, net => SeriesLedResistor(board, net));
        if (outputs.Count == 0)
        {
            return new SketchRule("GPIO output detected", "The series resistor should be in series with the LED.", "Fail");
        }

        return new SketchRule("GPIO output detected", "The LED is driven through its series resistor.", "Pass");
    }

    private static SketchRule GpioInput(SketchBoard board) =>
        ButtonConnected(board).Result == "Pass"
            ? new SketchRule("GPIO input detected", "A GPIO pin reads the push button.", "Pass")
            : new SketchRule("GPIO input detected", "The push button should connect the GPIO pin to 3V3.", "Fail");

    private static SketchRule PullResistor(SketchBoard board)
    {
        var inputs = Nets(board, end => board.Marked(end, Mark.Gpio) && NetTouches(board, NetIndex(board, end), SketchPartKind.Switch));
        var pulled = board.Sketch.Parts.Where(part => SketchPartKinds.Of(part) == SketchPartKind.Resistor).Any(part =>
        {
            var touched = Touched(board, part);
            if (!touched.Any(inputs.Contains) || touched.Any(index => NetTouches(board, index, SketchPartKind.Led) && !board.Nets[index].Any(end => IsGroundEnd(board, end))))
            {
                return false;
            }

            var gpio = touched.Where(inputs.Contains).ToList();
            if (touched.Any(index => !gpio.Contains(index) && !board.Nets[index].Any(end => IsSupplyEnd(board, end) || IsGroundEnd(board, end)) && NetTouchesOther(board, index, part, SketchPartKind.Resistor)))
            {
                return false;
            }

            var toSupply = touched.Any(index => board.Nets[index].Any(end => IsSupplyEnd(board, end)));
            var toGround = touched.Any(index => board.Nets[index].Any(end => IsGroundEnd(board, end)));
            var buttonToGround = ButtonSide(board, gpio, ground: true);
            var buttonToSupply = ButtonSide(board, gpio, ground: false);
            return (buttonToGround && toSupply) || (buttonToSupply && toGround);
        });
        return pulled
            ? new SketchRule("Pull resistor present", "A pull resistor holds the button GPIO pin.", "Pass")
            : new SketchRule("Pull resistor present", "The pull-down resistor should connect the button GPIO pin to GND.", "Fail");
    }

    private static SketchRule LedCurrent(SketchBoard board)
    {
        var series = board.Sketch.Parts.FirstOrDefault(part =>
            SketchPartKinds.Of(part) == SketchPartKind.Resistor
            && Touched(board, part).Any(index => NetTouches(board, index, SketchPartKind.Led) && !board.Nets[index].Any(end => IsGroundEnd(board, end))));
        if (series is null)
        {
            return new SketchRule("LED current safe", "The series resistor should be in series with the LED.", "Fail");
        }

        var ohms = Ohms(series);
        if (ohms is null or <= 0)
        {
            return new SketchRule("LED current safe", "The LED resistor has no resistance.", "Fail");
        }

        var volts = board.Nets.Any(net => net.Any(end => IsControllerPin(board, end, "3V3", "3.3V", "+3V3", "+3.3V") || end.Equals("3V3", StringComparison.OrdinalIgnoreCase) || end.Equals("3.3V", StringComparison.OrdinalIgnoreCase) || end.Equals("+3.3V", StringComparison.OrdinalIgnoreCase) || end.Equals("+3V3", StringComparison.OrdinalIgnoreCase)))
            ? 3.3
            : 5.0;
        var current = (volts - 2.0) / ohms.Value;
        return current is > 0 and <= 0.02
            ? new SketchRule("LED current safe", $"{current * 1000:0.0} mA is within the GPIO limit.", "Pass")
            : new SketchRule("LED current safe", $"{current * 1000:0.0} mA exceeds the 20 mA GPIO limit.", "Fail");
    }

    private static int NetIndex(SketchBoard board, string end)
    {
        for (var index = 0; index < board.Nets.Count; index++)
        {
            if (board.Nets[index].Contains(end))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool NetTouches(SketchBoard board, int index, SketchPartKind kind)
    {
        if (index < 0) return false;
        return board.Nets[index].Any(end => board.Owner(end) is { } part && SketchPartKinds.Of(part) == kind);
    }

    private static bool SeriesLedResistor(SketchBoard board, int gpioNet) =>
        board.Sketch.Parts.Where(part => SketchPartKinds.Of(part) == SketchPartKind.Resistor).Any(part =>
        {
            var touched = Touched(board, part);
            return touched.Contains(gpioNet)
                && touched.Any(index => index != gpioNet && NetTouches(board, index, SketchPartKind.Led) && !board.Nets[index].Any(end => IsGroundEnd(board, end)))
                && !touched.Any(index => index != gpioNet && (NetTouchesOther(board, index, part, SketchPartKind.Resistor) || NetTouchesOther(board, index, part, SketchPartKind.Switch)));
        });

    private static bool NetTouchesOther(SketchBoard board, int index, SketchPart self, SketchPartKind kind)
    {
        if (index < 0) return false;
        return board.Nets[index].Any(end => board.Owner(end) is { } part && !part.Id.Equals(self.Id, StringComparison.OrdinalIgnoreCase) && SketchPartKinds.Of(part) == kind);
    }

    private static bool ResistorBetween(SketchBoard board, int gpioNet, SketchPartKind kind) =>
        board.Sketch.Parts.Where(part => SketchPartKinds.Of(part) == SketchPartKind.Resistor).Any(part =>
        {
            var touched = Touched(board, part);
            return touched.Contains(gpioNet) && touched.Any(index => index != gpioNet && NetTouches(board, index, kind) && !board.Nets[index].Any(end => IsGroundEnd(board, end)));
        });

    private static HashSet<string> GpioPins(SketchBoard board, Func<int, bool> include)
    {
        var pins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < board.Nets.Count; index++)
        {
            if (!include(index)) continue;
            foreach (var end in board.Nets[index].Where(end => board.Marked(end, Mark.Gpio)))
            {
                pins.Add(SketchBoard.PinOf(end));
            }
        }

        return pins;
    }

    private static double? Ohms(SketchPart part)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            $"{part.Note} {part.Name}",
            @"(\d+(?:\.\d+)?)\s*(k|m)?\s*(?:ohms?|Ω)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        if (!match.Success || !double.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value))
        {
            return null;
        }

        var scale = match.Groups[2].Value.ToLowerInvariant();
        return value * (scale == "k" ? 1000 : scale == "m" ? 0.001 : 1);
    }

    private static SketchRule LedConnected(SketchBoard board)
    {
        var edges = Edges(board, SketchPartKind.Led, SketchPartKind.Resistor);
        var gpio = Nets(board, end => board.Marked(end, Mark.Gpio));
        var ground = Nets(board, WireEnds.IsGround);
        var supply = Nets(board, end => IsSupply(board, end));
        return Reach(edges, gpio, ground) || Reach(edges, supply, gpio)
            ? new SketchRule("LED connected", "The LED is wired from a GPIO pin through a resistor.", "Pass")
            : new SketchRule("LED connected", "The LED is not wired from a GPIO pin to ground through a resistor.", "Fail");
    }

    private static SketchRule ButtonConnected(SketchBoard board)
    {
        var passed = board.Sketch.Parts.Where(part => SketchPartKinds.Of(part) == SketchPartKind.Switch).All(part =>
        {
            var touched = Touched(board, part);
            return touched.Count >= 2
                && !ButtonFeedsLed(board, part)
                && touched.Any(index => board.Nets[index].Any(end => board.Marked(end, Mark.Gpio)))
                && touched.Any(index => board.Nets[index].Any(end => IsGroundEnd(board, end) || IsSupplyEnd(board, end)));
        });
        return passed
            ? new SketchRule("Button connected", "The push button connects a GPIO pin to a supply or to ground.", "Pass")
            : new SketchRule("Button connected", "The push button should connect the GPIO pin to 3V3.", "Fail");
    }

    private static bool ButtonFeedsLed(SketchBoard board, SketchPart button)
    {
        var touched = Touched(board, button);
        foreach (var led in board.Sketch.Parts.Where(part => SketchPartKinds.Of(part) == SketchPartKind.Led))
        {
            var ledNets = Touched(board, led);
            var shared = ledNets.Where(touched.Contains).ToList();
            if (shared.Count == 0)
            {
                continue;
            }

            var ownSignal = ledNets.Any(index => !touched.Contains(index) && !board.Nets[index].Any(end => IsGroundEnd(board, end)));
            if (!ownSignal || shared.Any(index => !board.Nets[index].Any(end => IsGroundEnd(board, end))))
            {
                return true;
            }
        }

        return false;
    }

    private static SketchRule CommonGround(SketchBoard board)
    {
        var controller = ControllerGround(board);
        var foreign = board.Nets
            .Select((net, index) => (net, index))
            .Where(item => item.net.Any(end => IsGroundEnd(board, end)) && item.net.Any(end => board.Owner(end) is { } part && !SketchBoard.IsController(part)))
            .Select(item => item.index)
            .ToHashSet();
        return controller.Count > 0 && foreign.All(controller.Contains)
            ? new SketchRule("Common ground", "The controller shares ground with the button and LED.", "Pass")
            : new SketchRule("Common ground", "The controller does not share ground with the button and LED.", "Fail");
    }

    private static HashSet<int> ControllerGround(SketchBoard board) =>
        Nets(board, end => IsControllerGroundPin(board, end) && board.Nets.Any(net => net.Contains(end) && net.Any(other => IsGroundEnd(board, other))));

    private static bool ButtonSide(SketchBoard board, List<int> gpioNets, bool ground) =>
        board.Sketch.Parts.Where(part => SketchPartKinds.Of(part) == SketchPartKind.Switch).Any(part =>
        {
            var touched = Touched(board, part);
            return touched.Any(gpioNets.Contains)
                && touched.Any(index => board.Nets[index].Any(end => ground ? IsGroundEnd(board, end) : IsSupplyEnd(board, end)));
        });

    private static bool IsControllerPowerPin(SketchBoard board, string end) =>
        IsControllerPin(board, end, "3V3", "3.3V", "+3V3", "+3.3V", "5V", "+5V", "VCC", "VDD", "VIN");

    private static bool IsControllerGroundPin(SketchBoard board, string end) =>
        IsControllerPin(board, end, "GND", "VSS", "AGND");

    private static bool IsSupplyEnd(SketchBoard board, string end) =>
        IsSupply(board, end) || IsControllerPowerPin(board, end);

    private static bool IsGroundEnd(SketchBoard board, string end) =>
        WireEnds.IsGround(end)
        || end.Equals("AGND", StringComparison.OrdinalIgnoreCase)
        || IsControllerGroundPin(board, end);

    private static bool IsControllerPin(SketchBoard board, string end, params string[] pins)
    {
        var part = board.Owner(end);
        return part is not null
            && SketchBoard.IsController(part)
            && pins.Any(pin => SketchBoard.PinOf(end).Equals(pin, StringComparison.OrdinalIgnoreCase));
    }

    private static bool SupplyToken(string end) =>
        end.Equals("3V3", StringComparison.OrdinalIgnoreCase)
        || end.Equals("3.3V", StringComparison.OrdinalIgnoreCase)
        || end.Equals("+3V3", StringComparison.OrdinalIgnoreCase)
        || end.Equals("+3.3V", StringComparison.OrdinalIgnoreCase)
        || end.Equals("VCC", StringComparison.OrdinalIgnoreCase)
        || end.Equals("VDD", StringComparison.OrdinalIgnoreCase)
        || end.Equals("VIN", StringComparison.OrdinalIgnoreCase);

    private static bool IsSupply(SketchBoard board, string end) =>
        WireEnds.IsSupply(end)
        || SupplyToken(end)
        || board.Owner(end) is { } part && !SketchBoard.IsController(part) && (SketchPartKinds.Of(part) == SketchPartKind.Supply || SupplyToken(part.Id) || SupplyToken(part.Name));

    private static HashSet<int> Nets(SketchBoard board, Func<string, bool> match)
    {
        var found = new HashSet<int>();
        for (var index = 0; index < board.Nets.Count; index++)
        {
            if (board.Nets[index].Any(match))
            {
                found.Add(index);
            }
        }

        return found;
    }

    private static List<int> Touched(SketchBoard board, SketchPart part)
    {
        var touched = new List<int>();
        for (var index = 0; index < board.Nets.Count; index++)
        {
            if (board.Nets[index].Any(end => SketchBoard.Belongs(end, part)))
            {
                touched.Add(index);
            }
        }

        return touched;
    }

    private static List<(int From, int To, bool Led, bool Resistor)> Edges(SketchBoard board, params SketchPartKind[] kinds)
    {
        var edges = new List<(int From, int To, bool Led, bool Resistor)>();
        foreach (var part in board.Sketch.Parts)
        {
            var kind = SketchPartKinds.Of(part);
            if (!kinds.Contains(kind))
            {
                continue;
            }

            var touched = Touched(board, part);
            if (touched.Count >= 2)
            {
                edges.Add((touched[0], touched[1], kind == SketchPartKind.Led, kind == SketchPartKind.Resistor));
            }
        }

        return edges;
    }

    private static bool Reach(
        List<(int From, int To, bool Led, bool Resistor)> edges,
        HashSet<int> starts,
        HashSet<int> goals)
    {
        var pending = new Queue<(int Net, bool Led, bool Resistor)>();
        var seen = new HashSet<(int Net, bool Led, bool Resistor)>();
        foreach (var net in starts)
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

            if (goals.Contains(state.Net) && state.Led && state.Resistor)
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

    private static List<SketchWire> SplitLeads(SchematicSketch sketch)
    {
        var wires = sketch.Wires.Select(wire => new SketchWire(wire.From.Trim(), wire.To.Trim())).ToList();
        foreach (var part in sketch.Parts)
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

            var pins = hits.Select(index => On(wires[index].From, part.Id) ? wires[index].From : wires[index].To)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
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
}
