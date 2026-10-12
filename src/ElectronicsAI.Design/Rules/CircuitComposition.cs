using System.Text.RegularExpressions;

namespace ElectronicsAI.Design;

public static class CircuitComposition
{
    private static readonly PartQuery SensorPart = new(["sensor", "temperature", "thermistor", "ntc", "lm35", "tmp36"], PartText.TypeName);

    private static readonly PartQuery ControllerPart = new(["mcu", "esp32", "arduino", "stm32", "rp2040", "ecu", "controller"], PartText.TypeName);

    private static readonly PartQuery Transistor = new(["mosfet", "nmos", "transistor", "npn", "bjt"], PartText.TypeName);

    private static readonly PartQuery Resistor = new(["resistor", "ohm"], PartText.TypeNameNote);

    private static readonly PartQuery LoadPart = new(["fan", "motor", "pump", "solenoid", "relay", "led"], PartText.TypeName);

    private static readonly End Gate = new(Transistor, ["g", "gate", "base"]);

    private static readonly End Drain = new(Transistor, ["d", "drain", "collector"]);

    private static readonly End Source = new(Transistor, ["s", "source", "emitter"]);

    public static SketchRuleResult? Compose(ISketchRuleSet? primary, SchematicSketch sketch, string? request)
    {
        if (primary is null)
        {
            var only = Additional(sketch, []);
            return only.Count == 0 ? null : new SketchRuleResult(Reason(sketch), only, []);
        }

        var outcome = primary.Evaluate(sketch, request);
        var extra = Additional(sketch, outcome.Rules);
        if (extra.Count == 0)
        {
            return outcome;
        }

        return outcome with
        {
            Reason = Reason(sketch),
            Rules = outcome.Rules.Concat(extra).ToList(),
        };
    }

    public static IReadOnlyList<SketchRule> Additional(SchematicSketch sketch, IReadOnlyList<SketchRule> present)
    {
        var roles = CircuitRoles.Present(sketch);
        var board = SketchBoard.Of(sketch);
        var rules = new List<SketchRule>();
        if (roles.HasFlag(CircuitRole.Sensor) && !Named(present, "Sensor powered"))
        {
            rules.AddRange(SensorMustBePoweredRule.Run(board));
        }

        if (roles.HasFlag(CircuitRole.Sensor) && roles.HasFlag(CircuitRole.Driver) && !Named(present, "ADC input"))
        {
            rules.Add(AdcInput(board));
        }

        if (roles.HasFlag(CircuitRole.Sensor) && roles.HasFlag(CircuitRole.Controller) && !Named(present, "Controller powered", "ECU powered"))
        {
            rules.Add(ControllerPowered(board));
        }

        if (roles.HasFlag(CircuitRole.Driver) && !Named(present, "Gate drive", "Gate resistor", "MOSFET or transistor", "MOSFET present", "MOSFET terminals"))
        {
            rules.Add(MosfetPresent(board));
            rules.Add(GateResistor(board));
        }

        if (roles.HasFlag(CircuitRole.Driver)
            && !Named(present, "Low-side drain", "MOSFET terminals", "Source grounded")
            && !present.Any(rule => rule.Name.Equals("Common ground", StringComparison.OrdinalIgnoreCase)))
        {
            rules.Add(SourceGrounded(board));
        }

        if (roles.HasFlag(CircuitRole.Load) && !Named(present, "LED off the GPIO", "Separate motor supply", "Solenoid not driven", "Load powered", "Load off the GPIO"))
        {
            rules.Add(LoadPowered(board));
            rules.Add(LoadOffGpio(board));
        }

        if (roles.HasFlag(CircuitRole.Load) && sketch.Parts.Any(CircuitRoles.IsInductive) && !Named(present, "Flyback"))
        {
            rules.AddRange(FlybackDiodeRule.AcrossMotor(board));
        }

        return rules;
    }

    public static string Reason(SchematicSketch sketch)
    {
        var names = new List<string>();
        var roles = CircuitRoles.Present(sketch);
        if (roles.HasFlag(CircuitRole.Sensor))
        {
            names.Add("sensor");
        }

        if (roles.HasFlag(CircuitRole.Input))
        {
            names.Add("input");
        }

        if (roles.HasFlag(CircuitRole.Controller))
        {
            names.Add("controller");
        }

        if (roles.HasFlag(CircuitRole.Driver))
        {
            names.Add("driver");
        }

        if (roles.HasFlag(CircuitRole.Load))
        {
            names.Add("load");
        }

        if (roles.HasFlag(CircuitRole.Communication))
        {
            names.Add("communication");
        }

        if (roles.HasFlag(CircuitRole.Protection))
        {
            names.Add("protection");
        }

        return names.Count == 0
            ? "Circuit checked without ngspice."
            : $"{string.Join(", ", names)} checked without ngspice.";
    }

    private static SketchRule AdcInput(SketchBoard board)
    {
        var reached = board.Nets.Any(net =>
            net.Any(end => board.Owner(end) is { } sensor && SensorPart.Matches(sensor))
            && net.Any(end => board.Owner(end) is { } controller && ControllerPart.Matches(controller) && IsAdc(SketchBoard.PinOf(end)))
            && !ClauseMatch.Fits(board, net, Gate));
        return Rule(
            "ADC input connected",
            reached ? "The sensor output reaches an ADC pin." : "The sensor output must reach an ADC pin, not the MOSFET gate.",
            reached);
    }

    private static SketchRule ControllerPowered(SketchBoard board)
    {
        var powered = board.Nets.Any(net =>
            net.Any(end => IsControllerSupply(board, end))
            && net.Any(end => IsSupply(board, end) || board.Owner(end) is null || board.Owner(end) is { } owner && !ControllerPart.Matches(owner)));
        return Rule(
            "Controller powered",
            powered ? "The controller supply pin is on a supply net." : "The controller supply pin is not on a supply net.",
            powered);
    }

    private static SketchRule MosfetPresent(SketchBoard board) =>
        Rule(
            "MOSFET present",
            board.Has(Transistor) ? "A MOSFET switches the load." : "The sketch has no MOSFET or transistor.",
            board.Has(Transistor));

    private static SketchRule GateResistor(SketchBoard board)
    {
        var driven = board.Sketch.Parts.Where(Resistor.Matches).Any(part =>
        {
            var nets = NetsOf(board, part);
            return nets.Any(net => net.Any(end => IsDrive(board, end)))
                && nets.Any(net => ClauseMatch.Fits(board, net, Gate) || ClauseMatch.Fits(board, net, new End(Transistor)) && !ClauseMatch.Fits(board, net, new End(LoadPart)));
        });
        return Rule(
            "Gate resistor present",
            driven ? "A resistor joins the GPIO pin to the gate." : "No resistor joins the GPIO pin to the gate.",
            driven);
    }

    private static SketchRule SourceGrounded(SketchBoard board)
    {
        var source = board.Nets.Any(net =>
            (ClauseMatch.Fits(board, net, Source) || ClauseMatch.Fits(board, net, new End(Transistor)))
            && IsGround(board, net)
            && !ClauseMatch.Fits(board, net, new End(LoadPart)));
        var controller = board.Nets.Any(net => IsGround(board, net) && net.Any(end => board.Owner(end) is { } part && ControllerPart.Matches(part)));
        var shared = source && controller;
        return Rule(
            "Common ground",
            shared ? "The controller ground joins the MOSFET source." : "The controller and the load switch do not share a ground.",
            shared);
    }

    private static SketchRule LoadPowered(SketchBoard board)
    {
        var powered = board.Sketch.Parts.Where(part => LoadPart.Matches(part) || SketchPartKinds.Of(part) == SketchPartKind.Led).Any(part =>
        {
            var nets = NetsOf(board, part);
            return nets.Any(net => IsSupply(board, net)) && nets.Any(net => ClauseMatch.Fits(board, net, Drain) || ClauseMatch.Fits(board, net, new End(Transistor)));
        });
        return Rule(
            "Load powered",
            powered ? "The load is powered through the switch." : "The load needs a supply and a path through the switch.",
            powered);
    }

    private static SketchRule LoadOffGpio(SketchBoard board)
    {
        var onGpio = board.Nets.Any(net =>
            net.Any(end => board.Owner(end) is { } part && (LoadPart.Matches(part) || SketchPartKinds.Of(part) == SketchPartKind.Led))
            && net.Any(end => IsDrive(board, end)));
        return Rule(
            "Load off the GPIO",
            onGpio ? "The load is wired to a GPIO pin. Drive the switch instead." : "The load current does not come from a GPIO pin.",
            !onGpio);
    }

    private static bool IsControllerSupply(SketchBoard board, string end)
    {
        var pin = SketchBoard.PinOf(end);
        return board.Owner(end) is { } part
            && ControllerPart.Matches(part)
            && Regex.IsMatch(pin, @"^(3V3|3\.3V|\+3V3|\+3\.3V|VCC|VDD|VIN|\+?5V)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool IsAdc(string pin) =>
        pin.Contains("ADC", StringComparison.OrdinalIgnoreCase)
        || Regex.IsMatch(pin, @"^(A\d+|AN\d*|TempIn|TEMP_IN|TSENS|ANALOG)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static bool IsDrive(SketchBoard board, string end)
    {
        if (IsSupply(board, end) || board.Marked(end, Mark.Ground) || board.Marked(end, Mark.WireGround))
        {
            return false;
        }

        var pin = SketchBoard.PinOf(end);
        return board.Owner(end) is { } part
            && ControllerPart.Matches(part)
            && !IsAdc(pin)
            && !IsControllerSupply(board, end)
            && !pin.Contains("GND", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSupply(SketchBoard board, HashSet<string> net) => net.Any(end => IsSupply(board, end));

    private static bool IsSupply(SketchBoard board, string end) =>
        board.Marked(end, Mark.WireSupply)
        || board.Marked(end, Mark.LogicHigh)
        || board.Marked(end, Mark.MotorFlybackSupply)
        || board.Marked(end, Mark.LowVoltage)
        || board.Marked(end, Mark.HighVoltage);

    private static bool IsGround(SketchBoard board, HashSet<string> net) => net.Any(end => board.Marked(end, Mark.Ground) || board.Marked(end, Mark.WireGround));

    private static List<HashSet<string>> NetsOf(SketchBoard board, SketchPart part) =>
        board.Nets.Where(net => net.Any(end => SketchBoard.Belongs(end, part))).ToList();

    private static bool Named(IReadOnlyList<SketchRule> rules, params string[] names) =>
        rules.Any(rule => names.Any(name => rule.Name.Contains(name, StringComparison.OrdinalIgnoreCase)));

    private static SketchRule Rule(string name, string detail, bool pass) => new(name, detail, pass ? "Pass" : "Fail");
}
