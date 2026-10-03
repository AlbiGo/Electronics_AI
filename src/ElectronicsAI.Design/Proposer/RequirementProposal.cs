using ElectronicsAI.Domain;

namespace ElectronicsAI.Design;

public abstract record RequirementProposal;

public sealed record SupportedRequirement(CircuitRequirement Requirement) : RequirementProposal;

public sealed record RefusedRequirement(string Reason) : RequirementProposal;

public interface IRequirementProposer
{
    Task<RequirementProposal> ProposeAsync(string description, CancellationToken cancellationToken);
}
