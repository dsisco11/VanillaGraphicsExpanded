using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>One active specialization value with its stage-owned ID and exact driver argument bits.</summary>
internal readonly record struct ShaderSpecializationArgument(uint Id, ShaderScalar Value);

/// <summary>A transient immutable stage selection; it owns no assets, serialized metadata or GL objects.</summary>
internal sealed class ShaderStageSelection
{
    public ShaderStageContract Stage { get; }
    public IReadOnlyDictionary<string, ShaderScalar> Structural { get; }
    public IReadOnlyList<ShaderSpecializationArgument> Specializations { get; }
    public string Key { get; }
    public string BinaryPath { get; }

    #region Projection
    /// <summary>Projects a coherent program snapshot, omitting inactive numeric inputs from effective selection.</summary>
    internal ShaderStageSelection(ShaderStageContract stage, IReadOnlyDictionary<string, ShaderScalar> values)
    {
        Stage = stage;
        Structural = new ReadOnlyDictionary<string, ShaderScalar>(stage.Structural.ToDictionary(o => o.Name, o => values[o.Name], StringComparer.Ordinal));
        Key = ShaderAssignments.Key(Structural);
        string defaultKey = ShaderAssignments.Key(stage.Structural.ToDictionary(o => o.Name, o => o.Default, StringComparer.Ordinal));
        BinaryPath = Key == defaultKey ? stage.BinaryAsset + ".spv" : "variants/" + stage.BinaryAsset + "/" +
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Key))).ToLowerInvariant() + ".spv";
        Specializations = Array.AsReadOnly(stage.Specializations.Where(s => s.Condition?.Evaluate(Structural) ?? true)
            .Select(s => new ShaderSpecializationArgument(s.Id, values[s.Option.Name])).ToArray());
    }

    /// <summary>Compares effective binary and specialization choices for reload coalescing.</summary>
    public bool SameInputs(ShaderStageSelection other) => Stage.Equivalent(other.Stage) && Key == other.Key &&
        Specializations.SequenceEqual(other.Specializations);
    #endregion
}
