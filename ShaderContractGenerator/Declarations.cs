using Microsoft.CodeAnalysis;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace ShaderContractGenerator;

/// <summary>One semantically resolved option and the expression that publishes its typed key.</summary>
internal sealed record OptionDeclaration(IPropertySymbol Property, string TypeName, string KeyName,
    ShaderOption Model, string Initializer, string KeyExpression, bool Shared);

/// <summary>A generated owner with validated options, groups and program declarations.</summary>
internal sealed class OwnerDeclaration(INamedTypeSymbol symbol)
{
    public INamedTypeSymbol Symbol { get; } = symbol;
    public string Name => Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    public Dictionary<string, OptionDeclaration> Options { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, (ShaderOptionGroup Model, string Expression)> Groups { get; } = new(StringComparer.Ordinal);
    public List<ProgramDeclaration> Programs { get; } = [];
}

/// <summary>A validated immutable program and its generated construction expression.</summary>
internal sealed record ProgramDeclaration(string Member, string Scope, GpuShaderContract Model, string Expression);
