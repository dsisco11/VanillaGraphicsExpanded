using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using Vintagestory.Client.NoObf;
namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks borrowed native bindings and engine shader ownership across the composed boundary.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
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
        fixture.MakeCurrent();
        var cache = StateCache.Current;
        int texture = GL.GenTexture(), sampler = GL.GenSampler(), buffer = GL.GenBuffer(), array = GL.GenVertexArray();
        int read = GL.GenFramebuffer(), draw = GL.GenFramebuffer();
        try
        {
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
            var footprint = new EngineBoundaryResources([(2, TextureTarget.Texture2D)], [1],
                [(BufferRangeTarget.UniformBuffer, 1), (BufferRangeTarget.ShaderStorageBuffer, 2)]);
            Assert.True(cache.TryBeginEngineBoundary(new EngineBoundaryDeclaration("Bindings"), out var scope, footprint),
                cache.BoundaryEntryFailure?.ToString());
            Assert.Equal((int)TextureUnit.Texture5, GL.GetInteger(GetPName.ActiveTexture));
            scope!.Run(() =>
            {
                cache.BindTexture(TextureTarget.Texture2D, 2, 0); cache.BindSampler(2, 0);
                cache.BindImageTexture(1, 0, 0, false, 0, TextureAccess.ReadOnly, SizedInternalFormat.Rgba8);
                cache.BindBufferBase(BufferRangeTarget.UniformBuffer, 1, 0);
                cache.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 2, 0);
                cache.BindVertexArray(0); cache.BindBuffer(BufferTarget.ArrayBuffer, 0);
                cache.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
                if (invalidate) cache.Invalidate(EPipelineState.Bindings);
            });
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
            long reads = cache.BoundaryQueries, textureCalls = cache.TextureBindCount, resourceCalls = cache.ResourceSlotBindCount;
            long checks = cache.BoundaryErrorChecks;
            Assert.True(cache.TryBeginEngineBoundary(new EngineBoundaryDeclaration("Warm"), out var warm, footprint)); warm!.Dispose();
            Assert.Equal(reads, cache.BoundaryQueries); Assert.Equal(textureCalls, cache.TextureBindCount);
            Assert.Equal(resourceCalls, cache.ResourceSlotBindCount);
            Assert.Equal(checks, cache.BoundaryErrorChecks);
        }
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

    /// <summary>Tracked retirement fails handoff instead of recreating or rebinding the retired borrowed name.</summary>
    [Fact]
    public void RetiredBorrowedTextureIsNotRebound()
    {
        fixture.MakeCurrent(); var cache = StateCache.Current;
        int texture = GL.GenTexture();
        GL.ActiveTexture(TextureUnit.Texture0); GL.BindTexture(TextureTarget.Texture2D, texture); cache.InvalidateAll();
        Assert.True(cache.TryBeginEngineBoundary(new EngineBoundaryDeclaration("Retirement"), out var scope,
            new EngineBoundaryResources([(0, TextureTarget.Texture2D)])));
        cache.DeleteTexture(texture);
        Assert.Throws<EngineBoundaryRestoreException>(() => scope!.Dispose());
        Assert.Equal(0, GL.GetInteger(GetPName.TextureBinding2D)); Assert.False(GL.IsTexture(texture));
        Assert.False(cache.TryGetCachedBoundTexture(TextureTarget.Texture2D, 0, out _));
        cache.InvalidateAll();
    }

    /// <summary>Unavailable slots fail entry without changing active texture selection or publishing a scope.</summary>
    [Fact]
    public void InvalidFootprintDoesNotBeginOptionalWork()
    {
        fixture.MakeCurrent(); var cache = StateCache.Current;
        cache.ActiveTexture(3);
        Assert.False(cache.TryBeginEngineBoundary(new EngineBoundaryDeclaration("Invalid"), out var scope,
            new EngineBoundaryResources([(int.MaxValue, TextureTarget.Texture2D)])));
        Assert.Null(scope); Assert.Equal((int)TextureUnit.Texture3, GL.GetInteger(GetPName.ActiveTexture));
        cache.ActiveTexture(0);
    }

    #endregion
    #region Shader ownership
    /// <summary>The existing UseScope restores real engine ownership before borrowed binding and pipeline handoff.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExistingShaderOwnerRestoresOnSuccessAndDrawFailure(bool failDraw)
    {
        fixture.MakeCurrent(); using var programs = new ComponentShaderPrograms();
        var previous = programs.Create<GpuProgramUseScopeTests.CountingShader>();
        var inner = programs.Create<GpuProgramUseScopeTests.CountingShader>();
        Assert.True(inner.EnsureReady());
        using (previous.UseScope())
        {
            var footprint = EngineBoundaryResources.From(inner.ProgramLayout.BinaryInterface!.PreparedBindings);
            var operationFailure = new InvalidOperationException("draw");
            Action run = () => Assert.True(EngineBoundaryExecution.TryRun(new EngineBoundaryDeclaration("Shader"), footprint, scope =>
            {
                scope.AddCleanup(EngineBoundaryCleanup.Shader, inner.UseScope());
                Assert.Same(inner, StateCache.ActiveProgram);
                if (failDraw) throw operationFailure;
            }));
            if (failDraw) Assert.Same(operationFailure, Assert.Throws<InvalidOperationException>(run)); else run();
            Assert.Same(previous, StateCache.ActiveProgram);
            Assert.Equal(previous.ProgramId, GL.GetInteger(GetPName.CurrentProgram));
            Assert.True(StateCache.Current.TryGetCachedCurrentProgram(out int cached)); Assert.Equal(previous.ProgramId, cached);
        }
    }

    /// <summary>Unowned executable footprints are rejected before the optional operation starts.</summary>
    [Fact]
    public void UnownedProgramIsRejectedBeforeOperation()
    {
        fixture.MakeCurrent(); using var programs = new ComponentShaderPrograms();
        var shader = programs.Create<GpuProgramUseScopeTests.CountingShader>(); Assert.True(shader.EnsureReady());
        var cache = StateCache.Current; cache.UseProgram(shader.ProgramId);
        try
        {
            bool ran = false;
            Assert.False(EngineBoundaryExecution.TryRun(new EngineBoundaryDeclaration("Foreign"), new EngineBoundaryResources(), _ => ran = true));
            Assert.False(ran); Assert.Equal(shader.ProgramId, GL.GetInteger(GetPName.CurrentProgram));
        }
        finally { cache.UseProgram(0); }
    }

    /// <summary>A real prior-owner activation failure remains visible alongside the draw error.</summary>
    [Fact]
    public void ShaderReactivationFailureIsNotOptionalFallback()
    {
        fixture.MakeCurrent(); using var programs = new ComponentShaderPrograms();
        var previous = programs.Create<GpuProgramUseScopeTests.CountingShader>();
        var inner = programs.Create<GpuProgramUseScopeTests.CountingShader>(); Assert.True(inner.EnsureReady());
        using (previous.UseScope())
        {
            try
            {
                var failure = Assert.Throws<AggregateException>(() => EngineBoundaryExecution.TryRun(
                    new EngineBoundaryDeclaration("ShaderFailure"), EngineBoundaryResources.From(inner.ProgramLayout.BinaryInterface!.PreparedBindings), scope =>
                    {
                        scope.AddCleanup(EngineBoundaryCleanup.Shader, inner.UseScope());
                        previous.FailSubmission = true;
                        throw new InvalidOperationException("draw failure");
                    }));
                Assert.True(EngineBoundaryRestoreException.IsRestorationFailure(failure));
                Assert.Equal("draw failure", failure.InnerExceptions[0].Message);
                Assert.False(StateCache.Current.TryGetCachedCurrentProgram(out _));
                Assert.Equal(0, GL.GetInteger(GetPName.CurrentProgram));
            }
            finally { previous.FailSubmission = false; previous.Use(); }
        }
    }

    /// <summary>Failed activation and its failed rollback remain distinguishable before a UseScope can be registered.</summary>
    [Fact]
    public void ActivationAndRollbackFailuresBothSurvive()
    {
        fixture.MakeCurrent(); using var programs = new ComponentShaderPrograms();
        var previous = programs.Create<GpuProgramUseScopeTests.CountingShader>();
        var inner = programs.Create<GpuProgramUseScopeTests.CountingShader>(); Assert.True(inner.EnsureReady());
        using (previous.UseScope())
        {
            try
            {
                var result = Assert.Throws<AggregateException>(() => EngineBoundaryExecution.TryRun(
                    new EngineBoundaryDeclaration("ActivationFailure"), EngineBoundaryResources.From(inner.ProgramLayout.BinaryInterface!.PreparedBindings), scope =>
                    {
                        previous.FailSubmission = true; inner.FailSubmission = true;
                        scope.AddCleanup(EngineBoundaryCleanup.Shader, inner.UseScope());
                    }));
                Assert.Equal(2, result.InnerExceptions.Count);
                Assert.IsType<InvalidOperationException>(result.InnerExceptions[0]);
                Assert.True(EngineBoundaryRestoreException.IsRestorationFailure(result));
                Assert.False(StateCache.Current.TryGetCachedCurrentProgram(out _));
            }
            finally { previous.FailSubmission = false; inner.FailSubmission = false; previous.Use(); }
        }
    }
    #endregion
    #endregion
}
