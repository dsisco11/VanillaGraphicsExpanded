using VanillaGraphicsExpanded.Tests.Support.GPU.Observations;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Postprocessing;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;

using VanillaGraphicsExpanded.Tests.Support.GPU.Native;
namespace VanillaGraphicsExpanded.Tests.Integration.Rendering.Shaders;

/// <summary>Checks linked composite samplers against the active device limits.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class LightShaftShaderTests(HeadlessGLFixture fixture, ITestOutputHelper output) : LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>Reports the active fragment sampler requirement and verifies the executing device supports it.</summary>
    [Fact]
    public void CompositeSamplerFootprintFitsDeviceCapacity()
    {
        // Arrange: Link the production composite executable and inspect its active sampler declarations.
        // Act: Count fragment sampler use and compare it with the current device limit.
        // Assert: The executing device supports the actual linked sampler footprint.
        EnsureShaderTestAvailable();var shader=Programs.Create<VanillaGraphicsExpanded.PBR.PBRCompositeShaderProgram>();
        int samplers = 0;
        var units = new HashSet<int>();
        // SPIR-V strips authored names; use the validated numeric linked interface.
        foreach (var entry in shader.ProgramLayout.BinaryInterface!.PreparedBindings.Entries)
        {
            if (!entry.Active || entry.Contract.Kind != VanillaGraphicsExpanded.Rendering.Contracts.ShaderBindingKind.Sampler) continue;
            samplers += entry.ActiveElements.Count(value => value);
            int unit = entry.Contract.Binding.Slot;
            Assert.InRange(unit, 0, GpuSupport.MaxCombinedTextureImageUnits - 1);
            Assert.True(units.Add(unit));
        }
        int driverLimit = GpuSupport.MaxTextureImageUnits;
        output.WriteLine($"Composite active samplers={samplers}; driver fragment limit={driverLimit}; required fragment sampler capacity={samplers}.");
        output.WriteLine($"Device array layers={GpuSupport.MaxArrayTextureLayers}; color attachments={GpuSupport.MaxColorAttachments}; draw buffers={GpuSupport.MaxDrawBuffers}.");
        Assert.True(GpuSupport.MaxArrayTextureLayers >= 7);
        Assert.True(GpuSupport.MaxColorAttachments >= 8);
        // Material energy occupies its shared high sampler slot independently of
        // the contiguous scene-composite inputs; per-stage limits count samplers.
        Assert.Equal(14, samplers);
        Assert.InRange(samplers, 0, driverLimit);
        Assert.Equal(Enumerable.Range(0, 13).Append(GpuBindingRegistry.Sampler.MicrofacetEnergy), units.Order());
    }
    #endregion

    #region Private
    /// <summary>Prepares this suite's declared observation programs before subject execution.</summary>
    private protected override void PrepareObservations(VanillaGraphicsExpanded.Tests.Support.GPU.Observations.GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("compare", 0, 0);
        programs.Prepare("predicate-lighting", 0, 0);
    }
    #endregion
}
