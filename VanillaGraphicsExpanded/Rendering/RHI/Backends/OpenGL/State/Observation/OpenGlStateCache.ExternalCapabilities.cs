using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Commits exact external enable effects, including global/indexed aliases and independent clip bits.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Preserves knowledge for clip-distance indices rejected by the context's cached limit.</summary>
    internal static bool AcceptsExternalCapability(EnableCap capability)
    {
        int index = (int)capability - (int)EnableCap.ClipDistance0;
        return index < 0 || index > 7 || index < GpuSupport.Graphics.MaxClipDistances;
    }

    /// <summary>Withholds one clip bit during dispatch without discarding independent clip enables.</summary>
    internal void WithdrawExternalClipDistance(int index)
    {
        ValidateBoundaryMutation(rasterizer: RasterizerStateKnowledge.ClipDistances);
        clipDistancesKnown &= ~(1u << index);
        rasterizerKnown &= ~RasterizerStateKnowledge.ClipDistances;
    }

    /// <summary>Commits one valid indexed blend enable without changing its generic/global neighbors.</summary>
    internal void ObserveExternalCapability(IndexedEnableCap capability, int index, bool enabled)
    {
        if (capability == IndexedEnableCap.Blend)
        {
            blend[index].Enabled = enabled;
            blendKnown[index] |= BlendStateKnowledge.Enabled;
        }
        else
        {
            rasterizer.ScissorEnabled = enabled;
            rasterizerKnown |= RasterizerStateKnowledge.ScissorEnabled;
        }
    }

    /// <summary>Commits a supported capability after exact dispatch returns to its original context.</summary>
    internal void ObserveExternalCapability(EnableCap capability, bool enabled)
    {
        switch (capability)
        {
            case EnableCap.DepthTest: depth.TestEnabled = enabled; depthKnown |= DepthStateKnowledge.TestEnabled; break;
            case EnableCap.CullFace: rasterizer.CullEnabled = enabled; rasterizerKnown |= RasterizerStateKnowledge.CullEnabled; break;
            case EnableCap.ScissorTest: rasterizer.ScissorEnabled = enabled; rasterizerKnown |= RasterizerStateKnowledge.ScissorEnabled; break;
            case EnableCap.StencilTest: stencil.TestEnabled = enabled; stencilKnown |= StencilStateKnowledge.TestEnabled; break;
            case EnableCap.Multisample: sampling.Multisample = enabled; samplingKnown |= SamplingStateKnowledge.Multisample; break;
            case EnableCap.SampleCoverage: sampling.SampleCoverageEnabled = enabled; samplingKnown |= SamplingStateKnowledge.SampleCoverageEnabled; break;
            case EnableCap.SampleMask: sampling.SampleMask = enabled; samplingKnown |= SamplingStateKnowledge.SampleMask; break;
            case EnableCap.SampleAlphaToCoverage: sampling.SampleAlphaToCoverage = enabled; samplingKnown |= SamplingStateKnowledge.SampleAlphaToCoverage; break;
            case EnableCap.SampleAlphaToOne: sampling.SampleAlphaToOne = enabled; samplingKnown |= SamplingStateKnowledge.SampleAlphaToOne; break;
            case EnableCap.SampleShading: sampling.SampleShading = enabled; samplingKnown |= SamplingStateKnowledge.SampleShading; break;
            case EnableCap.FramebufferSrgb: output.FramebufferSrgb = enabled; outputKnown |= OutputStateKnowledge.FramebufferSrgb; break;
            case EnableCap.Dither: output.Dither = enabled; outputKnown |= OutputStateKnowledge.Dither; break;
            case EnableCap.ColorLogicOp: output.ColorLogicOp = enabled; outputKnown |= OutputStateKnowledge.ColorLogicOp; break;
            case EnableCap.DepthClamp: rasterizer.DepthClamp = enabled; rasterizerKnown |= RasterizerStateKnowledge.DepthClamp; break;
            case EnableCap.RasterizerDiscard: rasterizer.RasterizerDiscard = enabled; rasterizerKnown |= RasterizerStateKnowledge.RasterizerDiscard; break;
            case EnableCap.PolygonOffsetFill: rasterizer.PolygonOffsetFill = enabled; rasterizerKnown |= RasterizerStateKnowledge.PolygonOffsetFill; break;
            case EnableCap.PolygonOffsetLine: rasterizer.PolygonOffsetLine = enabled; rasterizerKnown |= RasterizerStateKnowledge.PolygonOffsetLine; break;
            case EnableCap.PolygonOffsetPoint: rasterizer.PolygonOffsetPoint = enabled; rasterizerKnown |= RasterizerStateKnowledge.PolygonOffsetPoint; break;
            case EnableCap.ProgramPointSize: rasterizer.ProgramPointSize = enabled; rasterizerKnown |= RasterizerStateKnowledge.ProgramPointSize; break;
            case EnableCap.LineSmooth: rasterizer.LineSmooth = enabled; rasterizerKnown |= RasterizerStateKnowledge.LineSmooth; break;
            case EnableCap.PolygonSmooth: rasterizer.PolygonSmooth = enabled; rasterizerKnown |= RasterizerStateKnowledge.PolygonSmooth; break;
            case EnableCap.PrimitiveRestart: assembly.PrimitiveRestart = enabled; assemblyKnown |= PrimitiveAssemblyStateKnowledge.PrimitiveRestart; break;
            case EnableCap.PrimitiveRestartFixedIndex: assembly.PrimitiveRestartFixedIndex = enabled; assemblyKnown |= PrimitiveAssemblyStateKnowledge.PrimitiveRestartFixedIndex; break;
            case EnableCap.Blend:
                for (int i = 0; i < MaxDrawBuffers; i++) { blend[i].Enabled = enabled; blendKnown[i] |= BlendStateKnowledge.Enabled; }
                break;
            default:
                int index = (int)capability - (int)EnableCap.ClipDistance0;
                if (index < 0 || index >= GpuSupport.Graphics.MaxClipDistances || index >= 32) return;
                uint bit = 1u << index;
                if (enabled) rasterizer.ClipDistances |= bit; else rasterizer.ClipDistances &= ~bit;
                clipDistancesKnown |= bit;
                uint complete = GpuSupport.Graphics.MaxClipDistances >= 32 ? uint.MaxValue : (1u << GpuSupport.Graphics.MaxClipDistances) - 1;
                if ((clipDistancesKnown & complete) == complete) rasterizerKnown |= RasterizerStateKnowledge.ClipDistances;
                break;
        }
    }
    #endregion
}
