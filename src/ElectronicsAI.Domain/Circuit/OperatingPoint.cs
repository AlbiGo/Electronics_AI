namespace ElectronicsAI.Domain;

public sealed record OperatingPoint(
    double InputVolts,
    double OutputVolts,
    double LoadCurrentAmps);
