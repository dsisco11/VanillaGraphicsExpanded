using System;
namespace VanillaGraphicsExpanded.Rendering.RHI;
/// <summary>Retains pipelines arguments without consulting executed state.</summary>
public sealed partial class RHICommandList
{
    private readonly CommandPages<SetGraphicsPipelinePayload> payloadSetGraphicsPipeline = new();
    /// <summary>Locates fully retained arguments in typed tables.</summary>
    private readonly record struct SetGraphicsPipelinePayload(int pipeline);
    private readonly CommandPages<SetComputePipelinePayload> payloadSetComputePipeline = new();
    /// <summary>Locates fully retained arguments in typed tables.</summary>
    private readonly record struct SetComputePipelinePayload(int pipeline);
    private readonly CommandPages<SetDynamicStatePayload> payloadSetDynamicState = new();
    /// <summary>Locates fully retained arguments in typed tables.</summary>
    private readonly record struct SetDynamicStatePayload(int values);
    #region Public API
    /// <summary>Records set graphics pipeline with supplied inputs only; native validation remains at execution.</summary>
    public void SetPipeline(RHIGraphicsPipelineRef pipeline)
    {
        RequireRecording();
        if (pipeline.IsEmpty) throw new ArgumentException("A pipeline is required."); graphics.Add(pipeline); pipelineKind = 1;

        int payload = payloadSetGraphicsPipeline.Add(new(graphicsPipelineReferencesArguments.Add(pipeline)));
        headers.Add(new(CommandOperation.SetGraphicsPipeline, payload));
    }
    /// <summary>Records set compute pipeline with supplied inputs only; native validation remains at execution.</summary>
    public void SetPipeline(RHIComputePipelineRef pipeline)
    {
        RequireRecording();
        if (inPass || pipeline.IsEmpty) throw new InvalidOperationException("Select compute outside a pass."); compute.Add(pipeline); pipelineKind = 2;

        int payload = payloadSetComputePipeline.Add(new(computePipelineReferencesArguments.Add(pipeline)));
        headers.Add(new(CommandOperation.SetComputePipeline, payload));
    }
    /// <summary>Records set dynamic state with supplied inputs only; native validation remains at execution.</summary>
    public void SetDynamicState(RHIDynamicState values)
    {
        RequireRecording();
        ArgumentNullException.ThrowIfNull(values);

        int payload = payloadSetDynamicState.Add(new(dynamicStateArguments.Add(values)));
        headers.Add(new(CommandOperation.SetDynamicState, payload));
    }
    #endregion
    #region Internal API
    /// <summary>Interprets pipelines through the existing immediate context.</summary>
    internal void ExecutePipelines(RHICommand command, RHICommandContext context)
    {
        switch (command.Operation)
        {
            case CommandOperation.SetGraphicsPipeline:
            {
                var value = payloadSetGraphicsPipeline.Get(command.Payload);
                context.SetPipeline(graphicsPipelineReferencesArguments.Get(value.pipeline));

                return;
            }
            case CommandOperation.SetComputePipeline:
            {
                var value = payloadSetComputePipeline.Get(command.Payload);
                context.SetPipeline(computePipelineReferencesArguments.Get(value.pipeline));

                return;
            }
            case CommandOperation.SetDynamicState:
            {
                var value = payloadSetDynamicState.Get(command.Payload);
                context.SetDynamicState(dynamicStateArguments.Get(value.values));

                return;
            }
            default: throw new InvalidOperationException("Unexpected operation family.");
        }
    }
    #endregion
}
