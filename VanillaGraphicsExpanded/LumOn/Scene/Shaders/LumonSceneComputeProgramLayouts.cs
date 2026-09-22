using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.LumOn.Scene.Shaders;

internal static class LumonSceneComputeProgramLayouts
{
    internal sealed class FeedbackMarkPages : GpuProgramLayout
    {
        public FeedbackMarkPages()
        {
            RegisterContract(global::VanillaGraphicsExpanded.Rendering.Contracts.GpuShaderContracts.Create("lumonscene_feedback_mark_pages"));
        }
    }

    internal sealed class FeedbackCompactPages : GpuProgramLayout
    {
        public FeedbackCompactPages()
        {
            RegisterContract(global::VanillaGraphicsExpanded.Rendering.Contracts.GpuShaderContracts.Create("lumonscene_feedback_compact_pages"));
        }
    }

    internal sealed class FeedbackGather : GpuProgramLayout
    {
        public FeedbackGather()
        {
            RegisterContract(global::VanillaGraphicsExpanded.Rendering.Contracts.GpuShaderContracts.Create("lumonscene_feedback_gather"));
        }
    }

    internal sealed class CaptureVoxel : GpuProgramLayout
    {
        public CaptureVoxel()
        {
            RegisterContract(global::VanillaGraphicsExpanded.Rendering.Contracts.GpuShaderContracts.Create("lumonscene_capture_voxel"));
        }
    }

    internal sealed class CaptureMeshCard : GpuProgramLayout
    {
        public CaptureMeshCard()
        {
            RegisterContract(global::VanillaGraphicsExpanded.Rendering.Contracts.GpuShaderContracts.Create("lumonscene_capture_meshcard"));
        }
    }

    internal sealed class RelightVoxelDda : GpuProgramLayout
    {
        public RelightVoxelDda()
        {
            RegisterContract(global::VanillaGraphicsExpanded.Rendering.Contracts.GpuShaderContracts.Create("lumonscene_relight_voxel_dda"));
        }
    }
}
