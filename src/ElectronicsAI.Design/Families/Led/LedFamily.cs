namespace ElectronicsAI.Design;

public static class LedFamily
{
    public static CircuitPattern BareLed { get; } = new()
    {
        Category = CircuitCategory.Led,
        Reason = "Missing current limiting resistor.",
        FailOnly = true,
        Checks = [new BatchCheck(static (board, _) => MissingSeries(board) is { } rule ? [rule] : [])],
    };

    public static bool Matches(SchematicSketch sketch) =>
        sketch.Parts.Any(part => SketchPartKinds.Of(part) == SketchPartKind.Led);

    private static SketchRule? MissingSeries(SketchBoard board)
    {
        foreach (var led in board.Sketch.Parts.Where(part => SketchPartKinds.Of(part) == SketchPartKind.Led))
        {
            var anode = board.Nets.FirstOrDefault(net => net.Any(end => IsLedPin(end, led.Id, "anode", "a")));
            var cathode = board.Nets.FirstOrDefault(net => net.Any(end => IsLedPin(end, led.Id, "cathode", "k")));
            if (anode is null || cathode is null)
            {
                continue;
            }

            var supplyOnAnode = anode.Any(WireEnds.IsSupply) && cathode.Any(WireEnds.IsGround);
            var supplyOnCathode = cathode.Any(WireEnds.IsSupply) && anode.Any(WireEnds.IsGround);
            if (supplyOnAnode || supplyOnCathode)
            {
                return new SketchRule("Current limiting resistor", "Missing current limiting resistor.", "Fail");
            }
        }

        return null;
    }

    private static bool IsLedPin(string end, string partId, string name, string shortName)
    {
        if (!end.StartsWith(partId + ".", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var pin = end[(partId.Length + 1)..];
        return pin.Equals(shortName, StringComparison.OrdinalIgnoreCase)
            || pin.Contains(name, StringComparison.OrdinalIgnoreCase);
    }
}
