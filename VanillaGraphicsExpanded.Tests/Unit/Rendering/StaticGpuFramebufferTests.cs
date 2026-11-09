using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using System.Reflection;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;
using VanillaGraphicsExpanded.Tests.Support.GPU.Native;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering;

/// <summary>Qualifies immutable framebuffer configuration, construction proof and repeated-pass fast paths.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class StaticGpuFramebufferTests(HeadlessGLFixture fixture, ITestOutputHelper output) : RenderTestBase(fixture)
{


    #region Public API
    #region Configuration and lifetime
    /// <summary>Only the dynamic subclass exposes mutable factories; the shared base cannot be constructed.</summary>
    [Fact]
    public void SubclassFactoriesCannotSilentlyConstructTheWrongKind()
    {
        // Arrange: Inspect the abstract framebuffer base and its static and dynamic concrete subclasses.
        // Act: Query inheritance and public static factory declarations through reflection.
        // Assert: The base cannot be instantiated and only the dynamic subclass declares mutable factories.
        Assert.True(typeof(GpuFramebuffer).IsAbstract);
        Assert.Equal(typeof(GpuFramebuffer), typeof(StaticGpuFramebuffer).BaseType);
        Assert.Equal(typeof(GpuFramebuffer), typeof(DynamicGpuFramebuffer).BaseType);
        Assert.DoesNotContain(typeof(StaticGpuFramebuffer).GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy), method => method.Name is "CreateEmpty" or "Wrap" or "CreateMRT");
        Assert.Equal(typeof(DynamicGpuFramebuffer), typeof(DynamicGpuFramebuffer).GetMethod("CreateEmpty")!.DeclaringType);
    }



    #endregion
    #region External native effects

    #endregion

    #region Retirement


    #endregion
    #region Immutable submission

    #endregion
    #endregion
    #region Private
    /// <summary>Prepares this suite's declared observation programs before subject execution.</summary>
    private protected override void PrepareObservations(VanillaGraphicsExpanded.Tests.Support.GPU.Observations.GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("compare", 0, 0);
    }
    #endregion
}
