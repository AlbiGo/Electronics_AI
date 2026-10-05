using System.Text.RegularExpressions;
using static ElectronicsAI.Design.PatternChecks;

namespace ElectronicsAI.Design;

public static partial class SolenoidFamily
{
    private static readonly PartQuery Solenoid = new(["solenoid"]);

    private static readonly PartQuery Ecu = new(["ecu"], PartText.TypeName);

    private static readonly PartQuery Temperature = new(["temperature", "thermistor", "ntc", "lm35", "tmp36"], PartText.TypeName);

    private static readonly PartQuery SwitchPart = new(["switch"]);

    private static readonly PartQuery DiodePart = new(["diode", "flyback"], Exclude: ["led"]);

    private static readonly PartQuery PowerSwitch = new(["mosfet", "nmos", "transistor", "npn", "bjt"]);

    private static readonly End TempIn = new(Ecu, ["TempIn", "adc1", "gpio_in", "A0", "A1", "AN"], ["GPIO", "ADC", "AIN"]);

    private static readonly End SwitchIn = new(Ecu, ["SwitchIn", "gpio_in"], ["GPIO"]);

    private static readonly End EcuPin = new(Ecu, ["TempIn", "SwitchIn", "SolenoidOut", "gpio_in", "gpio_out", "adc1"], ["GPIO", "ADC", "PWM", "AIN"]);

    private static readonly End SensorOut = new(Temperature, ["OUT", "output", "AO", "A0", "SIG", "AN", "S", "DATA", "DAT"]);

    private static readonly End Drain = new(PowerSwitch, ["d", "drain", "c", "collector"]);

    private static readonly End Source = new(PowerSwitch, ["s", "source", "e", "emitter"]);

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
                new SameNet(SensorOut, TempIn, Ground), Temperature, "The sketch has no temperature sensor."),
            Check("Switch connected", "The switch reaches an ECU input.", "The switch is not connected to an ECU input.",
                new SameNet(new End(SwitchPart), SwitchIn, Ground), SwitchPart, "The sketch has no switch."),
            Check("Solenoid connected through driver", "The MOSFET switches the solenoid from the coil supply to ground.", "The solenoid is not switched by a MOSFET between the supply and ground.",
                new Every(
                    new SameNet(Drain, new End(Solenoid), Supply),
                    new SameNet(new End(Solenoid), Supply, Drain),
                    new SameNet(Source, Ground)),
                PowerSwitch, "The sketch has no MOSFET or transistor."),
            Check("Flyback diode present", "The diode is across the solenoid.", "The flyback diode is not across the solenoid.",
                new Every(new SameNet(Drain, new End(DiodePart)), new SameNet(new End(DiodePart), Supply)),
                DiodePart, "The sketch has no flyback diode."),
            Check("Common ground present", "The ECU ground joins the MOSFET source.", "The ECU and the solenoid driver do not share a ground.",
                new SameNet(new End(Ecu, Mark: Mark.Ground), Source)),
            Check("Solenoid not driven directly from ECU pin", "The solenoid is switched by the MOSFET, not by an ECU pin.", "The solenoid is wired directly to an ECU pin.",
                new Absent(new SameNet(new End(Solenoid), EcuPin))),
            new BatchCheck(SupplyVoltage),
        ],
    };

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
