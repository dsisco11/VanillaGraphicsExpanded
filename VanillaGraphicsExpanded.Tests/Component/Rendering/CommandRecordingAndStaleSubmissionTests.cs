using System;
using OpenTK.Windowing.GraphicsLibraryFramework;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;


namespace VanillaGraphicsExpanded.Tests.Component.Rendering;

/// <summary>Verifies command-list recording remains CPU-owned when native execution is unavailable or stale.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class CommandRecordingAndStaleSubmissionTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Recording copies inputs without a current context; debug catches stale use and release requires discard.</summary>
    [Fact]
    public unsafe void RecordingNeedsNoNativeContextOrLiveResource()
    {
        // Arrange: create a resource reference, retire its owner, and retain the originating context.
        Start();
        var context = RHICommandContext.Current;
        using var buffer = Buffer(context, 16);
        GpuBufferRef reference = buffer;
        buffer.Dispose();
        using var list = context.CreateCommandList();
        var current = GLFW.GetCurrentContext();

        // Act: record and close after removing the native context from the current thread.
        GLFW.MakeContextCurrent(null);
        try
        {
            list.UploadBuffer(reference, 0, new byte[16]);
            list.Close();
        }
        finally
        {
            GLFW.MakeContextCurrent(current);
        }

        // Assert: CPU recording completes; configuration-specific stale-submission policy remains intact.
        Assert.Equal(CommandListState.Closed, list.State);
#if DEBUG
        var result = context.Submit(list);
        Assert.Equal(CommandListState.Failed, result.State);
        Assert.Equal(0, result.FailureIndex);
        Assert.NotNull(result.Error);
#else
        // Recording is CPU-only, but the owner must discard this stale selection before use.
        // Release does not promise successful detection of arbitrary owner lifetime violations.
        list.Cancel();
        Assert.Equal(CommandListState.Cancelled, list.State);
#endif
    }
    #endregion

    #region Private
    /// <summary>Initializes the shared native fixture and its resource-disposal tracking.</summary>
    private void Start()
    {
        EnsureContextValid();
        fixture.InitializeResourceDisposal();
    }

    /// <summary>Creates mapped buffer storage owned by the active RHI context.</summary>
    private static GpuBufferObject Buffer(RHICommandContext context, int bytes) => context.CreateBuffer(new()
    {
        Role = BufferRole.CopyDestination,
        Mode = ResourceStorageMode.Mutable,
        Capacity = bytes,
        Usage = BufferUsage.DynamicDraw
    }, new(new byte[bytes])).RequireResource();
    #endregion
}
