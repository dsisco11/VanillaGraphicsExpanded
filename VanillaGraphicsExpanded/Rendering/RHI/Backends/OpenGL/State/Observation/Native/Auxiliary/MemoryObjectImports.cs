using System.Diagnostics;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Observes memory imports without converting opaque handles into invented descriptor facts.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Preserves the original allocation and empty schema for every FD import outcome.</summary>
    [InterceptOpenGL("glImportMemoryFdEXT")]
    internal static void ObserveImportMemoryFdEXT(uint memory, ulong size, int type, int fd)
    {
        ObserveMemoryObjectSetup(memory);
    }

    /// <summary>Preserves caller-owned Win32 handles without retry, disposal or descriptor discovery.</summary>
    [InterceptOpenGL("glImportMemoryWin32HandleEXT")]
    [InterceptOpenGL("glImportMemoryWin32NameEXT")]
    internal static void ObserveImportMemoryWin32EXT(uint memory, ulong size, int type, nint handle)
    {
        ObserveMemoryObjectSetup(memory);
    }

    /// <summary>Leaves unrepresented memory parameters out of canonical metadata.</summary>
    [InterceptOpenGL("glMemoryObjectParameterivEXT")]
    internal static void ObserveMemoryObjectParameterivEXT(uint memory, int pname, int* value)
    {
        ObserveMemoryObjectSetup(memory);
    }
    #endregion

    #region Private
    /// <summary>Checks producer coverage without treating native return as payload certification.</summary>
    private static void ObserveMemoryObjectSetup(uint memory)
    {
        if (memory == 0 || !OpenGLObservationContext.TryGet(out _, out _, false)) return;
        var entry = GpuResourceManagerSystem.CurrentManager!.FindObservedResource(GpuResourceKind.MemoryObject, unchecked((int)memory));
        if (entry is not { IsRealized: true })
            Debug.WriteLine("Memory setup has no observed creation identity; the native input may be invalid.");
        // Failed and repeated imports do not replace the name or alter the empty metadata.
        // No payload-success or ownership assertion can be inferred from a handle argument.
    }
    #endregion
}
