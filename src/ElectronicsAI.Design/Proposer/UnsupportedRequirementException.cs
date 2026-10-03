namespace ElectronicsAI.Design;

public sealed class UnsupportedRequirementException : Exception
{
    public UnsupportedRequirementException(string? message = null)
        : base(message ?? "This version analyzes a 12 V to 3.3 V divider with negligible load, a 12 V to 5 V supply at about 200 mA, or a counter from 1 to 8.")
    {
    }
}
