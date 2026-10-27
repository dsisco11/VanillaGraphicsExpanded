namespace VanillaGraphicsExpanded.Rendering.RHI;
/// <summary>Identifies semantic recordable operations without native overload selectors.</summary>
public enum CommandOperation
{
    /// <summary>Records upload buffer.</summary>
    UploadBuffer,
    /// <summary>Records copy buffer.</summary>
    CopyBuffer,
    /// <summary>Records clear buffer.</summary>
    ClearBuffer,
    /// <summary>Records upload texture.</summary>
    UploadTexture,
    /// <summary>Records transfer buffer to texture.</summary>
    TransferBufferToTexture,
    /// <summary>Records transfer texture to buffer.</summary>
    TransferTextureToBuffer,
    /// <summary>Records copy texture.</summary>
    CopyTexture,
    /// <summary>Records clear texture.</summary>
    ClearTexture,
    /// <summary>Records blit images.</summary>
    BlitImages,
    /// <summary>Records set graphics pipeline.</summary>
    SetGraphicsPipeline,
    /// <summary>Records set compute pipeline.</summary>
    SetComputePipeline,
    /// <summary>Records set dynamic state.</summary>
    SetDynamicState,
    /// <summary>Records bind resources.</summary>
    BindResources,
    /// <summary>Records bind uniform snapshot.</summary>
    BindUniformSnapshot,
    /// <summary>Records bind sampled textures.</summary>
    BindSampledTextures,
    /// <summary>Records begin render pass.</summary>
    BeginRenderPass,
    /// <summary>Records end render pass.</summary>
    EndRenderPass,
    /// <summary>Records draw.</summary>
    Draw,
    /// <summary>Records draw indexed.</summary>
    DrawIndexed,
    /// <summary>Records draw indexed batch.</summary>
    DrawIndexedBatch,
    /// <summary>Records draw indirect.</summary>
    DrawIndirect,
    /// <summary>Records draw indexed indirect.</summary>
    DrawIndexedIndirect,
    /// <summary>Records dispatch.</summary>
    Dispatch,
    /// <summary>Records dispatch indirect.</summary>
    DispatchIndirect,
    /// <summary>Records barrier.</summary>
    Barrier,
    /// <summary>Records wait gpu.</summary>
    WaitGpu,
    /// <summary>Records insert fence.</summary>
    InsertFence,
    /// <summary>Records wait semaphore.</summary>
    WaitSemaphore,
    /// <summary>Records signal semaphore.</summary>
    SignalSemaphore,
    /// <summary>Records begin query.</summary>
    BeginQuery,
    /// <summary>Records end query.</summary>
    EndQuery,
    /// <summary>Records write timestamp.</summary>
    WriteTimestamp,
    /// <summary>Records request query result.</summary>
    RequestQueryResult,
    /// <summary>Records enqueue readback.</summary>
    EnqueueReadback,
    /// <summary>Records begin conditional render.</summary>
    BeginConditionalRender,
    /// <summary>Records end conditional render.</summary>
    EndConditionalRender,
    /// <summary>Records begin transform feedback.</summary>
    BeginTransformFeedback,
    /// <summary>Records end transform feedback.</summary>
    EndTransformFeedback,
    /// <summary>Records pause transform feedback.</summary>
    PauseTransformFeedback,
    /// <summary>Records resume transform feedback.</summary>
    ResumeTransformFeedback,
}
