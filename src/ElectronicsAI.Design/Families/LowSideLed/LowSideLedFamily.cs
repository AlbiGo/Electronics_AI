using System.Globalization;
using System.Text.RegularExpressions;
using static ElectronicsAI.Design.PatternChecks;

namespace ElectronicsAI.Design;

public static partial class LowSideLedFamily
{
    private static readonly PartQuery Led = new(["led"], PartText.TypeName);

    private static readonly PartQuery Controller = new(["mcu", "esp32", "arduino", "stm32", "rp2040", "ecu", "controller"], PartText.TypeName);

    private static readonly PartQuery Transistor = new(["mosfet", "nmos", "transistor", "npn", "bjt"], PartText.TypeName);

    private static readonly PartQuery Resistor = new(["resistor", "ohm"], PartText.TypeNameNote);

    private static readonly PartQuery Motor = new(["motor", "pump"], PartText.TypeName);

    private static readonly PartQuery Solenoid = new(["solenoid"], PartText.TypeName);

    private static readonly PartQuery Relay = new(["relay"], PartText.TypeName);

    private static readonly End Gate = new(Transistor, ["g", "gate", "base"]);

    private static readonly End Drain = new(Transistor, ["d", "drain", "collector"]);

    private static readonly End Source = new(Transistor, ["s", "source", "emitter"]);

    public static CircuitPattern Pattern { get; } = new()
    {
        Category = CircuitCategory.LowSideLed,
        Reason = "Low-side LED driver checked without ngspice.",
        When = static board => board.Has(Led) && board.Has(Transistor) && board.Has(Controller) && !board.Has(Motor) && !board.Has(Solenoid) && !board.Has(Relay),
        Assume = static (_, _) =>
        [
            "The LED forward voltage is 2.0 V.",
            "The LED current stays in the MOSFET drain.",
        ],
        Checks =
        [
            new BatchCheck(GateDrive),
            new BatchCheck(LowSideDrain),
            new BatchCheck(SeriesResistor),
            new BatchCheck(LedOffGpio),
            new BatchCheck(SharedGround),
            new BatchCheck(LedCurrent),
        ],
    };

    private static IReadOnlyList<SketchRule> GateDrive(SketchBoard board, string? request)
    {
        var gateSide = PinsNamed(board) ? Gate : new End(Transistor);
        var driven = board.Sketch.Parts.Where(Resistor.Matches).Any(part =>
        {
            var nets = NetsOf(board, part);
            return nets.Any(net => IsSignalNet(board, net)) && nets.Any(net => Fits(board, net, gateSide) && !IsSignalNet(board, net));
        });
        return [Rule("Gate drive", driven ? "A GPIO pin drives the MOSFET gate through a resistor." : "The GPIO must drive the MOSFET gate through a resistor.", driven)];
    }

    private static IReadOnlyList<SketchRule> LowSideDrain(SketchBoard board, string? request)
    {
        var sink = PinsNamed(board)
            ? board.Nets.Any(net => Fits(board, net, new End(Led)) && Fits(board, net, Drain) && !IsSupplyNet(board, net))
                && board.Nets.Any(net => Fits(board, net, Source) && IsGroundNet(board, net) && !Fits(board, net, new End(Led)))
            : board.Nets.Any(net => Fits(board, net, new End(Transistor)) && Fits(board, net, new End(Led)) && !IsSupplyNet(board, net))
                && board.Nets.Any(net => Fits(board, net, new End(Transistor)) && IsGroundNet(board, net) && !Fits(board, net, new End(Led)));
        return [Rule("Low-side drain", sink ? "The LED sinks through the MOSFET drain, and the source is grounded." : "The LED must sink through the MOSFET drain, with the source at ground.", sink)];
    }

    private static IReadOnlyList<SketchRule> SeriesResistor(SketchBoard board, string? request)
    {
        var series = board.Sketch.Parts.Where(Resistor.Matches).Any(part =>
        {
            var nets = NetsOf(board, part);
            return nets.Any(net => Fits(board, net, new End(Led)) && !IsSignalNet(board, net))
                && nets.Any(net => IsSupplyNet(board, net))
                && nets.All(net => !IsSignalNet(board, net));
        });
        return [Rule("LED series resistor", series ? "The series resistor carries the LED current." : "The series resistor should carry the LED current, not the gate current.", series)];
    }

    private static IReadOnlyList<SketchRule> LedOffGpio(SketchBoard board, string? request)
    {
        var onSignal = board.Nets.Any(net => Fits(board, net, new End(Led)) && IsSignalNet(board, net));
        var through = board.Sketch.Parts.Where(Resistor.Matches).Any(part =>
        {
            var nets = NetsOf(board, part);
            return nets.Any(net => Fits(board, net, new End(Led))) && nets.Any(net => IsSignalNet(board, net));
        });
        var clear = !onSignal && !through;
        return [Rule("LED off the GPIO", clear ? "The LED current does not come from a GPIO pin." : "The LED is wired to a GPIO pin. Drive the MOSFET gate instead.", clear)];
    }

    private static IReadOnlyList<SketchRule> SharedGround(SketchBoard board, string? request)
    {
        var sourceGrounded = PinsNamed(board)
            ? board.Nets.Any(net => Fits(board, net, Source) && IsGroundNet(board, net))
            : board.Nets.Any(net => Fits(board, net, new End(Transistor)) && !Fits(board, net, new End(Led)) && IsGroundNet(board, net));
        var controllerGrounded = board.Nets.Any(net =>
            IsGroundNet(board, net) && net.Any(end => board.Owner(end) is { } part && Controller.Matches(part)));
        var shared = sourceGrounded && controllerGrounded;
        return [Rule("Common ground", shared ? "The controller ground joins the MOSFET source." : "The controller and the LED driver do not share a ground.", shared)];
    }

    private static bool IsSignalNet(SketchBoard board, HashSet<string> net) =>
        !IsSupplyNet(board, net) && !IsGroundNet(board, net) && net.Any(end => IsDriveEnd(board, end));

    private static bool IsSupplyNet(SketchBoard board, HashSet<string> net) => net.Any(end => IsSupplyEnd(board, end));

    private static bool IsGroundNet(SketchBoard board, HashSet<string> net) => net.Any(end => IsGroundEnd(board, end));

    private static bool IsDriveEnd(SketchBoard board, string end)
    {
        if (IsSupplyEnd(board, end) || IsGroundEnd(board, end))
        {
            return false;
        }

        var owner = board.Owner(end);
        if (owner is not null && Controller.Matches(owner))
        {
            return true;
        }

        var pin = SketchBoard.PinOf(end);
        return pin.Equals("OUT", StringComparison.OrdinalIgnoreCase)
            || pin.Equals("DOUT", StringComparison.OrdinalIgnoreCase)
            || pin.Equals("SIGNAL", StringComparison.OrdinalIgnoreCase)
            || SignalPin().IsMatch(pin);
    }

    private static bool IsSupplyEnd(SketchBoard board, string end)
    {
        if (board.Marked(end, Mark.LogicHigh) || board.Marked(end, Mark.LowVoltage) || board.Marked(end, Mark.HighVoltage) || board.Marked(end, Mark.WireSupply))
        {
            return true;
        }

        if (Rail().IsMatch(SketchBoard.PinOf(end)) || Rail().IsMatch(end))
        {
            return true;
        }

        var owner = board.Owner(end);
        if (owner is null || Controller.Matches(owner) || Transistor.Matches(owner) || Led.Matches(owner) || Resistor.Matches(owner))
        {
            return false;
        }

        return SketchPartKinds.Of(owner) == SketchPartKind.Supply || Rail().IsMatch($"{owner.Name} {owner.Note}");
    }

    private static bool IsGroundEnd(SketchBoard board, string end)
    {
        if (board.Marked(end, Mark.Ground) || board.Marked(end, Mark.WireGround))
        {
            return true;
        }

        var pin = SketchBoard.PinOf(end);
        if (pin.Contains("GND", StringComparison.OrdinalIgnoreCase) || pin.Contains("GROUND", StringComparison.OrdinalIgnoreCase) || pin.Contains("VSS", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var owner = board.Owner(end);
        return owner is not null && SketchPartKinds.Of(owner) == SketchPartKind.Ground;
    }

    private static bool PinsNamed(SketchBoard board) =>
        board.Sketch.Wires.SelectMany(wire => new[] { wire.From, wire.To }).Any(end =>
            board.Owner(end) is { } part && Transistor.Matches(part) && IsSwitchPin(SketchBoard.PinOf(end)));

    private static bool IsSwitchPin(string pin) =>
        pin.Equals("g", StringComparison.OrdinalIgnoreCase) || pin.Equals("gate", StringComparison.OrdinalIgnoreCase) || pin.Equals("base", StringComparison.OrdinalIgnoreCase)
        || pin.Equals("d", StringComparison.OrdinalIgnoreCase) || pin.Equals("drain", StringComparison.OrdinalIgnoreCase) || pin.Equals("collector", StringComparison.OrdinalIgnoreCase)
        || pin.Equals("s", StringComparison.OrdinalIgnoreCase) || pin.Equals("source", StringComparison.OrdinalIgnoreCase) || pin.Equals("emitter", StringComparison.OrdinalIgnoreCase);

    private static List<HashSet<string>> NetsOf(SketchBoard board, SketchPart part) =>
        board.Nets.Where(net => net.Any(end => SketchBoard.Belongs(end, part))).ToList();

    private static bool Fits(SketchBoard board, HashSet<string> net, End end) => ClauseMatch.Fits(board, net, end);

    private static SketchRule Rule(string name, string detail, bool pass) => new(name, detail, pass ? "Pass" : "Fail");

    private static IReadOnlyList<SketchRule> LedCurrent(SketchBoard board, string? request)
    {
        var series = board.Sketch.Parts.FirstOrDefault(part =>
            Resistor.Matches(part)
            && Touches(board, part, new End(Led))
            && !Touches(board, part, Gate));
        if (series is null || !Ohms().Match($"{series.Name} {series.Note}").Success)
        {
            return [new SketchRule("LED current", "The LED resistor has no resistance.", "Fail")];
        }

        var match = Ohms().Match($"{series.Name} {series.Note}");
        if (!double.TryParse(match.Groups["n"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return [new SketchRule("LED current", "The LED resistor has no resistance.", "Fail")];
        }

        var ohms = match.Groups["u"].Value.ToLowerInvariant() switch
        {
            "k" => number * 1_000,
            "meg" => number * 1_000_000,
            _ => number,
        };
        var volts = board.Nets.Any(net => net.Any(end => board.Marked(end, Mark.LowVoltage) || end.Contains("3V3", StringComparison.OrdinalIgnoreCase))) ? 3.3 : 5.0;
        var current = (volts - 2.0) / ohms;
        return
        [
            current is > 0 and <= 0.02
                ? new SketchRule("LED current", $"{current * 1000:0.0} mA is within the LED limit.", "Pass")
                : new SketchRule("LED current", $"{current * 1000:0.0} mA exceeds the 20 mA LED limit.", "Fail"),
        ];
    }

    private static bool Touches(SketchBoard board, SketchPart part, End end) =>
        board.Nets.Any(net => net.Any(item => SketchBoard.Belongs(item, part)) && ClauseMatch.Fits(board, net, end));

    [GeneratedRegex(@"(?<n>\d+(?:\.\d+)?)\s*(?<u>meg|k)?\s*(?:ohms?|ω|Ω)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Ohms();

    [GeneratedRegex(@"(^|[^A-Z0-9])(\+?\d+(?:\.\d+)?\s*V|3V3|VCC|VDD|VIN|VBAT)([^A-Z0-9]|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Rail();

    [GeneratedRegex(@"^(GPIO\d*|GP\d+|IO\d+|D\d+|PWM\d*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SignalPin();
}
