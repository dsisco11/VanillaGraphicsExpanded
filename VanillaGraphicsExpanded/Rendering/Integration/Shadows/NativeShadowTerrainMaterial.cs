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
        Span<byte> data = stackalloc byte[208]; data.Clear();
        for (int i = 0; i < 16; i++)
        {
            UboPacking.WriteFloat(data, i * 4, view.Matrix[i]);
            UboPacking.WriteFloat(data, 144 + i * 4, placement.Transform[i]);
        }
        var origin = placement.Origin;
        UboPacking.WriteVec4(data, 64, (float)origin.X, (float)origin.Y, (float)origin.Z, paddingX);
        UboPacking.WriteVec4(data, 80, native.PlayerPos.X, native.PlayerPos.Y, native.PlayerPos.Z, paddingY);
        UboPacking.WriteVec4(data, 96, native.TimeCounter, native.WindWaveCounter, native.WindWaveCounterHighFreq, native.WaterWaveCounter);
        UboPacking.WriteVec4(data, 112, native.WindSpeed, native.GlobalWorldWarp, native.GlitchWaviness, native.WindWaveIntensity);
        UboPacking.WriteVec4(data, 128, native.WaterWaveIntensity, 0, native.PerceptionEffectIntensity, 0);
        UboPacking.WriteInt32(data, 132, native.PerceptionEffectId);
        // Reject nonfinite native material values before they can disappear behind a
        // fragment-level write guard and leave an apparently complete empty depth page.
        for (int offset = 0; offset < data.Length; offset += 4)
            if (offset != 132 && !float.IsFinite(BitConverter.ToSingle(data.Slice(offset, 4))))
                throw new InvalidOperationException("Native shadow material contains nonfinite input.");
        shader.CaptureMaterial(data);
    }
    #endregion
}
