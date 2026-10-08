namespace ElectronicsAI.Design;

public static class PowerSourceRule
{
    public static SketchRule Module(SketchBoard board) =>
        ModuleNet.Shared(board, Mark.ModulePower, "Power connected", "3V3 or VIN is shared.");
}
