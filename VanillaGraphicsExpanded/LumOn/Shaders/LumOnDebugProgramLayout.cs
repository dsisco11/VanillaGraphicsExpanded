using System;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.LumOn.Scene;

namespace VanillaGraphicsExpanded.LumOn.Shaders;

internal sealed class LumOnDebugProgramLayout : GpuProgramLayout
{
    public LumOnDebugParamsUbo Params { get; } = new();

    internal LumOnNearFieldVisibilityBindings NearFieldVisibility { get; }

    /// <summary>Consumes the debug shader's generated resource contract and creates its visibility binder.</summary>
    public LumOnDebugProgramLayout()
    {
        RegisterContract(LumOnDebugShaderProgram.WorldProbeIrradianceCombinedContract.Stages[1].Bindings);
        NearFieldVisibility = new LumOnNearFieldVisibilityBindings(this);

    }

    public void BindParamsUbo(GpuProgram program, string debugName)
    {
        Params.BindTo(program, LumOnDebugParamsUbo.BlockName, debugName);
    }

    public void BindTexture2D(int programId, string uniformName, int textureId, Action<string>? warn)
        => TryBindSamplerTextureActive(programId, uniformName, TextureTarget.Texture2D, textureId, samplerId: 0, warn);

    public void BindNearestClamp2D(int programId, string uniformName, int textureId, Action<string>? warn)
        => TryBindSamplerTextureActive(programId, uniformName, TextureTarget.Texture2D, textureId, GpuSamplers.NearestClamp.SamplerId, warn);

    public void BindTexture3D(int programId, string uniformName, int textureId, Action<string>? warn)
        => TryBindSamplerTextureActive(programId, uniformName, TextureTarget.Texture3D, textureId, samplerId: 0, warn);
}
