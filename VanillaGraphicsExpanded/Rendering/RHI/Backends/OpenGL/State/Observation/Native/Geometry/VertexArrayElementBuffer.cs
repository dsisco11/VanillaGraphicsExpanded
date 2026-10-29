using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glVertexArrayElementBuffer after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glVertexArrayElementBuffer")]
    internal static void ObserveVertexArrayElementBuffer(uint a0, uint a1)
    {
        int @vaobj = unchecked((int)a0);
        int @buffer = unchecked((int)a1);
        if (!OpenGLObservationContext.TryGet(out _, out _, false)) return;
        if (!Current.PrepareVertexObservation(vaobj, true, false, buffer != 0 && GpuResourceManagerSystem.CurrentManager?.FindObservedResource(GpuResourceKind.Buffer, buffer) is GpuResourceMetadataEntry<GpuBufferMetadata> { IsRealized: false, PendingChange: GpuMetadataChange.None })) return;
        OpenGlStateCache.ObserveVertexMetadata(unchecked((int)@vaobj), true, true);
        if (buffer == 0 || Current.HasEstablishedNativeIdentity(GpuResourceKind.Buffer, buffer))
            Current.EstablishVertexArrayElementKnowledge(vaobj, buffer);
    }
    #endregion
}
