using System;
using System.Collections.Generic;
using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Coordinates asset preparation, CPU binding capture and retirement without owning command execution.</summary>
internal interface IGpuProgram : IShaderSubmissionTarget, IDisposable
{
    #region Public API
    #region Owner contract
    /// <summary>Gets the existing asset lifetime and capture guards.</summary>
    GpuProgramLifetime Lifetime { get; }
    /// <summary>Identifies a successful installation independently of the native name.</summary>
    ulong ExecutableRevision { get; }
    /// <summary>Identifies the native context which owns the installed executable.</summary>
    (nint Handle, long Generation) ExecutableContext { get; }
    /// <summary>Reports whether requested assets need installation.</summary>
    bool RequiresPreparation { get; }
    /// <summary>Prepares the owner's fixed executable selection without binding resources or dispatching work.</summary>
    bool PrepareExecutable();
    /// <summary>Freezes desired inputs for the exact installed executable without native inspection or allocation.</summary>
    RHIResourceBindings CaptureBindings();
    /// <summary>Releases the singular executable owner through existing retirement authority.</summary>
    void ReleaseExecutable();
    /// <summary>Releases extra asset-owned resources after retirement closes admission.</summary>
    void ReleaseResources();
    #endregion
    #region Preparation and retirement
    /// <summary>Checks CPU admission before family-specific executable preparation.</summary>
    bool Prepare()
    {
        Lifetime.RequireOutsidePublication();
        if (Lifetime.IsRetired || !Lifetime.IsSubmissionAdmitted) return false;
        return PrepareExecutable();
    }
    /// <summary>Closes lifetime once and attempts each independent resource cleanup while preserving all failures.</summary>
    void Retire()
    {
        if (Lifetime.IsRetired) return;
        Lifetime.RequireMutation(); Lifetime.RequireUnborrowed(); Lifetime.IsRetired = true;
        List<Exception>? failures = null;
        foreach (var buffer in Lifetime.OwnedUniforms)
            try { buffer.Dispose(); }
            catch (Exception failure) { (failures ??= new()).Add(failure); }
        Lifetime.OwnedUniforms.Clear();
        try { ReleaseResources(); }
        catch (Exception failure) { (failures ??= new()).Add(failure); }
        try { ReleaseExecutable(); }
        catch (Exception failure) { (failures ??= new()).Add(failure); }
        if (failures is not null) throw new AggregateException(failures);
    }
    /// <summary>Adopts a CPU block and guards its writes and future captures with this same logical asset lifetime.</summary>
    T RegisterUniform<T>(T buffer) where T : CpuUniformBuffer
    {
        ArgumentNullException.ThrowIfNull(buffer); Lifetime.RequireMutation();
        if (!Lifetime.OwnedUniforms.Contains(buffer)) Lifetime.OwnedUniforms.Add(buffer);
        buffer.SetWriteGuard(Lifetime.RequireMutation); buffer.SetCaptureGuard(Lifetime.RequireLive);
        return buffer;
    }
    #endregion
    #endregion
}
