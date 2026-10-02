namespace ElectronicsAI.Design;

public sealed record SketchRuleResult(
    string Reason,
    IReadOnlyList<SketchRule> Rules,
    IReadOnlyList<string> Assumptions,
    bool AttachNetlist = false);

public interface ISketchRuleSet
{
    CircuitCategory Category { get; }

    bool Applies(SchematicSketch sketch, string? request);

    SketchRuleResult Evaluate(SchematicSketch sketch, string? request);
}

public sealed class SketchRuleCatalog(IEnumerable<ISketchRuleSet> sets)
{
    private readonly ISketchRuleSet[] _sets = sets.ToArray();

    public IReadOnlyList<ISketchRuleSet> Sets => _sets;

    public ISketchRuleSet? Match(SchematicSketch sketch, string? request) =>
        _sets.FirstOrDefault(set => set.Applies(sketch, request));
}

public sealed class SingleRuleSet(CircuitCategory category, Func<SchematicSketch, string?, SketchRule?> find) : ISketchRuleSet
{
    public CircuitCategory Category => category;

    public bool Applies(SchematicSketch sketch, string? request) => find(sketch, request) is not null;

    public SketchRuleResult Evaluate(SchematicSketch sketch, string? request)
    {
        var rule = find(sketch, request)!;
        return new SketchRuleResult(rule.Detail, [rule], []);
    }
}

public sealed class ValidatorRuleSet(ISketchValidator validator) : ISketchRuleSet
{
    public CircuitCategory Category => validator.Category;

    public bool Applies(SchematicSketch sketch, string? request) => validator.Applies(sketch);

    public SketchRuleResult Evaluate(SchematicSketch sketch, string? request) =>
        new(validator.Reason, validator.Evaluate(sketch), []);
}

public sealed class ReportingRuleSet(
    CircuitCategory category,
    string reason,
    Func<SchematicSketch, string?, bool> applies,
    Func<SchematicSketch, string?, IReadOnlyList<SketchRule>> evaluate,
    Func<SchematicSketch, string?, IReadOnlyList<string>> assumptions,
    bool attachNetlist = false) : ISketchRuleSet
{
    public CircuitCategory Category => category;

    public bool Applies(SchematicSketch sketch, string? request) => applies(sketch, request);

    public SketchRuleResult Evaluate(SchematicSketch sketch, string? request) =>
        new(reason, evaluate(sketch, request), assumptions(sketch, request), attachNetlist);
}

public enum SwitchTerm
{
    Transistor,
    Npn,
    Pnp,
    Bjt,
    Mosfet,
    Nmos,
    TwoN2222,
    TwoN3904,
}

public static class SwitchTerms
{
    public static string Text(this SwitchTerm term) => term switch
    {
        SwitchTerm.Transistor => "transistor",
        SwitchTerm.Npn => "npn",
        SwitchTerm.Pnp => "pnp",
        SwitchTerm.Bjt => "bjt",
        SwitchTerm.Mosfet => "mosfet",
        SwitchTerm.Nmos => "nmos",
        SwitchTerm.TwoN2222 => "2n2222",
        SwitchTerm.TwoN3904 => "2n3904",
        _ => throw new ArgumentOutOfRangeException(nameof(term)),
    };
}

public sealed class RequiredPartRuleSet(
    CircuitCategory category,
    SwitchTerm[] requestWords,
    SwitchTerm[] partWords,
    string name,
    string detail) : ISketchRuleSet
{
    public CircuitCategory Category => category;

    public bool Applies(SchematicSketch sketch, string? request) =>
        Mentions(request) && !sketch.Parts.Any(HasPart);

    public SketchRuleResult Evaluate(SchematicSketch sketch, string? request) =>
        new(detail, [new SketchRule(name, detail, "Fail")], []);

    private bool Mentions(string? request) =>
        !string.IsNullOrWhiteSpace(request)
        && requestWords.Any(word => request.Contains(word.Text(), StringComparison.OrdinalIgnoreCase));

    private bool HasPart(SketchPart part)
    {
        var text = $"{part.Type} {part.Name} {part.Note}";
        return partWords.Any(word => text.Contains(word.Text(), StringComparison.OrdinalIgnoreCase));
    }
}
