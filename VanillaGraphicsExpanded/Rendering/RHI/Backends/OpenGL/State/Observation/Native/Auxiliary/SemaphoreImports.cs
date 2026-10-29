using System.Diagnostics;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Preserves original semaphore reservations without guessing external payload acceptance.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Observes FD setup without closing, retaining or reading the caller's handle.</summary>
    [InterceptOpenGL("glImportSemaphoreFdEXT")]
    internal static void ObserveImportSemaphoreFdEXT(uint semaphore, int type, int fd)
    {
        ObserveSemaphoreImport(semaphore);
    }

    /// <summary>Observes Win32 handle and name imports without changing caller ownership.</summary>
    [InterceptOpenGL("glImportSemaphoreWin32HandleEXT")]
    [InterceptOpenGL("glImportSemaphoreWin32NameEXT")]
    internal static void ObserveImportSemaphoreWin32EXT(uint semaphore, int type, nint handle)
    {
        ObserveSemaphoreImport(semaphore);
    }
    #endregion

    #region Private
    /// <summary>Diagnoses a missing producer while leaving an opaque import outcome uncertified.</summary>
    private static void ObserveSemaphoreImport(uint semaphore)
    {
        if (semaphore == 0 || !OpenGLObservationContext.TryGet(out _, out _, false)) return;
        if (GpuResourceManagerSystem.CurrentManager!.FindObservedResource(GpuResourceKind.Semaphore,
            unchecked((int)semaphore)) is null)
            Debug.WriteLine("Semaphore import has no observed generation identity; the native input may be invalid.");
        // The schema is empty. Failure or repeated setup cannot replace an allocation;
        // only an explicit positive lifecycle result can establish imported usability.
    }
    #endregion
}
