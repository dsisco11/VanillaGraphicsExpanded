using System;
using System.IO;
using System.Linq;
using System.Text;

namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Serializes resolved contract inputs deterministically for offline reuse without changing runtime selection.</summary>
internal static class ShaderContractProjection
{
    #region Public API
    /// <summary>Captures compiler-source and interface inputs after the resolver has evaluated conditions.</summary>
    internal static byte[] Effective(ShaderStageSelection selection)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        var stage = selection.Stage;
        writer.Write("effective-contract-v1");
        writer.Write(stage.Source); writer.Write((int)stage.Kind); writer.Write(stage.EntryPoint);
        // Length-prefixed strings, typed bits and explicit counts avoid delimiter and culture ambiguities.
        writer.Write(stage.FixedDefines.Count);
        foreach (var pair in stage.FixedDefines.OrderBy(p => p.Key, StringComparer.Ordinal))
        { writer.Write(pair.Key); WriteScalar(writer, pair.Value); }
        writer.Write(selection.Structural.Count);
        foreach (var pair in selection.Structural.OrderBy(p => p.Key, StringComparer.Ordinal))
        { writer.Write(pair.Key); WriteScalar(writer, pair.Value); }
        var active = selection.Specializations.Select(s => s.Id).ToHashSet();
        var constants = stage.Specializations.Where(s => active.Contains(s.Id)).OrderBy(s => s.Id).ToArray();
        writer.Write(constants.Length);
        foreach (var constant in constants)
        { writer.Write(constant.Id); writer.Write(constant.Option.Name); WriteScalar(writer, constant.Option.Default); }
        var entries = stage.Bindings.Entries;
        writer.Write(entries.Count);
        foreach (var entry in entries)
        {
            writer.Write((int)entry.Kind); writer.Write(entry.Name);
            var binding = entry.Binding;
            writer.Write(binding.Slot); writer.Write(binding.Required); writer.Write(binding.ArrayLength);
            writer.Write(binding.ShaderType.HasValue);
            if (binding.ShaderType.HasValue) writer.Write((int)binding.ShaderType.Value);
            writer.Write(binding.TextureTarget); writer.Write(binding.Sampler);
        }
        WriteLocations(writer, stage.Bindings.UniformLocations);
        WriteLocations(writer, stage.Bindings.VaryingLocations);
        WriteLocations(writer, stage.Bindings.FragmentOutputLocations);
        writer.Write(selection.Key);
        return stream.ToArray();
    }

    /// <summary>Captures exact registry output association independently of reusable compiler contents.</summary>
    internal static byte[] Membership(ShaderVariantResolver registry, string scope)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        writer.Write("catalogue-membership-v1"); writer.Write(scope);
        writer.Write(registry.Binaries.Count);
        foreach (var selection in registry.Binaries.OrderBy(s => s.Stage.Identity, StringComparer.Ordinal)
            .ThenBy(s => s.Key, StringComparer.Ordinal))
        {
            writer.Write(selection.Stage.Identity); writer.Write(selection.Key); writer.Write(selection.BinaryPath);
            byte[] contract = Effective(selection); writer.Write(contract.Length); writer.Write(contract);
        }
        return stream.ToArray();
    }
    #endregion

    #region Private
    /// <summary>Writes the exact scalar representation used by the owning model.</summary>
    private static void WriteScalar(BinaryWriter writer, ShaderScalar scalar)
    { writer.Write((int)scalar.Type); writer.Write(scalar.Bits); }

    /// <summary>Writes one location namespace in ordinal order, preserving namespace boundaries.</summary>
    private static void WriteLocations(BinaryWriter writer, System.Collections.Generic.IDictionary<string, int> locations)
    {
        writer.Write(locations.Count);
        foreach (var pair in locations.OrderBy(p => p.Key, StringComparer.Ordinal))
        { writer.Write(pair.Key); writer.Write(pair.Value); }
    }
    #endregion
}
