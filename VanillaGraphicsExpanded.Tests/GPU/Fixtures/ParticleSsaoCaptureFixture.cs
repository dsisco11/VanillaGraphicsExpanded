using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.SceneColor;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Supplies uploaded particle samples to real capture, receiver-resolve and SSAO-restore resources.</summary>
/// <remarks>This fixture models submission results; it does not rasterize particles or exercise engine scheduling.</remarks>
internal sealed class ParticleSsaoCaptureFixture : IDisposable
{
    private const int Width = 4;
    private readonly GpuResourceCollection resources = new();
    private static readonly GlPipelineDesc CaptureSetup = new(
        defaultMask: GlPipelineStateMask.From(GlPipelineStateId.ScissorTestEnable),
        nonDefaultMask: default, name: "Tests.ParticleSsao.CaptureSetup");

    internal DynamicTexture2D Material { get; } = null!;
    internal DynamicTexture2D Glow { get; } = null!;
    internal DynamicTexture2D Normal { get; } = null!;
    internal DynamicTexture2D Position { get; } = null!;
    internal DepthTexture Depth { get; } = null!;
    internal SceneColorParticleTargets Targets { get; } = null!;

    #region Public API
    #region Sample preparation
    /// <summary>Creates initialized engine-owned images and the production capture targets that borrow them.</summary>
    internal ParticleSsaoCaptureFixture()
    {
        try
        {
            Material = CreateColor([.125f, .25f, .5f, 1f]);
            Glow = CreateColor([0, 0, 0, 0]);
            Normal = CreateColor([1, 2, 3, 4]);
            Position = CreateColor([5, 6, 7, 8]);
            Depth = resources.Own(new DepthTexture(Width, 1, PixelInternalFormat.DepthComponent32f));
            Depth.UploadDataImmediate([.75f, .75f, .75f, .75f]);
            var primary = resources.Own(GpuFramebuffer.CreateMRT([Material, Glow, Normal, Position], Depth)!);
            Targets = new SceneColorParticleTargets(primary, captureSsao: true);
        }
        catch { Dispose(); throw; }
    }

    /// <summary>Captures exact before/after depth around uploaded partial-alpha, zero-alpha and untouched samples.</summary>
    internal void CaptureUploadedSamples()
    {
        StateCache.Current.Apply(CaptureSetup);
        Targets.BeginCapture();
        // These are submission results, including premultiplied radiance and a zero-alpha
        // sample that still writes depth. The fourth pixel receives no particle sample.
        Targets.DrawTarget[0].UploadDataImmediate([
            4, 2, 1, .5f, 0, 0, 0, 0, 4, 2, 1, .5f, 0, 0, 0, 0]);
        Targets.DrawTarget[2].UploadDataImmediate([
            .25f, .5f, .75f, .5f, .25f, .5f, .75f, 0,
            .25f, .5f, .75f, .5f, 0, 0, 0, 0]);
        Targets.DrawTarget[3].UploadDataImmediate([
            10, 20, 30, .125f, 10, 20, 30, .125f,
            10, 20, 30, .125f, 0, 0, 0, 0]);
        Depth.UploadDataImmediate([.5f, .5f, .5f, .75f]);
        Targets.EndCapture();
    }

    /// <summary>Models a later foreground replacement at the third pixel without modifying the saved depth pair.</summary>
    internal void ReplaceForegroundReceiver()
    {
        Depth.UploadDataImmediate([.25f], 2, 0, 1, 1);
        Normal.UploadDataImmediate([11f, 12f, 13f, 14f], 2, 0, 1, 1);
        Position.UploadDataImmediate([15f, 16f, 17f, 18f], 2, 0, 1, 1);
    }
    #endregion

    #region Production passes
    /// <summary>Runs the compiled production receiver separation against the captured inputs.</summary>
    internal void ResolveReceivers(SceneColorParticleShaderProgram program, ShaderTestFramework framework)
    {
        program.VisibilityDepth = Targets.VisibilityDepth;
        program.BeforeDepth = Targets.BeforeDepth;
        program.AfterDepth = Targets.AfterDepth;
        program.ParticleColor = Targets.ParticleColor;
        framework.RenderQuadTo(program, Targets.ResolveTarget);
    }

    /// <summary>Runs the compiled production SSAO restoration without clearing borrowed receiver images.</summary>
    internal void RestoreMetadata(SceneColorParticleSsaoShaderProgram program, ShaderTestFramework framework)
    {
        program.VisibilityDepth = Targets.VisibilityDepth;
        program.BeforeDepth = Targets.BeforeDepth;
        program.AfterDepth = Targets.AfterDepth;
        program.ParticleNormal = Targets.DrawTarget[2];
        program.ParticlePosition = Targets.DrawTarget[3];
        Targets.SsaoTarget!.BindWithViewport();
        framework.RenderQuad(program);
    }
    #endregion

    #region Lifetime
    /// <summary>Retires capture-owned resources before releasing the engine-owned images it borrowed.</summary>
    public void Dispose()
    {
        Targets?.Dispose();
        resources.Dispose();
        GpuResourceManagerSystem.CaptureDisposalQueue().DrainPending();
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Creates and initializes one four-pixel metadata or color image through the texture abstraction.</summary>
    private DynamicTexture2D CreateColor(float[] value)
    {
        var texture = resources.Own(DynamicTexture2D.Create(Width, 1, PixelInternalFormat.Rgba16f));
        texture.UploadDataImmediate(Enumerable.Repeat(value, Width).SelectMany(pixel => pixel).ToArray());
        return texture;
    }
    #endregion
}
