namespace VanillaGraphicsExpanded.PBR.Postprocessing;

/// <summary>Stores native graphics controls independently of the transient postprocessing pass gate.</summary>
internal readonly record struct EnginePostprocessInputs(bool Bloom,bool LightShafts,bool Ssao,bool Fxaa,int SsaoQuality,int LightShaftQuality=1)
{
    #region Public API
    /// <summary>Applies only the transient native pass gate to persistent cached settings.</summary>
    internal EnginePostprocessInputs ForPass(bool enabled) => this with
    {
        Bloom = Bloom && enabled, LightShafts = LightShafts && enabled,
        Ssao = Ssao && enabled, Fxaa = Fxaa && enabled
    };
    #endregion
}
