using System.Globalization;
using System.Text.RegularExpressions;
using ElectronicsAI.Domain;

namespace ElectronicsAI.Simulation;

public static partial class NgspiceOutputParser
{
    public static OperatingPoint Parse(string output, string inputNet, string outputNet, string? probeReference)
    {
        var input = ReadVoltage(output, inputNet);
        var vout = ReadVoltage(output, outputNet);
        var current = probeReference is null ? 0 : Math.Abs(ReadCurrent(output, probeReference));
        return new OperatingPoint(input, vout, current);
    }

    public static bool TryParse(string output, string inputNet, string outputNet, string? probeReference, out OperatingPoint? operatingPoint)
    {
        try
        {
            operatingPoint = Parse(output, inputNet, outputNet, probeReference);
            return true;
        }
        catch (FormatException)
        {
            operatingPoint = null;
            return false;
        }
    }

    private static double ReadVoltage(string output, string net)
    {
        var node = net.Equals("GND", StringComparison.OrdinalIgnoreCase) ? "0" : net.ToLowerInvariant();
        foreach (Match match in VoltagePattern().Matches(output))
        {
            if (match.Groups["node"].Value.Equals(node, StringComparison.OrdinalIgnoreCase))
            {
                return ParseNumber(match.Groups["value"].Value);
            }
        }

        foreach (Match match in TableRowPattern().Matches(output))
        {
            if (match.Groups["name"].Value.Equals(node, StringComparison.OrdinalIgnoreCase))
            {
                return ParseNumber(match.Groups["value"].Value);
            }
        }

        throw new FormatException($"ngspice output has no voltage for {node}.");
    }

    private static double ReadCurrent(string output, string reference)
    {
        foreach (Match match in CurrentPattern().Matches(output))
        {
            if (match.Groups["device"].Value.Equals(reference, StringComparison.OrdinalIgnoreCase))
            {
                return ParseNumber(match.Groups["value"].Value);
            }
        }

        var lines = output.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var header = Tokens(lines[i]);
            if (header.Length == 0 || !header[0].Equals("device", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var column = Array.FindIndex(header, token => token.Equals(reference, StringComparison.OrdinalIgnoreCase));
            if (column <= 0)
            {
                continue;
            }

            for (var j = i + 1; j < lines.Length; j++)
            {
                var row = Tokens(lines[j]);
                if (row.Length == 0)
                {
                    continue;
                }

                if (row[0].Equals("device", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                if (row[0].Equals("i", StringComparison.OrdinalIgnoreCase) && column < row.Length)
                {
                    return ParseNumber(row[column]);
                }
            }
        }

        throw new FormatException($"ngspice output has no current for {reference}.");
    }

    private static string[] Tokens(string line) =>
        line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    private static double ParseNumber(string text) =>
        double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);

    [GeneratedRegex(@"v\((?<node>[A-Za-z0-9_]+)\)\s*=\s*(?<value>[+-]?(?:\d+\.?\d*|\.\d+)(?:[eE][+-]?\d+)?)", RegexOptions.IgnoreCase)]
    private static partial Regex VoltagePattern();

    [GeneratedRegex(@"i\((?<device>[A-Za-z0-9_]+)\)\s*=\s*(?<value>[+-]?(?:\d+\.?\d*|\.\d+)(?:[eE][+-]?\d+)?)", RegexOptions.IgnoreCase)]
    private static partial Regex CurrentPattern();

    [GeneratedRegex(@"^\s*(?<name>[A-Za-z_][A-Za-z0-9_.]*)\s+(?<value>[+-]?(?:\d+\.?\d*|\.\d+)(?:[eE][+-]?\d+)?)\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex TableRowPattern();
}
