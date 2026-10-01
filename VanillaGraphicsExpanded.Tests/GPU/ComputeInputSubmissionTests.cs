using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.LumOn.Scene.Shaders;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks compute publication against actual indexed resources and nested graphics ownership.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class ComputeInputSubmissionTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Nested owners restore graphics, compute samplers and counter storage without resetting values.</summary>
    [Fact]
    public void NestedGraphicsAndComputeScopesRestoreRetainedInputs()
    {
        EnsureContextValid();
        using var programs = new ComponentShaderPrograms();
        using var assets = new BinaryShaderApiFixture();
        using var firstTexture = Texture2D.Create(1, 1, PixelInternalFormat.Rgba32ui);
        using var secondTexture = Texture2D.Create(1, 1, PixelInternalFormat.Rgba32ui);
        using var usage = Texture3D.Create(1, 1, 1, PixelInternalFormat.R32ui, textureTarget: TextureTarget.Texture2DArray);
        using var firstCounters = new ComponentAtomicCounters(17, 3);
        using var secondCounters = new ComponentAtomicCounters(29, 3);
        Assert.True(LumonSceneFeedbackMarkPagesComputeShader.TryCreate(assets.Api, out var firstOwner, out string firstLog), firstLog);
        Assert.True(LumonSceneFeedbackMarkPagesComputeShader.TryCreate(assets.Api, out var secondOwner, out string secondLog), secondLog);
        using var first = firstOwner!;
        using var second = secondOwner!;
        first.BindPatchIdGBuffer(firstTexture.TextureId);
        first.BindPageUsageStampImage(usage);
        first.BindDebugCounters(firstCounters.Buffer);
        second.BindPatchIdGBuffer(secondTexture.TextureId);
        second.BindPageUsageStampImage(usage);
        second.BindDebugCounters(secondCounters.Buffer);
        var graphics = programs.Create<LumOnHzbCopyShaderProgram>();
        graphics.PrimaryDepth = firstTexture.TextureId;
        using (graphics.UseScope())
        {
            using (first.UseScope())
            {
                Assert.Null(ShaderProgramBase.CurrentShaderProgram);
                AssertBound(first.ProgramId, firstTexture.TextureId, firstCounters.Buffer.BufferId);
                using (second.UseScope()) AssertBound(second.ProgramId, secondTexture.TextureId, secondCounters.Buffer.BufferId);
                AssertBound(first.ProgramId, firstTexture.TextureId, firstCounters.Buffer.BufferId);
                using (graphics.UseScope()) Assert.Same(graphics, ShaderProgramBase.CurrentShaderProgram);
                AssertBound(first.ProgramId, firstTexture.TextureId, firstCounters.Buffer.BufferId);
            }
            Assert.Same(graphics, ShaderProgramBase.CurrentShaderProgram);
            Assert.Equal(graphics.ProgramId, GL.GetInteger(GetPName.CurrentProgram));
        }
        Assert.Equal(17u, firstCounters.Read());
        Assert.Equal(29u, secondCounters.Read());
        Assert.Equal(0, GL.GetInteger(GetPName.CurrentProgram));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Range publication exposes initialized records only and rejects an overrun before binding.</summary>
    [Fact]
    public void StoragePublicationPreservesExactOffsetAndLength()
    {
        EnsureContextValid();
        using var assets = new BinaryShaderApiFixture();
        Assert.True(LumonSceneFeedbackCompactPagesComputeShader.TryCreate(assets.Api, out var owner, out string log), log);
        using var shader = owner!;
        Assert.True(shader.EnsureReady());
        using var storage = GpuShaderStorageBuffer.Create();
        int alignment = GL.GetInteger(GetPName.ShaderStorageBufferOffsetAlignment);
        storage.EnsureCapacity(alignment + 64, growExponentially: false);
        var range = new GpuStorageBufferBinding(storage, alignment, 32);
        ShaderBindingSubmission.ValidateStorageBlock(shader, "VgePageRequests", true, range);
        ShaderBindingSubmission.StorageBlock(shader, "VgePageRequests", range);
        GL.GetInteger(GetIndexedPName.ShaderStorageBufferBinding, 0, out int buffer);
        GL.GetInteger(GetIndexedPName.ShaderStorageBufferStart, 0, out int offset);
        GL.GetInteger(GetIndexedPName.ShaderStorageBufferSize, 0, out int length);
        Assert.Equal(storage.BufferId, buffer);
        Assert.Equal(alignment, offset);
        Assert.Equal(32, length);
        Assert.Throws<InvalidOperationException>(() => ShaderBindingSubmission.ValidateStorageBlock(shader, "VgePageRequests", true,
            new GpuStorageBufferBinding(storage, alignment, 128)));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion

    #region Private
    /// <summary>Observes executable and indexed bindings independently of the state cache.</summary>
    private static void AssertBound(int program, int texture, int counters)
    {
        Assert.Equal(program, GL.GetInteger(GetPName.CurrentProgram));
        GL.GetInteger((GetIndexedPName)All.AtomicCounterBufferBinding, 0, out int actualCounters);
        Assert.Equal(counters, actualCounters);
        int prior = GL.GetInteger(GetPName.ActiveTexture);
        GL.ActiveTexture(TextureUnit.Texture0);
        Assert.Equal(texture, GL.GetInteger(GetPName.TextureBinding2D));
        GL.ActiveTexture((TextureUnit)prior);
    }
    #endregion
}
