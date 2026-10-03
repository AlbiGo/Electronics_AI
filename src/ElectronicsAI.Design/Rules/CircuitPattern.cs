namespace ElectronicsAI.Design;

public sealed record End(PartQuery? Role = null, string[]? Exact = null, string[]? Contains = null, Mark? Mark = null);

public abstract record Clause
{
    public abstract bool Holds(SketchBoard board);
}

public sealed record Every(params Clause[] Of) : Clause
{
    public override bool Holds(SketchBoard board) => Of.All(item => item.Holds(board));
}

public sealed record Some(params Clause[] Of) : Clause
{
    public override bool Holds(SketchBoard board) => Of.Any(item => item.Holds(board));
}

public sealed record Absent(Clause Of) : Clause
{
    public override bool Holds(SketchBoard board) => !Of.Holds(board);
}

public sealed record HasPart(PartQuery Role) : Clause
{
    public override bool Holds(SketchBoard board) => board.Has(Role);
}

public sealed record PinsUsed(PartQuery Role, bool Yes = true) : Clause
{
    public override bool Holds(SketchBoard board)
    {
        var part = board.First(Role);
        var used = part is not null && board.UsesPins(part);
        return used == Yes;
    }
}

public sealed record HasNet(End Of) : Clause
{
    public override bool Holds(SketchBoard board) => board.Nets.Any(net => ClauseMatch.Fits(board, net, Of));
}

public sealed record SameNet(End A, End B, End? Without = null) : Clause
{
    public override bool Holds(SketchBoard board) =>
        board.Nets.Any(net => ClauseMatch.Fits(board, net, A) && ClauseMatch.Fits(board, net, B) && (Without is null || !ClauseMatch.Fits(board, net, Without)));
}

public sealed record ThroughPart(PartQuery Part, End A, End B, bool Distinct = false, string[]? Note = null, End? Avoid = null, End? BWithout = null) : Clause
{
    public override bool Holds(SketchBoard board)
    {
        foreach (var part in board.Parts(Part))
        {
            if (Note is { Length: > 0 })
            {
                var text = $"{part.Type} {part.Name} {part.Note}";
                if (!Note.Any(word => text.Contains(word, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }
            }

            var touched = board.Nets.Where(net => net.Any(end => SketchBoard.Belongs(end, part))).ToList();
            if (Avoid is not null && touched.Any(net => ClauseMatch.Fits(board, net, Avoid)))
            {
                continue;
            }

            var left = touched.Where(net => ClauseMatch.Fits(board, net, A)).ToList();
            var right = touched.Where(net => ClauseMatch.Fits(board, net, B) && (BWithout is null || !ClauseMatch.Fits(board, net, BWithout))).ToList();
            if (Distinct)
            {
                if (left.Any(first => right.Any(second => !ReferenceEquals(first, second))))
                {
                    return true;
                }
            }
            else if (left.Count > 0 && right.Count > 0)
            {
                return true;
            }
        }

        return false;
    }
}

public sealed record Touches(PartQuery Part, End Where) : Clause
{
    public override bool Holds(SketchBoard board) =>
        board.Parts(Part).Any(part => board.Nets.Any(net =>
            ClauseMatch.Fits(board, net, Where) && net.Any(end => SketchBoard.Belongs(end, part))));
}

public sealed record SpansNets(PartQuery Part, End Host, int Count) : Clause
{
    public override bool Holds(SketchBoard board)
    {
        var part = board.First(Part);
        if (part is null)
        {
            return false;
        }

        var count = board.Nets.Count(net => ClauseMatch.Fits(board, net, Host) && net.Any(end => SketchBoard.Belongs(end, part)));
        return count >= Count;
    }
}

public sealed record NetState(End Has, End? Without = null) : Clause
{
    public override bool Holds(SketchBoard board) =>
        board.Nets.Any(net => ClauseMatch.Fits(board, net, Has) && (Without is null || !ClauseMatch.Fits(board, net, Without)));
}

public sealed record OnPinNet(End Pin, End Has, End? Different = null) : Clause
{
    public override bool Holds(SketchBoard board)
    {
        var net = board.Nets.FirstOrDefault(item => ClauseMatch.Fits(board, item, Pin));
        if (net is null)
        {
            return false;
        }

        if (Different is not null && ClauseMatch.Fits(board, net, Different))
        {
            return false;
        }

        return ClauseMatch.Fits(board, net, Has);
    }
}

public static class ClauseMatch
{
    public static bool Fits(SketchBoard board, HashSet<string> net, End end) =>
        net.Any(item => FitsEnd(board, item, end));

    private static bool FitsEnd(SketchBoard board, string item, End end)
    {
        if (end.Role is { } role)
        {
            var owner = board.Owner(item);
            if (owner is null || !role.Matches(owner))
            {
                return false;
            }
        }

        if (!PinMatches(item, end.Exact, end.Contains))
        {
            return false;
        }

        if (end.Mark is { } mark && !board.Marked(item, mark))
        {
            return false;
        }

        return end.Role is not null || end.Exact is not null || end.Contains is not null || end.Mark is not null;
    }

    private static bool PinMatches(string item, string[]? exact, string[]? contains)
    {
        if ((exact is null || exact.Length == 0) && (contains is null || contains.Length == 0))
        {
            return true;
        }

        var pin = SketchBoard.PinOf(item);
        return exact?.Any(name => pin.Equals(name, StringComparison.OrdinalIgnoreCase)) == true
            || contains?.Any(name => pin.Contains(name, StringComparison.OrdinalIgnoreCase)) == true;
    }
}

public abstract class PatternCheck
{
    public abstract IEnumerable<SketchRule> Run(SketchBoard board, string? request);
}

public sealed class ClauseCheck : PatternCheck
{
    public required string Name { get; init; }

    public required string Pass { get; init; }

    public required string Fail { get; init; }

    public required Clause Body { get; init; }

    public PartQuery? Subject { get; init; }

    public string? Absent { get; init; }

    public PartQuery? Only { get; init; }

    public PartQuery? Unless { get; init; }

    public Func<SketchBoard, string>? FailOf { get; init; }

    public override IEnumerable<SketchRule> Run(SketchBoard board, string? request)
    {
        if (Only is not null && !board.Has(Only))
        {
            yield break;
        }

        if (Unless is not null && board.Has(Unless))
        {
            yield break;
        }

        if (Subject is not null && Absent is not null && !board.Has(Subject))
        {
            yield return new SketchRule(Name, Absent, "Fail");
            yield break;
        }

        var ok = Body.Holds(board);
        var detail = ok
            ? Pass.Replace("{name}", Subject is null ? "" : board.First(Subject)?.Name ?? "", StringComparison.Ordinal)
            : FailOf?.Invoke(board) ?? Fail;
        yield return new SketchRule(Name, detail, ok ? "Pass" : "Fail");
    }
}

public sealed class BatchCheck(Func<SketchBoard, string?, IReadOnlyList<SketchRule>> run) : PatternCheck
{
    public override IEnumerable<SketchRule> Run(SketchBoard board, string? request) => run(board, request);
}

public sealed class CircuitPattern : ISketchRuleSet
{
    public required CircuitCategory Category { get; init; }

    public required string Reason { get; init; }

    public Func<SketchBoard, bool> When { get; init; } = static _ => true;

    public PatternCheck[] Checks { get; init; } = [];

    public bool FailOnly { get; init; }

    public bool AttachNetlist { get; init; }

    public Func<SchematicSketch, string?, IReadOnlyList<string>>? Assume { get; init; }

    public bool Applies(SchematicSketch sketch, string? request) =>
        FailOnly
            ? Evaluate(sketch, request).Rules.Any(rule => rule.Result == "Fail")
            : When(SketchBoard.Of(sketch));

    public SketchRuleResult Evaluate(SchematicSketch sketch, string? request)
    {
        var board = SketchBoard.Of(sketch);
        var rules = Checks.SelectMany(check => check.Run(board, request)).ToArray();
        return new SketchRuleResult(Reason, rules, Assume?.Invoke(sketch, request) ?? [], AttachNetlist);
    }
}
