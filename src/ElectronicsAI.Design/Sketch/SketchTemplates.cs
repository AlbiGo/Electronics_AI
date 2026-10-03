using System.Text.RegularExpressions;

namespace ElectronicsAI.Design;

public sealed record SketchCompletion(SchematicSketch Sketch, string Note);

public sealed class SketchTemplate
{
    public string[][] RequestGroups { get; init; } = [];

    public string[] Kinds { get; init; } = [];

    public string[] SkipWhenPartContains { get; init; } = [];

    public bool ReplaceRequest { get; init; }

    public bool FillWhenUnwired { get; init; }

    public required string Title { get; init; }

    public required string Summary { get; init; }

    public required string Note { get; init; }

    public required IReadOnlyList<SketchPart> Parts { get; init; }

    public required IReadOnlyList<SketchWire> Wires { get; init; }

    public bool Requested(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || RequestGroups.Length == 0)
        {
            return false;
        }

        return RequestGroups.All(group => group.Any(word => text.Contains(word, StringComparison.OrdinalIgnoreCase)));
    }

    public bool KindIs(string? kind) =>
        !string.IsNullOrWhiteSpace(kind)
        && Kinds.Any(item => item.Equals(kind, StringComparison.OrdinalIgnoreCase));

    public bool AlreadyPresent(SketchPart part)
    {
        var text = $"{part.Type} {part.Name} {part.Note}";
        return SkipWhenPartContains.Any(word => text.Contains(word, StringComparison.OrdinalIgnoreCase));
    }

    public SketchCompletion Create(string? title, string? summary) =>
        new(Board(title, summary), Note);

    private SchematicSketch Board(string? title, string? summary) =>
        new(
            string.IsNullOrWhiteSpace(title) ? Title : title,
            string.IsNullOrWhiteSpace(summary) ? Summary : summary,
            Parts,
            Wires,
            null);
}

public sealed class ChainTemplate
{
    public required string[] Kinds { get; init; }

    public required string[] TextWords { get; init; }

    public int Min { get; init; } = 1;

    public int Max { get; init; } = 16;

    public required SketchPart Clock { get; init; }

    public required string StageId { get; init; }

    public required string StageName { get; init; }

    public required string StageType { get; init; }

    public string? StageNote { get; init; }

    public required string OutputId { get; init; }

    public required string OutputName { get; init; }

    public required string OutputType { get; init; }

    public required string Title { get; init; }

    public required string Summary { get; init; }

    public required string KindNote { get; init; }

    public required string PartialNote { get; init; }

    public required string TextNote { get; init; }

    public bool KindIs(string? kind) =>
        !string.IsNullOrWhiteSpace(kind)
        && Kinds.Any(item => item.Equals(kind, StringComparison.OrdinalIgnoreCase));

    public bool TextMentions(string? text) =>
        !string.IsNullOrWhiteSpace(text)
        && TextWords.Any(word => text.Contains(word, StringComparison.OrdinalIgnoreCase));

    public SketchCompletion? Create(int count, string? title, string? summary, string note)
    {
        if (count < Min || count > Max)
        {
            return null;
        }

        var parts = new List<SketchPart> { Clock };
        var wires = new List<SketchWire>();
        for (var index = 0; index < count; index++)
        {
            var stage = Token(StageId, index);
            var output = Token(OutputId, index);
            parts.Add(new SketchPart(stage, Token(StageName, index), StageType, StageNote));
            parts.Add(new SketchPart(output, Token(OutputName, index), OutputType, null));
            wires.Add(new SketchWire(index == 0 ? Clock.Id : Token(StageId, index - 1), stage));
            wires.Add(new SketchWire(stage, output));
        }

        var marker = count.ToString();
        return new SketchCompletion(
            new SchematicSketch(
                string.IsNullOrWhiteSpace(title) ? Title.Replace("{n}", marker, StringComparison.Ordinal) : title,
                string.IsNullOrWhiteSpace(summary) ? Summary.Replace("{n}", marker, StringComparison.Ordinal) : summary,
                parts,
                wires,
                null),
            note.Replace("{n}", marker, StringComparison.Ordinal));
    }

    private static string Token(string format, int index) =>
        format.Replace("{n}", index.ToString(), StringComparison.Ordinal);
}

public static partial class SketchTemplates
{
    private static readonly SketchTemplate[] Boards = [TransistorLedFamily.Board, Esp32SensorFamily.HeartBoard];

    private static readonly ChainTemplate[] Chains = [LogicFamily.Ripple];

    public static SketchCompletion? MatchRequest(string? request) =>
        Boards.FirstOrDefault(board => board.ReplaceRequest && board.Requested(request)) is { } board
            ? board.Create(null, null)
            : null;

    public static SketchCompletion? MatchKind(string? kind, int? count, string? title, string? summary, bool partial)
    {
        if (Boards.FirstOrDefault(board => board.KindIs(kind)) is { } board)
        {
            return board.Create(title, summary);
        }

        if (count is null || Chains.FirstOrDefault(item => item.KindIs(kind)) is not { } chain)
        {
            return null;
        }

        return chain.Create(count.Value, title, summary, partial ? chain.PartialNote : chain.KindNote);
    }

    public static SketchCompletion? MatchNames(string? names, string? title, string? summary) =>
        Boards.FirstOrDefault(board => board.FillWhenUnwired && board.Requested(names)) is { } board
            ? board.Create(title, summary)
            : null;

    public static SketchCompletion? MatchText(string? text)
    {
        if (Boards.FirstOrDefault(board => board.ReplaceRequest && board.Requested(text)) is { } board)
        {
            return board.Create(null, null);
        }

        var count = CountIn(text);
        if (count is null || Chains.FirstOrDefault(item => item.TextMentions(text)) is not { } chain)
        {
            return null;
        }

        return chain.Create(count.Value, null, null, chain.TextNote);
    }

    public static SketchCompletion? TryComplete(
        IReadOnlyList<SketchPart> parts,
        string? request,
        string? title,
        string? summary)
    {
        var template = Boards.FirstOrDefault(item => item.Requested(request) && !item.ReplaceRequest && !parts.Any(item.AlreadyPresent));
        return template?.Create(title, summary);
    }

    private static int? CountIn(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var match = BitCount().Match(text);
        return match.Success && int.TryParse(match.Groups[1].Value, out var count) ? count : null;
    }

    [GeneratedRegex(@"(\d+)\s*-?\s*bit", RegexOptions.IgnoreCase)]
    private static partial Regex BitCount();
}
