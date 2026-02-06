using System;
using System.Diagnostics;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Rendering;

using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.LumOn.Scene.Shaders;

internal sealed class LumonSceneCaptureMeshCardComputeShader : IDisposable
{
    public const string ShaderName = "lumonscene_capture_meshcard";

    private const int ParamsUboBinding = GpuBindingRegistry.Ubo.Object; // VGE_UBO_OBJECT_BINDING
    private const int ParamsUboSizeBytes = 32; // uvec4 + vec4

    private const int AtlasLayoutOffsetBytes = 0;
    private const int CaptureFloats0OffsetBytes = 16;

    private const int DepthAtlasImageUnit = 0;    // layout(binding=0, r16f)
    private const int MaterialAtlasImageUnit = 1; // layout(binding=1, rgba8)

    private const int MeshCardCaptureWorkSsboBindingIndex = 0; // layout(std430, binding=0)
    private const int PatchMetadataSsboBindingIndex = 1;       // layout(std430, binding=1)
    private const int TrianglesSsboBindingIndex = 2;           // layout(std430, binding=2)

    private readonly byte[] paramsBytes = new byte[ParamsUboSizeBytes];
    private GpuUniformBuffer? paramsUbo;

    private uint tileSizeTexels;
    private uint tilesPerAxis;
    private uint tilesPerAtlas;
    private uint borderTexels;
    private float captureDepthRange;

    private readonly GpuComputePipeline pipeline;

    public int ProgramId => pipeline.ProgramId;

    public bool IsValid => pipeline.IsValid;

    private LumonSceneCaptureMeshCardComputeShader(GpuComputePipeline pipeline)
    {
        this.pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));

        paramsUbo = GpuUniformBuffer.Create(debugName: "LumOnScene.CaptureMeshCard.ParamsUBO");
    }

    private void ApplyParamsUbo()
    {
        paramsUbo ??= GpuUniformBuffer.Create(debugName: "LumOnScene.CaptureMeshCard.ParamsUBO");
        UboPacking.WriteUVec4(paramsBytes, AtlasLayoutOffsetBytes, tileSizeTexels, tilesPerAxis, tilesPerAtlas, borderTexels);
        UboPacking.WriteVec4(paramsBytes, CaptureFloats0OffsetBytes, captureDepthRange, 0f, 0f, 0f);
        paramsUbo.UploadOrResize(paramsBytes, ParamsUboSizeBytes, growExponentially: false);
        paramsUbo.BindBase(ParamsUboBinding);
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
        this.tileSizeTexels = tileSizeTexels;
        this.tilesPerAxis = tilesPerAxis;
        this.tilesPerAtlas = tilesPerAtlas;
        this.borderTexels = borderTexels;
        ApplyParamsUbo();
    }

    public float CaptureDepthRange
    {
        set
        {
            captureDepthRange = value;
            ApplyParamsUbo();
        }
    }

    public void Dispose()
    {
        try
        {
            paramsUbo?.Dispose();
            paramsUbo = null;
            pipeline.Dispose();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[VGE] Exception disposing CaptureMeshCard compute shader: {ex}");
        }
    }
}
