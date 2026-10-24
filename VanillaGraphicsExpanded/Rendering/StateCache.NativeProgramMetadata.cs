using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Forwards observed program changes to the existing canonical registration and its aliases.</summary>
internal sealed partial class StateCache
{
    #region Internal API
    /// <summary>Resolves explicit or active-pipeline programs and excludes numeric uniform destinations.</summary>
    internal static NativeProgramMetadata BeginNativeProgramMetadata(int program, bool named, bool unit, int location, int count,
        bool executable = false, bool unsupportedUnits = false)
    {
        var manager = GpuResourceManagerSystem.CurrentManager;
        if (manager?.CanObserveNativeMetadata != true) return default;
        if (unit && (location < 0 || count <= 0)) return default;
        if (!named)
        {
            program = Current.GetCurrentProgram();
            // Uniform commands use the active program of a bound pipeline when UseProgram is zero.
            if (program == 0 && GpuSupport.Graphics.SupportsArbSeparateShaderObjects)
            {
                int pipeline = Current.GetCurrentProgramPipeline();
                if (pipeline != 0) GL.GetProgramPipeline(pipeline, ProgramPipelineParameter.ActiveProgram, out program);
            }
        }
        var entry = manager.FindObservedResource(GpuResourceKind.Program, program)
            as GpuResourceMetadataEntry<GpuProgramMetadata>;
        if (entry is null) return default;
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
        var change = GpuMetadataChange.Metadata | (executable ? GpuMetadataChange.Executable : GpuMetadataChange.None);
        if (unsupportedUnits) manager.WithdrawUnsupportedProgramUnits(entry);
        entry.BeginRefresh(change);
        entry.NotifyProgramDependents(change, available: false);
        return new(entry, unsupportedUnits);
    }
    #endregion

    /// <summary>Retains original program identity until the native command finishes.</summary>
    internal readonly struct NativeProgramMetadata(GpuResourceMetadataEntry<GpuProgramMetadata>? entry, bool unsupportedUnits)
    {
        #region Internal API
        /// <summary>Publishes measured executable/interface facts; dispatch failure leaves old snapshots unavailable.</summary>
        internal void Complete(bool returned)
        {
            if (unsupportedUnits || !returned || entry is null || !entry.Manager.CanObserveNativeMetadata
                || !entry.Manager.IsRegistered(entry) || entry.State != GpuResourceRegistrationState.Live) return;
            if (!entry.Manager.RefreshMetadata(entry, GpuMetadataChange.Metadata))
                throw new InvalidOperationException("Native program metadata is unavailable after mutation.");
        }
        #endregion
    }
}
