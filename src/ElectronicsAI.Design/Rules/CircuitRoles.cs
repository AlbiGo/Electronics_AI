using System.Text.RegularExpressions;

namespace ElectronicsAI.Design;

[Flags]
public enum CircuitRole
{
    None = 0,
    Power = 1,
    Sensor = 2,
    Input = 4,
    Controller = 8,
    Driver = 16,
    Load = 32,
    Communication = 64,
    Protection = 128,
}

public static class CircuitRoles
{
    public static CircuitRole Present(SchematicSketch sketch)
    {
        var role = CircuitRole.None;
        foreach (var part in sketch.Parts)
        {
            if (IsPower(part))
            {
                role |= CircuitRole.Power;
            }

            if (IsSensor(part))
            {
                role |= CircuitRole.Sensor;
            }

            if (IsInput(part))
            {
                role |= CircuitRole.Input;
            }

            if (SketchBoard.IsController(part))
            {
                role |= CircuitRole.Controller;
            }

            if (IsDriver(part))
            {
                role |= CircuitRole.Driver;
            }

            if (IsLoad(part))
            {
                role |= CircuitRole.Load;
            }

            if (IsInductive(part) || IsDiode(part))
            {
                role |= CircuitRole.Protection;
            }

            if (SensorLibrary.Find(part)?.Interface is SensorInterface.I2C or SensorInterface.Spi or SensorInterface.Uart)
            {
                role |= CircuitRole.Communication | CircuitRole.Sensor;
            }
        }

        var wires = string.Join(" ", sketch.Wires.Select(wire => $"{wire.From} {wire.To}"));
        if (HasPin(wires, "SDA") && HasPin(wires, "SCL")
            || HasPin(wires, "MOSI") && HasPin(wires, "MISO")
            || HasPin(wires, "RX") && HasPin(wires, "TX"))
        {
            role |= CircuitRole.Communication;
        }

        return role;
    }

    public static bool IsSensor(SketchPart part)
    {
        if (SketchBoard.IsController(part))
        {
            return false;
        }

        return Has(part, "sensor", "temperature", "thermistor", "ntc", "lm35", "tmp36")
            || SensorLibrary.Find(part) is not null;
    }

    public static bool IsDriver(SketchPart part) =>
        Has(part, "mosfet", "nmos", "transistor", "npn", "bjt", "h-bridge", "hbridge", "tb6612", "drv8833", "l298", "l293", "bts7960");

    public static bool IsLoad(SketchPart part) =>
        SketchPartKinds.Of(part) == SketchPartKind.Led
        || Has(part, "fan", "motor", "pump", "solenoid", "relay");

    public static bool IsInductive(SketchPart part) =>
        Has(part, "fan", "motor", "pump", "solenoid", "relay");

    private static bool IsPower(SketchPart part) =>
        SketchPartKinds.Of(part) == SketchPartKind.Supply
        || Has(part, "supply", "battery", "vsource", "voltage");

    private static bool IsInput(SketchPart part) =>
        SketchPartKinds.Of(part) == SketchPartKind.Switch
        || Regex.IsMatch($"{part.Type} {part.Name} {part.Note}", @"(^|[^a-z])(switch|button)([^a-z]|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static bool IsDiode(SketchPart part) =>
        Has(part, "diode", "flyback") && !Has(part, "led");

    private static bool HasPin(string text, string pin) =>
        text.Contains(pin, StringComparison.OrdinalIgnoreCase);

    private static bool Has(SketchPart part, params string[] words)
    {
        var text = $"{part.Type} {part.Name} {part.Note}";
        return words.Any(word => text.Contains(word, StringComparison.OrdinalIgnoreCase));
    }
}
