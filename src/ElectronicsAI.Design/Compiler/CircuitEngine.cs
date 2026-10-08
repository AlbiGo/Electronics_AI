using ElectronicsAI.Domain;

namespace ElectronicsAI.Design;

public sealed class CircuitEngine(CircuitCompiler compiler) : ICircuitCompiler
{
    public Circuit Compile(SchematicSketch sketch)
    {
        var unrecognized = sketch.Parts
            .Where(part => SketchPartKinds.Of(part) is var kind && kind != SketchPartKind.Switch && !SketchPartKinds.IsAnalog(kind))
            .Select(part => part.Name)
            .ToArray();
        if (sketch.Parts.Count == 0
            || unrecognized.Length > 0
            || !sketch.Parts.Any(part => SketchPartKinds.Of(part) == SketchPartKind.Resistor))
        {
            var names = unrecognized.Length == 0 ? "" : " Unrecognized: " + string.Join(", ", unrecognized) + ".";
            throw new CircuitCompileException("This schematic has no ngspice model yet." + names);
        }

        return compiler.Compile(sketch);
    }
}
