using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Counts native image descriptor reads through both intercepted and saved-original dispatch.</summary>
internal sealed unsafe class ImageDescriptorCapture : IDisposable
{
    private readonly NativeDriverCapture native = new();

    /// <summary>Matches a named texture parameter getter.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void TextureParameter(uint name, int parameter, int* value);
    /// <summary>Matches a named or bound image-level getter.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void TextureLevel(uint name, int level, int parameter, int* value);
    /// <summary>Matches a named framebuffer attachment getter.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void Attachment(uint name, int attachment, int parameter, int* value);

    #region Internal API
    internal int Reads { get; private set; }
    internal int SampleReads { get; private set; }
    internal int FormatReads { get; private set; }

    /// <summary>Installs native getter counters and optional deterministic operations in the same binding reload.</summary>
    internal ImageDescriptorCapture(Action<NativeDriverCapture>? configure = null)
    {
        foreach (string symbol in new[] { "glGetTextureParameteriv", "glGetTexParameteriv", "glGetNamedRenderbufferParameteriv", "glGetRenderbufferParameteriv", "glGetNamedFramebufferParameteriv", "glGetFramebufferParameteriv" })
        {
            var original = (delegate* unmanaged[Stdcall]<uint, int, int*, void>)native.Original(symbol);
            native.Add(symbol, new TextureParameter((name, parameter, value) =>
            {
                original(name, parameter, value);
                Reads++;
                if (parameter == (int)RenderbufferParameterName.RenderbufferSamples) SampleReads++;
            }));
        }
        foreach (string symbol in new[] { "glGetTextureLevelParameteriv", "glGetTexLevelParameteriv" })
        {
            var original = (delegate* unmanaged[Stdcall]<uint, int, int, int*, void>)native.Original(symbol);
            native.Add(symbol, new TextureLevel((name, level, parameter, value) =>
            {
                original(name, level, parameter, value);
                Reads++;
                if (parameter == (int)GetTextureParameter.TextureSamples) SampleReads++;
                if (parameter == (int)GetTextureParameter.TextureInternalFormat) FormatReads++;
            }));
        }
        foreach (string symbol in new[] { "glGetNamedFramebufferAttachmentParameteriv", "glGetFramebufferAttachmentParameteriv" })
        {
            var original = (delegate* unmanaged[Stdcall]<uint, int, int, int*, void>)native.Original(symbol);
            native.Add(symbol, new Attachment((name, attachment, parameter, value) =>
            {
                original(name, attachment, parameter, value);
                Reads++;
            }));
        }
        configure?.Invoke(native);
        native.Install();
    }
    #endregion

    #region Public API
    /// <summary>Restores loader state after all descriptor assertions.</summary>
    public void Dispose() => native.Dispose();
    #endregion
}
