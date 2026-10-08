namespace ElectronicsAI.Design;

static class ModuleNet
{
    public static SketchRule Shared(
        SketchBoard board,
        Mark pin,
        string name,
        string passDetail)
    {
        var modules = board.Sketch.Parts.Where(SketchBoard.IsModule).ToList();
        var controllers = modules.Where(SketchBoard.IsController).ToList();
        var peripherals = modules.Where(part => !SketchBoard.IsController(part)).ToList();
        if (controllers.Count == 0 || peripherals.Count == 0)
        {
            return new SketchRule(name, "The sketch needs a controller and a module.", "Fail");
        }

        var missing = peripherals.Where(peripheral => !board.Nets.Any(net =>
            net.Any(end => controllers.Any(part => SketchBoard.Belongs(end, part)) && board.Marked(end, pin))
            && net.Any(end => SketchBoard.Belongs(end, peripheral) && board.Marked(end, pin)))).ToList();
        return missing.Count == 0
            ? new SketchRule(name, passDetail, "Pass")
            : new SketchRule(name, $"Missing on {string.Join(", ", missing.Select(part => part.Name))}.", "Fail");
    }

    public static SketchRule PullUp(SketchBoard board, Mark bus, string name)
    {
        var busNet = board.Nets.FirstOrDefault(net => net.Any(end => board.Marked(end, bus)));
        var powerNet = board.Nets.FirstOrDefault(net => net.Any(end =>
            board.Marked(end, Mark.ModulePower) && board.Sketch.Parts.Any(part => SketchBoard.IsModule(part) && SketchBoard.Belongs(end, part))));
        if (busNet is null || powerNet is null)
        {
            return new SketchRule(name, "The bus pin is not connected.", "Fail");
        }

        var pulled = board.Sketch.Parts.Where(part => SketchPartKinds.Of(part) == SketchPartKind.Resistor).Any(resistor =>
            busNet.Any(end => SketchBoard.Belongs(end, resistor)) && powerNet.Any(end => SketchBoard.Belongs(end, resistor)));
        return pulled
            ? new SketchRule(name, "A resistor ties the bus pin to the supply.", "Pass")
            : new SketchRule(name, "No pull-up resistor ties this pin to the supply.", "Fail");
    }

    public static SketchRule Linked(SketchBoard board, string name, string passDetail, string[] pins)
    {
        var modules = board.Sketch.Parts.Where(SketchBoard.IsModule).ToList();
        var controllers = modules.Where(SketchBoard.IsController).ToList();
        var peripherals = modules.Where(part => !SketchBoard.IsController(part)).ToList();
        if (controllers.Count == 0 || peripherals.Count == 0)
        {
            return new SketchRule(name, "The sketch needs a controller and a sensor.", "Fail");
        }

        var missing = peripherals.Where(peripheral => !board.Nets.Any(net =>
            net.Any(end => controllers.Any(part => SketchBoard.Belongs(end, part)))
            && net.Any(end => SketchBoard.Belongs(end, peripheral) && pins.Any(pin => SketchBoard.PinOf(end).Equals(pin, StringComparison.OrdinalIgnoreCase))))).ToList();
        return missing.Count == 0
            ? new SketchRule(name, passDetail, "Pass")
            : new SketchRule(name, $"Missing on {string.Join(", ", missing.Select(part => part.Name))}.", "Fail");
    }
}
