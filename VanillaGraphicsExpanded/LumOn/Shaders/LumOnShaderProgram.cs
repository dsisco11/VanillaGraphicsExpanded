using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.LumOn.Shaders;

/// <summary>Shares externally owned lighting buffers across LumOn passes using each installed shader contract.</summary>
public abstract class LumOnShaderProgram : GpuProgram, ILumOnFrameShader, ILumOnWorldProbeShader
{
    #region Shared lighting buffers
    /// <summary>Binds frame data when the installed variant consumes it, retaining caller ownership.</summary>
    internal GpuUniformBuffer FrameUniformBuffer
    {
        set => TryBindUniformBlock(LumOnUniformBuffers.FrameBlockName, value);
    }

    /// <summary>Binds world-probe data when the installed variant consumes it, retaining caller ownership.</summary>
    internal GpuUniformBuffer WorldProbeUniformBuffer
    {
        set => TryBindUniformBlock(LumOnUniformBuffers.WorldProbeBlockName, value);
    }
    /// <summary>Exposes the same frame binding through the shared consumer interface.</summary>
    GpuUniformBuffer ILumOnFrameShader.FrameUniformBuffer { set => FrameUniformBuffer = value; }

    /// <summary>Exposes the same world-probe binding through the shared consumer interface.</summary>
    GpuUniformBuffer ILumOnWorldProbeShader.WorldProbeUniformBuffer { set => WorldProbeUniformBuffer = value; }
    #endregion
}
