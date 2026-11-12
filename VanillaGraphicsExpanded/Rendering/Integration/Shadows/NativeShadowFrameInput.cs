using System.Collections.Immutable;
using VanillaGraphicsExpanded.Rendering.Shadows.Candidates;
using VanillaGraphicsExpanded.Rendering.Shadows.Geometry;
using VanillaGraphicsExpanded.Rendering.Shadows.Lights;
using VanillaGraphicsExpanded.Rendering.Shadows.Scheduling;
using VanillaGraphicsExpanded.Rendering.Shadows.World;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Rendering.Integration.Shadows;

/// <summary>One frozen native frame prepared after current light capture and before any participating surface consumer.</summary>
internal sealed record NativeShadowFrameInput(ShadowLightSnapshot Lights, NativeShadowScene? Scene,
    ShadowWorldPublication World, ShadowWorldGpuBuffers WorldBuffers, ShadowCandidatePool Candidates,
    ShadowSceneChanges Changes, ShadowChangeBatch SceneChanges, ImmutableArray<ShadowFrameLight> Sources,
    ImmutableArray<ShaderProgramBase> SurfacePrograms, IClientWorldAccessor NativeWorld,
    System.Func<bool,bool,ShadowNativeMaterialShader> TerrainShader, ShadowEntityShader EntityShader,
    ShadowStaticShader ItemShader, float DeltaTime, float PaddingX, float PaddingY,
    long OtherLiveAndRetiringBytes, bool CurrentInputsComplete = true, ShadowFrameBudget? DemandBudget = null,
    ImmutableArray<ShadowCandidateDiagnostic> CandidateDiagnostics = default);
