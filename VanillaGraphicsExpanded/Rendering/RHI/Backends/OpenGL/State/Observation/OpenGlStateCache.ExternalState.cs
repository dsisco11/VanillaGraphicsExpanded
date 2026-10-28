namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Admits exact external state effects and withdraws only their represented knowledge.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Withdraws affected external facts without applying managed command-admission rules.</summary>
    internal void ForgetExternalStateMutation(DepthStateKnowledge depth = default,
        RasterizerStateKnowledge rasterizer = default, PrimitiveAssemblyStateKnowledge assembly = default,
        DynamicDrawStateKnowledge dynamic = default, BlendStateKnowledge blend = default, int index = -1,
        bool clearColor = false, StencilStateKnowledge stencil = default, SamplingStateKnowledge sampling = default,
        OutputStateKnowledge output = default, bool sampleMaskWords = false)
    {
        // Native observation cannot veto a command because a managed boundary omitted its state.
        // Managed setters validate their declared coverage separately before native dispatch.
        depthKnown &= ~depth;
        rasterizerKnown &= ~rasterizer;
        assemblyKnown &= ~assembly;
        dynamicKnown &= ~dynamic;
        stencilKnown &= ~stencil;
        samplingKnown &= ~sampling;
        outputKnown &= ~output;
        if (rasterizer.HasFlag(RasterizerStateKnowledge.ClipDistances)) clipDistancesKnown = 0;
        if (clearColor) clearColorKnown = false;
        if (sampleMaskWords) sampleMasks.Clear();
        if (blend == BlendStateKnowledge.None) return;
        // Out-of-range native indices have no represented slot; their native error remains untouched.
        if (index >= 0)
        {
            if (index < blendKnown.Length) blendKnown[index] &= ~blend;
        }
        else for (int slot = 0; slot < blendKnown.Length; slot++) blendKnown[slot] &= ~blend;
    }
    #endregion
}
