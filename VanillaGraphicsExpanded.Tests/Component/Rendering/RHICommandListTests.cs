using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;


namespace VanillaGraphicsExpanded.Tests.Component.Rendering;

/// <summary>Verifies command-list completion promises follow the originating context lifetime.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class RHICommandListTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Retiring an origin cancels its unsubmitted fence without borrowing a replacement registration.</summary>
    [Fact]
    public void RetiredContextCancelsUnsubmittedWork()
    {
        // Arrange: register an explicit lifetime and record a completion marker against it.
        Start();
        var domain = new object();
        RenderContextRegistry.RegisterCurrent(domain, static _ => true);
        try
        {
            var context = RHICommandContext.Current;
            using var list = context.CreateCommandList();
            using var marker = list.InsertFence();
            list.Close();

            // Act: retire the recorded lifetime before attempting submission.
            RenderContextRegistry.Retire(domain);

            // Assert: submitted work is rejected and its pending promise is cancelled.
            Assert.Equal(CommandListState.Cancelled, context.Submit(list).State);
            Assert.Equal(CompletionStatus.Cancelled, marker.Poll());
        }
        finally
        {
            fixture.RestoreContextRegistration();
            OpenGlStateCache.Current.InvalidateAll();
        }
    }

    /// <summary>Unissued markers, readbacks, and query tickets observe retirement before list submission.</summary>
    [Fact]
    public void PendingTicketsObserveRetirementWithoutSubmission()
    {
        // Arrange: record independent promise types against one registered execution lifetime.
        Start();
        var domain = new object();
        RenderContextRegistry.RegisterCurrent(domain, static _ => true);
        try
        {
            var context = RHICommandContext.Current;
            using var buffer = Buffer(context, 16);
            using var query = context.CreateQuery(new(QueryKind.Timestamp)).RequireResource();

            using var list = context.CreateCommandList(); 
            using var marker = list.InsertFence();
            using var readback = list.EnqueueReadback(new(new RHIBufferRange(buffer, 0, 16)));
            var result = list.RequestQueryResult(query);
            list.Close();

            // Act: retire the origin without submitting or cancelling the list.
            RenderContextRegistry.Retire(domain);

            // Assert: every pending result is cancelled and the list releases its recorded commands.
            Assert.Equal(CompletionStatus.Cancelled, marker.Poll());
            Assert.Equal(CompletionStatus.Cancelled, readback.Poll());
            Assert.Equal(CompletionStatus.Cancelled, result.Poll());
            Assert.Equal(CommandListState.Cancelled, list.State);
            Assert.Equal(0, list.Count);
        }
        finally
        {
            fixture.RestoreContextRegistration();
            OpenGlStateCache.Current.InvalidateAll();
        }
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
