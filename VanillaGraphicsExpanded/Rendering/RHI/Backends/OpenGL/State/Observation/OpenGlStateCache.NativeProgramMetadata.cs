using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Forwards observed program changes to the existing canonical registration and its aliases.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Resolves explicit or active-pipeline programs and excludes numeric uniform destinations.</summary>
    internal static NativeProgramMetadata BeginNativeProgramMetadata(int program, bool named, bool unit, int location, int count,
        bool executable = false, bool unsupportedUnits = false, ProgramInterface block = 0, int blockIndex = -1, int? supplied = null)
    {
        var manager = GpuResourceManagerSystem.CurrentManager;
        if (manager?.CanObserveNativeMetadata != true) return default;
        if (unit && (location < 0 || count <= 0)) return default;
        if (!named)
        {
            program = Current.GetCurrentProgram();
            // Uniform commands use the active program of a bound pipeline when UseProgram is zero.
            if (program == 0)
            {
                int pipeline = Current.GetCurrentProgramPipeline();
                if (pipeline != 0) GL.GetProgramPipeline(pipeline, ProgramPipelineParameter.ActiveProgram, out program);
            }
        }
        var entry = manager.FindObservedResource(GpuResourceKind.Program, program)
            as GpuResourceMetadataEntry<GpuProgramMetadata>;
        if (entry is null) return default;
        if (entry.OperationInProgress || entry.CompletionInProgress) throw new InvalidOperationException("A program operation is already in progress.");
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
            if (!represented) return default;
        }
        entry.Reader ??= GpuProgramMetadataReader.Instance;
        if (known && block != 0)
        {
            bool represented = false;
            foreach (var resource in entry.Metadata.Resources)
                if (resource.Interface == OpenGLShaderValues.Resource((OpenTK.Graphics.OpenGL4.ProgramInterface)block) && resource.Index == blockIndex) { represented = true; break; }
            if (!represented) return default;
        }
        var change = GpuMetadataChange.Metadata | (executable ? GpuMetadataChange.Executable : GpuMetadataChange.None);
        if (unsupportedUnits) manager.WithdrawUnsupportedProgramUnits(entry);
        var previous = entry.Metadata;
        entry.OperationInProgress = true;
        entry.BeginRefresh(change);
        try { entry.NotifyProgramDependents(change, available: false); }
        catch { entry.OperationInProgress = false; throw; }
        return new(entry, unsupportedUnits, known, previous, unit, location, count, block, blockIndex, supplied);
    }
    #endregion

    /// <summary>Retains original program identity until the native command finishes.</summary>
    internal readonly struct NativeProgramMetadata(GpuResourceMetadataEntry<GpuProgramMetadata>? entry, bool unsupportedUnits,
        bool known, GpuProgramMetadata previous, bool unit, int location, int count, ProgramInterface block, int index, int? supplied)
    {
        #region Internal API
        /// <summary>Publishes affected facts once, retaining native failure and original registration semantics.</summary>
        internal void Complete(bool returned)
        {
            if (entry is null) return;
            entry.OperationInProgress = false;
            if (unsupportedUnits || !returned || !entry.Manager.CanObserveNativeMetadata
                || !entry.Manager.IsRegistered(entry) || entry.State != GpuResourceRegistrationState.Live) return;
            bool success = known && (unit || block != 0)
                ? entry.Manager.RefreshProgramFields(entry, previous, unit, location, count, block == ProgramInterface.UniformBlock ? ProgramBlockKind.Uniform : ProgramBlockKind.Storage, index, supplied)
                : entry.Manager.RefreshMetadata(entry, GpuMetadataChange.Metadata);
            if (!success) throw new InvalidOperationException("Native program metadata is unavailable after mutation.");
        }
        #endregion
    }
}
