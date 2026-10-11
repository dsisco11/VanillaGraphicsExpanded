using System;
using System.Linq;
using VanillaGraphicsExpanded.Rendering.Contracts;
namespace VanillaGraphicsExpanded.Rendering.Shaders;
/// <summary>Captures immutable identity, contract and defaults independently of later configuration edits.</summary>
internal sealed record GpuProgramDeclaration(string Domain, string Name, Type Type, ShaderSettings Settings)
{
    /// <summary>Compares declaration inputs, including the actual contract and every declared option.</summary>
    internal bool Matches(GpuProgramDeclaration other) => Domain == other.Domain && Name == other.Name && Type == other.Type &&
        ReferenceEquals(Settings.Contract, other.Settings.Contract) && Settings.Values.Count == other.Settings.Values.Count &&
        Settings.Values.All(pair => other.Settings.Values.TryGetValue(pair.Key, out var value) && value == pair.Value);
}
