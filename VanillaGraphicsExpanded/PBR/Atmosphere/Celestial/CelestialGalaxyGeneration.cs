using VanillaGraphicsExpanded.Rendering.ShaderCompilation;
using VanillaGraphicsExpanded.Rendering.RHI;
using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;

namespace VanillaGraphicsExpanded.PBR.Atmosphere.Celestial;

/// <summary>Rasterizes bounded source and mip tiles into separate scratch storage before copying to the unpublished cube.</summary>
internal sealed class CelestialGalaxyGeneration : IDisposable
{
    private readonly CelestialGenerationProgram program;
    private readonly CelestialSource source;
    private readonly CelestialResidual residual;
    private readonly GpuResourceCollection owned = new();
    private ArrayGraphicsGeometry? scratchGeometry;
    private GraphicsPipelineAsset? scratchPipeline;
    private DynamicTextureCube? cube;
    private GpuFence? fence;
    private bool submitted;

    #region Public API
    /// <summary>Retains a ready executable and prepares bounded residual data without publishing a candidate.</summary>
    internal CelestialGalaxyGeneration(CelestialGenerationProgram program, CelestialSource source)
    {
        this.program = program; this.source = source; residual = new(source);
    }

    /// <summary>Submits world-loading work on the current context; each raster submission covers at most 128 squared texels.</summary>
    internal void Submit()
    {
        if (submitted) throw new InvalidOperationException("A source generation can only be submitted once.");
        submitted = true;
        // Loading callbacks run outside the ordinary frame epoch. Borrow an active allocator
        // when available; otherwise use the existing controller with a private bounded load epoch.
        RHIUniformRingFrameController? loadingFrame = null;
        bool loadingFrameBegan = false;
        try
        {
            if (!RHIUniformRingSystem.TryGetCurrent(out _))
            {
                loadingFrame = new(new GpuUniformRingBuffer(pageSizeBytes: 2 * 1024 * 1024, pageCount: 1, debugName: "Celestial.LoadUniforms"));
                loadingFrame.BeginFrame(); loadingFrameBegan = true;
            }
            cube = RHICommandContext.Current.CreateTextureCube(new RHITextureDescription { Dimension = TextureDimension.Cube, Format = TextureFormat.R11fG11fB10f, Width = source.Quality.FaceSize, Height = source.Quality.FaceSize, MipLevels = 1 + (int)MathF.Floor(MathF.Log2(source.Quality.FaceSize)), Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Linear, BaseLevel = 0, MaxLevel = (1 + (int)MathF.Floor(MathF.Log2(source.Quality.FaceSize))) - 1, Label = "Celestial.Galaxy" }).RequireResource();
            var catalog = owned.Own(RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, CelestialSource.StarCapacity * 2), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource());
            catalog.UploadData(residual.Catalog);
            var values = new List<float>();
            int[] offsets = new int[residual.Tiles.Length];
            for (int i = 0; i < offsets.Length; i++)
            {
                offsets[i] = values.Count / 4;
                foreach (int id in residual.Tiles[i]) values.AddRange([id, 0, 0, 0]);
            }
            int indexHeight = Math.Max(1, (values.Count / 4 + 4095) / 4096);
            var indexData = new float[4096 * indexHeight * 4]; values.CopyTo(indexData);
            var indices = owned.Own(RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 4096), Height = System.Math.Max(1, indexHeight), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource()); indices.UploadData(indexData);
            var scratch = owned.Own(RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, CelestialResidual.TileSize), Height = System.Math.Max(1, CelestialResidual.TileSize), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource());

            var layout = new RHIVertexLayout([]);
            var geometry = scratchGeometry = new ArrayGraphicsGeometry(layout, PrimitiveTopology.Triangles,
                new Dictionary<int, GpuVbo>(), proceduralVertices: 3);
            var pass = new RHIRenderPassDescription([new(Image:RHIAttachment.FromTexture(scratch))],label:"Celestial.GenerationScratch");
            var signature=RHICommandContext.Current.DescribeRenderTargets(pass);
            scratchPipeline = new GraphicsPipelineAsset(program,new(program.Shader, layout, signature, DynamicPipelineState.Viewport));
            var pipeline = scratchPipeline.Pipeline;
            program.Catalog = catalog; program.Indices = indices; program.ParentMap = cube;
            int baseTiles = (source.Quality.FaceSize + 127) / 128;
            for (int level = 0; level < cube.StorageMipLevels; level++) for (int face = 0; face < 6; face++)
            {
                int size = Math.Max(1, source.Quality.FaceSize >> level);

                for (int y = 0; y < size; y += 128) for (int x = 0; x < size; x += 128)
                {
                    int tile = face * baseTiles * baseTiles + y / 128 * baseTiles + x / 128;
                    int count = level == 0 ? residual.Tiles[tile].Count : 0;
                    program.SetTile(face, size, x, y, level == 0 ? offsets[tile] : 0, count,
                        level - 1, Math.Max(1, source.Quality.FaceSize >> Math.Max(0, level - 1)), source);
                    int width = Math.Min(128, size - x), height = Math.Min(128, size - y);
                    var commands=RHICommandContext.Current;
                    using var boundary=commands.EnterBoundary(new("Celestial.Generate",true,graphics:[pipeline],operations:BoundaryOperations.RenderPasses));
                    boundary.Run(()=>
                    {
                        commands.BeginRenderPass(pass); commands.SetPipeline(pipeline);
                        commands.SetDynamicState(new() { Viewport = new RHIViewport(0,0,width,height) });
                        commands.BindResources(program.CaptureBindings());
                        commands.Draw(geometry.Inputs, new(0, 3)); commands.EndRenderPass();
                    });
                    // Scratch decouples sampling a parent mip from writing the cube, avoiding texture feedback.
                    commands.BlitImages(new(RHIAttachment.FromTexture(scratch),new(0,0,width,height),
                        RHIAttachment.FromTexture(cube,new(level,face)),new(x,y,width,height)));
                }
            }
            fence = RHICommandContext.Current.InsertFence(); RHICommandContext.Current.Flush();
        }
        catch { Dispose(); throw; }
        finally
        {
            if (loadingFrame is not null)
            {
                try { if (loadingFrameBegan) loadingFrame.EndFrame(); }
                finally { loadingFrame.Dispose(); }
            }
        }
    }

    /// <summary>Polls once without waiting; transfers only a successfully completed entire resource set.</summary>
    internal CelestialResourceSet? TryComplete()
    {
        if (fence is null) return null;
        var status = RHICommandContext.Current.Poll(fence);
        if (status == CompletionStatus.Pending) return null;
        if (status != CompletionStatus.Ready)
            throw new InvalidOperationException("Celestial generation fence failed.");
        fence.Dispose(); fence = null;
        ReleaseScratch();
        var completed = new CelestialResourceSet(source, cube!); cube = null;
        return completed;
    }

    /// <summary>Discards only unpublished resources, using the GPU owner's deferred retirement.</summary>
    public void Dispose()
    {
        Rendering.Integration.RenderingCleanup.Run(
            () => { var owner = fence; fence = null; owner?.Dispose(); },
            () => { var owner = cube; cube = null; owner?.Dispose(); },
            ReleaseScratch);
    }
    #endregion

    #region Private
    /// <summary>Retires managed draw owners and all scratch resources before transferring the completed cube.</summary>
    private void ReleaseScratch()
    {
        // Managed draw realizations are outside GpuResourceCollection's resource contract.
        // The shared collection retires FBO borrowers before storage and attempts every release.
        Rendering.Integration.RenderingCleanup.Run(
            () => { var owner = scratchPipeline; scratchPipeline = null; owner?.Dispose(); },
            () => { var owner = scratchGeometry; scratchGeometry = null; owner?.Dispose(); },
            owned.Dispose);
    }
    #endregion
}
