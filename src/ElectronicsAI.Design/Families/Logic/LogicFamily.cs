namespace ElectronicsAI.Design;

public static class LogicFamily
{
    public static ChainTemplate Ripple { get; } = new()
    {
        Kinds = ["ripple-counter", "ripplecounter", "counter"],
        TextWords = ["counter", "ripple", "flip-flop", "flipflop"],
        Clock = new("clk", "CLK", "input", null),
        StageId = "tff{n}",
        StageName = "TFF{n}",
        StageType = "flipflop",
        StageNote = "T=1",
        OutputId = "q{n}",
        OutputName = "Q{n}",
        OutputType = "output",
        Title = "{n}-bit ripple counter",
        Summary = "A ripple counter of {n} T flip-flops. Each T input is tied high, so the stage toggles on every clock edge.",
        KindNote = "Building a {n}-bit ripple counter",
        PartialNote = "Building a {n}-bit ripple counter from the partial reply",
        TextNote = "Reply text describes a {n}-bit ripple counter",
    };

    public static bool Matches(SchematicSketch sketch) =>
        sketch.Parts.Any(part =>
        {
            var text = $"{part.Type} {part.Name}";
            if (text.Contains("adder", StringComparison.OrdinalIgnoreCase) || text.Contains("flip", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return SketchPartKinds.Of(part) != SketchPartKind.Resistor
                && text.Contains("gate", StringComparison.OrdinalIgnoreCase);
        });
}
