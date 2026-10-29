using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Models EXT bind-to-create semantics using real core objects and the production callback boundary.</summary>
internal sealed unsafe class ImplicitImageDriver : IDisposable
{
    private readonly ImageDescriptorCapture descriptors;

    #region Internal API
    internal int Calls { get; private set; }
    internal int Reads => descriptors.Reads;

    /// <summary>Installs deterministic EXT operations alongside native descriptor counters.</summary>
    internal ImplicitImageDriver()
    {
        // Backend admission can reload OpenTK bindings. Finish that setup before
        // installing decorators so the first reader is counted like subsequent readers.
        _ = RHICommandContext.Current.Capabilities;
        descriptors = new ImageDescriptorCapture(Configure);
    }
    #endregion

    #region Public API
    /// <summary>Restores original driver bindings.</summary>
    public void Dispose() => descriptors.Dispose();
    #endregion

    #region Private
    /// <summary>Matches the native texture parameter ABI.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void TextureParameter(uint texture, int target, int parameter, int value);

    /// <summary>Matches the native named renderbuffer storage ABI.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void RenderbufferStorage(uint renderbuffer, int format, int width, int height);

    /// <summary>Matches the native named framebuffer status ABI.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int FramebufferStatus(uint framebuffer, int target);

    /// <summary>Matches the native whole-buffer texture association ABI.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void BufferTexture(uint texture, int target, int format, uint buffer);

    /// <summary>Matches the native ranged buffer texture association ABI.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void BufferTextureRange(uint texture, int target, int format, uint buffer, nint offset, nint size);

    /// <summary>Preserves incoming bindings while modeling implicit realization through saved driver entrypoints.</summary>
    private void Configure(NativeDriverCapture native)
    {
        var get = (delegate* unmanaged[Stdcall]<int, int*, void>)native.Original("glGetIntegerv");
        var bindTexture = (delegate* unmanaged[Stdcall]<int, uint, void>)native.Original("glBindTexture");
        var parameter = (delegate* unmanaged[Stdcall]<int, int, int, void>)native.Original("glTexParameteri");
        native.Add("glTextureParameteriEXT", new TextureParameter((texture, target, name, value) =>
        {
            Calls++;
            int previous = 0;
            get(target == (int)TextureTarget.Texture3D ? (int)GetPName.TextureBinding3D : (int)GetPName.TextureBinding2D, &previous);
            bindTexture(target, texture);
            parameter(target, name, value);
            bindTexture(target, unchecked((uint)previous));
        }));

        var bufferTexture = (delegate* unmanaged[Stdcall]<int, int, uint, void>)native.Original("glTexBuffer");
        var bufferTextureRange = (delegate* unmanaged[Stdcall]<int, int, uint, nint, nint, void>)native.Original("glTexBufferRange");
        native.Add("glTextureBufferEXT", new BufferTexture((texture, target, format, buffer) =>
        {
            Calls++;
            int previous = 0;
            get((int)GetPName.TextureBindingBuffer, &previous);
            bindTexture(target, texture);
            bufferTexture(target, format, buffer);
            bindTexture(target, unchecked((uint)previous));
        }));
        native.Add("glTextureBufferRangeEXT", new BufferTextureRange((texture, target, format, buffer, offset, size) =>
        {
            Calls++;
            int previous = 0;
            get((int)GetPName.TextureBindingBuffer, &previous);
            bindTexture(target, texture);
            bufferTextureRange(target, format, buffer, offset, size);
            bindTexture(target, unchecked((uint)previous));
        }));

        var bindRenderbuffer = (delegate* unmanaged[Stdcall]<int, uint, void>)native.Original("glBindRenderbuffer");
        var storage = (delegate* unmanaged[Stdcall]<int, int, int, int, void>)native.Original("glRenderbufferStorage");
        native.Add("glNamedRenderbufferStorageEXT", new RenderbufferStorage((renderbuffer, format, width, height) =>
        {
            Calls++;
            int previous = 0;
            get((int)GetPName.RenderbufferBinding, &previous);
            bindRenderbuffer((int)RenderbufferTarget.Renderbuffer, renderbuffer);
            storage((int)RenderbufferTarget.Renderbuffer, format, width, height);
            bindRenderbuffer((int)RenderbufferTarget.Renderbuffer, unchecked((uint)previous));
        }));

        var bindFramebuffer = (delegate* unmanaged[Stdcall]<int, uint, void>)native.Original("glBindFramebuffer");
        var status = (delegate* unmanaged[Stdcall]<int, int>)native.Original("glCheckFramebufferStatus");
        native.Add("glCheckNamedFramebufferStatusEXT", new FramebufferStatus((framebuffer, target) =>
        {
            Calls++;
            int previous = 0;
            get((int)GetPName.DrawFramebufferBinding, &previous);
            bindFramebuffer((int)FramebufferTarget.DrawFramebuffer, framebuffer);
            int result = status((int)FramebufferTarget.DrawFramebuffer);
            bindFramebuffer((int)FramebufferTarget.DrawFramebuffer, unchecked((uint)previous));
            return result;
        }));
    }
    #endregion
}
