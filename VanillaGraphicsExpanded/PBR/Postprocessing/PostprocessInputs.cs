using System.Numerics;
using VanillaGraphicsExpanded.Rendering;
namespace VanillaGraphicsExpanded.PBR.Postprocessing;
/// <summary>Packs filtering, exposure and solar inputs for luma and shaft operations.</summary>
internal sealed class PostprocessInputs : CpuUniformBuffer
{
    #region Public API
    internal const int BlockSize = 64;
    internal const int PassOffset = 0;
    internal const int EffectOffset = 16;
    internal const int SourceOffset = 32;
    internal const int SolarOffset = 48;
    /// <summary>Declares this layout's immutable extent.</summary>
    public override int SizeBytes => BlockSize;

    /// <summary>Stages luminance preparation and exposure selection.</summary>
    internal void CaptureLuma(bool antialias, bool automaticExposure, float manualLogGain)
    {
        WriteVector4(PassOffset, new(0, 0, antialias ? 1 : 0, automaticExposure ? 1 : 0));
        WriteVector4(EffectOffset, new(0, 0, 0, manualLogGain));
        WriteVector4(SourceOffset, Vector4.Zero);
        WriteVector4(SolarOffset, Vector4.Zero);
    }

    /// <summary>Stages glare extraction or independent distance occlusion.</summary>
    internal void CaptureShaftExtraction(bool occlusionOnly, bool automaticExposure, float threshold,
        float limit, float strength, float manualLogGain, LightShaftSource source)
    {
        WriteVector4(PassOffset, new(0, 0, occlusionOnly ? 4 : 0, automaticExposure ? 1 : 0));
        WriteVector4(EffectOffset, new(threshold, limit, strength, manualLogGain));
        CaptureSource(source);
    }

    /// <summary>Stages a radial sampling operation.</summary>
    internal void CaptureShaftFilter(int samples, float step, LightShaftSource source)
    {
        WriteVector4(PassOffset, new(0, 0, 1, samples));
        WriteVector4(EffectOffset, new(step, 0, 0, 0));
        CaptureSource(source);
    }

    /// <summary>Stages publication of the filtered glare or occlusion signal.</summary>
    internal void CaptureShaftResolve(bool occlusionOnly, LightShaftSource source)
    {
        WriteVector4(PassOffset, new(0, 0, occlusionOnly ? 3 : 2, 0));
        WriteVector4(EffectOffset, Vector4.Zero);
        CaptureSource(source);
    }
    #endregion

    #region Private
    /// <summary>Packs emitter geometry and tint for every shaft operation.</summary>
    private void CaptureSource(LightShaftSource source)
    {
        WriteVector4(SourceOffset, new(source.ScreenPosition, source.Visibility, source.AspectRatio));
        WriteVector4(SolarOffset, new(source.Tint, source.MaximumOcclusionDistance));
    }
    #endregion
}
