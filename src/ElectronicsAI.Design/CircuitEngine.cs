using ElectronicsAI.Domain;

namespace ElectronicsAI.Design;

public sealed class CircuitEngine(CircuitCompiler compiler) : ICircuitCompiler
{
    public Circuit Compile(SchematicSketch sketch)
    {
        if (sketch.Parts.Count == 0
            || sketch.Parts.Any(part => !SketchPartKinds.IsAnalog(SketchPartKinds.Of(part)))
            || !sketch.Parts.Any(part => SketchPartKinds.Of(part) == SketchPartKind.Resistor))
        {
            throw new CircuitCompileException("This schematic has no ngspice model yet.");
        }

        return compiler.Compile(sketch);
    }
}
