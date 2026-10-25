using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Publishes native executable facts after managed program mutation commands.</summary>
internal sealed partial class StateCache
{
    #region Internal API
    /// <summary>Preserves manager-owned acquisition for context-local wrappers without a registry entry.</summary>
    internal void LinkProgram(GpuProgramObject program)
    {
        program.PendingLink = null;
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
    internal void SetProgramUniformUnit(int program, int location, int unit)
    {
        // Authored helper calls can preserve stable canonical assignments. Exact external
        // adapters still forward native commands unchanged, including their error behavior.
        GpuResourceManagerSystem.CurrentManager?.ValidateProgramUnit(program, location, unit);
        if (GpuResourceManagerSystem.CurrentManager?.IsProgramUnitCurrent(program, location, unit) == true) return;
        var observation = BeginNativeProgramMetadata(program, true, true, location, 1, supplied: unit);
        bool returned = false;
        try
        {
            if (GpuSupport.Graphics.SupportsArbSeparateShaderObjects) GL.ProgramUniform1(program, location, unit);
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
