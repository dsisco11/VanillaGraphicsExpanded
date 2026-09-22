using System;
using System.Collections.Generic;
using System.Linq;

namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>An explicitly named reusable group of accepted program settings.</summary>
internal sealed class ShaderOptionGroup
{
    public string Identity { get; }
    public IReadOnlyList<ShaderOption> Options { get; }

    #region Declaration
    /// <summary>Copies the group's declarations, leaving membership explicit at each program.</summary>
    public ShaderOptionGroup(string identity, params ShaderOption[] options)
    {
        ShaderContractNames.ValidatePath(identity);
        Identity = identity; Options = Array.AsReadOnly(options.ToArray());
        if (options.Select(o => o.Name).Distinct(StringComparer.Ordinal).Count() != options.Length)
            throw new ArgumentException($"Option group '{identity}' repeats a canonical option.");
    }
    #endregion
}
