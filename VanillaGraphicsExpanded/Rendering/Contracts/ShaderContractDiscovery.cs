using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Discovers immutable shader-owned declarations without a manually maintained program list.</summary>
internal static class ShaderContractDiscovery
{
    #region Discovery
    /// <summary>Reads declared static contracts once; the caller caches the resulting validated catalog.</summary>
    public static IReadOnlyList<GpuShaderContract> Discover(IEnumerable<Type> owners)
    {
        var contracts = new List<GpuShaderContract>();
        const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        foreach (var owner in owners.OrderBy(type => type.FullName, StringComparer.Ordinal))
        {
            if (owner.IsDefined(typeof(ExcludeFromShaderCatalogAttribute), inherit: false)) continue;
            // Restrict initialization to members whose exact declared type is the contract.
            // Collection helpers and inherited members are not additional declarations.
            var members = owner.GetMembers(flags).Where(member =>
                member is PropertyInfo property && property.PropertyType == typeof(GpuShaderContract) ||
                member is FieldInfo field && field.FieldType == typeof(GpuShaderContract) &&
                    !field.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false))
                .OrderBy(member => member.Name, StringComparer.Ordinal);
            foreach (var member in members)
            {
                string identity = owner.FullName + "." + member.Name;
                if (owner.ContainsGenericParameters)
                    throw new InvalidOperationException($"Shader contract '{identity}' belongs to an open generic type.");
                object? value;
                try
                {
                    value = member switch
                    {
                        PropertyInfo property when property.GetMethod != null && property.SetMethod == null &&
                            property.GetIndexParameters().Length == 0 => property.GetValue(null),
                        FieldInfo field when field.IsInitOnly => field.GetValue(null),
                        _ => throw new InvalidOperationException("Expected a get-only static property or static readonly field.")
                    };
                }
                catch (Exception error)
                {
                    throw new InvalidOperationException($"Cannot read shader contract '{identity}'.", error);
                }
                if (value is not GpuShaderContract contract)
                    throw new InvalidOperationException($"Shader contract '{identity}' returned null.");
                contracts.Add(contract);
            }
        }
        return contracts.AsReadOnly();
    }
    #endregion
}
