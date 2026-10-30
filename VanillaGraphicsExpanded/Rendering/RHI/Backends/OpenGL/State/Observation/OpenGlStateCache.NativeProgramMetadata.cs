using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Classifies native executable changes and applies supplied interface assignments.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Preserves irrelevant/rejected writes and publishes established changes on their original entry.</summary>
    internal static void ObserveProgramMetadata(int program, bool named, bool unit, int location, int count,
        bool executable = false, bool unsupportedUnits = false, ProgramInterface block = 0, int blockIndex = -1,
        int? supplied = null, ReadOnlySpan<int> values = default)
    {
        var manager = RHIResourceManagerSystem.CurrentManager;
        if (manager?.CanObserveNativeMetadata != true || unit && (location < 0 || count <= 0)) return;
        if (!named)
        {
            // Reuse established selectors; only an uncertain selector needs targeted state
            // recovery. This never authorizes executable or interface discovery.
            program = Current.GetCurrentProgram();
            if (program == 0 && Current.GetCurrentProgramPipeline() is { } pipeline && pipeline != 0)
                program = Current.GetPipelineUniformProgram(pipeline);
        }
        if (manager.FindObservedResource(GpuResourceKind.Program, program)
            is not GpuResourceMetadataEntry<GpuProgramMetadata> entry) return;
        if (executable)
        {
            var feedback = OpenGLFeedbackBindings.GetExternalActivity(Current);
            if (feedback.Active && !feedback.Paused && Current.currentProgram == program) return;
        }
        var previous = entry.Metadata;
        bool known = entry.IsUsable;
        if (!executable)
        {
            if (!previous.ExecutableAvailable || previous.Resources.IsDefault || previous.TextureUnits.IsDefault) return;
            if (unit)
            {
                GpuProgramResourceMetadata? selected = null;
                foreach (var resource in previous.Resources)
                    if (resource.Interface == ProgramResourceKind.Uniform && resource.Location is { } first && first >= 0
                        && location >= first && (long)location - first < (resource.ArraySize ?? 1))
                    { selected = resource; break; }
                if (selected is not { Type: { } type, Location: { } start } uniform
                    || !GpuProgramLayout.IsSamplerType(type) && !GpuProgramLayout.IsImageType(type)) return;
                if (count > 1 && uniform.ArraySize == 1 && uniform.Name?.EndsWith("[0]", StringComparison.Ordinal) != true) return;
                int changed = Math.Min(count, (uniform.ArraySize ?? 1) - (location - start));
                if (!unsupportedUnits && !supplied.HasValue && values.Length < changed) return;
                if (!unsupportedUnits && GpuSupport.HasCachedCapabilitiesForCurrentContext)
                {
                    int limit = GpuProgramLayout.IsImageType(type) ? GpuSupport.Graphics.MaxImageUnits : GpuSupport.Graphics.MaxCombinedTextureImageUnits;
                    for (int i = 0; i < changed; i++)
                        if ((uint)(supplied ?? values[i]) >= (uint)limit) return;
                }
                count = changed;
            }
            else if (block != 0)
            {
                bool represented = false;
                foreach (var resource in previous.Resources)
                    if (resource.Interface == (block == ProgramInterface.UniformBlock ? ProgramResourceKind.UniformBlock : ProgramResourceKind.ShaderStorageBlock)
                        && resource.Index == blockIndex) { represented = true; break; }
                if (!represented || !supplied.HasValue) return;
                if (GpuSupport.HasCachedCapabilitiesForCurrentContext && (uint)supplied.Value >= (uint)(block == ProgramInterface.UniformBlock
                    ? GpuSupport.Graphics.MaxUniformBufferBindings : GpuSupport.Graphics.MaxShaderStorageBufferBindings)) return;
            }
            else return;
        }
        Current.InterruptNativeResourceOperation(GpuResourceKind.Program, program);
        var change = GpuMetadataChange.Metadata | (executable ? GpuMetadataChange.Executable : GpuMetadataChange.None);
        if (InterruptNativeMetadataCompletion(entry, change)) return;
        using var delivery = manager.BeginChangeDelivery();
        if (executable) entry.CompletionInterrupted = true;
        entry.BeginRefresh(change);
        if (executable)
        {
            manager.ResetProgramUnitRepresentation(entry);
            entry.Reader ??= GpuProgramMetadataReader.Instance;
            // Early engine compilation can precede admission. Withdraw the executable now;
            // explicit completion after admission may obtain outputs without discovery here.
            if (!GpuSupport.HasCachedCapabilitiesForCurrentContext) { entry.Withdraw(change); return; }
            // Parallel linking may still be pending after native return. Its nonblocking
            // output flag is distinct from interface discovery and ordinary assignments.
            if (GpuSupport.Graphics.SupportsArbParallelShaderCompile || GpuSupport.Graphics.SupportsKhrParallelShaderCompile)
            {
                OpenGLDriverCalls.GetProgram(program, (GetProgramParameterName)ArbParallelShaderCompile.CompletionStatusArb, out int complete);
                if (complete == 0) { entry.Withdraw(change); return; }
            }
            manager.RefreshMetadata(entry, change);
        }
        else if (unsupportedUnits) { manager.WithdrawUnsupportedProgramUnits(entry); entry.Withdraw(change); }
        else if (known && GpuSupport.HasCachedCapabilitiesForCurrentContext)
            manager.PublishProgramAssignment(entry, previous, unit, location,
                supplied.HasValue ? [supplied.Value] : values[..count],
                block == ProgramInterface.UniformBlock ? ProgramBlockKind.Uniform : ProgramBlockKind.Storage, blockIndex);
        else entry.Withdraw(change);
        delivery.Complete();
        // An unavailable executable stays unavailable. Assignments are never implicit recovery.
    }
    #endregion
}
