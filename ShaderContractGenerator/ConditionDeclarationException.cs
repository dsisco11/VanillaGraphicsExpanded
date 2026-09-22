using Microsoft.CodeAnalysis;

namespace ShaderContractGenerator;

/// <summary>Carries the offending attribute location through declaration validation.</summary>
internal sealed class ConditionDeclarationException(string message, Location location, Exception? inner = null) : ArgumentException(message, inner)
{
    public Location Location { get; } = location;
}
