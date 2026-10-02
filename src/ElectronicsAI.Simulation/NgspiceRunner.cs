using System.Diagnostics;
using ElectronicsAI.Domain;

namespace ElectronicsAI.Simulation;

public sealed record SimulationResult(bool Available, string? Reason, OperatingPoint? OperatingPoint)
{
    public static SimulationResult Unavailable(string reason) => new(false, reason, null);

    public static SimulationResult Ok(OperatingPoint operatingPoint) => new(true, null, operatingPoint);
}

public sealed class NgspiceRunner
{
    public static bool IsAvailable() => FindExecutable() is not null;

    public SimulationResult Run(string netlist, Circuit circuit)
    {
        var executable = FindExecutable();
        if (executable is null)
        {
            return SimulationResult.Unavailable("ngspice is not installed or not on PATH.");
        }

        var directory = Path.Combine(Path.GetTempPath(), "electronicsai", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "circuit.cir");

        try
        {
            File.WriteAllText(file, netlist);
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = directory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            var executableDirectory = Path.GetDirectoryName(executable);
            if (!string.IsNullOrEmpty(executableDirectory))
            {
                var path = Environment.GetEnvironmentVariable("PATH");
                process.StartInfo.Environment["PATH"] = executableDirectory + Path.PathSeparator + path;
            }

            process.StartInfo.ArgumentList.Add("-b");
            process.StartInfo.ArgumentList.Add(file);

            if (!process.Start())
            {
                return SimulationResult.Unavailable("ngspice could not be started.");
            }

            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            if (!process.WaitForExit(20_000))
            {
                process.Kill(entireProcessTree: true);
                return SimulationResult.Unavailable("ngspice timed out.");
            }

            var probe = circuit.ProbeComponentId is null
                ? null
                : circuit.Components.Single(component => component.Id == circuit.ProbeComponentId).Reference;

            if (NgspiceOutputParser.TryParse(stdout, circuit.InputNet, circuit.OutputNet, probe, out var operatingPoint)
                && operatingPoint is not null)
            {
                return SimulationResult.Ok(operatingPoint);
            }

            var detail = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            var trimmed = detail.Trim();
            if (trimmed.Length > 500)
            {
                trimmed = trimmed[..500];
            }

            var reason = trimmed.Length == 0
                ? "ngspice did not return an operating point."
                : "ngspice did not return an operating point. " + trimmed;
            return SimulationResult.Unavailable(reason);
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return SimulationResult.Unavailable("ngspice is not installed or not on PATH.");
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    public static string? FindExecutable()
    {
        var names = OperatingSystem.IsWindows()
            ? new[] { "ngspice_con.exe", "ngspice.exe" }
            : new[] { "ngspice" };

        foreach (var directory in SearchDirectories())
        {
            foreach (var name in names)
            {
                var candidate = Path.Combine(directory, name);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static IEnumerable<string> SearchDirectories()
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(path))
        {
            foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                yield return directory.Trim();
            }
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        yield return Path.Combine(home, "Downloads", "Spice64", "bin");

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        yield return Path.Combine(programFiles, "Spice64", "bin");
    }
}
