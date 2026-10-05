namespace ElectronicsAI.Design;

public enum PartText
{
    Type,
    TypeName,
    TypeNameNote,
}

public enum Mark
{
    Ground,
    WireSupply,
    WireGround,
    MotorDiscreteSupply,
    MotorFlybackSupply,
    RelaySupply,
    TransistorSupply,
    LogicHigh,
    Gpio,
    PwmOrGpio,
    AnyPwm,
    Drive,
    ModulePower,
    ModuleGround,
    LowVoltage,
    HighVoltage,
    Sda,
    Scl,
    Data,
}

public sealed record PartQuery(string[] Words, PartText Text = PartText.Type, string[]? Exclude = null, SketchPartKind? Kind = null)
{
    public bool Matches(SketchPart part)
    {
        var full = $"{part.Type} {part.Name} {part.Note}";
        if (Exclude?.Any(word => full.Contains(word, StringComparison.OrdinalIgnoreCase)) == true)
        {
            return false;
        }

        if (Kind is { } kind && SketchPartKinds.Of(part) == kind)
        {
            return true;
        }

        if (Words.Length == 0)
        {
            return false;
        }

        var text = Text switch
        {
            PartText.Type => part.Type ?? "",
            PartText.TypeName => $"{part.Type} {part.Name}",
            _ => full,
        };
        return Words.Any(word => text.Contains(word, StringComparison.OrdinalIgnoreCase));
    }
}

public sealed class SketchBoard
{
    private SketchBoard(SchematicSketch sketch, List<HashSet<string>> nets)
    {
        Sketch = sketch;
        Nets = nets;
    }

    public SchematicSketch Sketch { get; }

    public IReadOnlyList<HashSet<string>> Nets { get; }

    public static SketchBoard Of(SchematicSketch sketch) => new(sketch, Build(sketch));

    public bool Has(PartQuery query) => Sketch.Parts.Any(query.Matches);

    public SketchPart? First(PartQuery query) => Sketch.Parts.FirstOrDefault(query.Matches);

    public IReadOnlyList<SketchPart> Parts(PartQuery query) => Sketch.Parts.Where(query.Matches).ToList();

    public SketchPart? Owner(string end) => Sketch.Parts.FirstOrDefault(part => Belongs(end, part));

    public bool UsesPins(SketchPart part) =>
        Sketch.Wires.Any(wire =>
            (Belongs(wire.From, part) && wire.From.Contains('.'))
            || (Belongs(wire.To, part) && wire.To.Contains('.')));

    public bool Marked(string end, Mark mark) => mark switch
    {
        Mark.Ground => IsNamed(end, "GND", "VSS", "0"),
        Mark.WireSupply => IsToken(end, "VCC", "VDD", "5V", "+5V"),
        Mark.WireGround => IsToken(end, "GND", "0", "VSS"),
        Mark.MotorDiscreteSupply => IsToken(end, "VCC", "VMOT", "+5V", "5V", "12V", "+12V") || TypeHas(Owner(end), "supply", "vsource"),
        Mark.MotorFlybackSupply => IsToken(end, "VCC", "VMOT", "+5V", "5V", "12V", "+12V") || TypeHas(Owner(end), "supply", "vsource", "battery"),
        Mark.RelaySupply => IsToken(end, "VCC", "VDD", "+5V", "5V") || TypeHas(Owner(end), "supply", "vsource", "source"),
        Mark.TransistorSupply => IsTransistorSupply(end),
        Mark.LogicHigh => IsToken(end, "VCC", "VMOT", "+5V", "5V", "12V", "3V3") || IsNamed(end, "3V3") || TypeHas(Owner(end), "supply", "vsource", "battery"),
        Mark.Gpio => IsGpio(end, pwm: false),
        Mark.PwmOrGpio => IsGpio(end, pwm: true),
        Mark.AnyPwm => IsGpio(end, pwm: true) || PinOf(end).Contains("PWM", StringComparison.OrdinalIgnoreCase),
        Mark.Drive => IsDrive(end),
        Mark.ModulePower => IsModulePower(PinOf(end)),
        Mark.ModuleGround => IsModuleGround(PinOf(end)),
        Mark.LowVoltage => IsLowVoltage(PinOf(end)),
        Mark.HighVoltage => IsHighVoltage(PinOf(end)),
        Mark.Sda => IsSda(PinOf(end)),
        Mark.Scl => IsScl(PinOf(end)),
        Mark.Data => IsData(PinOf(end)),
        _ => false,
    };

    public static bool Belongs(string end, SketchPart part) =>
        end.Equals(part.Id, StringComparison.OrdinalIgnoreCase)
        || end.StartsWith(part.Id + ".", StringComparison.OrdinalIgnoreCase);

    public static string PinOf(string end)
    {
        var dot = end.IndexOf('.');
        return dot < 0 ? end : end[(dot + 1)..];
    }

    public static bool IsController(SketchPart part)
    {
        var text = $"{part.Type} {part.Name}";
        return text.Contains("mcu", StringComparison.OrdinalIgnoreCase)
            || text.Contains("esp32", StringComparison.OrdinalIgnoreCase)
            || text.Contains("arduino", StringComparison.OrdinalIgnoreCase)
            || text.Contains("stm32", StringComparison.OrdinalIgnoreCase)
            || text.Contains("rp2040", StringComparison.OrdinalIgnoreCase)
            || text.Contains("ecu", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsModule(SketchPart part)
    {
        var text = $"{part.Type} {part.Name}";
        return text.Contains("mcu", StringComparison.OrdinalIgnoreCase)
            || text.Contains("sensor", StringComparison.OrdinalIgnoreCase)
            || text.Contains("relay", StringComparison.OrdinalIgnoreCase)
            || text.Contains("esp32", StringComparison.OrdinalIgnoreCase)
            || text.Contains("max30102", StringComparison.OrdinalIgnoreCase)
            || text.Contains("dht", StringComparison.OrdinalIgnoreCase)
            || text.Contains("oled", StringComparison.OrdinalIgnoreCase);
    }

    public bool UsesI2C()
    {
        var text = string.Join(" ", Sketch.Parts.Select(part => $"{part.Name} {part.Type} {string.Join(" ", part.Pins)}"));
        text += " " + string.Join(" ", Sketch.Wires.Select(wire => $"{wire.From} {wire.To}"));
        return text.Contains("sda", StringComparison.OrdinalIgnoreCase)
            || text.Contains("scl", StringComparison.OrdinalIgnoreCase)
            || text.Contains("gpio21", StringComparison.OrdinalIgnoreCase)
            || text.Contains("gpio22", StringComparison.OrdinalIgnoreCase)
            || text.Contains("max30102", StringComparison.OrdinalIgnoreCase)
            || text.Contains("oled", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsTransistorSupply(string end)
    {
        if (IsNamed(end, "VCC", "VDD", "VIN", "+5V", "5V"))
        {
            return true;
        }

        var part = Owner(end);
        if (part is null)
        {
            return false;
        }

        var text = $"{part.Type} {part.Name} {part.Note}";
        return SketchPartKinds.Of(part) == SketchPartKind.Supply
            || part.Name.Equals("VCC", StringComparison.OrdinalIgnoreCase)
            || text.Contains("supply", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsDrive(string end)
    {
        if (IsTransistorSupply(end) || PinOf(end).StartsWith("GPIO", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var part = Owner(end);
        if (part is null)
        {
            return false;
        }

        var text = $"{part.Type} {part.Name} {part.Note}";
        return SketchPartKinds.Of(part) == SketchPartKind.Node
            || part.Name.Equals("IN", StringComparison.OrdinalIgnoreCase)
            || part.Name.Equals("VIN", StringComparison.OrdinalIgnoreCase)
            || text.Contains("control", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsGpio(string end, bool pwm)
    {
        var pin = PinOf(end);
        var named = pin.StartsWith("GPIO", StringComparison.OrdinalIgnoreCase)
            || (pwm && pin.Contains("PWM", StringComparison.OrdinalIgnoreCase));
        if (!named)
        {
            return false;
        }

        var part = Owner(end);
        return part is not null && TypeHas(part, "mcu", "esp32", "ecu");
    }

    private static bool IsModulePower(string pin) =>
        IsLowVoltage(pin) || IsHighVoltage(pin)
        || pin.Equals("VIN", StringComparison.OrdinalIgnoreCase)
        || pin.Equals("VCC", StringComparison.OrdinalIgnoreCase)
        || pin.Equals("VDD", StringComparison.OrdinalIgnoreCase);

    private static bool IsModuleGround(string pin) =>
        pin.Equals("GND", StringComparison.OrdinalIgnoreCase)
        || pin.Equals("VSS", StringComparison.OrdinalIgnoreCase);

    private static bool IsLowVoltage(string pin) =>
        pin.Equals("3V3", StringComparison.OrdinalIgnoreCase)
        || pin.Equals("3.3V", StringComparison.OrdinalIgnoreCase);

    private static bool IsHighVoltage(string pin) =>
        pin.Equals("5V", StringComparison.OrdinalIgnoreCase)
        || pin.Equals("VBUS", StringComparison.OrdinalIgnoreCase);

    private static bool IsSda(string pin) =>
        pin.Equals("SDA", StringComparison.OrdinalIgnoreCase)
        || pin.Equals("GPIO21", StringComparison.OrdinalIgnoreCase);

    private static bool IsScl(string pin) =>
        pin.Equals("SCL", StringComparison.OrdinalIgnoreCase)
        || pin.Equals("GPIO22", StringComparison.OrdinalIgnoreCase);

    private static bool IsData(string pin) =>
        pin.Equals("DATA", StringComparison.OrdinalIgnoreCase)
        || pin.Equals("DAT", StringComparison.OrdinalIgnoreCase)
        || pin.Equals("DOUT", StringComparison.OrdinalIgnoreCase)
        || pin.StartsWith("GPIO", StringComparison.OrdinalIgnoreCase);

    private static bool IsNamed(string end, params string[] names)
    {
        var pin = PinOf(end);
        return names.Any(name => pin.Equals(name, StringComparison.OrdinalIgnoreCase) || end.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsToken(string end, params string[] names) =>
        names.Any(name => end.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static bool TypeHas(SketchPart? part, params string[] words)
    {
        var type = part?.Type ?? "";
        return words.Any(word => type.Contains(word, StringComparison.OrdinalIgnoreCase));
    }

    private static List<HashSet<string>> Build(SchematicSketch sketch)
    {
        var parent = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string Find(string key)
        {
            if (!parent.ContainsKey(key))
            {
                parent[key] = key;
            }

            var root = key;
            while (!parent[root].Equals(root, StringComparison.OrdinalIgnoreCase))
            {
                root = parent[root];
            }

            return parent[key] = root;
        }

        foreach (var wire in sketch.Wires)
        {
            var left = Find(wire.From.Trim());
            var right = Find(wire.To.Trim());
            if (!left.Equals(right, StringComparison.OrdinalIgnoreCase))
            {
                parent[left] = right;
            }
        }

        return parent.Keys
            .GroupBy(key => Find(key), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.ToHashSet(StringComparer.OrdinalIgnoreCase))
            .ToList();
    }
}
