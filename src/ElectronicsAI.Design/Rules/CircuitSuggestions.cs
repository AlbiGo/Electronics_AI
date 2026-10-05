namespace ElectronicsAI.Design;

public sealed record CircuitSuggestion(string Text);

public static class CircuitSuggestions
{
    public static CircuitSuggestion? Find(IEnumerable<(string Name, string Detail, string Result)> checks)
    {
        var problems = checks
            .Where(check => check.Result is "Fail" or "Unavailable")
            .Select(check => string.IsNullOrWhiteSpace(check.Detail) ? check.Name : check.Detail.Trim())
            .Where(detail => detail.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (problems.Length == 0)
        {
            return null;
        }

        return new CircuitSuggestion(
            "This circuit is not finished. " + string.Join(" ", problems) + " Draw a corrected circuit?");
    }
}
