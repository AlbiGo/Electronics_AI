using ElectronicsAI.Domain;

namespace ElectronicsAI.Design;

public interface ICircuitCompiler
{
    Circuit Compile(SchematicSketch sketch);
}
