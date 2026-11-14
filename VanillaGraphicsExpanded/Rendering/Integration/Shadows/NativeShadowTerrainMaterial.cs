using System;
using VanillaGraphicsExpanded.Rendering.Shadows.Geometry;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Rendering.Integration.Shadows;

/// <summary>Packs current native deformation and placement inputs into the owned terrain shadow material contract.</summary>
internal static class NativeShadowTerrainMaterial
{
    #region Public API
    /// <summary>Preserves the native reference origin and all warp parameters before shadow resources are captured.</summary>
    internal static void Capture(ShadowNativeMaterialShader shader, DefaultShaderUniforms native,
        NativeShadowTerrainPlacement placement, ShadowDrawView view, float paddingX, float paddingY)
    {
        ArgumentNullException.ThrowIfNull(native);
        if (!placement.IsCurrent || placement.Reference != view.Origin || native.PlayerPos is null)
            throw new InvalidOperationException("Native material and virtual view do not share a current render origin.");
        Span<byte> data = stackalloc byte[ShadowMaterialParameters.BlockSize];
        var deformation = new ShadowDeformationInputs(new(native.PlayerPos.X, native.PlayerPos.Y, native.PlayerPos.Z),
            new(native.TimeCounter, native.WindWaveCounter, native.WindWaveCounterHighFreq, native.WaterWaveCounter),
            new(native.WindSpeed, native.GlobalWorldWarp, native.GlitchWaviness, native.WindWaveIntensity),
            native.WaterWaveIntensity, native.PerceptionEffectId, native.PerceptionEffectIntensity);
        ShadowMaterialParameters.PackTerrain(data, view.Matrix.AsSpan(), placement.Transform.AsSpan(),
            placement.Origin, deformation, paddingX, paddingY);
        shader.CaptureMaterial(data);
    }
    #endregion
}
