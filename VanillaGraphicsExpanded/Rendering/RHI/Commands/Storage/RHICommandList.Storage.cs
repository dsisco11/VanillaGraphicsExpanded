namespace VanillaGraphicsExpanded.Rendering.RHI;
/// <summary>Owns typed argument/reference tables and reusable payload storage for one recording.</summary>
public sealed partial class RHICommandList
{
    private readonly CommandPages<RHICommand> headers = new();
    private readonly CommandBytes bytes = new();
    private readonly CommandPages<GpuBufferRef> bufferReferencesArguments = new();
    private readonly CommandPages<GpuTextureRef> textureReferencesArguments = new();
    private readonly CommandPages<RHITextureTransfer> textureTransferArguments = new();
    private readonly CommandPages<RHIImageBlit> imageBlitArguments = new();
    private readonly CommandPages<RHIGraphicsPipelineRef> graphicsPipelineReferencesArguments = new();
    private readonly CommandPages<RHIComputePipelineRef> computePipelineReferencesArguments = new();
    private readonly CommandPages<RHIDynamicState> dynamicStateArguments = new();
    private readonly CommandPages<RHIResourceBindings> resourceBindingsArguments = new();
    private readonly CommandPages<RHIUniformSnapshot> uniformSnapshotArguments = new();
    private readonly CommandPages<RHIRenderPassDescription> renderPassDescriptionArguments = new();
    private readonly CommandPages<RHIGeometryInputs> geometryInputsArguments = new();
    private readonly CommandPages<RHIIndirectDraw> indirectDrawArguments = new();
    private readonly CommandPages<RHIBufferRange> bufferRangeArguments = new();
    private readonly CommandPages<GpuSyncRef> syncReferencesArguments = new();
    private readonly CommandPages<RHIFenceTicket> fenceTicketArguments = new();
    private readonly CommandPages<GpuSemaphoreRef> semaphoreReferencesArguments = new();
    private readonly CommandPages<GpuQueryRef> queryReferencesArguments = new();
    private readonly CommandPages<RHIQueryTicket> queryTicketArguments = new();
    private readonly CommandPages<RHIReadbackDescription> readbackDescriptionArguments = new();
    private readonly CommandPages<RHIReadbackTicket> readbackTicketArguments = new();
    private readonly CommandPages<GpuTransformFeedbackRef> transformFeedbackReferencesArguments = new();
    private readonly CommandArray<RHISampledTextureSlot> sampledInputs = new();
    private readonly CommandArray<RHIDrawIndexed> indexedDraws = new();
    private readonly CommandArray<GpuBufferRef> semaphoreBuffers = new();
    private readonly CommandArray<RHIExternalTextureTransition> semaphoreTextures = new();
    #region Private
    /// <summary>Releases retained CPU data without disposing resources or issued completion tickets.</summary>
    private void ClearStorage()
    {
        headers.Clear(); bytes.Clear();
        requests.Clear(); graphics.Clear(); compute.Clear(); queries.Clear(); uniformSlots.Clear(); sampledSlots.Clear();
        boundary = null; operations = default; inPass = conditional = capture = paused = false; pipelineKind = 0;
        bufferReferencesArguments.Clear();
        textureReferencesArguments.Clear();
        textureTransferArguments.Clear();
        imageBlitArguments.Clear();
        graphicsPipelineReferencesArguments.Clear();
        computePipelineReferencesArguments.Clear();
        dynamicStateArguments.Clear();
        resourceBindingsArguments.Clear();
        uniformSnapshotArguments.Clear();
        renderPassDescriptionArguments.Clear();
        geometryInputsArguments.Clear();
        indirectDrawArguments.Clear();
        bufferRangeArguments.Clear();
        syncReferencesArguments.Clear();
        fenceTicketArguments.Clear();
        semaphoreReferencesArguments.Clear();
        queryReferencesArguments.Clear();
        queryTicketArguments.Clear();
        readbackDescriptionArguments.Clear();
        readbackTicketArguments.Clear();
        transformFeedbackReferencesArguments.Clear();
        sampledInputs.Clear();
        indexedDraws.Clear();
        semaphoreBuffers.Clear();
        semaphoreTextures.Clear();
        payloadUploadBuffer.Clear();
        payloadCopyBuffer.Clear();
        payloadClearBuffer.Clear();
        payloadUploadTexture.Clear();
        payloadTransferBufferToTexture.Clear();
        payloadTransferTextureToBuffer.Clear();
        payloadCopyTexture.Clear();
        payloadClearTexture.Clear();
        payloadBlitImages.Clear();
        payloadSetGraphicsPipeline.Clear();
        payloadSetComputePipeline.Clear();
        payloadSetDynamicState.Clear();
        payloadBindResources.Clear();
        payloadBindUniformSnapshot.Clear();
        payloadBindSampledTextures.Clear();
        payloadBeginRenderPass.Clear();
        payloadEndRenderPass.Clear();
        payloadDraw.Clear();
        payloadDrawIndexed.Clear();
        payloadDrawIndexedBatch.Clear();
        payloadDrawIndirect.Clear();
        payloadDrawIndexedIndirect.Clear();
        payloadDispatch.Clear();
        payloadDispatchIndirect.Clear();
        payloadBarrier.Clear();
        payloadWaitGpu.Clear();
        payloadInsertFence.Clear();
        payloadWaitSemaphore.Clear();
        payloadSignalSemaphore.Clear();
        payloadBeginQuery.Clear();
        payloadEndQuery.Clear();
        payloadWriteTimestamp.Clear();
        payloadRequestQueryResult.Clear();
        payloadEnqueueReadback.Clear();
        payloadBeginConditionalRender.Clear();
        payloadEndConditionalRender.Clear();
        payloadBeginTransformFeedback.Clear();
        payloadEndTransformFeedback.Clear();
        payloadPauseTransformFeedback.Clear();
        payloadResumeTransformFeedback.Clear();
    }
    #endregion
}
