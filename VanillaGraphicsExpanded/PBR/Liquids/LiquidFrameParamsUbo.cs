using System;
using System.Numerics;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.PBR.Liquids;

/// <summary>Packs liquid frame inputs into the owned std140 block.</summary>
internal sealed class LiquidFrameParamsUbo : CpuUniformBuffer
{
    internal const string BlockName = "VgeLiquidFrameParams";
    internal const int BlockSize = 4640;
    #region Frame parameters
    /// <summary>Allocates the block declared by liquids/params.glsl.</summary>
    internal LiquidFrameParamsUbo() : base(BlockSize) { }
    /// <summary>Stages the ProjectionMatrix field.</summary>
    internal ReadOnlySpan<float> ProjectionMatrix { set => WriteMatrix4(0, value); }
    /// <summary>Stages the ShadowMatrixNear field.</summary>
    internal ReadOnlySpan<float> ShadowMatrixNear { set => WriteMatrix4(64, value); }
    /// <summary>Stages the ShadowMatrixFar field.</summary>
    internal ReadOnlySpan<float> ShadowMatrixFar { set => WriteMatrix4(128, value); }
    /// <summary>Stages the Animation field.</summary>
    internal Vector4 Animation { set => WriteVector4(192, value); }
    /// <summary>Stages the ShadowRanges field.</summary>
    internal Vector4 ShadowRanges { set => WriteVector4(208, value); }
    /// <summary>Stages the PlayerPosition field.</summary>
    internal Vector4 PlayerPosition { set => WriteVector4(224, value); }
    /// <summary>Stages the AtlasMetrics field.</summary>
    internal Vector4 AtlasMetrics { set => WriteVector4(240, value); }
    /// <summary>Stages the DepthRangeAndFrameSize field.</summary>
    internal Vector4 DepthRangeAndFrameSize { set => WriteVector4(256, value); }
    /// <summary>Stages the Season field.</summary>
    internal Vector4 Season { set => WriteVector4(272, value); }
    /// <summary>Stages the SunDirection field.</summary>
    internal Vector4 SunDirection { set => WriteVector4(288, value); }
    /// <summary>Stages the SolarIrradiance field.</summary>
    internal Vector4 SolarIrradiance { set => WriteVector4(304, value); }
    /// <summary>Stages the EnvironmentIrradiance field.</summary>
    internal Vector4 EnvironmentIrradiance { set => WriteVector4(320, value); }
    /// <summary>Stages the AerialParameters field.</summary>
    internal Vector4 AerialParameters { set => WriteVector4(336, value); }
    /// <summary>Stages the Perception field.</summary>
    internal Vector4 Perception { set => WriteVector4(368, value); }
    /// <summary>Stages the PerceptionPosition field.</summary>
    internal Vector4 PerceptionPosition { set => WriteVector4(384, value); }
    /// <summary>Enables the current material-medium lookup; zero selects the measured clear-water default.</summary>
    internal bool MediumLookupEnabled { set => WriteFloat(4624, value ? 1 : 0); }
    /// <summary>Moves bulk attenuation to scene-linear composition when the boundary capture is valid.</summary>
    internal bool VolumeTransportEnabled { set => WriteFloat(4628, value ? 1 : 0); }
    /// <summary>Sets the bounded numbers of active point lights and fog spheres.</summary>
    internal void SetCounts(int lights, int spheres) => WriteIntVector4(352, lights, spheres, 0, 0);
    /// <summary>Stages one ColorMapRect array element with std140 stride.</summary>
    internal void SetColorMapRect(int index, Vector4 value)
    {
        if ((uint)index >= 40) throw new ArgumentOutOfRangeException(nameof(index));
        WriteVector4(400 + index * 16, value);
    }
    /// <summary>Stages one PointLightPosition array element with std140 stride.</summary>
    internal void SetPointLightPosition(int index, Vector3 value)
    {
        if ((uint)index >= 100) throw new ArgumentOutOfRangeException(nameof(index));
        WriteVector3(1040 + index * 16, value);
    }
    /// <summary>Stages one PointLightColor array element with std140 stride.</summary>
    internal void SetPointLightColor(int index, Vector3 value)
    {
        if ((uint)index >= 100) throw new ArgumentOutOfRangeException(nameof(index));
        WriteVector3(2640 + index * 16, value);
    }
    /// <summary>Stages one FogSphereComponent array element with std140 stride.</summary>
    internal void SetFogSphereComponent(int index, float value)
    {
        if ((uint)index >= 24) throw new ArgumentOutOfRangeException(nameof(index));
        WriteFloat(4240 + index * 16, value);
    }
    #endregion
}
