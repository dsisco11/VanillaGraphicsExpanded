using HarmonyLib;
using VanillaGraphicsExpanded.PBR.Postprocessing;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Groups immutable AO executables for one test case while scene inputs remain independently owned.</summary>
internal sealed class AmbientOcclusionScenePrograms
{
    #region Public API
    /// <summary>Creates one set of production AO programs under the existing component owner.</summary>
    internal AmbientOcclusionScenePrograms(ComponentShaderPrograms owner)
    {
        Horizon=owner.Create<PostSsaoShaderProgram>();
        Filter=owner.Create<AmbientOcclusionFilterShaderProgram>();
        DepthHierarchy=owner.CreateDepthHierarchy();
        MinimumChildMask=owner.Create<MinimumChildMaskShaderProgram>();
    }

    /// <summary>Gets the case-owned horizon executable.</summary>
    internal PostSsaoShaderProgram Horizon { get; }
    /// <summary>Gets the case-owned spatial filter executable.</summary>
    internal AmbientOcclusionFilterShaderProgram Filter { get; }
    /// <summary>Gets the case-owned depth hierarchy executable.</summary>
    internal DepthHierarchyComputeShader DepthHierarchy { get; }
    /// <summary>Gets the case-owned minimum child mask executable.</summary>
    internal MinimumChildMaskShaderProgram MinimumChildMask { get; }

    /// <summary>Releases scene-owned texture and camera references while preserving case-owned executables.</summary>
    internal void ClearBorrowedInputs()
    {
        Horizon.DepthImage=default;Horizon.SurfaceImage=default;Horizon.DepthHierarchy=default;Horizon.MinimumChildren=default;
        Filter.SourceImage=default;Filter.DepthImage=default;Filter.SurfaceImage=default;
        DepthHierarchy.PrimaryDepth=default;DepthHierarchy.Tail=null;
        DepthHierarchy.Mip0=default;DepthHierarchy.Mip1=default;DepthHierarchy.Mip2=default;DepthHierarchy.Mip3=default;
        DepthHierarchy.Mip4=default;DepthHierarchy.Mip5=default;DepthHierarchy.Mip6=default;
        MinimumChildMask.Hierarchy=default;
        // Program wrappers intentionally expose camera inputs as read-only captured state; clear that test-owned borrow before disposal.
        horizonFrameInputs.SetValue(Horizon,null);filterFrameInputs.SetValue(Filter,null);
    }

    /// <summary>Confirms scene teardown released camera references without retiring the shared programs.</summary>
    internal void AssertSceneReleased()
    {
        Assert.Null(horizonFrameInputs.GetValue(Horizon));Assert.Null(filterFrameInputs.GetValue(Filter));
        Assert.True(Horizon.IsLinked);Assert.True(Filter.IsLinked);Assert.True(DepthHierarchy.IsValid);Assert.True(MinimumChildMask.IsLinked);
    }
    #endregion

    #region Private
    private static readonly System.Reflection.FieldInfo horizonFrameInputs=AccessTools.Field(typeof(PostSsaoShaderProgram),"frameInputs")
        ??throw new MissingFieldException(typeof(PostSsaoShaderProgram).FullName,"frameInputs");
    private static readonly System.Reflection.FieldInfo filterFrameInputs=AccessTools.Field(typeof(AmbientOcclusionFilterShaderProgram),"frameInputs")
        ??throw new MissingFieldException(typeof(AmbientOcclusionFilterShaderProgram).FullName,"frameInputs");
    #endregion
}
