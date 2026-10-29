using VanillaGraphicsExpanded.Rendering.Integration.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Creates view objects with explicit original source provenance.</summary>
internal sealed partial class OpenGLBackend
{
    #region Public API
    /// <inheritdoc />
    public RHICreationResult<GpuTextureView> CreateTextureView(in RHITextureViewDescription description)
    {
        var request = description;
        RequireContext();
        for (int field = 0; field < 7; field++)
            if (!request.Has(field)) throw new ArgumentException("Source, representation and complete mip/layer selection are required.");
        var source = request.Source.Resource ?? throw new ArgumentException("A texture source is required.");
        source.RequireOriginalContext();
        var facts = request.Source.RequireMetadata();
        var initialization = new GpuTextureViewInitialization
        {
            Source = (int)source.ResourceId, Target = request.Dimension,
            Format = request.Format, BaseLevel = request.BaseMip, LevelCount = request.MipCount,
            BaseLayer = request.BaseLayer, LayerCount = request.LayerCount
        };
        var owner = new GpuTextureView((int)source.ResourceId);
        try
        {
            if (!RHIResourceManager.CreateTextureView(initialization, owner, out _, source).HasValue)
                return new(null, "Texture view initialization did not complete.");
            RHIResourceManager.EstablishTextureViewSource(owner, source);
            _ = RHIResourceManager.RequireTextureReferenceMetadata(owner);
            owner.SetDebugName(request.Label);
            return new(owner, null);
        }
        catch { try { owner.Dispose(); } catch { } throw; }
    }
    #endregion
}
