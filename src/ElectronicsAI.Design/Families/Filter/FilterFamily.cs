namespace ElectronicsAI.Design;

public static class FilterFamily
{
    public static CircuitPattern Pattern { get; } = new()
    {
        Category = CircuitCategory.Filter,
        Reason = "Cutoff checked from the resistor and capacitor.",
        When = static board => RcLowPassRules.Applies(board.Sketch),
        AttachNetlist = true,
        Assume = RcLowPassRules.Assumptions,
        Checks = [new BatchCheck(static (board, request) => RcLowPassRules.Evaluate(board.Sketch, request))],
    };
}
