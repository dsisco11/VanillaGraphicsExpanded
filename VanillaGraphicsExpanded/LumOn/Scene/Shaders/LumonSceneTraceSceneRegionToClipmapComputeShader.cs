using System;
using System.Diagnostics;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Rendering;

using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.LumOn.Scene.Shaders;

internal sealed class LumonSceneTraceSceneRegionToClipmapComputeShader : IDisposable
{
    public const string ShaderName = "lumonscene_trace_scene_region_to_clipmap";

    private const int RegionPayloadWordsSsboBindingIndex = 0; // layout(std430, binding=0)
    private const int RegionUpdatesSsboBindingIndex = 1;      // layout(std430, binding=1)

    private const int RegionUpdateCountLocation = 0; // layout(location=0) uint
    private const int LevelsLocation = 1;            // layout(location=1) int
    private const int ResolutionLocation = 2;        // layout(location=2) int

    private const int OriginMinCellBaseLocation = 3; // layout(location=3) ivec3[8]
    private const int RingBaseLocation = 11;         // layout(location=11) ivec3[8]

    private const int OccLevelImageBaseUnit = 0;     // layout(binding=0) uimage3D vge_occLevels[8]

    private readonly GpuComputePipeline pipeline;

    public int ProgramId => pipeline.ProgramId;

    public bool IsValid => pipeline.IsValid;

    private LumonSceneTraceSceneRegionToClipmapComputeShader(GpuComputePipeline pipeline)
    {
        this.pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    }

    public static bool TryCreate(
        ICoreAPI api,
        out LumonSceneTraceSceneRegionToClipmapComputeShader? shader,
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
            infoLog = (infoLog.Length > 0 ? infoLog + "\n" : string.Empty) + "[VGE] RegionToClipmap pipeline creation returned null.";
            return false;
        }

        shader = new LumonSceneTraceSceneRegionToClipmapComputeShader(pipeline);
        return true;
    }

    public IDisposable UseScope() => pipeline.UseScope();

    public void Use() => pipeline.Use();

    public void BindRegionPayloadWordsSsbo(GpuShaderStorageBuffer ssbo)
    {
        if (ssbo is null) throw new ArgumentNullException(nameof(ssbo));
        ssbo.BindBase(RegionPayloadWordsSsboBindingIndex);
    }

    public void BindRegionUpdatesSsbo(GpuShaderStorageBuffer ssbo)
    {
        if (ssbo is null) throw new ArgumentNullException(nameof(ssbo));
        ssbo.BindBase(RegionUpdatesSsboBindingIndex);
    }

    public void BindOccLevelImage(int level, GpuTexture occLevel, TextureAccess access = TextureAccess.WriteOnly)
    {
        if (occLevel is null) throw new ArgumentNullException(nameof(occLevel));
        if (level < 0 || level >= 8) throw new ArgumentOutOfRangeException(nameof(level));

        occLevel.BindImageUnit(
            unit: OccLevelImageBaseUnit + level,
            access: access,
            level: 0,
            layered: true,
            layer: 0,
            format: SizedInternalFormat.R32ui);
    }

    public uint RegionUpdateCount
    {
        set
        {
            Use();
            GL.Uniform1(RegionUpdateCountLocation, value);
        }
    }

    public int Levels
    {
        set
        {
            Use();
            GL.Uniform1(LevelsLocation, value);
        }
    }

    public int Resolution
    {
        set
        {
            Use();
            GL.Uniform1(ResolutionLocation, value);
        }
    }

    public void SetOriginMinCell(int level, int x, int y, int z)
    {
        if (level < 0 || level >= 8) throw new ArgumentOutOfRangeException(nameof(level));
        Use();
        GL.Uniform3(OriginMinCellBaseLocation + level, x, y, z);
    }

    public void SetRing(int level, int x, int y, int z)
    {
        if (level < 0 || level >= 8) throw new ArgumentOutOfRangeException(nameof(level));
        Use();
        GL.Uniform3(RingBaseLocation + level, x, y, z);
    }

    public void Dispose()
    {
        try
        {
            pipeline.Dispose();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[VGE] Exception disposing RegionToClipmap compute shader: {ex}");
        }
    }
}
