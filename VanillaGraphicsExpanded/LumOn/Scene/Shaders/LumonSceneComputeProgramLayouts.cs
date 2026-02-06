using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.LumOn.Scene.Shaders;

internal static class LumonSceneComputeProgramLayouts
{
    internal sealed class FeedbackMarkPages : GpuProgramLayout
    {
        public FeedbackMarkPages()
        {
            RegisterUniformBlockBinding("VgeLumOnSceneFeedbackMarkParamsUBO", GpuBindingRegistry.Ubo.Object);
            RegisterSamplerUnit("vge_patchIdGBuffer", 0);
            RegisterSamplerUnit("vge_chunkSlotGenerationTex", 1, required: false);
            RegisterImageUnit("vge_pageUsageStamp", 0);
        }
    }

    internal sealed class FeedbackCompactPages : GpuProgramLayout
    {
        public FeedbackCompactPages()
        {
            RegisterUniformBlockBinding("VgeLumOnSceneFeedbackCompactParamsUBO", GpuBindingRegistry.Ubo.Object);
            RegisterSamplerUnit("vge_pageUsageStamp", 0);
            RegisterSamplerUnit("vge_pageTableMip0", 1);
            RegisterShaderStorageBlockBinding("VgePageRequests", 0);
        }
    }

    internal sealed class FeedbackGather : GpuProgramLayout
    {
        public FeedbackGather()
        {
            RegisterUniformBlockBinding("VgeLumOnSceneFeedbackGatherParamsUBO", GpuBindingRegistry.Ubo.Object);
            RegisterSamplerUnit("vge_patchIdGBuffer", 0);
            RegisterShaderStorageBlockBinding("VgePageRequests", 0);
        }
    }

    internal sealed class CaptureVoxel : GpuProgramLayout
    {
        public CaptureVoxel()
        {
            RegisterUniformBlockBinding("VgeLumOnSceneCaptureVoxelParamsUBO", GpuBindingRegistry.Ubo.Object);
            RegisterImageUnit("vge_depthAtlas", 0);
            RegisterImageUnit("vge_materialAtlas", 1);
            RegisterSamplerUnit("vge_occL0", 2);
            RegisterSamplerUnit("vge_materialPalette", 3);
            RegisterShaderStorageBlockBinding("VgeCaptureWork", 0);
            RegisterShaderStorageBlockBinding("VgePatchMetadata", 1);
            RegisterShaderStorageBlockBinding("VgeChunkSlotInfo", 2);
        }
    }

    internal sealed class CaptureMeshCard : GpuProgramLayout
    {
        public CaptureMeshCard()
        {
            RegisterUniformBlockBinding("VgeLumOnSceneCaptureMeshCardParamsUBO", GpuBindingRegistry.Ubo.Object);
            RegisterImageUnit("vge_depthAtlas", 0);
            RegisterImageUnit("vge_materialAtlas", 1);
            RegisterShaderStorageBlockBinding("VgeMeshCardCaptureWork", 0);
            RegisterShaderStorageBlockBinding("VgePatchMetadataBuffer", 1);
            RegisterShaderStorageBlockBinding("VgeTriangles", 2);
        }
    }

    internal sealed class TraceSceneRegionToClipmap : GpuProgramLayout
    {
        public TraceSceneRegionToClipmap()
        {
            RegisterUniformBlockBinding("VgeLumOnSceneTraceRegionParamsUBO", GpuBindingRegistry.Ubo.Object);
            RegisterShaderStorageBlockBinding("VgeRegionPayloadWords", 0);
            RegisterShaderStorageBlockBinding("VgeRegionUpdates", 1);

            // vge_occLevels is declared as uimage3D vge_occLevels[8] with layout(binding=0).
            // Register the full array mapping for fallback paths that require uniform assignment.
            RegisterImageUnitArray("vge_occLevels", firstUnit: 0, count: 8);
        }
    }

    internal sealed class RelightVoxelDda : GpuProgramLayout
    {
        public RelightVoxelDda()
        {
            RegisterUniformBlockBinding("VgeLumOnSceneRelightParamsUBO", GpuBindingRegistry.Ubo.Object);

            RegisterSamplerUnit("vge_depthAtlas", 0);
            RegisterSamplerUnit("vge_materialAtlas", 1);
            RegisterSamplerUnit("vge_occL0", 2);
            RegisterSamplerUnit("vge_lightColorLut", 3);
            RegisterSamplerUnit("vge_blockLevelScalarLut", 4);
            RegisterSamplerUnit("vge_sunLevelScalarLut", 5);
            RegisterSamplerUnit("vge_materialPalette", 6);
            RegisterSamplerUnit("vge_surfaceLut", 7);

            RegisterImageUnit("vge_irradianceAtlas", 0);

            RegisterShaderStorageBlockBinding("VgeRelightWork", 0);
            RegisterShaderStorageBlockBinding("VgePatchMetadata", 1);
        }
    }
}
