using VanillaGraphicsExpanded.Rendering.Shadows.Geometry;
using System;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Rendering.Integration.Shadows;

/// <summary>Freezes native frame uniforms that can change the geometry produced by a shadow material.</summary>
internal sealed class NativeShadowWarpSnapshot
{
    private readonly DefaultShaderUniforms source;
    private readonly float[] values;
    private readonly int perception;

    #region Public API
    /// <summary>Captures finite native deformation values before any replacement is admitted.</summary>
    internal NativeShadowWarpSnapshot(DefaultShaderUniforms source)
    {
        this.source = source ?? throw new ArgumentNullException(nameof(source));
        values = new float[13];
        Read(source, values);
        foreach (float value in values)
            if (!float.IsFinite(value)) throw new InvalidOperationException("Native shadow deformation contains nonfinite input.");
        perception = source.PerceptionEffectId;
    }

    /// <summary>Rejects geometry changes caused by frame uniforms even when mesh storage and pose are unchanged.</summary>
    internal bool IsCurrent
    {
        get
        {
            Span<float> current = stackalloc float[13];
            Read(source, current);
            return source.PerceptionEffectId == perception && current.SequenceEqual(values);
        }
    }

    /// <summary>Exposes frozen semantic deformation values without leaking the material byte layout.</summary>
    internal ShadowDeformationInputs Deformation => new(
        new(values[0], values[1], values[2]), new(values[3], values[4], values[5], values[6]),
        new(values[7], values[8], values[9], values[10]), values[11], perception, values[12]);
    #endregion

    #region Private
    /// <summary>Reads all shared values in the same order for capture and subsequent currentness checks.</summary>
    private static void Read(DefaultShaderUniforms source, Span<float> values)
    {
        if (source.PlayerPos is null) throw new InvalidOperationException("Native player reference is unavailable.");
        values[0] = source.PlayerPos.X; values[1] = source.PlayerPos.Y; values[2] = source.PlayerPos.Z;
        values[3] = source.TimeCounter; values[4] = source.WindWaveCounter;
        values[5] = source.WindWaveCounterHighFreq; values[6] = source.WaterWaveCounter;
        values[7] = source.WindSpeed; values[8] = source.GlobalWorldWarp; values[9] = source.GlitchWaviness;
        values[10] = source.WindWaveIntensity; values[11] = source.WaterWaveIntensity;
        values[12] = source.PerceptionEffectIntensity;
    }
    #endregion
}
