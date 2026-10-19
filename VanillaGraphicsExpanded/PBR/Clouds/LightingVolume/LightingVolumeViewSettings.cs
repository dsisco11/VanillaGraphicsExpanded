using System;

namespace VanillaGraphicsExpanded.PBR.Clouds.LightingVolume;

/// <summary>Defines independent deterministic view controls without borrowing current cloud quality settings.</summary>
internal readonly record struct LightingVolumeViewSettings(int Maximum,float BudgetDistance,float Stop,
    bool Detail,bool SelfShadowing,bool Diffuse)
{
    #region Public API
    /// <summary>Provides the selected full-resolution experimental view contract.</summary>
    internal static LightingVolumeViewSettings Default=>new(96,10000,.005f,true,true,true);
    /// <summary>Rejects unsupported finite schedules before executable or GPU mutation.</summary>
    internal void Validate()
    {
        if(Maximum is <4 or >96 || !float.IsFinite(BudgetDistance)||BudgetDistance is <50 or >10000 ||
            !float.IsFinite(Stop)||Stop is <0 or >.005f) throw new ArgumentOutOfRangeException(nameof(Maximum));
    }
    #endregion
}
