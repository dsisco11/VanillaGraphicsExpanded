using System;

namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>A stage-owned specialization ID, typed option and optional structural availability condition.</summary>
internal sealed class ShaderSpecialization
{
    public uint Id { get; }
    public ShaderOption Option { get; }
    public ShaderCondition? Condition { get; }

    #region Declaration
    /// <summary>Validates the numeric ID before it can be encoded for a driver.</summary>
    public ShaderSpecialization(int id, ShaderOption option, ShaderCondition? condition = null)
    {
        if (id < 0) throw new ArgumentException($"Option '{option.Name}' has invalid specialization ID '{id}'.");
        Id = (uint)id; Option = option; Condition = condition;
    }
    /// <summary>Compares shared stage declarations independently of their owning program.</summary>
    internal bool Equivalent(ShaderSpecialization other) => Id == other.Id && Option.Equivalent(other.Option) &&
        (Condition == null ? other.Condition == null : other.Condition != null && Condition.Equivalent(other.Condition));
    #endregion
}
