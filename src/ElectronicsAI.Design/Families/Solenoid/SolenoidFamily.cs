using System.Text.RegularExpressions;
using static ElectronicsAI.Design.PatternChecks;

namespace ElectronicsAI.Design;

public static partial class SolenoidFamily
{
    private static readonly PartQuery Solenoid = new(["solenoid"], PartText.TypeName);

    private static readonly PartQuery Ecu = new(["ecu"], PartText.TypeName);

    private static readonly PartQuery Temperature = new(["temperature", "thermistor", "ntc", "lm35", "tmp36"], PartText.TypeName);

    private static readonly PartQuery SwitchPart = new(["switch", "button"], PartText.TypeName);

    private static readonly PartQuery DiodePart = new(["diode", "flyback"], PartText.TypeName, ["led"]);

    private static readonly PartQuery PowerSwitch = new(["mosfet", "nmos", "transistor", "npn", "bjt"], PartText.TypeName);

    private static readonly End TempIn = new(Ecu, ["TempIn", "adc1", "gpio_in", "A0", "A1", "AN", "TEMP", "TSENS", "ANALOG"], ["GPIO", "ADC", "AIN", "TEMP"]);

    private static readonly End SwitchIn = new(Ecu, ["SwitchIn", "gpio_in", "IN", "DIN", "SW"], ["GPIO", "SW"]);

    private static readonly End EcuPin = new(Ecu, ["TempIn", "SwitchIn", "SolenoidOut", "gpio_in", "gpio_out", "adc1"], ["GPIO", "ADC", "PWM", "AIN"]);

    private static readonly End Drain = new(PowerSwitch, ["d", "drain", "collector"]);

    private static readonly End Source = new(PowerSwitch, ["s", "source", "emitter"]);

    private static readonly PartQuery ResistorPart = new(["resistor", "ohm"], PartText.TypeNameNote);

    private static readonly End Supply = new(Mark: Mark.MotorFlybackSupply);

    private static readonly End Ground = new(Mark: Mark.Ground);

    public static CircuitPattern Pattern { get; } = new()
    {
        Category = CircuitCategory.Solenoid,
        Reason = "ECU solenoid circuit checked without ngspice.",
        When = static board => board.Has(Solenoid) && (board.Has(Ecu) || board.Has(Temperature) || board.Has(SwitchPart)),
        Assume = static (_, _) =>
        [
            "The ECU logic pins are 3.3 V.",
            "The solenoid current stays in the MOSFET.",
        ],
        Checks =
        [
            Check("ECU present", "{name} is present.", "The sketch has no ECU.", new HasPart(Ecu), Ecu, "The sketch has no ECU."),
            Check("Temperature sensor connected", "The temperature sensor output reaches the ECU.", "The temperature sensor is not connected to the ECU.",
                new SameNet(new End(Temperature), TempIn, Ground), Temperature, "The sketch has no temperature sensor."),
            Check("Switch connected", "The switch reaches an ECU input.", "The switch is not connected to an ECU input.",
                new SameNet(new End(SwitchPart), SwitchIn, Ground), SwitchPart, "The sketch has no switch."),
            Check("Solenoid connected through driver", "The MOSFET switches the solenoid from the coil supply to ground.", "The solenoid is not switched by a MOSFET between the supply and ground.",
                new Every(
                    new SameNet(Drain, new End(Solenoid), Supply),
                    new SameNet(new End(Solenoid), Supply, Drain),
                    new SameNet(Source, Ground, new End(Solenoid))),
                PowerSwitch, "The sketch has no MOSFET or transistor."),
            new BatchCheck(Flyback),
            new BatchCheck(MosfetTerminals),
            new BatchCheck(GateResistor),
            Check("Common ground present", "The ECU ground joins the MOSFET source.", "The ECU and the solenoid driver do not share a ground.",
                new SameNet(new End(Ecu, Mark: Mark.Ground), new End(PowerSwitch), new End(Solenoid))),
            Check("Solenoid not driven directly from ECU pin", "The solenoid is switched by the MOSFET, not by an ECU pin.", "The solenoid is wired directly to an ECU pin.",
                new Absent(new SameNet(new End(Solenoid), EcuPin))),
            new BatchCheck(SupplyVoltage),
        ],
    };

    private static IReadOnlyList<SketchRule> Flyback(SketchBoard board, string? request)
    {
        var diode = board.Sketch.Parts.FirstOrDefault(DiodePart.Matches);
        if (diode is null)
        {
            return [Rule("Flyback diode present", "Add a flyback diode across the solenoid, cathode toward the coil supply, so the inductive spike does not reach the controller pin.", false)];
        }

        var nets = Touched(board, diode);
        if (nets.Count < 2)
        {
            return [Rule("Flyback diode present", "Add a flyback diode across the solenoid, cathode toward the coil supply, so the inductive spike does not reach the controller pin.", false)];
        }

        var supplySide = nets.FirstOrDefault(net => On(board, net, Solenoid) && OnMark(board, net, Mark.MotorFlybackSupply));
        var drainSide = nets.FirstOrDefault(net => On(board, net, Solenoid) && OnPin(net, Drain) && !OnMark(board, net, Mark.MotorFlybackSupply));
        if (supplySide is null || drainSide is null || ReferenceEquals(supplySide, drainSide))
        {
            return [Rule("Flyback diode present", "The flyback diode is in series with the control path. Place it across the solenoid, cathode toward the coil supply and anode toward the drain.", false)];
        }

        var named = nets.SelectMany(net => net).Where(end => SketchBoard.Belongs(end, diode)).ToList();
        var cathodeOnSupply = named.Any(end => supplySide.Contains(end) && IsCathode(end));
        var anodeOnDrain = named.Any(end => drainSide.Contains(end) && IsAnode(end));
        var polarityNamed = named.Any(IsCathode) && named.Any(IsAnode);
        if (polarityNamed && (!cathodeOnSupply || !anodeOnDrain))
        {
            return [Rule("Flyback diode present", "The flyback diode is backwards. Connect the cathode to the coil supply and the anode to the drain.", false)];
        }

        return [Rule("Flyback diode present", "The diode is across the solenoid, cathode toward the coil supply.", true)];
    }

    private static IReadOnlyList<SketchRule> MosfetTerminals(SketchBoard board, string? request)
    {
        var fet = board.Sketch.Parts.FirstOrDefault(PowerSwitch.Matches);
        if (fet is null)
        {
            return [];
        }

        var labeled = board.Sketch.Wires.SelectMany(wire => new[] { wire.From, wire.To }).Where(end => SketchBoard.Belongs(end, fet)).Any(end => IsGate(end) || IsDrain(end) || IsSource(end));
        if (!labeled)
        {
            return [Rule("MOSFET terminals", "Label the MOSFET gate, drain, and source.", false)];
        }

        var gateOnCoil = board.Nets.Any(net => OnPin(net, new End(PowerSwitch, ["g", "gate", "base"])) && On(board, net, Solenoid));
        var drainOnSignal = board.Nets.Any(net => OnPin(net, Drain) && On(board, net, Ecu) && !On(board, net, Solenoid));
        if (gateOnCoil || drainOnSignal)
        {
            return [Rule("MOSFET terminals", "The MOSFET gate and drain are swapped. Drive the gate from the gate resistor, and connect the solenoid to the drain.", false)];
        }

        var drainOnCoil = board.Nets.Any(net => OnPin(net, Drain) && On(board, net, Solenoid) && !OnMark(board, net, Mark.MotorFlybackSupply));
        var sourceOnGround = board.Nets.Any(net => OnPin(net, Source) && OnMark(board, net, Mark.Ground) && !On(board, net, Solenoid));
        return [Rule("MOSFET terminals", drainOnCoil && sourceOnGround ? "The solenoid uses the drain and the source is grounded." : "The MOSFET gate and drain are swapped. Drive the gate from the gate resistor, and connect the solenoid to the drain.", drainOnCoil && sourceOnGround)];
    }

    private static IReadOnlyList<SketchRule> GateResistor(SketchBoard board, string? request)
    {
        var resistors = board.Sketch.Parts.Where(part => SketchPartKinds.Of(part) == SketchPartKind.Resistor || ResistorPart.Matches(part)).ToList();
        foreach (var resistor in resistors)
        {
            var nets = Touched(board, resistor);
            if (nets.Count < 2 && nets.Any(net => On(board, net, Ecu) || On(board, net, PowerSwitch)))
            {
                return [Rule("Gate resistor", "The gate resistor is bypassed by a parallel wire. The control signal must pass through the resistor to the gate.", false)];
            }

            var drives = nets.Any(net => On(board, net, Ecu)) && nets.Any(net => On(board, net, PowerSwitch));
            if (!drives)
            {
                continue;
            }

            var far = nets.First(net => On(board, net, PowerSwitch));
            if (OnPin(far, Drain) && !OnPin(far, new End(PowerSwitch, ["g", "gate", "base"])))
            {
                return [Rule("Gate resistor", "The gate resistor drives the MOSFET drain. Connect it to the gate.", false)];
            }

            return [Rule("Gate resistor", "The gate resistor joins the ECU output to the MOSFET gate.", true)];
        }

        return [Rule("Gate resistor", "No resistor joins the ECU output to the MOSFET gate.", false)];
    }

    private static List<HashSet<string>> Touched(SketchBoard board, SketchPart part) =>
        board.Nets.Where(net => net.Any(end => SketchBoard.Belongs(end, part))).ToList();

    private static bool On(SketchBoard board, HashSet<string> net, PartQuery role) =>
        net.Any(end => board.Owner(end) is { } part && role.Matches(part));

    private static bool OnMark(SketchBoard board, HashSet<string> net, Mark mark) =>
        net.Any(end => board.Marked(end, mark));

    private static bool OnPin(HashSet<string> net, End pin) =>
        net.Any(end => IsPin(end, pin));

    private static bool IsPin(string end, End pin) =>
        pin.Exact?.Any(name => SketchBoard.PinOf(end).Equals(name, StringComparison.OrdinalIgnoreCase)) == true;

    private static bool IsDrain(string end) => IsPin(end, Drain);

    private static bool IsGate(string end) => IsPin(end, new End(Exact: ["g", "gate", "base"]));

    private static bool IsSource(string end) => IsPin(end, Source);

    private static bool IsCathode(string end) => IsPin(end, new End(Exact: ["k", "cathode"]));

    private static bool IsAnode(string end) => IsPin(end, new End(Exact: ["a", "anode"]));

    private static SketchRule Rule(string name, string detail, bool pass) =>
        new(name, detail, pass ? "Pass" : "Fail");

    private static IReadOnlyList<SketchRule> SupplyVoltage(SketchBoard board, string? request)
    {
        var text = string.Join(" ", board.Sketch.Parts.Select(part => $"{part.Name} {part.Note}"))
            + " " + string.Join(" ", board.Sketch.Wires.Select(wire => $"{wire.From} {wire.To}"));
        var stated = Volts().IsMatch(text);
        return
        [
            new SketchRule(
                "External supply provided",
                stated ? "The solenoid has an external supply." : "The solenoid has no external supply.",
                stated ? "Pass" : "Fail"),
        ];
    }

    [GeneratedRegex(@"\d+(?:\.\d+)?\s*V\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Volts();
}
