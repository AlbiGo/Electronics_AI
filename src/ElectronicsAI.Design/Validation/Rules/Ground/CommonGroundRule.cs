using static ElectronicsAI.Design.PatternChecks;

namespace ElectronicsAI.Design;

public static class CommonGroundRule
{
    private static readonly PartQuery Controller = new(["mcu", "esp32", "ecu"], PartText.TypeName);

    private static readonly PartQuery BridgePart = new(["tb6612", "h-bridge", "hbridge", "drv8833", "l298", "l293", "bts7960"], PartText.TypeName);

    private static readonly PartQuery PowerSwitch = new(["mosfet", "nmos", "transistor", "npn", "bjt"]);

    private static readonly End Source = new(PowerSwitch, ["s", "source", "e", "emitter"]);

    public static SketchRule Module(SketchBoard board) =>
        ModuleNet.Shared(board, Mark.ModuleGround, "Ground connected", "GND is shared.");

    public static IReadOnlyList<SketchRule> Analog(SketchBoard board) =>
        Check(
            "Common ground present",
            "The controller and the sensor share a ground.",
            "The controller and the sensor do not share a ground.",
            new SameNet(SensorWiring.ControllerGround, SensorWiring.SensorGround)).Run(board, null).ToArray();

    public static IReadOnlyList<SketchRule> Relay(SketchBoard board) =>
        Check(
            "Common ground",
            "The controller ground joins the coil ground.",
            "The controller ground is not tied to the coil supply ground.",
            new SameNet(new End(Controller, Mark: Mark.Ground), new End(Mark: Mark.Ground)),
            Controller,
            "The sketch has no controller.").Run(board, null).ToArray();

    public static IReadOnlyList<SketchRule> Motor(SketchBoard board) =>
        Check(
            "Common ground",
            "The controller ground joins the motor ground.",
            "The controller and the motor do not share a ground.",
            new SameNet(new End(Controller, Mark: Mark.Ground), Source)).Run(board, null).ToArray();

    public static IReadOnlyList<SketchRule> Bridge(SketchBoard board) =>
        Check(
            "Common ground",
            "The controller ground joins the driver ground.",
            "The controller and the driver do not share a ground.",
            new SameNet(new End(Controller, Mark: Mark.Ground), new End(BridgePart, ["GND"]))).Run(board, null).ToArray();
}
