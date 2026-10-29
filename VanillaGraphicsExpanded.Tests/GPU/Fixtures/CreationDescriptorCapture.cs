using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Counts actual native descriptors and validity reads for fresh framebuffer/shader/program handoff.</summary>
internal sealed unsafe class CreationDescriptorCapture : IDisposable
{
    private readonly NativeDriverCapture native = new();

    /// <summary>Matches a scalar parameter output from a named object or bound framebuffer.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void Parameter(uint name, int parameter, int* value);
    /// <summary>Matches an interface or attachment parameter output.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void IndexedParameter(uint name, int index, int parameter, int* value);
    /// <summary>Matches a native GLboolean object-existence result.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate byte Exists(uint name);

    #region Internal API
    /// <summary>Counts all descriptor and validity outputs in the measured interval.</summary>
    internal int Reads { get; private set; }
    /// <summary>Retains attachment selections for focused partial-update assertions.</summary>
    internal HashSet<FramebufferAttachment> QueriedSlots { get; } = [];

    /// <summary>Decorates exact native slots so saved-original dispatch cannot bypass the oracle.</summary>
    internal CreationDescriptorCapture()
    {
        foreach (string symbol in new[] { "glGetShaderiv", "glGetProgramiv", "glGetFramebufferParameteriv", "glGetNamedFramebufferParameteriv" })
        {
            var original = (delegate* unmanaged[Stdcall]<uint, int, int*, void>)native.Original(symbol);
            native.Add(symbol, new Parameter((name, parameter, value) => { original(name, parameter, value); Reads++; }));
        }
        foreach (string symbol in new[] { "glGetProgramInterfaceiv", "glGetFramebufferAttachmentParameteriv", "glGetNamedFramebufferAttachmentParameteriv" })
        {
            var original = (delegate* unmanaged[Stdcall]<uint, int, int, int*, void>)native.Original(symbol);
            bool attachment = symbol != "glGetProgramInterfaceiv";
            native.Add(symbol, new IndexedParameter((name, index, parameter, value) =>
            {
                original(name, index, parameter, value);
                Reads++;
                if (attachment) QueriedSlots.Add((FramebufferAttachment)index);
            }));
        }
        foreach (string symbol in new[] { "glIsShader", "glIsProgram", "glIsFramebuffer" })
        {
            var original = (delegate* unmanaged[Stdcall]<uint, byte>)native.Original(symbol);
            native.Add(symbol, new Exists(name => { byte result = original(name); Reads++; return result; }));
        }
        native.Install();
    }

    /// <summary>Starts a new measured interval without reloading native tables.</summary>
    internal void Reset() { Reads = 0; QueriedSlots.Clear(); }
    #endregion

    #region Public API
    /// <summary>Restores ordinary dispatch after measuring creation and supplied metadata updates.</summary>
    public void Dispose() => native.Dispose();
    #endregion
}
