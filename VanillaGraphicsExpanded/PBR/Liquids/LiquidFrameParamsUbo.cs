using System;
using System.Numerics;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.PBR.Liquids;

/// <summary>Packs liquid frame inputs into the owned std140 block.</summary>
internal sealed class LiquidFrameParamsUbo : CpuUniformBuffer
{
    internal const string BlockName = "VgeLiquidFrameParams";
    internal const int BlockSize = 1360;
    #region Public API
    /// <summary>Allocates the block declared by liquids/params.glsl.</summary>
    internal LiquidFrameParamsUbo() : base(BlockSize, Rendering.Uniforms.UniformBufferUsage.SingleFrame) { }
    /// <summary>Stages the ShadowMatrixNear field.</summary>
    internal ReadOnlySpan<float> ShadowMatrixNear { set => WriteMatrix4(0, value); }
    /// <summary>Stages the ShadowMatrixFar field.</summary>
    internal ReadOnlySpan<float> ShadowMatrixFar { set => WriteMatrix4(64, value); }
    /// <summary>Stages the Animation field.</summary>
    internal Vector4 Animation { set => WriteVector4(128, value); }
    /// <summary>Stages the ShadowRanges field.</summary>
    internal Vector4 ShadowRanges { set => WriteVector4(144, value); }
    /// <summary>Stages the PlayerPosition field.</summary>
    internal Vector4 PlayerPosition { set => WriteVector4(160, value); }
    /// <summary>Stages the AtlasMetrics field.</summary>
    internal Vector4 AtlasMetrics { set => WriteVector4(176, value); }
    /// <summary>Stages the Season field.</summary>
    internal Vector4 Season { set => WriteVector4(192, value); }
    /// <summary>Stages the SunDirection field.</summary>
    internal Vector4 SunDirection { set => WriteVector4(208, value); }
    /// <summary>Stages the SolarIrradiance field.</summary>
    internal Vector4 SolarIrradiance { set => WriteVector4(224, value); }
    /// <summary>Stages the EnvironmentIrradiance field.</summary>
    internal Vector4 EnvironmentIrradiance { set => WriteVector4(240, value); }
    /// <summary>Stages the AerialParameters field.</summary>
    internal Vector4 AerialParameters { set => WriteVector4(256, value); }
    /// <summary>Stages the Perception field.</summary>
    internal Vector4 Perception { set => WriteVector4(288, value); }
    /// <summary>Stages the PerceptionPosition field.</summary>
    internal Vector4 PerceptionPosition { set => WriteVector4(304, value); }
    /// <summary>Enables the current material-medium lookup; zero selects the measured clear-water default.</summary>
    internal bool MediumLookupEnabled { set => WriteFloat(1344, value ? 1 : 0); }
    /// <summary>Moves bulk attenuation to scene-linear composition when the boundary capture is valid.</summary>
    internal bool VolumeTransportEnabled { set => WriteFloat(1348, value ? 1 : 0); }
    /// <summary>Enables traversal only when coherent immutable opaque inputs were published.</summary>
    internal bool RefractionEnabled { set => WriteFloat(1352, value ? 1 : 0); }
    /// <summary>Selects unexposed scene-linear RGB for the shared HDR handoff.</summary>
    internal bool SceneLinear { set => WriteFloat(1356, value ? 1 : 0); }
    /// <summary>Sets the bounded number of active fog spheres; dynamic lights use the shared block.</summary>
    internal void SetFogSphereCount(int spheres) => WriteIntVector4(272, 0, spheres, 0, 0);
    /// <summary>Stages one ColorMapRect array element with std140 stride.</summary>
    internal void SetColorMapRect(int index, Vector4 value)
    {
        if ((uint)index >= 40) throw new ArgumentOutOfRangeException(nameof(index));
        WriteVector4(320 + index * 16, value);
    }
    /// <summary>Stages one FogSphereComponent array element with std140 stride.</summary>
    internal void SetFogSphereComponent(int index, float value)
    {
        if ((uint)index >= 24) throw new ArgumentOutOfRangeException(nameof(index));
        WriteFloat(960 + index * 16, value);
    }
    #endregion
}
