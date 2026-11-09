using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering;

/// <summary>Checks borrowed native bindings and engine shader ownership across the composed boundary.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class EngineBoundaryBindingTests(HeadlessGLFixture fixture)
{
    #region Public API
    #region Borrowed resources
    /// <summary>Restores texture/sampler/image/range aliases, active unit, quad geometry and independent framebuffers.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BorrowedResourceFootprintRestoresNativeState(bool invalidate)
    {
        // Arrange: Seed aliased bindings, indexed ranges, independent framebuffers, and a footprint containing only borrowed slots.
        Start(); var cache = OpenGlStateCache.Current;
        int texture = GL.GenTexture(), sampler = GL.GenSampler(), buffer = GL.GenBuffer(), array = GL.GenVertexArray();
        int read = GL.GenFramebuffer(), draw = GL.GenFramebuffer();
        VanillaGraphicsExpanded.Rendering.EngineBoundaryScope? scope = null;
        try
        {
            // Establish deliberately aliased native bindings, then declare exactly the borrowed footprint.
            GL.ActiveTexture(TextureUnit.Texture2); GL.BindTexture(TextureTarget.Texture2D, texture);
            GL.TexStorage2D(TextureTarget2d.Texture2D, 1, SizedInternalFormat.Rgba8, 2, 2);
            GL.BindSampler(2, sampler);
            GL.BindImageTexture(1, texture, 0, false, 0, TextureAccess.ReadWrite, SizedInternalFormat.Rgba8);
            GL.BindBuffer(BufferTarget.UniformBuffer, buffer);
            GL.BufferData(BufferTarget.UniformBuffer, 256, IntPtr.Zero, BufferUsageHint.DynamicDraw);
            GL.BindBufferRange(BufferRangeTarget.UniformBuffer, 1, buffer, IntPtr.Zero, 64);
            GL.BindBufferRange(BufferRangeTarget.ShaderStorageBuffer, 2, buffer, IntPtr.Zero, 128);
            GL.BindBuffer(BufferTarget.UniformBuffer, 0); GL.BindBuffer(BufferTarget.ShaderStorageBuffer, 0);
            GL.BindVertexArray(array); GL.BindBuffer(BufferTarget.ArrayBuffer, buffer);
            GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, read); GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, draw);
            GL.ActiveTexture(TextureUnit.Texture5); cache.InvalidateAll();
            Assert.Equal(ErrorCode.NoError, GL.GetError());
            var footprint = new EngineBoundaryResources([(2, TextureDimension.Two)], [1],
                [(BufferRangeTarget.UniformBuffer, 1), (BufferRangeTarget.ShaderStorageBuffer, 2)]);
            Assert.True(cache.TryBeginEngineBoundary(new EngineBoundaryDeclaration("Bindings"), out scope, footprint),
                cache.BoundaryEntryFailure?.ToString());
            Assert.Equal((int)TextureUnit.Texture5, GL.GetInteger(GetPName.ActiveTexture));
            // Act: Mutate each declared binding family, optionally invalidating cached knowledge before handback.
            scope!.Run(() =>
            {
                // Mutate every declared binding family and optionally invalidate before restoration.
                OpenGLImageBindings.BindTexture(cache, TextureTarget.Texture2D, 2, 0); OpenGLImageBindings.BindSampler(cache, 2, 0);
                OpenGLImageSlotBindings.BindImageTexture(cache, 1, 0, 0, false, 0, TextureAccess.ReadOnly, SizedInternalFormat.Rgba8);
                OpenGLBufferBindings.BindBufferBase(cache, BufferRangeTarget.UniformBuffer, 1, 0);
                OpenGLBufferBindings.BindBufferBase(cache, BufferRangeTarget.ShaderStorageBuffer, 2, 0);
                OpenGLVertexSelection.Bind(cache, 0);
                OpenGLFramebufferSelection.Bind(cache, FramebufferTarget.Framebuffer, 0);
                if (invalidate) cache.Invalidate(EPipelineState.Bindings);
            });
            // Assert: Native bindings and indexed ranges return to their captured values after either handback path.
            Assert.Equal((int)TextureUnit.Texture5, GL.GetInteger(GetPName.ActiveTexture));
            Assert.Equal(read, GL.GetInteger(GetPName.ReadFramebufferBinding));
            Assert.Equal(draw, GL.GetInteger(GetPName.DrawFramebufferBinding));
            Assert.Equal(array, GL.GetInteger(GetPName.VertexArrayBinding));
            Assert.Equal(buffer, GL.GetInteger(GetPName.ArrayBufferBinding));
            Assert.Equal(0, GL.GetInteger(GetPName.UniformBufferBinding));
            Assert.Equal(0, GL.GetInteger(GetPName.ShaderStorageBufferBinding));
            GL.GetInteger((GetIndexedPName)GetPName.UniformBufferBinding, 1, out int uniform);
            GL.GetInteger64((GetIndexedPName)All.UniformBufferSize, 1, out long size);
            Assert.Equal(buffer, uniform); Assert.Equal(64, size);
            GL.GetInteger((GetIndexedPName)GetPName.ShaderStorageBufferBinding, 2, out int storage);
            GL.GetInteger64((GetIndexedPName)All.ShaderStorageBufferSize, 2, out long storageSize);
            Assert.Equal(buffer, storage); Assert.Equal(128, storageSize);
            GL.GetInteger((GetIndexedPName)All.ImageBindingName, 1, out int image);
            GL.GetInteger((GetIndexedPName)All.ImageBindingAccess, 1, out int access);
            Assert.Equal(texture, image); Assert.Equal((int)TextureAccess.ReadWrite, access);
            GL.ActiveTexture(TextureUnit.Texture2);
            Assert.Equal(texture, GL.GetInteger(GetPName.TextureBinding2D));
            Assert.Equal(sampler, GL.GetInteger(GetPName.SamplerBinding));
            GL.ActiveTexture(TextureUnit.Texture5);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
#if DEBUG
            // A warmed entry should observe the known footprint without repeating native queries or binds.
            long reads = cache.BoundaryQueries, textureCalls = cache.TextureBindCount, resourceCalls = cache.ResourceSlotBindCount;
            long checks = cache.BoundaryErrorChecks;
#endif
            Assert.True(cache.TryBeginEngineBoundary(new EngineBoundaryDeclaration("Warm"), out var warm, footprint)); warm!.Dispose();
#if DEBUG
            Assert.Equal(reads, cache.BoundaryQueries);
            Assert.Equal(textureCalls, cache.TextureBindCount);
            Assert.Equal(resourceCalls, cache.ResourceSlotBindCount);
            Assert.Equal(checks, cache.BoundaryErrorChecks);
#endif
        }
        finally
        {
            try { scope?.Dispose(); }
            finally
            {
                GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0); GL.BindVertexArray(0);
                GL.BindBuffer(BufferTarget.ArrayBuffer, 0); GL.BindSampler(2, 0);
                GL.BindImageTexture(1, 0, 0, false, 0, TextureAccess.ReadOnly, SizedInternalFormat.Rgba8);
                GL.BindBufferBase(BufferRangeTarget.UniformBuffer, 1, 0); GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 2, 0);
                GL.DeleteTexture(texture); GL.DeleteSampler(sampler); GL.DeleteBuffer(buffer); GL.DeleteVertexArray(array);
                GL.DeleteFramebuffer(read); GL.DeleteFramebuffer(draw); GL.ActiveTexture(TextureUnit.Texture0); cache.InvalidateAll();
            }
        }
    }

    /// <summary>Tracked retirement fails handoff instead of recreating or rebinding the retired borrowed name.</summary>
    [Fact]
    public void RetiredBorrowedTextureIsNotRebound()
    {
        // Arrange: Admit a boundary that captures one borrowed texture binding.
        Start(); var cache = OpenGlStateCache.Current;
        int texture = GL.GenTexture();
        VanillaGraphicsExpanded.Rendering.EngineBoundaryScope? scope = null;
        try
        {
            GL.ActiveTexture(TextureUnit.Texture0); GL.BindTexture(TextureTarget.Texture2D, texture); cache.InvalidateAll();
            Assert.True(cache.TryBeginEngineBoundary(new EngineBoundaryDeclaration("Retirement"), out scope,
                new EngineBoundaryResources([(0, TextureDimension.Two)])));
            // Act: Retire the borrowed object before handoff so restoration cannot legally rebind it.
            VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Texture, texture));
            Assert.Throws<EngineBoundaryRestoreException>(() => scope!.Dispose());
            // Assert: The retired name stays deleted and its cached binding knowledge is withdrawn.
            Assert.Equal(0, GL.GetInteger(GetPName.TextureBinding2D)); Assert.False(GL.IsTexture(texture));
            Assert.False(cache.TryGetCachedBoundTexture(TextureTarget.Texture2D, 0, out _));
        }
        finally
        {
            try { scope?.Dispose(); }
            finally { cache.InvalidateAll(); }
        }
    }

    /// <summary>Unavailable slots fail entry without changing active texture selection or publishing a scope.</summary>
    [Fact]
    public void InvalidFootprintDoesNotBeginOptionalWork()
    {
        // Arrange: Select a nondefault texture unit before asking for a footprint with an unsupported unit index.
        Start();
        var cache = OpenGlStateCache.Current;
        OpenGLImageBindings.ActiveTexture(cache, 3);
        // Act: Attempt boundary admission for the unavailable slot.
        Assert.False(cache.TryBeginEngineBoundary(new EngineBoundaryDeclaration("Invalid"), out var scope,
            new EngineBoundaryResources([(int.MaxValue, TextureDimension.Two)])));
        // Assert: Admission publishes no scope and leaves the active unit untouched.
        Assert.Null(scope); Assert.Equal((int)TextureUnit.Texture3, GL.GetInteger(GetPName.ActiveTexture));
        OpenGLImageBindings.ActiveTexture(cache, 0);
    }

    #endregion
    #region Executable restoration
    /// <summary>Restoration selects the original native executable without activating shader owners or replaying inputs.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OriginalExecutableRestoresOnSuccessAndOperationFailure(bool fail)
    {
        // Arrange: Select the prior executable and prepare nested shader inputs under the declared pipeline footprint.
        Start();
        using var programs=new ComponentShaderPrograms();
        var previous=programs.Create<RasterDepthCopyShaderProgram>();
        var inner=programs.Create<RasterDepthCopyShaderProgram>();
        var commands=RHICommandContext.Current;
        using var source=commands.CreateTexture2D(new(){Dimension=TextureDimension.Two,Width=1,Height=1,
            Format=TextureFormat.R32f,MipLevels=1,Storage=ResourceStorageMode.Mutable}).RequireResource();
        inner.PrimaryDepth=source;
        using var pipeline=commands.CreateGraphicsPipeline(new(inner.Shader,new([]),new([new(TextureFormat.R32f)]),
            VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions.DynamicPipelineState.Viewport)).RequireResource();
        OpenGLProgramSelection.Select(OpenGlStateCache.Current,previous.ProgramId);
        var expected=new ArithmeticException("fixture operation");
        void Execute()
        {
            using var boundary=commands.EnterBoundary(new("Tests.ExecutableRestore",true,graphics:[pipeline]));
            boundary.Run(()=>
            {
                commands.SetPipeline(pipeline);commands.BindResources(inner.CaptureBindings());
                Assert.Equal(inner.ProgramId,GL.GetInteger(GetPName.CurrentProgram));
                if(fail)throw expected;
            });
        }
        try
        {
            // Act: Run nested pipeline selection and resource binding, optionally throwing inside the boundary callback.
            if(fail)Assert.Same(expected,Assert.Throws<ArithmeticException>(Execute));else Execute();
            // Assert: Normal and exceptional exits select the original executable and restore its cached knowledge.
            Assert.Equal(previous.ProgramId,GL.GetInteger(GetPName.CurrentProgram));
            Assert.True(OpenGlStateCache.Current.TryGetCachedCurrentProgram(out int cached));Assert.Equal(previous.ProgramId,cached);
        }
        finally{OpenGLProgramSelection.Select(OpenGlStateCache.Current,0);}
    }

    /// <summary>Pipeline selection cannot widen an already admitted execution footprint.</summary>
    [Fact]
    public void UndeclaredExecutableRejectsBeforePublication()
    {
        // Arrange: Admit an empty executable footprint while retaining a concrete pipeline for the rejected selection.
        Start();using var programs=new ComponentShaderPrograms();
        var shader=programs.Create<RasterDepthCopyShaderProgram>();var commands=RHICommandContext.Current;
        using var pipeline=commands.CreateGraphicsPipeline(new(shader.Shader,new([]),new([new(TextureFormat.R32f)]),
            VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions.DynamicPipelineState.Viewport)).RequireResource();
        int previous=GL.GetInteger(GetPName.CurrentProgram);
        using var boundary=commands.EnterBoundary(new("Tests.UndeclaredExecutable",true));
        // Act: Attempt pipeline selection from inside the admitted boundary.
        boundary.Run(()=>Assert.Throws<InvalidOperationException>(()=>commands.SetPipeline(pipeline)));
        // Assert: Rejection occurs before changing the native program selection.
        Assert.Equal(previous,GL.GetInteger(GetPName.CurrentProgram));
    }
    #endregion
    #endregion
    #region Private
    /// <summary>Admits the final backend before directly inspecting private cache restoration machinery.</summary>
    private void Start()
    {
        fixture.MakeCurrent();
        RHICommandContext.Current.RequireContext();
    }
    #endregion
}
