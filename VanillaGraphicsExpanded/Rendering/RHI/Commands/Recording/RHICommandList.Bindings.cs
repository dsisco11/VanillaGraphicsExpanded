using System;
namespace VanillaGraphicsExpanded.Rendering.RHI;
/// <summary>Retains bindings arguments without consulting executed state.</summary>
public sealed partial class RHICommandList
{
    private readonly CommandPages<BindResourcesPayload> payloadBindResources = new();
    /// <summary>Locates fully retained arguments in typed tables.</summary>
    private readonly record struct BindResourcesPayload(int inputs);
    private readonly CommandPages<BindUniformSnapshotPayload> payloadBindUniformSnapshot = new();
    /// <summary>Locates fully retained arguments in typed tables.</summary>
    private readonly record struct BindUniformSnapshotPayload(int slot, int snapshot);
    private readonly CommandPages<BindSampledTexturesPayload> payloadBindSampledTextures = new();
    /// <summary>Locates fully retained arguments in typed tables.</summary>
    private readonly record struct BindSampledTexturesPayload((int Offset, int Length) inputs);
    #region Public API
    /// <summary>Records bind resources with supplied inputs only; native validation remains at execution.</summary>
    public void BindResources(RHIResourceBindings inputs)
    {
        RequireRecording();
        ArgumentNullException.ThrowIfNull(inputs); if (pipelineKind == 0) throw new InvalidOperationException("Record a pipeline before its resources.");

        int payload = payloadBindResources.Add(new(resourceBindingsArguments.Add(inputs)));
        headers.Add(new(CommandOperation.BindResources, payload));
    }
    /// <summary>Records bind uniform snapshot with supplied inputs only; native validation remains at execution.</summary>
    public void BindUniformSnapshot(int slot, RHIUniformSnapshot snapshot)
    {
        RequireRecording();
        ArgumentNullException.ThrowIfNull(snapshot); if (slot < 0) throw new ArgumentOutOfRangeException(nameof(slot)); uniformSlots.Add(slot);

        int payload = payloadBindUniformSnapshot.Add(new(slot, uniformSnapshotArguments.Add(snapshot)));
        headers.Add(new(CommandOperation.BindUniformSnapshot, payload));
    }
    /// <summary>Records bind sampled textures with supplied inputs only; native validation remains at execution.</summary>
    public void BindSampledTextures(ReadOnlySpan<RHISampledTextureSlot> inputs)
    {
        RequireRecording();
        foreach (var input in inputs)
            if (input.Slot < 0 || !Enum.IsDefined(input.Dimension)) throw new ArgumentException("Invalid supplied sampled slot.");
        foreach (var input in inputs) sampledSlots.Add(new(input.Slot, input.Dimension));

        int payload = payloadBindSampledTextures.Add(new(sampledInputs.Add(inputs)));
        headers.Add(new(CommandOperation.BindSampledTextures, payload));
    }
    #endregion
    #region Internal API
    /// <summary>Interprets bindings through the existing immediate context.</summary>
    internal void ExecuteBindings(RHICommand command, RHICommandContext context)
    {
        switch (command.Operation)
        {
            case CommandOperation.BindResources:
            {
                var value = payloadBindResources.Get(command.Payload);
                context.BindResources(resourceBindingsArguments.Get(value.inputs));

                return;
            }
            case CommandOperation.BindUniformSnapshot:
            {
                var value = payloadBindUniformSnapshot.Get(command.Payload);
                if (!context.BindUniformSnapshot(value.slot, uniformSnapshotArguments.Get(value.snapshot)))
                    throw new InvalidOperationException("Recorded uniforms require a current publication epoch.");

                return;
            }
            case CommandOperation.BindSampledTextures:
            {
                var value = payloadBindSampledTextures.Get(command.Payload);
                context.BindSampledTextures(sampledInputs.Read(value.inputs));

                return;
            }
            default: throw new InvalidOperationException("Unexpected operation family.");
        }
    }
    #endregion
}
