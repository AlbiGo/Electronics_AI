namespace ElectronicsAI.Design;

public static class DividerFamily
{
    public static CircuitPattern OpenDivider { get; } = new()
    {
        Category = CircuitCategory.Divider,
        Reason = "Missing the bottom resistor.",
        FailOnly = true,
        Checks = [new BatchCheck(static (board, _) => MissingBottom(board) is { } rule ? [rule] : [])],
    };

    public static bool Matches(SchematicSketch sketch)
    {
        var resistors = sketch.Parts.Count(part => SketchPartKinds.Of(part) == SketchPartKind.Resistor);
        return resistors >= 2 && sketch.Parts.All(part => SketchPartKinds.IsAnalog(SketchPartKinds.Of(part)));
    }

    private static SketchRule? MissingBottom(SketchBoard board)
    {
        if (board.Sketch.Parts.Any(part => SketchPartKinds.Of(part) == SketchPartKind.Led))
        {
            return null;
        }

        var resistors = board.Sketch.Parts.Where(part => SketchPartKinds.Of(part) == SketchPartKind.Resistor).ToList();
        if (resistors.Count == 0)
        {
            return null;
        }

        var spansRails = resistors.Any(resistor =>
        {
            var touched = board.Nets.Where(net => net.Any(end => SketchBoard.Belongs(end, resistor))).ToList();
            return touched.Any(net => net.Any(WireEnds.IsSupply)) && touched.Any(net => net.Any(WireEnds.IsGround));
        });
        var midpoint = board.Nets.Any(net =>
            !net.Any(WireEnds.IsSupply)
            && !net.Any(WireEnds.IsGround)
            && resistors.Count(resistor => net.Any(end => SketchBoard.Belongs(end, resistor))) >= 2);
        return spansRails && !midpoint
            ? new SketchRule("Divider", "Missing the bottom resistor.", "Fail")
            : null;
    }
}
