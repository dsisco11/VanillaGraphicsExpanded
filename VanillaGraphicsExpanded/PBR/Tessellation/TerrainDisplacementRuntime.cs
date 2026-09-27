using System;
using System.Runtime.CompilerServices;
using VanillaGraphicsExpanded.ModSystems;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;
using VanillaGraphicsExpanded.PBR.Materials;

namespace VanillaGraphicsExpanded.PBR.Tessellation;

/// <summary>Publishes bounded draw parameters and restricts shared shadow programs to eligible terrain pools.</summary>
internal static class TerrainDisplacementRuntime
{
    internal static ICoreClientAPI? Api { get; set; }
    internal static MeshDataPoolManager[][]? TerrainPools { get; set; }
    internal static bool EligiblePool { get; set; }
    internal static bool MovingPool { get; set; }
    private static float focalPixels;
    private static (double X, double Y, double Z, float Focal, int Level, float Pixels, float Start, float End)? previousGeometry;
    private static bool previousBuildComplete;
    private static bool reactive = true;
    private static long frameSerial;
    private static readonly ConditionalWeakTable<ShaderProgramBase, Parameters> published = new();

    /// <summary>Caches only VGE-owned uniforms for one executable; engine matrices remain engine-owned.</summary>
    private sealed class Parameters
    {
        internal int ProgramId;
        internal long Frame = -1;
        internal bool? Enabled;
    }

    #region Draw policy
    /// <summary>Matches the engine's opaque and topsoil pool entries by identity, without classifying individual blocks.</summary>
    internal static bool IsEligiblePool(MeshDataPoolManager manager)
    {
        const int opaque = (int)EnumChunkRenderPass.Opaque, topsoil = (int)EnumChunkRenderPass.TopSoil;
        if (TerrainPools is not { } pools || pools.Length <= topsoil) return false;
        return Array.IndexOf(pools[opaque], manager) >= 0 || Array.IndexOf(pools[topsoil], manager) >= 0;
    }

    /// <summary>Freezes one perspective metric before shadow and visible passes so both choose identical edge levels.</summary>
    internal static void CaptureView()
    {
        if (Api?.World?.Player?.Entity is not { } player) return;
        frameSerial++;
        var render = Api.Render;
        double focal = Math.Abs(render.PerspectiveProjectionMat[5]) * render.FrameHeight * .5;
        focalPixels = double.IsFinite(focal) && focal > 0 ? (float)focal : render.FrameHeight;
        var position = player.CameraPos;
        var settings = ConfigModSystem.Config.MaterialAtlas.TerrainSubdivision;
        var geometry = (position.X, position.Y, position.Z, focalPixels, settings.MaximumLevel,
            settings.TargetEdgePixels, settings.FadeStartMetres, settings.FadeEndMetres);
        bool built = MaterialAtlasSystem.Instance.IsBuildComplete;
        reactive = previousGeometry != geometry || !built || !previousBuildComplete;
        previousGeometry = geometry;
        previousBuildComplete = built;
    }

    /// <summary>Binds parameters through the engine uniform API immediately before an adaptive grouped draw.</summary>
    internal static void Bind()
    {
        if (ShaderProgramBase.CurrentShaderProgram is not { } program || !TerrainTessellationPrograms.Adaptive(program)) return;
        var settings = ConfigModSystem.Config.MaterialAtlas.TerrainSubdivision;
        bool eligible = TerrainTessellationPrograms.Complete && !MovingPool && EligiblePool;
        var state = published.GetValue(program, static _ => new Parameters());
        if (state.ProgramId != program.ProgramId)
        {
            state.ProgramId = program.ProgramId;
            state.Frame = -1;
            state.Enabled = null;
        }
        if (state.Enabled != eligible)
        {
            program.Uniform("vge_displacementEnabled", eligible ? 1 : 0);
            state.Enabled = eligible;
        }
        if (state.Frame == frameSerial) return;
        state.Frame = frameSerial;
        if (program.PassName != "chunkshadowmap") program.Uniform("vge_displacementReactive", reactive ? 1 : 0);
        program.Uniform("vge_tessellationDistance", settings.FadeStartMetres, settings.FadeEndMetres);
        program.Uniform("vge_tessellationPixels", (float)(Api?.Render.FrameWidth ?? 1),
            (float)(Api?.Render.FrameHeight ?? 1), settings.TargetEdgePixels, (float)settings.MaximumLevel);
        program.Uniform("vge_tessellationFocalPixels", Math.Max(1, focalPixels));
    }

    /// <summary>Rejects the first frame after shader/world replacement without invalidating unrelated lighting.</summary>
    internal static void ResetHistory()
    {
        previousGeometry = null;
        previousBuildComplete = false;
        reactive = true;
        frameSerial++;
    }
    #endregion
}
