using System;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Owns synchronous backend vertex configuration and its publication or failure withdrawal.</summary>
internal static class OpenGLVertexConfiguration
{
    #region Internal API
    /// <summary>Spans real native setup, preserving the original identity and excluding partial acquisition until publication.</summary>
    internal static void Execute(OpenGlStateCache cache, int vao, bool elementBinding, Action apply, int? elementBuffer = null)
    {
        var origin = Integration.RenderContextRegistry.CaptureResourceContext();
        using var operation = cache.EnterResourceOperation(new(GpuResourceKind.VertexArray, vao));
        var manager = RHIResourceManagerSystem.CurrentManager;
        var entry = manager?.FindObservedResource(GpuResourceKind.VertexArray, vao)
            as GpuResourceMetadataEntry<GpuVertexArrayMetadata>;
        cache.InvalidateVertexConfiguration(vao);
        if (elementBinding) cache.ForgetVertexArrayElementKnowledge(vao);
        if (entry is not null) { entry.OperationInProgress = true; entry.BeginRefresh(GpuMetadataChange.Metadata); }
        using var delivery = manager?.BeginChangeDelivery();
        bool committed = false;
        try
        {
            apply();
            if (operation.Cancelled || !cache.OwnsKnowledgeFor(origin) || !Integration.RenderContextRegistry.IsCurrentResourceContext(origin))
                throw new InvalidOperationException("Vertex configuration was interrupted or changed context.");
            if (entry is not null)
            {
                entry.OperationInProgress = false;
                if (!manager!.IsRegistered(entry) || entry.State != GpuResourceRegistrationState.Live)
                    throw new InvalidOperationException("Vertex configuration identity was retired.");
                manager.PublishMetadata(entry, default(GpuVertexArrayMetadata), GpuMetadataChange.Metadata, operation: operation);
                committed = true;
                delivery?.Complete();
            }
            // Complete backend binding knowledge before the delivery scope invokes consumer code.
            if (!cache.OwnsKnowledgeFor(origin) || !Integration.RenderContextRegistry.IsCurrentResourceContext(origin)
                || entry is not null && (!manager!.IsRegistered(entry) || entry.State != GpuResourceRegistrationState.Live))
                throw new InvalidOperationException("Vertex configuration publication lost its original identity or context.");
            if (elementBuffer.HasValue) cache.EstablishVertexArrayElementKnowledge(vao, elementBuffer.Value);
        }
        finally
        {
            // An unsuccessful operation leaves the already withdrawn entry and helper configuration unavailable.
            if (entry is not null) { entry.OperationInProgress = false; if (!committed) entry.Withdraw(); }
        }
    }
    #endregion
}
