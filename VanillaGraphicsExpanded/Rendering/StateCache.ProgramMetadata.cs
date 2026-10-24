using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Publishes native executable facts after managed program mutation commands.</summary>
internal sealed partial class StateCache
{
    #region Internal API
    /// <summary>Submits linking without completion queries; the candidate coordinator owns the later validation boundary.</summary>
    internal void SubmitProgramLink(GpuProgramObject program)
    {
        _ = BeginNativeProgramMetadata(program.ProgramId, true, false, -1, 1, executable: true);
        if (program.ResourceManager?.GetResourceRegistration(program) is null)
            program.OnNativeProgramMetadataChanged(GpuMetadataChange.Executable, available: false);
        GL.LinkProgram(program.ProgramId);
    }

    /// <summary>Acquires executable facts only after a caller has reached its link-completion dependency.</summary>
    internal void CompleteProgramLink(int program)
    {
        var manager = GpuResourceManagerSystem.CurrentManager;
        if (manager?.FindObservedResource(GpuResourceKind.Program, program)
            is not GpuResourceMetadataEntry<GpuProgramMetadata> entry) return;
        if (!manager.RefreshMetadata(entry, GpuMetadataChange.Metadata | GpuMetadataChange.Executable))
            throw new System.InvalidOperationException("Program metadata is unavailable after link completion.");
    }
    /// <summary>Preserves manager-owned acquisition for context-local wrappers without a registry entry.</summary>
    internal void LinkProgram(GpuProgramObject program)
    {
        bool tracked = program.ResourceManager?.GetResourceRegistration(program) is not null;
        if (tracked)
        {
            LinkProgram(program.ProgramId);
            return;
        }
        const GpuMetadataChange change = GpuMetadataChange.Metadata | GpuMetadataChange.Executable;
        program.OnNativeProgramMetadataChanged(change, available: false);
        try
        {
            LinkProgram(program.ProgramId);
            GpuResourceManager.ObserveWrapperMetadata(program, GpuProgramMetadataReader.Instance, change);
            program.OnNativeProgramMetadataChanged(change, available: true);
        }
        catch
        {
            GpuResourceManager.DiscardUntrackedMetadata(program, change);
            program.OnNativeProgramMetadataChanged(change, available: false);
            throw;
        }
    }

    /// <summary>Relinks a managed program and refreshes actual interface facts even when linking fails.</summary>
    internal void LinkProgram(int program)
    {
        var observation = BeginNativeProgramMetadata(program, true, false, -1, 1, executable: true);
        bool returned = false;
        try { GL.LinkProgram(program); returned = true; }
        finally { observation.Complete(returned); }
    }

    /// <summary>Assigns an explicit program's integer uniform without changing its active-program selection when supported.</summary>
    internal void SetProgramUniformUnit(int program, int location, int unit, bool direct)
    {
        // Authored helper calls can preserve stable canonical assignments. Exact external
        // adapters still forward native commands unchanged, including their error behavior.
        if (GpuResourceManagerSystem.CurrentManager?.IsProgramUnitCurrent(program, location, unit) == true) return;
        var observation = BeginNativeProgramMetadata(program, true, true, location, 1);
        bool returned = false;
        try
        {
            if (direct) GL.ProgramUniform1(program, location, unit);
            else
            {
                using var scope = UseProgramScope(program);
                GL.Uniform1(location, unit);
            }
            returned = true;
        }
        finally { observation.Complete(returned); }
    }
    #endregion
}
