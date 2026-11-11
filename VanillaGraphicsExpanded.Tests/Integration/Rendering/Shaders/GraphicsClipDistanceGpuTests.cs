using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.ProgramBinaries;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Tests.Support.Cache;
using VanillaGraphicsExpanded.Tests.Support.GPU.Native;
using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering.Shaders;

/// <summary>Verifies packaged clip outputs through actual OpenGL rasterization and executable caching.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class GraphicsClipDistanceGpuTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Links and rasterizes active final clip outputs from vertex, tessellation-evaluation and geometry stages.</summary>
    [Fact]
    public void FinalVertexTessellationAndGeometryProducersClipNativeRasterization()
    {
        // Arrange: compile the real packaged producer chains and record the context limits governing their outputs.
        EnsureContextValid();
        using var cacheBypass = DriverProgramCache.UseStoreForTesting(null);
        var capabilities = GpuSupport.Graphics;
        Assert.SkipWhen(capabilities.MaxClipDistances < 4, "The context cannot execute the four-plane final geometry fixture.");
        Assert.SkipWhen(!capabilities.Tessellation, "The context cannot execute the packaged tessellation producer fixtures.");
        using var assets = new BinaryShaderApiFixture();
        var catalog = ShaderBuildTool.Spirv.TestShaderPrograms.Create();
        using var vertex = BuiltShaderFixture.LoadProgram(new(new ShaderSettings(catalog.Programs["tests/clip-distance-vertex"])));
        using var tessellation = BuiltShaderFixture.LoadProgram(new(new ShaderSettings(catalog.Programs["tests/clip-distance-tessellation"])));
        using var geometry = BuiltShaderFixture.LoadProgram(new(new ShaderSettings(catalog.Programs["tests/clip-distance-tessellation-geometry"])));
        using var vertexGeometry = BuiltShaderFixture.LoadProgram(new(new ShaderSettings(catalog.Programs["tests/clip-distance-geometry"])));
        var commands = RHICommandContext.Current;
        var layout = new RHIVertexLayout([]);
        using var vertices = new ArrayGraphicsGeometry(layout, PrimitiveTopology.Triangles,
            new Dictionary<int, GpuVbo>(), proceduralVertices: 3);
        using var vertexTarget = CreateRenderTarget(8, 8, TextureFormat.Rgba8);
        using var tessellationTarget = CreateRenderTarget(8, 8, TextureFormat.Rgba8);
        using var geometryTarget = CreateRenderTarget(8, 8, TextureFormat.Rgba8);
        using var vertexGeometryTarget = CreateRenderTarget(8, 8, TextureFormat.Rgba8);
        var vertexPass = FramebufferTestSetup.DescribeAttachments(vertexTarget,
            [new(AttachmentIndex: 0, Load: AttachmentLoad.Clear, Clear: ColorClearValue.Float(0, 0, 0, 0))]);
        var tessellationPass = FramebufferTestSetup.DescribeAttachments(tessellationTarget,
            [new(AttachmentIndex: 0, Load: AttachmentLoad.Clear, Clear: ColorClearValue.Float(0, 0, 0, 0))]);
        var geometryPass = FramebufferTestSetup.DescribeAttachments(geometryTarget,
            [new(AttachmentIndex: 0, Load: AttachmentLoad.Clear, Clear: ColorClearValue.Float(0, 0, 0, 0))]);
        var vertexGeometryPass = FramebufferTestSetup.DescribeAttachments(vertexGeometryTarget,
            [new(AttachmentIndex: 0, Load: AttachmentLoad.Clear, Clear: ColorClearValue.Float(0, 0, 0, 0))]);
        using var vertexPipeline = commands.CreateGraphicsPipeline(new(vertex, layout,
            commands.DescribeRenderTargets(vertexPass), DynamicPipelineState.Viewport)).RequireResource();
        using var tessellationPipeline = commands.CreateGraphicsPipeline(new(tessellation, layout,
            commands.DescribeRenderTargets(tessellationPass), DynamicPipelineState.Viewport,
            assembly: new() { Topology = PrimitiveTopology.Patches, PatchVertices = 3 })).RequireResource();
        using var geometryPipeline = commands.CreateGraphicsPipeline(new(geometry, layout,
            commands.DescribeRenderTargets(geometryPass), DynamicPipelineState.Viewport,
            assembly: new() { Topology = PrimitiveTopology.Patches, PatchVertices = 3 })).RequireResource();
        using var vertexGeometryPipeline = commands.CreateGraphicsPipeline(new(vertexGeometry, layout,
            commands.DescribeRenderTargets(vertexGeometryPass), DynamicPipelineState.Viewport)).RequireResource();

        // Act: execute each packaged stage chain through the native linker and rasterizer under compiler-derived state.
        using (var boundary = commands.EnterBoundary(new("Tests.ClipDistance.FinalProducerStages", true,
                   graphics: [vertexPipeline, tessellationPipeline, geometryPipeline, vertexGeometryPipeline], operations: BoundaryOperations.RenderPasses)))
        {
            boundary.Run(() =>
            {
                commands.BeginRenderPass(vertexPass);
                commands.SetPipeline(vertexPipeline);
                commands.SetDynamicState(new() { Viewport = commands.PassViewport });
                commands.Draw(vertices.Inputs, new(0, 3));
#if DEBUG
                long warmCalls = OpenGlStateCache.Current.FixedFunctionCalls;
                commands.SetPipeline(vertexPipeline);
                commands.SetDynamicState(new() { Viewport = commands.PassViewport });
                commands.Draw(vertices.Inputs, new(0, 3));
                Assert.Equal(warmCalls, OpenGlStateCache.Current.FixedFunctionCalls);
#endif
                commands.EndRenderPass();
                Draw(commands, tessellationPipeline, tessellationPass, vertices);
                Draw(commands, geometryPipeline, geometryPass, vertices);
                Draw(commands, vertexGeometryPipeline, vertexGeometryPass, vertices);
            });
        }

        // Assert: a negative-x sample is clipped and a positive-x sample survives each final producer chain.
        foreach (var target in new[] { vertexTarget, tessellationTarget, geometryTarget, vertexGeometryTarget })
        {
            Assert.Equal(new[] { 0f, 0f, 0f, 0f }, target[0].ReadPixelsRegion(1, 4, 1, 1));
            Assert.Equal(new[] { 1f, 1f, 1f, 1f }, target[0].ReadPixelsRegion(6, 4, 1, 1));
        }
    }

    /// <summary>Rasterizes a clipped triangle, switches to an inactive final producer, and restores external enables.</summary>
    [Fact]
    public void PackagedClippingControlsRasterizationAndStateTransitions()
    {
        // Arrange: use packaged precompiled shaders and preserve the engine-facing clip state around both draws.
        EnsureContextValid();
        using var cacheBypass = DriverProgramCache.UseStoreForTesting(null);
        int maximum = GpuSupport.Graphics.MaxClipDistances;
        Assert.SkipWhen(maximum < 4, "The context cannot execute the four-plane geometry fixture.");
        bool[] original = Enumerable.Range(0, maximum)
            .Select(index => GL.IsEnabled((EnableCap)((int)EnableCap.ClipDistance0 + index))).ToArray();
        var cache = OpenGlStateCache.Current;
        try
        {
            for (int index = 0; index < maximum; index++)
                GL.Disable((EnableCap)((int)EnableCap.ClipDistance0 + index));
            GL.Enable(EnableCap.ClipDistance0);
            bool[] boundaryBaseline = Enumerable.Range(0, maximum)
                .Select(index => GL.IsEnabled((EnableCap)((int)EnableCap.ClipDistance0 + index))).ToArray();
            cache.InvalidateAll();

            var catalog = ShaderBuildTool.Spirv.TestShaderPrograms.Create();
            using var clipped = BuiltShaderFixture.LoadProgram(new(new ShaderSettings(catalog.Programs["tests/clip-distance-vertex"])));
            using var uncut = BuiltShaderFixture.LoadProgram(new(new ShaderSettings(catalog.Programs["tests/clip-distance-geometry-inactive"])));
            Assert.Equal(2, clipped.Interface.Graphics!.ClipDistanceCount);
            Assert.Equal(0, uncut.Interface.Graphics!.ClipDistanceCount);

            var commands = RHICommandContext.Current;
            using var clippedTarget = CreateRenderTarget(8, 8, TextureFormat.Rgba8);
            using var uncutTarget = CreateRenderTarget(8, 8, TextureFormat.Rgba8);
            var clippedPass = FramebufferTestSetup.DescribeAttachments(clippedTarget,
                [new(AttachmentIndex: 0, Load: AttachmentLoad.Clear, Clear: ColorClearValue.Float(0, 0, 0, 0))]);
            var uncutPass = FramebufferTestSetup.DescribeAttachments(uncutTarget,
                [new(AttachmentIndex: 0, Load: AttachmentLoad.Clear, Clear: ColorClearValue.Float(0, 0, 0, 0))]);
            var layout = new RHIVertexLayout([]);
            using var clippedPipeline = commands.CreateGraphicsPipeline(new(clipped, layout,
                commands.DescribeRenderTargets(clippedPass), DynamicPipelineState.Viewport)).RequireResource();
            using var uncutPipeline = commands.CreateGraphicsPipeline(new(uncut, layout,
                commands.DescribeRenderTargets(uncutPass), DynamicPipelineState.Viewport)).RequireResource();
            using var geometry = new ArrayGraphicsGeometry(layout, PrimitiveTopology.Triangles,
                new Dictionary<int, GpuVbo>(), proceduralVertices: 3);

            // Act: render both packaged generations in one boundary so the second pipeline disables the first one's clip state.
            using (var boundary = commands.EnterBoundary(new("Tests.ClipDistance.Graphics", true,
                       graphics: [clippedPipeline, uncutPipeline], operations: BoundaryOperations.RenderPasses)))
            {
                boundary.Run(() =>
                {
                    commands.BeginRenderPass(clippedPass);
                    commands.SetPipeline(clippedPipeline);
                    commands.SetDynamicState(new() { Viewport = commands.PassViewport });
                    commands.Draw(geometry.Inputs, new(0, 3));
                    Assert.True(GL.IsEnabled(EnableCap.ClipDistance0));
                    Assert.True(GL.IsEnabled(EnableCap.ClipDistance1));
                    for (int index = 2; index < maximum; index++)
                        Assert.False(GL.IsEnabled((EnableCap)((int)EnableCap.ClipDistance0 + index)));
                    commands.EndRenderPass();

                    commands.BeginRenderPass(uncutPass);
                    commands.SetPipeline(uncutPipeline);
                    commands.SetDynamicState(new() { Viewport = commands.PassViewport });
                    commands.Draw(geometry.Inputs, new(0, 3));
                    for (int index = 0; index < maximum; index++)
                        Assert.False(GL.IsEnabled((EnableCap)((int)EnableCap.ClipDistance0 + index)));
                    commands.EndRenderPass();
                });
            }

            // Assert: the first image contains only the positive-x half; the later inactive producer covers the whole triangle.
            Assert.Equal(new[] { 0f, 0f, 0f, 0f }, clippedTarget[0].ReadPixelsRegion(1, 4, 1, 1));
            Assert.Equal(new[] { 1f, 1f, 1f, 1f }, clippedTarget[0].ReadPixelsRegion(6, 4, 1, 1));
            Assert.Equal(new[] { 1f, 1f, 1f, 1f }, uncutTarget[0].ReadPixelsRegion(1, 4, 1, 1));
            Assert.True(GL.IsEnabled(EnableCap.ClipDistance0));
            for (int index = 1; index < maximum; index++)
                Assert.False(GL.IsEnabled((EnableCap)((int)EnableCap.ClipDistance0 + index)));

            // Act: apply the same compiler-prepared clipping state and fail after ending the pass inside its boundary.
            var failure = new InvalidOperationException("fixture failure after packaged clipped draw");
            using (var failedBoundary = commands.EnterBoundary(new("Tests.ClipDistance.FailedRestore", true,
                       graphics: [clippedPipeline], operations: BoundaryOperations.RenderPasses)))
            {
                var observed = Assert.Throws<InvalidOperationException>(() => failedBoundary.Run(() =>
                {
                    commands.BeginRenderPass(clippedPass);
                    commands.SetPipeline(clippedPipeline);
                    commands.SetDynamicState(new() { Viewport = commands.PassViewport });
                    commands.Draw(geometry.Inputs, new(0, 3));
                    commands.EndRenderPass();
                    throw failure;
                }));
                Assert.Same(failure, observed);
            }

            // Assert: exceptional boundary exit restores the exact native clip-enable mask captured on entry.
            for (int index = 0; index < maximum; index++)
                Assert.Equal(boundaryBaseline[index], GL.IsEnabled((EnableCap)((int)EnableCap.ClipDistance0 + index)));

            // Act: record the same packaged clipping pipeline and submit it through its frozen boundary.
            using var recordedTarget = CreateRenderTarget(8, 8, TextureFormat.Rgba8);
            var recordedPass = FramebufferTestSetup.DescribeAttachments(recordedTarget,
                [new(AttachmentIndex: 0, Load: AttachmentLoad.Clear, Clear: ColorClearValue.Float(0, 0, 0, 0))]);
            using var list = commands.CreateCommandList();
            list.BeginRenderPass(recordedPass);
            list.SetPipeline(clippedPipeline);
            list.SetDynamicState(new() { Viewport = new(0, 0, 8, 8) });
            list.Draw(geometry.Inputs, new(0, 3));
            list.EndRenderPass();
            list.Close(conditionalRenderingInactive: true);
            using (var recordedBoundary = commands.EnterBoundary(list.Boundary))
                Assert.Equal(CommandListState.Executed, commands.Submit(list).State);

            // Assert: the recorded executable retained the same compiler-derived clipping interface and boundary restoration.
            Assert.Equal(new[] { 0f, 0f, 0f, 0f }, recordedTarget[0].ReadPixelsRegion(1, 4, 1, 1));
            Assert.Equal(new[] { 1f, 1f, 1f, 1f }, recordedTarget[0].ReadPixelsRegion(6, 4, 1, 1));
            Assert.True(GL.IsEnabled(EnableCap.ClipDistance0));

            // Act: retain a recorded use of this exact executable, then retire its shader generation before admission.
            using var retiredList = commands.CreateCommandList();
            retiredList.SetPipeline(clippedPipeline);
            retiredList.Close(conditionalRenderingInactive: true);
            clipped.Dispose();

            // Assert: boundary admission rejects the exact retired executable before it can use stale clip facts.
            Assert.Throws<ObjectDisposedException>(() => commands.EnterBoundary(retiredList.Boundary));
        }
        finally
        {
            for (int index = 0; index < maximum; index++)
            {
                var capability = (EnableCap)((int)EnableCap.ClipDistance0 + index);
                if (original[index]) GL.Enable(capability); else GL.Disable(capability);
            }
            cache.InvalidateAll();
        }
    }

    /// <summary>Cached driver executables retain clip facts from their packaged SPIR-V stages.</summary>
    [Fact]
    public void CachedExecutableRetainsPackagedClipEnableCount()
    {
        // Arrange: use an isolated executable cache and a shader with two compiler-active clip outputs.
        EnsureContextValid();
        Assert.SkipWhen(GpuSupport.Graphics.ProgramBinaryFormats.IsEmpty,
            "The context does not expose a program-binary format for the optional cache route.");
        string directory = Path.Combine(Path.GetTempPath(), "VGE.ClipDistanceCacheTests", Guid.NewGuid().ToString("N"));
        var storage = new CountingDiskCacheFileSystem();
        try
        {
            using var cache = DriverProgramCache.UseStoreForTesting(new ProgramBinaryStore(directory, fileSystem: storage));
            var catalog = ShaderBuildTool.Spirv.TestShaderPrograms.Create();
            var plan = new ShaderLoadPlan(new ShaderSettings(catalog.Programs["tests/clip-distance-vertex"]));

            // Act: load a fresh generation, then load the same packaged inputs from the persisted driver binary.
            using (var fresh = BuiltShaderFixture.LoadProgram(plan))
            {
                Assert.False(DriverProgramCache.LastLoadWasHit);
                Assert.Equal(2, fresh.Interface.Graphics!.ClipDistanceCount);
            }
            using var cached = BuiltShaderFixture.LoadProgram(plan);

            // Assert: the cache path reconstructs the same prepared clip-enable facts.
            Assert.True(DriverProgramCache.LastLoadWasHit);
            Assert.Equal(2, cached.Interface.Graphics!.ClipDistanceCount);
        }
        finally
        {
            if (storage.DirectoryExists(directory)) storage.DeleteDirectory(directory, recursive: true);
        }
    }
    #endregion

    #region Private
    /// <summary>Submits one pipeline to its pass while keeping the target stage chain and viewport behavior intact.</summary>
    private static void Draw(RHICommandContext commands, RHIGraphicsPipeline pipeline,
        RHIRenderPassDescription pass, ArrayGraphicsGeometry geometry)
    {
        commands.BeginRenderPass(pass);
        commands.SetPipeline(pipeline);
        commands.SetDynamicState(new() { Viewport = commands.PassViewport });
        commands.Draw(geometry.Inputs, new(0, 3));
        commands.EndRenderPass();
    }
    #endregion
}
