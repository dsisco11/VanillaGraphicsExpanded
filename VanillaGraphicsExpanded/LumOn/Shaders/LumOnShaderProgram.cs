using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.LumOn.Shaders;

/// <summary>Shares externally owned lighting buffers across LumOn passes using each installed shader contract.</summary>
public abstract class LumOnShaderProgram : GpuProgram, ILumOnFrameShader, ILumOnWorldProbeShader
{
    private GpuUniformBuffer? retainedFrame;
    private GpuUniformBuffer? retainedWorldProbe;

    /// <summary>Identifies owners whose common lighting references are published by activation.</summary>
    protected virtual bool UsesRetainedLightingInputs => false;

    #region Shared lighting buffers
    /// <summary>Retains frame data for retained-input owners or binds it for compatibility consumers.</summary>
    internal GpuUniformBuffer FrameUniformBuffer
    {
        set
        {
            if (UsesRetainedLightingInputs)
            {
                RequireInputMutation();
                retainedFrame = value;
            }
            else TryBindUniformBlock(LumOnUniformBuffers.FrameBlockName, value);
        }
    }

    /// <summary>Retains world-probe data for retained-input owners or binds it for compatibility consumers.</summary>
    internal GpuUniformBuffer WorldProbeUniformBuffer
    {
        set
        {
            if (UsesRetainedLightingInputs)
            {
                RequireInputMutation();
                retainedWorldProbe = value;
            }
            else TryBindUniformBlock(LumOnUniformBuffers.WorldProbeBlockName, value);
        }
    }
    /// <summary>Exposes the same frame binding through the shared consumer interface.</summary>
    GpuUniformBuffer ILumOnFrameShader.FrameUniformBuffer { set => FrameUniformBuffer = value; }

    /// <summary>Exposes the same world-probe binding through the shared consumer interface.</summary>
    GpuUniformBuffer ILumOnWorldProbeShader.WorldProbeUniformBuffer { set => WorldProbeUniformBuffer = value; }
    #endregion

    /// <summary>Exposes the retained frame resource to derived binding-contract getters.</summary>
    protected GpuUniformBuffer? RetainedFrame => retainedFrame;
    /// <summary>Exposes retained world-probe storage for contracts that consume it.</summary>
    protected GpuUniformBuffer? RetainedWorldProbe => retainedWorldProbe;
}