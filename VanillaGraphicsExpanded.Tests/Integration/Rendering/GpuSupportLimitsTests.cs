using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Tests.Support.GPU.Native;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering;

/// <summary>Verifies shared capability limits against the current native implementation.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class GpuSupportLimitsTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Shared resource and feature values agree with native limits and advertised support.</summary>
    [Fact]
    public void AdditionalCapabilitiesMatchNativeImplementation()
    {
        // Arrange: Initialize the shared capability snapshot for the current real GL context.
        fixture.MakeCurrent();
        // Act: Compare queried native limits and advertised features with that snapshot.
        var support = GpuSupport.Graphics;
        // Assert: Values and extension/version-derived support agree without leaving a GL error.
        Assert.Equal(GL.GetInteger(GetPName.UniformBufferOffsetAlignment), support.UniformBufferOffsetAlignment);
        Assert.Equal(GL.GetInteger(GetPName.MaxTextureBufferSize), support.MaxTextureBufferSize);
        if (support.SupportsTextureBufferRange)
            Assert.Equal(GL.GetInteger(GetPName.TextureBufferOffsetAlignment), support.TextureBufferOffsetAlignment);
        Assert.Equal(GL.GetInteger(GetPName.MaxLabelLength), support.MaxLabelLength);
        int count = GL.GetInteger(GetPName.NumProgramBinaryFormats);
        int[] formats = new int[count];
        if (count > 0) GL.GetInteger(GetPName.ProgramBinaryFormats, formats);
        Assert.Equal(formats, support.ProgramBinaryFormats.ToArray());
        Assert.Equal(support.ApiVersion >= new Version(4, 3) || GpuSupport.Supports("GL_ARB_texture_buffer_range"), support.SupportsTextureBufferRange);
        Assert.Equal(support.ApiVersion >= new Version(4, 4) || GpuSupport.Supports("GL_ARB_clear_texture"), support.SupportsClearTexture);
        Assert.Equal(GpuSupport.Supports("GL_ARB_sparse_texture"), support.SupportsSparseTexture);
        Assert.Equal(GpuSupport.Supports("GL_ARB_sparse_buffer"), support.SupportsSparseBuffer);
        Assert.Equal(GpuSupport.Supports("GL_ARB_parallel_shader_compile"), support.SupportsArbParallelShaderCompile);
        Assert.Equal(GpuSupport.Supports("GL_KHR_parallel_shader_compile"), support.SupportsKhrParallelShaderCompile);
        Assert.Equal(support.ApiVersion >= new Version(4, 3) || GpuSupport.Supports("GL_ARB_internalformat_query2"), support.SupportsInternalFormatQuery2);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Checks resource and dynamic limits using their exact GL query names.</summary>
    [Fact]
    public void SharedLimitsMatchNativeImplementation()
    {
        // Arrange: Ensure context capabilities are published before reading native limit values.
        fixture.MakeCurrent();
        GpuSupport.EnsureCurrentContext();
        // Act and assert: Compare each shared resource and dynamic-state limit with its exact driver query.
        int[] viewport = new int[2];
        GL.GetInteger(GetPName.MaxViewportDims, viewport);
        Assert.Equal(viewport[0], GpuSupport.MaxViewportWidth);
        Assert.Equal(viewport[1], GpuSupport.MaxViewportHeight);
        Assert.Equal(GL.GetInteger(GetPName.ShaderStorageBufferOffsetAlignment), GpuSupport.ShaderStorageBufferOffsetAlignment);
        Assert.Equal(GL.GetInteger(GetPName.MaxDrawBuffers), GpuSupport.MaxDrawBuffers);
        Assert.Equal(GL.GetInteger(GetPName.MaxPatchVertices), GpuSupport.MaxPatchVertices);
        var pipeline = GpuSupport.Graphics;
        Assert.True(pipeline.Graphics33);
        Assert.Equal(GpuSupport.MaxDrawBuffers, pipeline.MaxDrawBuffers);
        Assert.Equal(GpuSupport.MaxVertexAttribs, pipeline.MaxVertexAttributes);
        Assert.Equal(GL.GetInteger(GetPName.MaxVertexAttribBindings), pipeline.MaxVertexBindings);
        Assert.Equal(GL.GetInteger(GetPName.MaxVertexAttribRelativeOffset), pipeline.MaxVertexRelativeOffset);
        // MAX_VERTEX_ATTRIB_STRIDE is new core 4.4 state, even if a 4.3 driver accepts the query.
        // https://registry.khronos.org/OpenGL/specs/gl/glspec44.core.pdf Appendix G.1.
        Assert.Equal(GpuSupport.ApiVersion >= new Version(4, 4) ? GL.GetInteger((GetPName)All.MaxVertexAttribStride) : int.MaxValue, pipeline.MaxVertexStride);
        Assert.Equal(GL.GetInteger(GetPName.MaxSampleMaskWords), pipeline.MaxSampleMaskWords);
        Assert.Equal(GpuSupport.MaxSamples, pipeline.MaxSamples);
        float[] line = new float[2], point = new float[2];
        GL.GetFloat(GetPName.AliasedLineWidthRange, line);
        GL.GetFloat(GetPName.AliasedPointSizeRange, point);
        Assert.Equal(line[0], pipeline.MinLineWidth);
        Assert.Equal(line[1], pipeline.MaxLineWidth);
        Assert.Equal(point[0], pipeline.MinPointSize);
        Assert.Equal(point[1], pipeline.MaxPointSize);
        Assert.Equal(GL.GetInteger(GetPName.MaxCombinedTextureImageUnits), GpuSupport.MaxCombinedTextureImageUnits);
        Assert.Equal(GL.GetInteger((GetPName)All.MaxImageUnits), GpuSupport.MaxImageUnits);
        Assert.Equal(GL.GetInteger((GetPName)All.MaxCombinedImageUniforms), GpuSupport.MaxCombinedImageUnits);
        Assert.Equal(GL.GetInteger(GetPName.MaxUniformBufferBindings), GpuSupport.MaxUniformBufferBindings);
        Assert.Equal(GL.GetInteger(GetPName.MaxShaderStorageBufferBindings), GpuSupport.MaxShaderStorageBufferBindings);
        Assert.Equal(GL.GetInteger((GetPName)All.MaxAtomicCounterBufferBindings), GpuSupport.MaxAtomicCounterBufferBindings);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Mutable state invalidation neither recaptures capabilities nor consumes pending GL errors.</summary>
    [Fact]
    public void WarmCapabilitiesSurviveStateInvalidationWithoutNativePolling()
    {
        // Arrange: Warm the capability snapshot and record its capture count when available.
        fixture.MakeCurrent();
        GpuSupport.EnsureCurrentContext();
#if DEBUG
        long captures = GpuSupport.CaptureCount;
#endif
        // Act: Invalidate mutable state, queue a native query error, then request the same capabilities again.
        OpenGlStateCache.Current.InvalidateAll();
        GL.GetInteger((GetPName)(-1));
        GpuSupport.EnsureCurrentContext();
#if DEBUG
        // Assert: Capability reuse neither recaptures support nor consumes the unrelated pending GL error.
        Assert.Equal(captures, GpuSupport.CaptureCount);
#endif
        Assert.Equal(ErrorCode.InvalidEnum, GL.GetError());
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Repeated reads reuse the shared instance without recapturing limits or consuming pending native errors.</summary>
    [Fact]
    public void RepeatedReadsReuseSharedCapabilities()
    {
        // Arrange: Warm the shared capability instance before installing an observable pending GL error.
        fixture.MakeCurrent();
        GpuSupport.EnsureCurrentContext();
#if DEBUG
        long captures = GpuSupport.CaptureCount;
#endif
        // Act: Read both capability views repeatedly while the driver error remains pending.
        // Assert: Reads share the same capability instance without recapture and leave the queued GL error for the caller.
        GL.GetInteger((GetPName)(-1));
        var first = GpuSupport.Graphics;
        var second = GpuSupport.Graphics;
        Assert.Same(first, second);
        Assert.Equal(first, second);
        Assert.Equal(GpuSupport.SupportsIndependentBlend, first.IndependentBlend);
        Assert.Equal(GpuSupport.SupportsSampleShading, first.SampleShading);
        Assert.Equal(GpuSupport.SupportsFixedIndexRestart, first.FixedIndexRestart);
        Assert.Equal(GpuSupport.SupportsDoubleAttributes, first.DoubleAttributes);
        Assert.Equal(GpuSupport.SupportsDepthClamp, first.DepthClamp);
        Assert.Equal(GpuSupport.IsCoreProfile, first.CoreProfile);
        Assert.Equal(GpuSupport.MaxVertexBindings, first.MaxVertexBindings);
        Assert.Equal(GpuSupport.MaxVertexRelativeOffset, first.MaxVertexRelativeOffset);
        Assert.Equal(GpuSupport.MaxVertexStride, first.MaxVertexStride);
        Assert.Equal(GpuSupport.MaxSampleMaskWords, first.MaxSampleMaskWords);
        Assert.Equal(GpuSupport.MaxPatchVertices, first.MaxPatchVertices);
        Assert.Equal(GpuSupport.MinLineWidth, first.MinLineWidth);
        Assert.Equal(GpuSupport.MaxLineWidth, first.MaxLineWidth);
        Assert.Equal(GpuSupport.MinPointSize, first.MinPointSize);
        Assert.Equal(GpuSupport.MaxPointSize, first.MaxPointSize);
#if DEBUG
        Assert.Equal(captures, GpuSupport.CaptureCount);
#endif
        Assert.Equal(ErrorCode.InvalidEnum, GL.GetError());
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Publishing refreshed support preserves retained immutable values and their nested compute limits.</summary>
    [Fact]
    public void ForcedInitializationPublishesCompleteImmutableCapabilities()
    {
        // Arrange: Retain the currently published immutable values and their nested compute-limit arrays.
        fixture.MakeCurrent();
        GpuSupport.EnsureCurrentContext();
        var retained = GpuSupport.Graphics;
#if DEBUG
        long captures = GpuSupport.CaptureCount;
#endif
        int[] retainedCounts = retained.MaxComputeWorkGroupCount.ToArray();
        int[] retainedSizes = retained.MaxComputeWorkGroupSize.ToArray();
        // Immutable collection edits produce separate data without altering the published owner.
        var edited = retained with { MaxComputeWorkGroupCount = retained.MaxComputeWorkGroupCount.SetItem(0, -1) };
        Assert.Equal(-1, edited.MaxComputeWorkGroupCount[0]);
        Assert.Equal(retainedCounts, retained.MaxComputeWorkGroupCount.ToArray());

        // Act: Force a fresh native capture while retaining the old published object for comparison.
        GpuSupport.Initialize(force: true);
        var current = GpuSupport.Graphics;
        // Assert: New support is complete, while retained snapshot values remain detached and unchanged.
        Assert.NotSame(retained, current);
        Assert.Same(current, GpuSupport.Graphics);
#if DEBUG
        Assert.Equal(captures + 1, GpuSupport.CaptureCount);
#endif
        Assert.Equal(retainedCounts, retained.MaxComputeWorkGroupCount.ToArray());
        Assert.Equal(retainedSizes, retained.MaxComputeWorkGroupSize.ToArray());
        Assert.Equal(retainedCounts, current.MaxComputeWorkGroupCount.ToArray());
        Assert.Equal(retainedSizes, current.MaxComputeWorkGroupSize.ToArray());
        Assert.Equal(GL.GetString(StringName.Version), current.VersionString);
        Assert.Equal(GL.GetString(StringName.Vendor), current.VendorString);
        Assert.Equal(GL.GetString(StringName.Renderer), current.RendererString);
        Assert.Equal(GL.GetString(StringName.ShadingLanguageVersion), current.ShadingLanguageVersionString);
        Assert.Equal(GL.GetInteger(GetPName.ContextFlags), current.ContextFlags);
        Assert.Equal(GL.GetInteger(GetPName.MaxTextureSize), current.MaxTextureSize);
        Assert.Equal(GL.GetInteger(GetPName.Max3DTextureSize), current.Max3DTextureSize);
        Assert.Equal(GL.GetInteger(GetPName.MaxUniformBlockSize), current.MaxUniformBlockSize);
        Assert.Equal(GL.GetInteger(GetPName.MaxComputeWorkGroupInvocations), current.MaxComputeWorkGroupInvocations);
        Assert.Equal(GL.GetInteger((GetPName)All.MaxComputeSharedMemorySize), current.MaxComputeSharedMemorySize);
        Assert.Equal(GpuSupport.Supports("GL_ARB_buffer_storage") || current.ApiVersion >= new Version(4, 4), current.SupportsArbBufferStorage);
        Assert.Equal(GpuSupport.Supports("GL_ARB_compute_shader") || current.ApiVersion >= new Version(4, 3), current.SupportsArbComputeShader);
        Assert.Equal(current.MaxTextureSize, GpuSupport.MaxTextureSize);
        Assert.Equal(current.MaxUniformBlockSize, GpuSupport.MaxUniformBlockSize);
        Assert.Equal(current.SupportsArbBufferStorage, GpuSupport.SupportsArbBufferStorage);
        Assert.Equal(current.ApiVersion, GpuSupport.ApiVersion);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    #endregion
}
