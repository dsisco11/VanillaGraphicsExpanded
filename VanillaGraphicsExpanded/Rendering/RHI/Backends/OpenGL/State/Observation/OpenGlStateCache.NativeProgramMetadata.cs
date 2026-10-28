using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Forwards observed program changes to the existing canonical registration and its aliases.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Reconciles a completed native mutation and releases metadata bookkeeping on every exit.</summary>
    internal static void ObserveProgramMetadata(int program, bool named, bool unit, int location, int count,
        bool executable = false, bool unsupportedUnits = false, ProgramInterface block = 0, int blockIndex = -1, int? supplied = null)
    {
        var manager = GpuResourceManagerSystem.CurrentManager;
        if (manager?.CanObserveNativeMetadata != true) return;
        if (unit && (location < 0 || count <= 0)) return;
        if (!named)
        {
            program = Current.GetCurrentProgram();
            // Uniform commands use the active program of a bound pipeline when UseProgram is zero.
            if (program == 0)
            {
                int pipeline = Current.GetCurrentProgramPipeline();
                if (pipeline != 0) program = Current.GetPipelineUniformProgram(pipeline);
            }
        }
        Current.InterruptNativeResourceOperation(GpuResourceKind.Program, program);
        var entry = manager.FindObservedResource(GpuResourceKind.Program, program)
            as GpuResourceMetadataEntry<GpuProgramMetadata>;
        if (entry is null) return;
        if (InterruptNativeMetadataCompletion(entry, GpuMetadataChange.Metadata | (executable ? GpuMetadataChange.Executable : GpuMetadataChange.None))) return;
        bool known = entry.IsUsable;
        if (unit)
        {
            bool represented = false;
            if (!entry.Metadata.TextureUnits.IsDefault)
                foreach (var binding in entry.Metadata.TextureUnits)
                    if (binding.Location >= location && (long)binding.Location - location < count)
                    {
                        represented = true;
                        break;
                    }
            if (!represented) return;
        }
        entry.Reader ??= GpuProgramMetadataReader.Instance;
        if (known && block != 0)
        {
            bool represented = false;
            foreach (var resource in entry.Metadata.Resources)
                if (resource.Interface == OpenGLShaderValues.Resource((OpenTK.Graphics.OpenGL4.ProgramInterface)block) && resource.Index == blockIndex) { represented = true; break; }
            if (!represented) return;
        }
        var change = GpuMetadataChange.Metadata | (executable ? GpuMetadataChange.Executable : GpuMetadataChange.None);
        if (unsupportedUnits) manager.WithdrawUnsupportedProgramUnits(entry);
        var previous = entry.Metadata;
        entry.CompletionInterrupted = false;
        entry.OperationInProgress = true;
        entry.BeginRefresh(change);
        try
        {
            entry.NotifyProgramDependents(change, available: false);
            if (entry.CompletionInterrupted)
                throw new InvalidOperationException("Program observation was interrupted by a nested mutation.");
            entry.OperationInProgress = false;
            if (unsupportedUnits || !entry.Manager.CanObserveNativeMetadata
                || !entry.Manager.IsRegistered(entry) || entry.State != GpuResourceRegistrationState.Live) return;
            bool success = known && (unit || block != 0)
                ? entry.Manager.RefreshProgramFields(entry, previous, unit, location, count, block == ProgramInterface.UniformBlock ? ProgramBlockKind.Uniform : ProgramBlockKind.Storage, blockIndex, supplied)
                : entry.Manager.RefreshMetadata(entry, GpuMetadataChange.Metadata);
            if (!success) throw new InvalidOperationException("Native program metadata is unavailable after mutation.");
        }
        finally { if (entry is not null) entry.OperationInProgress = false; }
    }
    #endregion
}
