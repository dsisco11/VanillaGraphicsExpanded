using VanillaGraphicsExpanded.Rendering.Contracts;
using System;
using System.Diagnostics;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Rendering;

using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.LumOn.Scene.Shaders;

/// <summary>Owns the compute shader contract and dispatch resources for this scene operation.</summary>
[ShaderProgram("Contract", "lumonscene_capture_meshcard", 1)]
[ShaderStage("Contract", ShaderStageKind.Compute, "lumonscene_capture_meshcard.csh")]
[ShaderBindingSet(typeof(ShaderInterfaceLocations), Defaults = true)]
[ShaderBindingSet(typeof(ShaderIncludeBindings), Defaults = true)]
internal sealed partial class LumonSceneCaptureMeshCardComputeShader : IDisposable
{

    #region Private: GPU binding declarations
    /// <summary>Declares the VgeLumOnSceneCaptureMeshCardParamsUBO UniformBlock slot.</summary>
    [ShaderBinding("VgeLumOnSceneCaptureMeshCardParamsUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Object, ShaderStageKind.Compute)]
    private partial GpuUniformBuffer Parameters { set; }
    /// <summary>Declares the vge_depthAtlas Image slot.</summary>
    [ShaderBinding("vge_depthAtlas", ShaderBindingKind.Image, 0, ShaderStageKind.Compute)]
    private partial GpuTextureBinding DepthAtlas { set; }
    /// <summary>Declares the vge_materialAtlas Image slot.</summary>
    [ShaderBinding("vge_materialAtlas", ShaderBindingKind.Image, 1, ShaderStageKind.Compute)]
    private partial GpuTextureBinding MaterialAtlas { set; }
    /// <summary>Declares the VgeMeshCardCaptureWork StorageBlock slot.</summary>
    [ShaderBinding("VgeMeshCardCaptureWork", ShaderBindingKind.StorageBlock, 0, ShaderStageKind.Compute)]
    private partial GpuShaderStorageBuffer MeshCardCaptureWork { set; }
    /// <summary>Declares the VgePatchMetadataBuffer StorageBlock slot.</summary>
    [ShaderBinding("VgePatchMetadataBuffer", ShaderBindingKind.StorageBlock, 1, ShaderStageKind.Compute)]
    private partial GpuShaderStorageBuffer PatchMetadata { set; }
    /// <summary>Declares the VgeTriangles StorageBlock slot.</summary>
    [ShaderBinding("VgeTriangles", ShaderBindingKind.StorageBlock, 2, ShaderStageKind.Compute)]
    private partial GpuShaderStorageBuffer Triangles { set; }
    #endregion

    public static string ShaderName => Contract.Identity;

    private const int ParamsUboSizeBytes = 32; // uvec4 + vec4

    private const int AtlasLayoutOffsetBytes = 0;
    private const int CaptureFloats0OffsetBytes = 16;



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

    /// <summary>Uploads parameters and assigns the generated uniform-buffer binding.</summary>
    private void ApplyParamsUbo()
    {
        paramsUbo ??= GpuUniformBuffer.Create(debugName: "LumOnScene.CaptureMeshCard.ParamsUBO");
        UboPacking.WriteUVec4(paramsBytes, AtlasLayoutOffsetBytes, tileSizeTexels, tilesPerAxis, tilesPerAtlas, borderTexels);
        UboPacking.WriteVec4(paramsBytes, CaptureFloats0OffsetBytes, captureDepthRange, 0f, 0f, 0f);
        paramsUbo.UploadOrResize(paramsBytes, ParamsUboSizeBytes, growExponentially: false);
        Parameters = paramsUbo;
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
            log: api.Logger,
            layout: new LumonSceneComputeProgramLayouts.CaptureMeshCard()))
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

    /// <summary>Assigns the typed GPU resource through its generated binding property.</summary>
    public void BindDepthAtlasImage(GpuTexture depthAtlas, TextureAccess access = TextureAccess.WriteOnly)
    {
        if (depthAtlas is null) throw new ArgumentNullException(nameof(depthAtlas));

        DepthAtlas = new(depthAtlas,
            Access: access,
            Level: 0,
            Layered: true,
            Layer: 0,
            Format: SizedInternalFormat.R16f);
    }

    /// <summary>Assigns the typed GPU resource through its generated binding property.</summary>
    public void BindMaterialAtlasImage(GpuTexture materialAtlas, TextureAccess access = TextureAccess.WriteOnly)
    {
        if (materialAtlas is null) throw new ArgumentNullException(nameof(materialAtlas));

        MaterialAtlas = new(materialAtlas,
            Access: access,
            Level: 0,
            Layered: true,
            Layer: 0,
            Format: SizedInternalFormat.Rgba8);
    }

    /// <summary>Assigns the typed GPU resource through its generated binding property.</summary>
    public void BindMeshCardCaptureWorkSsbo(GpuShaderStorageBuffer ssbo)
    {
        if (ssbo is null) throw new ArgumentNullException(nameof(ssbo));
        MeshCardCaptureWork = ssbo;
    }

    /// <summary>Assigns the typed GPU resource through its generated binding property.</summary>
    public void BindPatchMetadataSsbo(GpuShaderStorageBuffer ssbo)
    {
        if (ssbo is null) throw new ArgumentNullException(nameof(ssbo));
        PatchMetadata = ssbo;
    }

    /// <summary>Assigns the typed GPU resource through its generated binding property.</summary>
    public void BindTrianglesSsbo(GpuShaderStorageBuffer ssbo)
    {
        if (ssbo is null) throw new ArgumentNullException(nameof(ssbo));
        Triangles = ssbo;
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
