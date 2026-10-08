using static ElectronicsAI.Design.PatternChecks;

namespace ElectronicsAI.Design;

public static class FlybackDiodeRule
{
    private static readonly PartQuery DiodePart = new(["diode"]);

    private static readonly PartQuery RelayPart = new(["relay"]);

    private static readonly PartQuery PowerSwitch = new(["mosfet", "nmos", "transistor", "npn", "bjt"]);

    private static readonly End Coil = new(RelayPart, ["A1", "A2"], ["coil"]);

    private static readonly End Drain = new(PowerSwitch, ["d", "drain", "c", "collector"]);

    public static IReadOnlyList<SketchRule> AcrossRelay(SketchBoard board) =>
        Check(
            "Flyback diode",
            "The diode is connected across the relay coil.",
            "The diode is not connected across the coil.",
            new SpansNets(DiodePart, Coil, 2),
            DiodePart,
            "No diode is across the relay coil.").Run(board, null).ToArray();

    public static IReadOnlyList<SketchRule> AcrossMotor(SketchBoard board) =>
        Check(
            "Flyback diode",
            "The diode is across the motor and the switch.",
            "The diode is not connected across the motor.",
            new Every(new SameNet(Drain, new End(DiodePart)), new SameNet(new End(DiodePart), new End(Mark: Mark.MotorFlybackSupply))),
            DiodePart,
            "No diode is across the motor.").Run(board, null).ToArray();
}
