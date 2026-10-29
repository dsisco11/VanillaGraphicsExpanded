using System;
using OpenTK.Graphics.OpenGL;
namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
/// <summary>Executes optional EXT creation/import, using the pinned binding's available extension owner.</summary>
internal sealed partial class OpenGLBackend
{
    #region Public API
    /// <inheritdoc />
    public RHICreationResult<GpuSemaphore> CreateSemaphore(in RHISemaphoreDescription description)
    {
        RequireContext();
        if (!Capabilities.ExternalSemaphores) throw new NotSupportedException("External semaphores are unavailable.");
        var request = description;
        if (request.Import is { } handle) ValidateExternalHandle(handle, false);
        var owner = new GpuSemaphore(request.Import.HasValue);
        try
        {
            if (!RHIResourceManager.CreateSemaphore(new(), owner, out _).HasValue)
            { owner.Dispose(); return new(null, "Semaphore creation failed."); }
            if (request.Import is { } imported) ImportSemaphore(owner, imported);
            RequireContext();
            return new(owner, null);
        }
        catch { try { owner.Dispose(); } catch { } throw; }
    }
    /// <inheritdoc />
    public RHICreationResult<GpuMemoryObject> CreateMemoryObject(in RHIMemoryObjectDescription description)
    {
        RequireContext();
        if (!Capabilities.ExternalMemory) throw new NotSupportedException("External memory is unavailable.");
        var request = description;
        if (request.Import is { } handle)
        {
            ValidateExternalHandle(handle, true);
            if (request.Size == 0 || request.Size > long.MaxValue) throw new ArgumentOutOfRangeException(nameof(description));
        }
        else if (request.Size != 0) throw new ArgumentException("A memory size requires an import.");
        var owner = new GpuMemoryObject();
        try
        {
            if (!RHIResourceManager.CreateMemoryObject(new(), owner, out _).HasValue)
            { owner.Dispose(); return new(null, "Memory object creation failed."); }
            if (request.Dedicated is { } dedicated)
            {
                int value = dedicated ? 1 : 0;
                OpenGLDriverCalls.Ext.MemoryObjectParameter(owner.MemoryObjectId, MemoryObjectParameterName.DedicatedMemoryObjectExt, ref value);
                RequireContext();
            }
            if (request.Import is { } imported) ImportMemory(owner, request.Size, imported);
            RequireContext();
            return new(owner, null);
        }
        catch { try { owner.Dispose(); } catch { } throw; }
    }
    #endregion
    #region Internal API
    /// <summary>Creates one extension semaphore for private manager handoff.</summary>
    public GpuSemaphoreMetadata? InitializeSemaphore(out nint id)
    {
        RequireContext();
        if (!Capabilities.ExternalSemaphores) throw new NotSupportedException("External semaphores are unavailable.");
        int name = 0;
        try { OpenGLDriverCalls.Ext.GenSemaphores(1, out name); }
        finally { id = name; }
        RequireContext();
        return id == 0 ? null : new GpuSemaphoreMetadata();
    }
    /// <summary>Creates one extension memory object for private manager handoff.</summary>
    public GpuMemoryObjectMetadata? InitializeMemoryObject(out nint id)
    {
        RequireContext();
        if (!Capabilities.ExternalMemory) throw new NotSupportedException("External memory is unavailable.");
        int name = 0;
        try { OpenGLDriverCalls.Ext.CreateMemoryObjects(1, out name); }
        finally { id = name; }
        RequireContext();
        return name == 0 ? null : new GpuMemoryObjectMetadata();
    }
    #endregion
    #region Private
    /// <summary>Rejects unavailable platform imports or mismatched ownership before native allocation.</summary>
    private void ValidateExternalHandle(RHIExternalHandle handle, bool memory)
    {
        bool fd = handle.Kind == ExternalHandleKind.OpaqueFileDescriptor;
        if (!Enum.IsDefined(handle.Kind) || handle.Value == -1 || (fd ? handle.Value < 0 || handle.Value > int.MaxValue : handle.Value == 0)
            || handle.Ownership != (fd ? ExternalHandleOwnership.TransferToDriver : ExternalHandleOwnership.RetainByCaller))
            throw new ArgumentException("Invalid platform handle or import ownership.");
        bool supported = fd ? (memory ? NativeCapabilities.SupportsExtMemoryObjectFd : NativeCapabilities.SupportsExtSemaphoreFd)
            : (memory ? Capabilities.ExternalMemoryWin32 : Capabilities.ExternalSemaphoresWin32);
        if (!supported || fd && OperatingSystem.IsWindows() || !fd && !OperatingSystem.IsWindows())
            throw new NotSupportedException("The requested platform import is unavailable.");
    }
    /// <summary>Imports once without closing caller-owned Win32 handles or retrying failed dispatch.</summary>
    private void ImportSemaphore(GpuSemaphore owner, RHIExternalHandle handle)
    {
        if (handle.Kind == ExternalHandleKind.OpaqueFileDescriptor)
            OpenGLDriverCalls.Ext.ImportSemaphoreF(owner.SemaphoreId, ExternalHandleType.HandleTypeOpaqueFdExt, (int)handle.Value);
        else OpenGLDriverCalls.Ext.ImportSemaphoreWin32Handle(owner.SemaphoreId, handle.Kind == ExternalHandleKind.OpaqueWin32
            ? ExternalHandleType.HandleTypeOpaqueWin32Ext : ExternalHandleType.HandleTypeOpaqueWin32KmtExt, handle.Value);
        RequireContext();
        // This targeted lifecycle outcome is necessary because an opaque external handle
        // and a void return cannot certify a payload for subsequent wait/signal use.
        if (!OpenGLDriverCalls.Ext.IsSemaphore(owner.SemaphoreId))
            throw new InvalidOperationException("Semaphore payload import did not establish a usable object.");
        RequireContext();
        owner.ResourceManager?.ObserveSemaphoreRealization(owner.ResourceId);
    }
    /// <summary>Imports the explicit memory size once under the selected ownership contract.</summary>
    private void ImportMemory(GpuMemoryObject owner, ulong size, RHIExternalHandle handle)
    {
        if (handle.Kind == ExternalHandleKind.OpaqueFileDescriptor)
            OpenGLDriverCalls.Ext.ImportMemoryF(owner.MemoryObjectId, (long)size, ExternalHandleType.HandleTypeOpaqueFdExt, (int)handle.Value);
        else OpenGLDriverCalls.Ext.ImportMemoryWin32Handle(owner.MemoryObjectId, (long)size, handle.Kind == ExternalHandleKind.OpaqueWin32
            ? ExternalHandleType.HandleTypeOpaqueWin32Ext : ExternalHandleType.HandleTypeOpaqueWin32KmtExt, handle.Value);
        RequireContext();
    }
    #endregion
}
