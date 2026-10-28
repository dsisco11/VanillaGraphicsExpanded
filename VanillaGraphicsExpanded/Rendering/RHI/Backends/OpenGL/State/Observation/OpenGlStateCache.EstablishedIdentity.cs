namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Uses existing realized associations and selector knowledge to prove native selector eligibility.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Checks existing authority without acquiring facts, probing GL or retaining another name registry.</summary>
    internal bool HasEstablishedNativeIdentity(GpuResourceKind kind, int id)
    {
        if (id == 0) return true;
        var manager = GpuResourceManagerSystem.CurrentManager;
        if (manager?.CanObserveNativeMetadata == true && manager.FindObservedResource(kind, id) is { IsUsable: true }) return true;
        // An executed selector proves realization until the single observed deletion path removes it.
        switch (kind)
        {
            case GpuResourceKind.Buffer:
                foreach (var value in bufferBindingByTarget.Values) if (value == id) return true;
                foreach (var value in elementArrayBufferByVao.Values) if (value == id) return true;
                foreach (var value in indexedBufferBindings.Values) if (value.Buffer == id) return true;
                break;
            case GpuResourceKind.Texture:
                if (textureBindingsByUnit is not null)
                    foreach (var unit in textureBindingsByUnit)
                        if (unit is not null) foreach (var value in unit.Values) if (value == id) return true;
                break;
            case GpuResourceKind.Sampler:
                if (samplerBindingByUnit is not null) foreach (var value in samplerBindingByUnit) if (value == id) return true;
                break;
            case GpuResourceKind.Renderbuffer: return currentRenderbuffer == id;
            case GpuResourceKind.VertexArray: return currentVao == id;
            case GpuResourceKind.Framebuffer: return currentReadFramebuffer == id || currentDrawFramebuffer == id || framebufferDrawRouting.ContainsKey(id);
            case GpuResourceKind.ProgramPipeline: return currentProgramPipeline == id || pipelineUniformPrograms.ContainsKey(id);
            case GpuResourceKind.TransformFeedback: return currentTransformFeedback == id || feedbackActivity.ContainsKey(id);
        }
        // A previously unseen native name has no realization proof. Its first selector outcome
        // remains initially unknown; later observed commands must preserve established knowledge.
        return false;
    }
    #endregion
}
