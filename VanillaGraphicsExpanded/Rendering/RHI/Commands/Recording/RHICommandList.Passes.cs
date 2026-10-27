using System;
namespace VanillaGraphicsExpanded.Rendering.RHI;
/// <summary>Retains passes arguments without consulting executed state.</summary>
public sealed partial class RHICommandList
{
    private readonly CommandPages<BeginRenderPassPayload> payloadBeginRenderPass = new();
    /// <summary>Locates fully retained arguments in typed tables.</summary>
    private readonly record struct BeginRenderPassPayload(int description);
    private readonly CommandPages<EndRenderPassPayload> payloadEndRenderPass = new();
    /// <summary>Locates fully retained arguments in typed tables.</summary>
    private readonly record struct EndRenderPassPayload();
    #region Public API
    /// <summary>Records begin render pass with supplied inputs only; native validation remains at execution.</summary>
    public void BeginRenderPass(RHIRenderPassDescription description)
    {
        RequireRecording();
        ArgumentNullException.ThrowIfNull(description); if (inPass) throw new InvalidOperationException("Passes cannot nest."); inPass = true; pipelineKind = 0; operations |= BoundaryOperations.RenderPasses;

        int payload = payloadBeginRenderPass.Add(new(renderPassDescriptionArguments.Add(description)));
        headers.Add(new(CommandOperation.BeginRenderPass, payload));
    }
    /// <summary>Records end render pass with supplied inputs only; native validation remains at execution.</summary>
    public void EndRenderPass()
    {
        RequireRecording();
        if (!inPass) throw new InvalidOperationException("No recorded pass is active."); inPass = false; pipelineKind = 0;

        int payload = payloadEndRenderPass.Add(new());
        headers.Add(new(CommandOperation.EndRenderPass, payload));
    }
    #endregion
    #region Internal API
    /// <summary>Interprets passes through the existing immediate context.</summary>
    internal void ExecutePasses(RHICommand command, RHICommandContext context)
    {
        switch (command.Operation)
        {
            case CommandOperation.BeginRenderPass:
            {
                var value = payloadBeginRenderPass.Get(command.Payload);
                context.recordedPass = true;
                context.BeginRenderPass(renderPassDescriptionArguments.Get(value.description));

                return;
            }
            case CommandOperation.EndRenderPass:
            {
                var value = payloadEndRenderPass.Get(command.Payload);
                try { context.EndRenderPass(); } finally { context.recordedPass = false; }

                return;
            }
            default: throw new InvalidOperationException("Unexpected operation family.");
        }
    }
    #endregion
}
