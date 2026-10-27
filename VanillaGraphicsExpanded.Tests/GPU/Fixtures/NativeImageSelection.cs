using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Isolates one explicitly native fixture image selector; production shaders use captured RHI tables.</summary>
internal sealed class NativeImageSelection : IDisposable
{
    private readonly int unit;
    private readonly StateCache.ImageBinding previous;
    private readonly (nint Handle, long Generation) origin;
    #region Public API
    /// <summary>Preserves native fixture state before selecting a certified managed texture image.</summary>
    internal NativeImageSelection(int unit, GpuTextureRef texture, TextureAccess access = TextureAccess.ReadOnly,
        int level = 0, bool layered = false, int layer = 0, SizedInternalFormat? formatOverride = null)
    {
        var metadata = texture.RequireMetadata();
        this.unit = unit;
        origin = GpuResource.CaptureCreationContext(GpuResourceKind.Texture);
        int Read(All field) { GL.GetInteger((GetIndexedPName)field, unit, out int value); return value; }
        previous = new(Read(All.ImageBindingName), Read(All.ImageBindingLevel), Read(All.ImageBindingLayered) != 0,
            Read(All.ImageBindingLayer), (TextureAccess)Read(All.ImageBindingAccess), (SizedInternalFormat)Read(All.ImageBindingFormat));
        var format = formatOverride ?? (metadata.Buffer?.Format ?? (SizedInternalFormat)texture.RequireImage(level).Format);
        OpenGLImageSlotBindings.BindImageTexture(StateCache.Current, unit, texture.TextureId, level, layered, layer, access, format);
    }
    /// <summary>Restores exact native setup only in the originating fixture namespace.</summary>
    public void Dispose()
    {
        if (!GpuResource.IsCurrentResourceContext(origin)) throw new InvalidOperationException("Native fixture context changed.");
        OpenGLImageSlotBindings.BindImageTexture(StateCache.Current, unit, previous.Texture, previous.Level,
            previous.Layered, previous.Layer, previous.Access, previous.Format);
    }
    #endregion
}
