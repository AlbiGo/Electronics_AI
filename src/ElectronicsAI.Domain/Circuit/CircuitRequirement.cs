namespace ElectronicsAI.Domain;

public enum CircuitFamily
{
    Inferred,
    Divider,
    LinearRegulator,
    Counter,
}

public sealed record CircuitRequirement(
    double InputVolts,
    double OutputVolts,
    double LoadCurrentAmps,
    string? Description = null,
    CircuitFamily Family = CircuitFamily.Inferred,
    int Steps = 8,
    double ClockHertz = 1,
    double ForwardVolts = 2);
