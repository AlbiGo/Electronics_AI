namespace ElectronicsAI.Design;

static class WireEnds
{
    public static bool IsSupply(string end) =>
        end.Equals("VCC", StringComparison.OrdinalIgnoreCase)
        || end.Equals("VDD", StringComparison.OrdinalIgnoreCase)
        || end.Equals("5V", StringComparison.OrdinalIgnoreCase)
        || end.Equals("+5V", StringComparison.OrdinalIgnoreCase);

    public static bool IsGround(string end) =>
        end.Equals("GND", StringComparison.OrdinalIgnoreCase)
        || end.Equals("0", StringComparison.OrdinalIgnoreCase)
        || end.Equals("VSS", StringComparison.OrdinalIgnoreCase);
}
