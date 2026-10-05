using System.Text.RegularExpressions;

namespace ElectronicsAI.Design;

public sealed record SketchCompletion(SchematicSketch Sketch, string Note);

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
    private static readonly ChainTemplate[] Chains = [LogicFamily.Ripple];

    public static SketchCompletion? MatchKind(string? kind, int? count, string? title, string? summary, bool partial)
    {
        if (count is null || Chains.FirstOrDefault(item => item.KindIs(kind)) is not { } chain)
        {
            return null;
        }

        return chain.Create(count.Value, title, summary, partial ? chain.PartialNote : chain.KindNote);
    }

    public static SketchCompletion? MatchText(string? text)
    {
        var count = CountIn(text);
        if (count is null || Chains.FirstOrDefault(item => item.TextMentions(text)) is not { } chain)
        {
            return null;
        }

        return chain.Create(count.Value, null, null, chain.TextNote);
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
