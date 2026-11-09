using System;
using System.Collections.Generic;
using VanillaGraphicsExpanded.PBR.Materials;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.RHI;
using System.IO;
using Xunit;

using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;

namespace VanillaGraphicsExpanded.Tests.Component.PBR.Materials.Fixtures;

/// <summary>Owns the shipped height-bake shader programs and submits their passes through the production RHI.</summary>
internal sealed class PbrHeightBakeTestPrograms : IDisposable
{
    private readonly EngineShaderPlatformScope platform;
    private readonly BinaryShaderApiFixture assets;
    private readonly List<PbrHeightBakeShaderProgram> programs = [];
    private readonly Dictionary<(PbrHeightBakeShaderProgram Program, TextureFormat Format, ColorBlendDesc? Blend), RHIGraphicsPipeline> pipelines = [];
    private readonly ArrayGraphicsGeometry geometry;
    private bool disposed;

    #region Public API
    #region Public members
    /// <summary>Constructs the built-asset provider and the production CPU uniform payload used by each pass.</summary>
    internal PbrHeightBakeTestPrograms()
    {
        var createdPlatform = new EngineShaderPlatformScope();
        BinaryShaderApiFixture? createdAssets = null;
        PbrHeightBakeParamsUbo? createdParameters = null;
        ArrayGraphicsGeometry? createdGeometry = null;
        try
        {
            createdAssets = new BinaryShaderApiFixture();
            createdParameters = new PbrHeightBakeParamsUbo();
            // The shipped vertex stage derives its fullscreen triangle from gl_VertexID.
            // Three procedural vertices therefore preserve the original fullscreen UV domain without a test mesh.
            createdGeometry = new(new([]), PrimitiveTopology.Triangles, new Dictionary<int, GpuVbo>(), proceduralVertices: 3);
            platform = createdPlatform;
            assets = createdAssets;
            Parameters = createdParameters;
            geometry = createdGeometry;
        }
        catch
        {
            createdGeometry?.Dispose();
            createdParameters?.Dispose();
            createdAssets?.Dispose();
            createdPlatform.Dispose();
            throw;
        }
    }

    /// <summary>Preserves the existing skip behavior when built shader assets are unavailable.</summary>
    internal static void EnsureAssetsAvailable()
    {
        string shaderPath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders");
        string includePath = Path.Combine(shaderPath, "includes");
        Assert.SkipWhen(!TestAssetSnapshot.HasDirectory(shaderPath) || !TestAssetSnapshot.HasDirectory(includePath),
            "Test shader assets not found in output directory.");
    }

    /// <summary>Gets the production 528-byte bake parameter payload shared by the submitted pass.</summary>
    internal PbrHeightBakeParamsUbo Parameters { get; }

    /// <summary>Prepares and retains one shipped production bake program by its production asset identity.</summary>
    internal PbrHeightBakeShaderProgram Create(string passName)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var existing = programs.Find(program => program.PassName == passName);
        if (existing is not null) return existing;

        var program = new PbrHeightBakeShaderProgram(passName, "vanillagraphicsexpanded");
        try
        {
            program.Initialize(assets.Api);
            if (!program.CompileAndLink())
                throw new InvalidOperationException($"Could not prepare production bake program '{passName}'.{Environment.NewLine}{string.Join(Environment.NewLine, assets.Logs)}");
            programs.Add(program);
            return program;
        }
        catch
        {
            program.Dispose();
            throw;
        }
    }

    /// <summary>Executes the selected production bake shader against one image using its captured bindings.</summary>
    internal void Draw(GpuTextureRef destination, PbrHeightBakeShaderProgram program, bool clear = false, ColorBlendDesc? blend = null)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (destination.IsEmpty) throw new ArgumentException("A destination texture is required.", nameof(destination));
        ArgumentNullException.ThrowIfNull(program);
        program.Parameters = Parameters;

        var commands = RHICommandContext.Current;
        var pass = new RHIRenderPassDescription([new(Image: RHIAttachment.FromTexture(destination),
            Load: clear ? AttachmentLoad.Clear : AttachmentLoad.Preserve,
            Clear: clear ? ColorClearValue.Float(0, 0, 0, 0) : null)]);
        var signature = commands.DescribeRenderTargets(pass);
        var format = signature.Colors[0].Format ?? throw new InvalidOperationException("The bake pass target has no color format.");
        var pipeline = GetPipeline(commands, program, format, signature, blend);

        using var boundary = commands.EnterBoundary(new($"Tests.PbrHeightBake.{program.PassName}", true,
            graphics: [pipeline], operations: BoundaryOperations.RenderPasses));
        // Capture the assigned production UBO and current sampler references only after pass/pipeline selection is established.
        boundary.Run(() =>
        {
            commands.BeginRenderPass(pass);
            commands.SetPipeline(pipeline);
            commands.SetDynamicState(new() { Viewport = commands.PassViewport });
            commands.BindResources(program.CaptureBindings());
            commands.Draw(geometry.Inputs, new(0, 3));
            commands.EndRenderPass();
        });
    }
    #endregion

    #region Lifetime
    /// <summary>Releases pipeline realizations and executable owners before their asset and context fixtures.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        foreach (var pipeline in pipelines.Values) pipeline.Dispose();
        pipelines.Clear();
        foreach (var program in programs) program.Dispose();
        programs.Clear();
        geometry.Dispose();
        Parameters.Dispose();
        assets.Dispose();
        platform.Dispose();
    }
    #endregion

    #endregion

    #region Private
    /// <summary>Reuses an immutable production pipeline for the exact pass owner and output format.</summary>
    private RHIGraphicsPipeline GetPipeline(RHICommandContext commands, PbrHeightBakeShaderProgram program,
        TextureFormat format, RenderTargetSignature signature, ColorBlendDesc? blend)
    {
        if (pipelines.TryGetValue((program, format, blend), out var existing)) return existing;
        var layout = new RHIVertexLayout([]);
        var pipeline = commands.CreateGraphicsPipeline(new(program.Shader, layout, signature,
            DynamicPipelineState.Viewport, blending: blend is null ? null : new ColorBlendDesc[] { blend })).RequireResource();
        pipelines.Add((program, format, blend), pipeline);
        return pipeline;
    }
    #endregion
}
