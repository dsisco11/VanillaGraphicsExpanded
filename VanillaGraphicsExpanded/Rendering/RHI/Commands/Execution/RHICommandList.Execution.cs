using System;
namespace VanillaGraphicsExpanded.Rendering.RHI;
/// <summary>Interprets opcode order only; native execution and all publication remain with the immediate context.</summary>
public sealed partial class RHICommandList
{
    #region Internal API
    /// <summary>Executes one immutable header through its operation family.</summary>
    internal void Execute(int index, RHICommandContext context)
    {
        var command = headers.Get(index);
        switch (command.Operation)
        {
            case CommandOperation.UploadBuffer:
            case CommandOperation.CopyBuffer:
            case CommandOperation.ClearBuffer:
                ExecuteBuffers(command, context); return;
            case CommandOperation.UploadTexture:
            case CommandOperation.TransferBufferToTexture:
            case CommandOperation.TransferTextureToBuffer:
            case CommandOperation.CopyTexture:
            case CommandOperation.ClearTexture:
            case CommandOperation.BlitImages:
                ExecuteImages(command, context); return;
            case CommandOperation.SetGraphicsPipeline:
            case CommandOperation.SetComputePipeline:
            case CommandOperation.SetDynamicState:
                ExecutePipelines(command, context); return;
            case CommandOperation.BindResources:
            case CommandOperation.BindUniformSnapshot:
            case CommandOperation.BindSampledTextures:
                ExecuteBindings(command, context); return;
            case CommandOperation.BeginRenderPass:
            case CommandOperation.EndRenderPass:
                ExecutePasses(command, context); return;
            case CommandOperation.Draw:
            case CommandOperation.DrawIndexed:
            case CommandOperation.DrawIndexedBatch:
            case CommandOperation.DrawIndirect:
            case CommandOperation.DrawIndexedIndirect:
            case CommandOperation.Dispatch:
            case CommandOperation.DispatchIndirect:
                ExecuteGeometry(command, context); return;
            case CommandOperation.Barrier:
            case CommandOperation.WaitGpu:
            case CommandOperation.InsertFence:
            case CommandOperation.WaitSemaphore:
            case CommandOperation.SignalSemaphore:
                ExecuteSynchronization(command, context); return;
            case CommandOperation.BeginQuery:
            case CommandOperation.EndQuery:
            case CommandOperation.WriteTimestamp:
            case CommandOperation.RequestQueryResult:
            case CommandOperation.EnqueueReadback:
            case CommandOperation.BeginConditionalRender:
            case CommandOperation.EndConditionalRender:
                ExecuteQueries(command, context); return;
            case CommandOperation.BeginTransformFeedback:
            case CommandOperation.EndTransformFeedback:
            case CommandOperation.PauseTransformFeedback:
            case CommandOperation.ResumeTransformFeedback:
                ExecuteCapture(command, context); return;
            default: throw new InvalidOperationException("Unknown recorded operation.");
        }
    }
    #endregion
}
