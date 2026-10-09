using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.LumOn.Shaders;

/// <summary>Shares externally owned lighting buffers across LumOn passes using each installed shader contract.</summary>
public abstract class LumOnShaderProgram : GpuProgram, ILumOnFrameShader, ILumOnWorldProbeShader
{
    private VgeFrameUniformBuffer? frameInputs;
    private GpuUniformBuffer? retainedFrame;
    private GpuUniformBuffer? retainedWorldProbe;


    #region Public API
    /// <summary>Supplies an explicitly owned view or reuses the world camera publication.</summary>
    internal VgeFrameUniformBuffer? FrameInputs
    {
        get => frameInputs;
        set { RequireInputMutation(); frameInputs = value; }
    }
    /// <summary>Retains frame data until generated submission.</summary>
    internal GpuUniformBuffer FrameUniformBuffer
    {
        set
        {
            RequireInputMutation();
            retainedFrame = value;
        }
    }

    /// <summary>Retains world-probe data until generated submission.</summary>
    internal GpuUniformBuffer WorldProbeUniformBuffer
    {
        set
        {
            RequireInputMutation();
            retainedWorldProbe = value;
        }
    }
    #endregion

    #region Protected API
    /// <summary>Supplies the immutable shared camera publication to derived shader contracts.</summary>
    protected CpuUniformBuffer SharedCamera => frameInputs ?? VgeFrameRenderer.Current;
    /// <summary>Exposes the retained frame resource to derived binding-contract getters.</summary>
    protected GpuUniformBuffer? RetainedFrame => retainedFrame;
    /// <summary>Exposes retained world-probe storage for contracts that consume it.</summary>
    protected GpuUniformBuffer? RetainedWorldProbe => retainedWorldProbe;
    #endregion

    #region Private
    /// <summary>Exposes the same camera through the shared effect-consumer boundary.</summary>
    VgeFrameUniformBuffer ILumOnFrameShader.FrameInputs { set => FrameInputs = value; }
    /// <summary>Exposes the same frame binding through the shared consumer interface.</summary>
    GpuUniformBuffer ILumOnFrameShader.FrameUniformBuffer { set => FrameUniformBuffer = value; }

    /// <summary>Exposes the same world-probe binding through the shared consumer interface.</summary>
    GpuUniformBuffer ILumOnWorldProbeShader.WorldProbeUniformBuffer { set => WorldProbeUniformBuffer = value; }
    #endregion
}