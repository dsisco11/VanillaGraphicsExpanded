using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glBindVertexArray after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glBindVertexArray")]
    internal static void ObserveBindVertexArray(uint a0)
    {
        int @array = unchecked((int)a0);
        if (!OpenGLObservationContext.TryGet(out _, out _, false)) return;
        if (RHIResourceManagerSystem.CurrentManager!.FindObservedResource(GpuResourceKind.VertexArray, array)
            is GpuResourceMetadataEntry<GpuVertexArrayMetadata> { IsRealized: false, InitialDiscovery: GpuInitialDiscovery.None, PendingChange: GpuMetadataChange.None } entry)
        {
            entry.Manager.PublishMetadata(entry, new GpuVertexArrayMetadata(), GpuMetadataChange.None);
            Current.EstablishVertexArrayElementKnowledge(array, 0);
        }
        OpenGlStateCache.ObserveVertexSelection(unchecked((int)@array));
    }
    #endregion
}
