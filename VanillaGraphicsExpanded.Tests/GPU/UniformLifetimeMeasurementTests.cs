using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn.Shaders;
using VanillaGraphicsExpanded.PBR.Liquids;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Measures matched publication work with real changed water blocks and stable non-water constants.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class UniformLifetimeMeasurementTests(HeadlessGLFixture fixture, ITestOutputHelper output) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Copy savings are measured separately from unchanged binds, resident bytes and retirement polls.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MixedWaterAndStableResolvePublicationCounters(bool mapped)
    {
        EnsureContextValid();
        using var ring = new GpuUniformRingBuffer(65536, 3, mapped);
        using var frame = new LiquidFrameParamsUbo();
        using var draw = new LiquidDrawParamsUbo();
        using var resolve = new LumOnWorldProbeResolveParamsUbo();
        using var referenceResolve = new PackedUniformBuffer(resolve.SizeBytes);
        resolve.AtlasSize = new(256, 128);
        referenceResolve.SetBytes(resolve.Bytes);
        long publications = 0;
        GpuUniformRingSystem.SetCurrent(ring);
        try
        {
            for (int index = 0; index < 8; index++)
            {
                ring.BeginFrame(index);
                frame.Animation = new Vector4(index, 0, 0, 0);
                for (int mesh = 0; mesh < 4; mesh++)
                {
                    draw.SetOrigin(new Vector3(index, mesh, 0));
                    Assert.True(frame.TryBindToSlot(0));
                    Assert.True(draw.TryBindToSlot(1));
                    Assert.True(resolve.TryBindToSlot(2));
                    Assert.True(referenceResolve.TryBindToSlot(3));
                    publications += 4;
                }
                ring.EndFrame();
                GL.Finish();
                ring.PersistentStorage.Collect();
            }
            long waterCopies = 8L * frame.SizeBytes + 32L * draw.SizeBytes;
            Assert.Equal(48, ring.AllocationsWritten);
            Assert.Equal(waterCopies + 8L * resolve.SizeBytes, ring.BytesWritten);
            Assert.Equal(1, ring.PersistentStorage.AllocationsWritten);
            Assert.Equal(resolve.SizeBytes, ring.PersistentStorage.BytesWritten);
            Assert.Equal(32, ring.PersistentStorage.Bindings);
            Assert.Equal(0, ring.PersistentStorage.PendingBytes);
            output.WriteLine($"backendMapped={ring.UsesPersistentMapping}; publications={publications}; waterCopies={waterCopies}; " +
                $"referenceStableAllocations=8; referenceStableBytes={8 * resolve.SizeBytes}; retainedAllocations={ring.PersistentStorage.AllocationsWritten}; " +
                $"retainedBytes={ring.PersistentStorage.BytesWritten}; retainedBinds={ring.PersistentStorage.Bindings}; " +
                $"residentBytes={ring.PersistentStorage.ResidentBytes}; pendingBytes={ring.PersistentStorage.PendingBytes}; fencePolls={ring.PersistentStorage.FencePolls}; " +
                $"transientResidentBytes={ring.PageCount * ring.PageSizeBytes}; transientFenceWaits={ring.FenceWaits}; retainedBlockingWaits=0; CPU/GPU/live timings unmeasured.");
        }
        finally { GpuUniformRingSystem.ClearCurrent(); }
    }
    #endregion
}
