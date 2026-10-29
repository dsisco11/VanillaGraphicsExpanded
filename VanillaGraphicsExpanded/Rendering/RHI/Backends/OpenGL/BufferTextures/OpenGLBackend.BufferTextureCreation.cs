using VanillaGraphicsExpanded.Rendering.Integration.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Creates a completed existing owner through manager identity and snapshot handoff.</summary>
internal sealed partial class OpenGLBackend
{
    #region Public API
    /// <inheritdoc />
    public RHICreationResult<GpuBufferTexture> CreateBufferTexture(in RHIBufferTextureDescription description)
    {
        var request = description;
        RequireContext();
        if (!request.Has(0) || !request.Has(1) || !request.Has(2)) throw new ArgumentException("Backing buffer, format and whole/range intent are required.");
        var buffer = request.Buffer.RequireResource();
        _ = RHIResourceManager.RequireBufferReferenceMetadata(buffer);
        var initialization = new GpuBufferTextureInitialization { Buffer = (int)buffer.ResourceId,
            Format = request.Format, Mode = request.WholeBuffer ? GpuBufferTextureMode.Whole : GpuBufferTextureMode.Range };
        if (!request.WholeBuffer)
        {
            if (!request.Has(3) || !request.Has(4)) throw new ArgumentException("A ranged view requires an explicit offset and length.");
            initialization.Offset = request.Offset; initialization.Size = request.Length;
        }
        else if (request.Has(3) || request.Has(4)) throw new ArgumentException("A whole-buffer view has no offset or length.");
        var owner = new GpuBufferTexture();
        try
        {
            if (!RHIResourceManager.CreateBufferTexture(initialization, owner, out _, buffer).HasValue) return new(null, "Buffer texture initialization did not complete.");
            _ = RHIResourceManager.RequireBufferReferenceMetadata(buffer);
            RHIResourceManager.EstablishBufferTextureSource(owner, buffer);
            _ = RHIResourceManager.RequireTextureReferenceMetadata(owner);
            owner.SetDebugName(request.Label);
            return new(owner, null);
        }
        catch { try { owner.Dispose(); } catch { } throw; }
    }
    #endregion
}
