using System;
using OpenTK.Graphics.OpenGL;
namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
/// <summary>Executes explicit external waits/signals after complete resource and layout validation.</summary>
internal sealed partial class OpenGLBackend
{
    #region Public API
    /// <inheritdoc />
    public void WaitSemaphore(GpuSemaphoreRef semaphore, ReadOnlySpan<GpuBufferRef> buffers, ReadOnlySpan<RHIExternalTextureTransition> textures)
    {
        var owner = RequireSemaphore(semaphore);
        var names = ExternalTransitions(buffers, textures);
        GL.Ext.WaitSemaphore(owner.SemaphoreId, names.Buffers.Length, names.Buffers, names.Textures.Length, names.Textures, names.Layouts);
        RequireContext();
    }
    /// <inheritdoc />
    public void SignalSemaphore(GpuSemaphoreRef semaphore, ReadOnlySpan<GpuBufferRef> buffers, ReadOnlySpan<RHIExternalTextureTransition> textures)
    {
        var owner = RequireSemaphore(semaphore);
        var names = ExternalTransitions(buffers, textures);
        GL.Ext.SignalSemaphore(owner.SemaphoreId, names.Buffers.Length, names.Buffers, names.Textures.Length, names.Textures, names.Layouts);
        RequireContext();
    }
    #endregion
    #region Private
    /// <summary>Rejects unsupported or retired semaphore use before touching any native inputs.</summary>
    private GpuSemaphore RequireSemaphore(GpuSemaphoreRef reference)
    {
        RequireContext();
        if (!Capabilities.ExternalSemaphores) throw new NotSupportedException("External semaphores are unavailable.");
        var owner = reference.RequireResource(); owner.RequireOriginalContext();
        if (!owner.IsValid) throw new InvalidOperationException("The semaphore has retired.");
        cache.RequireResourceOperationAvailable(GpuResourceKind.Semaphore, owner.ResourceId);
        return owner;
    }
    /// <summary>Freezes the complete native argument set only after validating each original resource.</summary>
    private (int[] Buffers, int[] Textures, TextureLayout[] Layouts) ExternalTransitions(ReadOnlySpan<GpuBufferRef> buffers, ReadOnlySpan<RHIExternalTextureTransition> textures)
    {
        int[] bufferNames = new int[buffers.Length], textureNames = new int[textures.Length];
        var layouts = new TextureLayout[textures.Length];
        for (int i = 0; i < buffers.Length; i++) bufferNames[i] = (int)RequireRange(buffers[i], 0, 0).ResourceId;
        for (int i = 0; i < textures.Length; i++)
        {
            _ = textures[i].Texture.RequireMetadata();
            var resource = textures[i].Texture.Resource!; resource.RequireOriginalContext();
            textureNames[i] = (int)resource.ResourceId;
            layouts[i] = textures[i].Layout switch
            {
                RHIExternalTextureLayout.General => TextureLayout.LayoutGeneralExt,
                RHIExternalTextureLayout.ColorAttachment => TextureLayout.LayoutColorAttachmentExt,
                RHIExternalTextureLayout.DepthStencilAttachment => TextureLayout.LayoutDepthStencilAttachmentExt,
                RHIExternalTextureLayout.DepthStencilReadOnly => TextureLayout.LayoutDepthStencilReadOnlyExt,
                RHIExternalTextureLayout.ShaderReadOnly => TextureLayout.LayoutShaderReadOnlyExt,
                RHIExternalTextureLayout.TransferSource => TextureLayout.LayoutTransferSrcExt,
                RHIExternalTextureLayout.TransferDestination => TextureLayout.LayoutTransferDstExt,
                RHIExternalTextureLayout.DepthReadOnlyStencilAttachment => TextureLayout.LayoutDepthReadOnlyStencilAttachmentExt,
                RHIExternalTextureLayout.DepthAttachmentStencilReadOnly => TextureLayout.LayoutDepthAttachmentStencilReadOnlyExt,
                _ => throw new ArgumentOutOfRangeException(nameof(textures))
            };
        }
        return (bufferNames, textureNames, layouts);
    }
    #endregion
}
