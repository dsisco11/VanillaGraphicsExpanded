namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// Phase 1 PSO descriptor skeleton: intent bitsets only.
/// </summary>
/// <remarks>
/// Value payloads for <see cref="NonDefaultMask"/> are added in Phase 2.
/// </remarks>
internal readonly struct GlPipelineDesc
{
    public GlPipelineStateMask DefaultMask { get; }
    public GlPipelineStateMask NonDefaultMask { get; }

    public GlPipelineDesc(GlPipelineStateMask defaultMask, GlPipelineStateMask nonDefaultMask)
    {
        DefaultMask = defaultMask;
        NonDefaultMask = nonDefaultMask;

#if DEBUG
        // Phase 1: there are no value payload fields yet, so treat all non-default bits as "present".
        GlPipelineStateValidation.ValidateMasks(DefaultMask, NonDefaultMask, valuesPresentMask: NonDefaultMask);
#endif
    }
}

