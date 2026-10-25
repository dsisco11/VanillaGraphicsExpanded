using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Installs cached executables and publishes their driver-produced interface to the owning wrapper.</summary>
internal sealed partial class StateCache
{
    #region Internal API
    /// <summary>Loads a managed candidate and acquires its executable result once before preparation or transfer.</summary>
    internal bool ProgramBinary(GpuProgramObject program, BinaryFormat format, nint data, int length)
    {
        const GpuMetadataChange change = GpuMetadataChange.Metadata | GpuMetadataChange.Executable;
        program.PendingLink = null;
        bool local = program.ResourceManager?.GetResourceRegistration(program) is null;
        using var operation = new GpuResourceManager.MetadataOperation<GpuProgramMetadata>(program, change);
        // The wrapper is retained even without a registry. Raw-ID observation cannot deliver
        // the newly linked snapshot to that local owner, so this operation owns both steps.
        if (local) program.OnNativeProgramMetadataChanged(change, available: false);
        GL.ProgramBinary(program.ProgramId, format, data, length);
        if (!GpuProgramMetadataReader.Instance.TryRead(program.ProgramId, out var candidate))
            throw new InvalidOperationException("Program metadata is unavailable after binary installation.");
        operation.Commit(candidate);
        if (local) program.OnNativeProgramMetadataChanged(change, available: true);
        return candidate.ExecutableAvailable;
    }
    #endregion
}
