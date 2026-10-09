using System;
using VanillaGraphicsExpanded.Rendering;
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
    internal static bool EligiblePool { get; set; }
    internal static bool MovingPool { get; set; }
    private static float focalPixels;
    private static readonly TerrainDisplacementHistory history = new();
    private static bool reactive = true;
    private static long capturedAtlasRevision;
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
        => MeshPoolClassifier.Current?.IsDisplacementEligible(manager) == true;

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
        var geometry = (position.X, position.Y, position.Z, focalPixels, (int)settings.MaximumLevel,
            settings.TargetEdgePixels, settings.FadeStartMetres, settings.FadeEndMetres);
        var atlas = MaterialAtlasSystem.Instance;
        capturedAtlasRevision = atlas.SurfaceDetailRevision;
        reactive = history.Observe(geometry, capturedAtlasRevision, atlas.IsBuildComplete);
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
        if (program.PassName != "chunkshadowmap")
        {
            // Atlas publication can run after the view snapshot in the Before render stage.
            // Catch that change at first use too; the next snapshot advances retained history.
            var atlas = MaterialAtlasSystem.Instance;
            bool changed = reactive || atlas.SurfaceDetailRevision != capturedAtlasRevision || !atlas.IsBuildComplete;
            program.Uniform("vge_displacementReactive", changed ? 1 : 0);
        }
        program.Uniform("vge_tessellationDistance", settings.FadeStartMetres, settings.FadeEndMetres);
        program.Uniform("vge_tessellationPixels", settings.TargetEdgePixels, (float)settings.MaximumLevel);
        program.Uniform("vge_tessellationFocalPixels", Math.Max(1, focalPixels));
    }

    /// <summary>Rejects the first frame after shader/world replacement without invalidating unrelated lighting.</summary>
    internal static void ResetHistory()
    {
        history.Reset();
        reactive = true;
        frameSerial++;
    }
    #endregion
}
