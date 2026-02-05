using System;
using System.Diagnostics;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Rendering;

using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.LumOn.Scene.Shaders;

internal sealed class LumonSceneCaptureMeshCardComputeShader : IDisposable
{
    public const string ShaderName = "lumonscene_capture_meshcard";

    private const int DepthAtlasImageUnit = 0;    // layout(binding=0, r16f)
    private const int MaterialAtlasImageUnit = 1; // layout(binding=1, rgba8)

    private const int MeshCardCaptureWorkSsboBindingIndex = 0; // layout(std430, binding=0)
    private const int PatchMetadataSsboBindingIndex = 1;       // layout(std430, binding=1)
    private const int TrianglesSsboBindingIndex = 2;           // layout(std430, binding=2)

    private const int TileSizeTexelsLocation = 0; // layout(location=0)
    private const int TilesPerAxisLocation = 1;   // layout(location=1)
    private const int TilesPerAtlasLocation = 2;  // layout(location=2)
    private const int BorderTexelsLocation = 3;   // layout(location=3)

    private const int CaptureDepthRangeLocation = 4; // layout(location=4)

    private readonly GpuComputePipeline pipeline;

    public int ProgramId => pipeline.ProgramId;

    public bool IsValid => pipeline.IsValid;

    private LumonSceneCaptureMeshCardComputeShader(GpuComputePipeline pipeline)
    {
        this.pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    }

    public static bool TryCreate(
        ICoreAPI api,
        out LumonSceneCaptureMeshCardComputeShader? shader,
        out string infoLog,
        bool preferSpirv = true,
        string? debugName = null)
    {
        shader = null;

        if (!GpuComputePipeline.TryCreateFromAssets(
            api: api,
            shaderName: ShaderName,
            pipeline: out var pipeline,
            sourceCode: out _,
            infoLog: out infoLog,
            preferSpirv: preferSpirv,
            stageExtension: "csh",
            defines: null,
            debugName: debugName,
            log: api.Logger))
        {
            return false;
        }

        if (pipeline is null)
        {
            infoLog = (infoLog.Length > 0 ? infoLog + "\n" : string.Empty) + "[VGE] CaptureMeshCard pipeline creation returned null.";
            return false;
        }

        shader = new LumonSceneCaptureMeshCardComputeShader(pipeline);
        return true;
    }

    public IDisposable UseScope() => pipeline.UseScope();

    public void Use() => pipeline.Use();

    public void BindDepthAtlasImage(GpuTexture depthAtlas, TextureAccess access = TextureAccess.WriteOnly)
    {
        if (depthAtlas is null) throw new ArgumentNullException(nameof(depthAtlas));

        depthAtlas.BindImageUnit(
            unit: DepthAtlasImageUnit,
            access: access,
            level: 0,
            layered: true,
            layer: 0,
            format: SizedInternalFormat.R16f);
    }

    public void BindMaterialAtlasImage(GpuTexture materialAtlas, TextureAccess access = TextureAccess.WriteOnly)
    {
        if (materialAtlas is null) throw new ArgumentNullException(nameof(materialAtlas));

        materialAtlas.BindImageUnit(
            unit: MaterialAtlasImageUnit,
            access: access,
            level: 0,
            layered: true,
            layer: 0,
            format: SizedInternalFormat.Rgba8);
    }

    public void BindMeshCardCaptureWorkSsbo(GpuShaderStorageBuffer ssbo)
    {
        if (ssbo is null) throw new ArgumentNullException(nameof(ssbo));
        ssbo.BindBase(MeshCardCaptureWorkSsboBindingIndex);
    }

    public void BindPatchMetadataSsbo(GpuShaderStorageBuffer ssbo)
    {
        if (ssbo is null) throw new ArgumentNullException(nameof(ssbo));
        ssbo.BindBase(PatchMetadataSsboBindingIndex);
    }

    public void BindTrianglesSsbo(GpuShaderStorageBuffer ssbo)
    {
        if (ssbo is null) throw new ArgumentNullException(nameof(ssbo));
        ssbo.BindBase(TrianglesSsboBindingIndex);
    }

    public void SetAtlasLayout(uint tileSizeTexels, uint tilesPerAxis, uint tilesPerAtlas, uint borderTexels)
    {
        Use();
        GL.Uniform1(TileSizeTexelsLocation, tileSizeTexels);
        GL.Uniform1(TilesPerAxisLocation, tilesPerAxis);
        GL.Uniform1(TilesPerAtlasLocation, tilesPerAtlas);
        GL.Uniform1(BorderTexelsLocation, borderTexels);
    }

    public float CaptureDepthRange
    {
        set
        {
            Use();
            GL.Uniform1(CaptureDepthRangeLocation, value);
        }
    }

    public void Dispose()
    {
        try
        {
            pipeline.Dispose();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[VGE] Exception disposing CaptureMeshCard compute shader: {ex}");
        }
    }
}
