using System.Reflection;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.LumOn.Shaders;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises staged inputs against the production upsample program and real uniform allocator.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class ShaderInputSubmissionTests : RenderTestBase
{
    /// <summary>Uses the mandatory headless context without starting the game.</summary>
    public ShaderInputSubmissionTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Public API
    /// <summary>Each HZB draw publishes one source mip and retains resource edits until that draw.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HzbMipDrawsPublishOneBlockAndDeferResourceChanges(bool persistent)
    {
        EnsureContextValid();
        using var programs = new ComponentShaderPrograms();
        using var first = DynamicTexture2D.CreateMipmapped(4, 4, PixelInternalFormat.R32f, 3);
        using var second = DynamicTexture2D.CreateMipmapped(4, 4, PixelInternalFormat.R32f, 3);
        using var target = CreateRenderTarget(1, 1, PixelInternalFormat.R32f);
        using var draws = new VanillaGraphicsExpanded.Tests.GPU.Helpers.ShaderTestFramework();
        using var ring = new GpuUniformRingBuffer(4096, 1, persistent);
        ring.BeginFrame(0);
        GpuUniformRingSystem.SetCurrent(ring);
        try
        {
            var shader = programs.Create<LumOnHzbDownsampleShaderProgram>();
            int priorTexture = BoundTextures()[0];
            for (int mip = 0; mip < 2; mip++)
            {
                // Repeated assignments remain CPU work even between consecutive draws.
                shader.HzbDepth = first;
                shader.HzbDepth = mip == 0 ? first : second;
                shader.SrcMip = 2;
                shader.SrcMip = mip;
                Assert.Equal(mip, ring.AllocationsWritten);
                Assert.Equal(priorTexture, BoundTextures()[0]);
                draws.RenderQuadTo(shader, target);
                Assert.Equal(mip + 1, ring.AllocationsWritten);
                Assert.Equal(mip, BitConverter.SingleToInt32Bits(SubmittedDepthSigma()));
                priorTexture = mip == 0 ? first.TextureId : second.TextureId;
                Assert.Equal(priorTexture, BoundTextures()[0]);
            }
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { GpuUniformRingSystem.ClearCurrent(); }
    }

    /// <summary>Scope restoration binds the previous owner's actual retained textures and parameter bytes.</summary>
    [Fact]
    public void NestedScopeRestoresRetainedResources()
    {
        EnsureContextValid();
        using var programs = new ComponentShaderPrograms();
        using var firstTexture = Texture2D.Create(1, 1, PixelInternalFormat.Rgba16f);
        using var secondTexture = Texture2D.Create(1, 1, PixelInternalFormat.Rgba16f);
        using var frame = GpuUniformBuffer.Create();
        frame.Allocate(544);
        using var ring = new GpuUniformRingBuffer(4096, 1, false);
        ring.BeginFrame(0);
        GpuUniformRingSystem.SetCurrent(ring);
        try
        {
            var first = programs.Create<LumOnUpsampleShaderProgram>();
            var second = programs.Create<LumOnUpsampleShaderProgram>();
            AssignResources(first, firstTexture, frame, 0.25f);
            AssignResources(second, secondTexture, frame, 0.75f);
            using (first.UseScope())
            {
                Assert.Equal(firstTexture.TextureId, BoundTextures()[0]);
                Assert.Equal(0.25f, SubmittedDepthSigma());
                using (second.UseScope())
                {
                    Assert.Equal(secondTexture.TextureId, BoundTextures()[0]);
                    Assert.Equal(0.75f, SubmittedDepthSigma());
                }
                Assert.Equal(firstTexture.TextureId, BoundTextures()[0]);
                Assert.Equal(0.25f, SubmittedDepthSigma());
            }
            Assert.Equal(3, ring.AllocationsWritten);
        }
        finally { GpuUniformRingSystem.ClearCurrent(); }
    }

    /// <summary>Ordinary assignments retain partial edits and writes one parameter block at each use boundary.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UpsampleAssignmentsDeferUploadsAndRetainValues(bool persistent)
    {
        EnsureContextValid();
        using var programs = new ComponentShaderPrograms();
        using var texture = Texture2D.Create(1, 1, PixelInternalFormat.Rgba16f);
        using var frame = GpuUniformBuffer.Create();
        frame.Allocate(544);
        using var ring = new GpuUniformRingBuffer(4096, 1, persistent);
        ring.BeginFrame(0);
        GpuUniformRingSystem.SetCurrent(ring);
        try
        {
            var shader = programs.Create<LumOnUpsampleShaderProgram>();
            int[] priorTextures = BoundTextures();
                        shader.FrameUniformBuffer = frame;
            shader.IndirectHalf = texture;
            shader.PrimaryDepth = texture.TextureId;
            shader.GBufferNormal = texture.TextureId;
            shader.UpsampleDepthSigma = 0.25f;
            shader.UpsampleNormalSigma = 12f;
            shader.UpsampleSpatialSigma = 2f;
            shader.HoleFillRadius = 3;
            shader.HoleFillMinConfidence = 0.5f;

            Assert.Equal(0, ring.AllocationsWritten);
            Assert.Equal(priorTextures, BoundTextures());
            Assert.Null(ShaderProgramBase.CurrentShaderProgram);
            GpuUniformRingSystem.ClearCurrent();
            Assert.False(shader.TryUse());
            Assert.True(Parameters(shader).IsDirty);
            Assert.Null(ShaderProgramBase.CurrentShaderProgram);
            GpuUniformRingSystem.SetCurrent(ring);
            ((Vintagestory.API.Client.IShaderProgram)shader).Use();
            Assert.Equal(1, ring.AllocationsWritten);
            shader.Stop();
            shader.UpsampleDepthSigma = 0.75f;
            Assert.Equal(1, ring.AllocationsWritten);
            Assert.Equal(12f, UboPacking.ReadFloat(Parameters(shader).Bytes, 4));
            shader.Use();
            Assert.Equal(2, ring.AllocationsWritten);
            // The retained resource set remains usable after program replacement and page reuse.
            shader.InvalidateAssets();
            GL.Finish();
            ring.BeginFrame(0);
            shader.Use();
            Assert.Equal(3, ring.AllocationsWritten);
            Assert.Equal(shader.ProgramId, GL.GetInteger(GetPName.CurrentProgram));
            Assert.Equal(0.75f, UboPacking.ReadFloat(Parameters(shader).Bytes, 0));
            shader.Stop();
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { GpuUniformRingSystem.ClearCurrent(); }
    }

    /// <summary>The generated anchor submission publishes one retained CPU block after ordinary assignments.</summary>
    [Fact]
    public void ProbeAnchorGeneratedSubmissionDefersUploadsAndRetainsValues()
    {
        EnsureContextValid();
        using var programs = new ComponentShaderPrograms();
        using var texture = Texture2D.Create(1, 1, PixelInternalFormat.Rgba16f);
        using var frame = GpuUniformBuffer.Create();
        frame.Allocate(544);
        using var ring = new GpuUniformRingBuffer(4096, 1, false);
        ring.BeginFrame(0);
        GpuUniformRingSystem.SetCurrent(ring);
        try
        {
            var shader = programs.Create<LumOnProbeAnchorShaderProgram>();
            int[] priorTextures = BoundTextures();
            shader.FrameUniformBuffer = frame;
            shader.PrimaryDepth = texture.TextureId;
            shader.GBufferNormal = texture.TextureId;
            shader.PmjJitter = texture;
            shader.DepthDiscontinuityThreshold = 0.25f;
            shader.DepthDiscontinuityThreshold = 0.75f;
            Assert.Equal(0, ring.AllocationsWritten);
            Assert.Equal(priorTextures, BoundTextures());
            shader.Use();
            Assert.Equal(1, ring.AllocationsWritten);
            Assert.Equal(0.75f, SubmittedDepthSigma(48));
            shader.Use();
            Assert.Equal(2, ring.AllocationsWritten);
            Assert.Equal(0.75f, SubmittedDepthSigma(48));
            shader.Stop();
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { GpuUniformRingSystem.ClearCurrent(); }
    }
    /// <summary>Missing required resources fail use before any parameter allocation can be consumed.</summary>
    [Fact]
    public void MissingRequiredUpsampleInputsPreventSubmission()
    {
        EnsureContextValid();
        using var programs = new ComponentShaderPrograms();
        using var ring = new GpuUniformRingBuffer(4096, 1, false);
        ring.BeginFrame(0);
        GpuUniformRingSystem.SetCurrent(ring);
        try
        {
            var shader = programs.Create<LumOnUpsampleShaderProgram>();
            Assert.False(shader.TryUse());
            Assert.Equal(0, ring.AllocationsWritten);
            Assert.Null(ShaderProgramBase.CurrentShaderProgram);
        }
        finally { GpuUniformRingSystem.ClearCurrent(); }
    }

    /// <summary>An optimized-out normal sampler and unused shared world-probe block need no prepared resource.</summary>
    [Fact]
    public void InactiveUpsampleInputsCanRemainUnset()
    {
        EnsureContextValid();
        using var programs = new ComponentShaderPrograms();
        using var texture = Texture2D.Create(1, 1, PixelInternalFormat.Rgba16f);
        using var frame = GpuUniformBuffer.Create();
        frame.Allocate(544);
        using var ring = new GpuUniformRingBuffer(4096, 1, false);
        ring.BeginFrame(0);
        GpuUniformRingSystem.SetCurrent(ring);
        try
        {
            var shader = programs.Create<LumOnUpsampleShaderProgram>(s =>
            {
                s.DenoiseEnabled = false;
                s.HoleFillEnabled = false;
            });
            Assert.Equal(GpuProgramLayout.ResolutionState.Missing,
                shader.ProgramLayout.ResolveUniformLocation(shader.ProgramId, "gBufferNormal").State);
            Assert.Equal(GpuProgramLayout.ResolutionState.Missing,
                shader.ProgramLayout.ResolveUniformBlockActive(shader.ProgramId, LumOnUpsampleParamsUbo.BlockName).State);
                        shader.FrameUniformBuffer = frame;
            shader.IndirectHalf = texture;
            shader.PrimaryDepth = texture.TextureId;

            shader.Use();
            Assert.Equal(0, ring.AllocationsWritten);
            shader.Stop();
        }
        finally { GpuUniformRingSystem.ClearCurrent(); }
    }
    #endregion

    #region Private
    /// <summary>Supplies required borrowed inputs while giving each owner distinct parameter bytes.</summary>
    private static void AssignResources(LumOnUpsampleShaderProgram shader, GpuTexture texture, GpuUniformBuffer frame, float sigma)
    {
                shader.FrameUniformBuffer = frame;
        shader.IndirectHalf = texture;
        shader.PrimaryDepth = texture.TextureId;
        shader.GBufferNormal = texture.TextureId;
        shader.UpsampleDepthSigma = sigma;

    }

    /// <summary>Reads the first packed parameter from the actual driver-bound uniform-buffer range.</summary>
    private static float SubmittedDepthSigma(int parameterOffset = 0)
    {
        const int slot = 14;
        GL.GetInteger((GetIndexedPName)All.UniformBufferBinding, slot, out int buffer);
        GL.GetInteger((GetIndexedPName)All.UniformBufferStart, slot, out int offset);
        int prior = GL.GetInteger(GetPName.UniformBufferBinding);
        GL.BindBuffer(BufferTarget.UniformBuffer, buffer);
        float[] value = new float[1];
        GL.GetBufferSubData(BufferTarget.UniformBuffer, (IntPtr)(offset + parameterOffset), sizeof(float), value);
        GL.BindBuffer(BufferTarget.UniformBuffer, prior);
        return value[0];
    }

    /// <summary>Reads driver bindings for the three upsample units without relying on the production cache.</summary>
    private static int[] BoundTextures()
    {
        int priorUnit = GL.GetInteger(GetPName.ActiveTexture);
        int[] bindings = new int[3];
        for (int unit = 0; unit < bindings.Length; unit++)
        {
            GL.ActiveTexture(TextureUnit.Texture0 + unit);
            bindings[unit] = GL.GetInteger(GetPName.TextureBinding2D);
        }
        GL.ActiveTexture((TextureUnit)priorUnit);
        return bindings;
    }

    /// <summary>Observes the retained CPU block without exposing test-only production accessors.</summary>
    private static CpuUniformBuffer Parameters(LumOnUpsampleShaderProgram shader) =>
        (CpuUniformBuffer)typeof(LumOnUpsampleShaderProgram).GetField("paramsUbo", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(shader)!;
    #endregion
}
