using System;
using VanillaGraphicsExpanded.Numerics;
using System.Numerics;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

using VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.Profiling;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.LumOn.WorldProbes;

/// <summary>
/// Phase 18 world-probe clipmap updater.
/// Runs at the very end of the frame (Done stage) so it can schedule/trace/upload work
/// for the next frame without blocking the main LumOn render passes.
/// </summary>
internal sealed partial class LumOnWorldProbeUpdateRenderer : IRenderer, IDisposable
{
	private const double RenderOrderValue = 0.9999;
	private const int RenderRangeValue = 1;
	private const int MaximumSunLightLevel = 32;
	private const double DebugTraceRayLengthInProbeSpacings = 0.5;

	private readonly ICoreClientAPI capi;
	private readonly VgeConfig config;
	private readonly Func<LumOnCameraState?> readCamera;
	private readonly LumOnWorldProbeClipmapBufferManager clipmapBufferManager;

	private LumOnWorldProbeScheduler? scheduler;
	private Action<LumOnWorldProbeScheduler.WorldProbeAnchorShiftEvent>? schedulerAnchorShiftHandler;

	private LumOnWorldProbeTraceService? traceService;
	private BlockAccessorWorldProbeTraceScene? traceScene;
	private IBlockAccessor? traceBlockAccessor;


	private readonly System.Collections.Generic.List<LumOnWorldProbeScheduler.ProbeCenterValidation> validationResults = new();
	private readonly System.Collections.Generic.List<LumOnWorldProbeUpdateRequest> enqueuedTraceRequests = new();

	// Debug-only CPU->GPU heatmap buffer for world-probe lifecycle visualization.
	private LumOnWorldProbeLifecycleState[]? lifecycleScratch;
	private bool[]? unavailableProbeSlots;
	private ushort[]? debugStateTexels;

	private readonly LumOnWorldProbeClipmapBufferManager.DebugTraceRay[] debugQueuedTraceRaysScratch =
		new LumOnWorldProbeClipmapBufferManager.DebugTraceRay[LumOnWorldProbeClipmapBufferManager.MaxDebugTraceRays];

	private bool startupLogged;
	private int frameIndex;

	public double RenderOrder => RenderOrderValue;

	public int RenderRange => RenderRangeValue;

	/// <summary>Registers asynchronous probe updates with an optional camera source; ordinary runtime reads the engine player.</summary>
	public LumOnWorldProbeUpdateRenderer(
		ICoreClientAPI capi,
		VgeConfig config,
		LumOnWorldProbeClipmapBufferManager clipmapBufferManager,
		Func<LumOnCameraState?>? readCamera = null)
	{
		this.capi = capi ?? throw new ArgumentNullException(nameof(capi));
		this.config = config ?? throw new ArgumentNullException(nameof(config));
		this.readCamera = readCamera ?? (() => LumOnCameraState.Read(capi));
		this.clipmapBufferManager = clipmapBufferManager ?? throw new ArgumentNullException(nameof(clipmapBufferManager));

		capi.Event.RegisterRenderer(this, EnumRenderStage.Done, "vge_worldprobe_update");
		capi.Event.LeaveWorld += OnLeaveWorld;

		capi.Logger.Notification("[VGE] World-probe update renderer registered (Done @ {0})", RenderOrderValue);
	}

    /// <summary>Publishes the clipmap layout and traces geometry independently of surface-lighting readiness.</summary>
	public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
	{
		if (stage != EnumRenderStage.Done)
		{
			return;
		}

		frameIndex++;

		if (!config.LumOn.Enabled)
		{
			return;
		}

		clipmapBufferManager.EnsureResources();
		var resources = clipmapBufferManager.Resources;
		var uploader = clipmapBufferManager.Uploader;
		if (resources is null || uploader is null)
		{
			return;
		}

		EnsureScheduler(resources);
        PrepareSurfaceLighting(resources);
		if (scheduler is null)
		{
			return;
		}

		if (!TryGetPlayerOriginWorld(out Vec3d playerOriginWorld))
		{
			return;
		}

		var cfg = config.WorldProbeClipmap;
		double baseSpacing = Math.Max(1e-6, cfg.ClipmapBaseSpacing);
		int wpTileSize = cfg.OctahedralTileSize;
		int wpTexelsPerUpdate = cfg.AtlasTexelsPerUpdate;

		using (Profiler.BeginScope("LumOn.WorldProbe.Schedule.UpdateOrigins", "LumOn"))
		{
			scheduler.UpdateOrigins(playerOriginWorld, baseSpacing);
		}

		ApplyPendingWorldProbeDirtyChunks(baseSpacing);

		UpdateRuntimeParams(resources, playerOriginWorld, baseSpacing);

        // Geometry and sky visibility do not require a surface-lighting snapshot.
        // Surface hits remain deferred until the render-thread resolver can validate their lighting.

		// World-space tracing requires the game world to be ready.
		traceBlockAccessor ??= capi.World?.BlockAccessor;
		var worldAccessor = traceBlockAccessor;
		if (worldAccessor is null)
		{
            resources.FlushHistoryInvalidation();
			return;
		}

		var mainThreadAccessor = capi.World?.BlockAccessor;
		if (mainThreadAccessor is null)
		{
            resources.FlushHistoryInvalidation();
			return;
		}

		int[] perLevelBudgets = cfg.PerLevelProbeUpdateBudget ?? Array.Empty<int>();
		EnsureUnavailableProbeSlots(resources);
		validationResults.Clear();
		using (Profiler.BeginScope("LumOn.WorldProbe.ValidateSolidCenters", "LumOn"))
		{
			scheduler.ValidateProbeCenters(
				baseSpacing,
				perLevelBudgets,
				(level, probePosWorld) =>
				{
					LumOnWorldProbeCenterOccupancy occupancy = LumOnWorldProbeSolidBlockCheck.ClassifyProbeCenter(
						mainThreadAccessor,
						new VanillaGraphicsExpanded.Numerics.Vector3d(probePosWorld.X, probePosWorld.Y, probePosWorld.Z));
					return occupancy;
				},
				validationResults);
		}

		for (int i = 0; i < validationResults.Count; i++)
		{
			var validation = validationResults[i];
			SetUnavailableProbeSlot(validation.Request, validation.Occupancy == LumOnWorldProbeCenterOccupancy.Unavailable);
            if (IsProbeCenterSuppressed(validation.Occupancy))
                {
                var storage = new VectorInt3(validation.Request.StorageIndex.X, validation.Request.StorageIndex.Y, validation.Request.StorageIndex.Z);
                resources.QueueClearLocalBox(validation.Request.Level, VectorInt3.Zero, storage, storage);
            }
		}

		// Hit radiance is resolved from the Surface Cache; workers need only collision geometry.
		traceScene ??= new BlockAccessorWorldProbeTraceScene(worldAccessor, sampleVanillaLighting: false);
        // A retired worker must never claim work on a replacement scheduler.
        var traceScheduler = scheduler;
		traceService ??= new LumOnWorldProbeTraceService(
			traceScene,
			maxQueuedWorkItems: 2048,
			tryClaim: (req, frame) => traceScheduler.TryClaim(req, frame));

		System.Collections.Generic.List<LumOnWorldProbeUpdateRequest> requests;
		using (Profiler.BeginScope("LumOn.WorldProbe.Schedule.BuildList", "LumOn"))
		{
			requests = scheduler.BuildUpdateList(
				frameIndex,
				playerOriginWorld,
				baseSpacing,
				perLevelBudgets,
				cfg.TraceMaxProbesPerFrame,
				cfg.UploadBudgetBytesPerFrame,
				cfg.AtlasTexelsPerUpdate);
		}

		if (!startupLogged)
		{
			startupLogged = true;
			capi.Logger.Notification(
				"[VGE] Phase 18 world-probe clipmap update active (levels={0}, res={1}, baseSpacing={2:0.###})",
				resources.Levels,
				resources.Resolution,
				baseSpacing);
		}

		enqueuedTraceRequests.Clear();
		using (Profiler.BeginScope("LumOn.WorldProbe.Trace.Enqueue", "LumOn"))
		{
			for (int i = 0; i < requests.Count; i++)
			{
				var req = requests[i];
				if (!scheduler.TryGetLevelParams(req.Level, out var originMinCorner, out _))
				{
					scheduler.Unqueue(req);
					continue;
				}

				double spacing = LumOnClipmapTopology.GetSpacing(baseSpacing, req.Level);
				Vec3d probePosWorldVs = LumOnClipmapTopology.IndexToProbeCenterWorld(req.LocalIndex, originMinCorner, spacing);
				var probePosWorld = new VanillaGraphicsExpanded.Numerics.Vector3d(probePosWorldVs.X, probePosWorldVs.Y, probePosWorldVs.Z);
				// Determine sunlight level at the probe position.
				int sunlight = mainThreadAccessor.GetLightLevel(
					(int)Math.Floor(probePosWorld.X),
					(int)Math.Floor(probePosWorld.Y),
					(int)Math.Floor(probePosWorld.Z),
					EnumLightLevelType.OnlySunLight);
				int rainMapHeight = mainThreadAccessor.GetRainMapHeightAt(
					(int)Math.Floor(probePosWorld.X),
					(int)Math.Floor(probePosWorld.Z));
				LumOnWorldProbeCenterOccupancy occupancy = LumOnWorldProbeSolidBlockCheck.ClassifyProbeCenter(mainThreadAccessor, probePosWorld);
				SetUnavailableProbeSlot(req, occupancy == LumOnWorldProbeCenterOccupancy.Unavailable);
				if (IsProbeCenterSuppressed(occupancy))
				{
					// Probes with unavailable center chunks wait for ChunkDirty/NewlyLoaded to re-enable them.
					// Centers inside solid collision cannot contribute and are likewise re-checked on ring reuse.
					var storage = new VectorInt3(req.StorageIndex.X, req.StorageIndex.Y, req.StorageIndex.Z);
                    resources.QueueClearLocalBox(req.Level, VectorInt3.Zero, storage, storage);
                    scheduler.Disable(req);
					continue;
				}

				LumOnWorldProbeImportanceFlags dynamicImportanceFlags = LumOnWorldProbeImportance.GetDynamicFlags(
					sunlight,
					MaximumSunLightLevel,
					req.LocalIndex.Y,
					probePosWorld.Y,
					spacing,
					rainMapHeight);
				scheduler.UpdateImportanceFlags(
					req.Level,
					req.StorageLinearIndex,
					dynamicImportanceFlags,
					LumOnWorldProbeImportance.DynamicFlags);

				double maxDist = spacing * resources.Resolution;
				var item = new LumOnWorldProbeTraceWorkItem(
					frameIndex,
					req,
					probePosWorld,
					maxDist,
					wpTileSize,
					wpTexelsPerUpdate,
					EnableDirectionPIS: cfg.EnableDirectionPIS,
					DirectionPISExploreFraction: cfg.DirectionPISExploreFraction,
					DirectionPISExploreCount: cfg.DirectionPISExploreCount,
					DirectionPISWeightEpsilon: cfg.DirectionPISWeightEpsilon,
					NearbySolidHitDistance: spacing * 0.5d, DeferSurfaceLighting: true, SurfaceRevision: surfaceRevision);
				if (!traceService.TryEnqueue(item))
				{
					scheduler.Unqueue(req);
				}
				else
				{
					enqueuedTraceRequests.Add(req);
				}
			}
		}

		if (config.LumOn.DebugMode == LumOnDebugMode.WorldProbeOrbsPoints)
		{
			PublishQueuedTraceRaysForDebug(resources, baseSpacing, enqueuedTraceRequests);
		}
		else
		{
			clipmapBufferManager.ClearDebugTraceRays(frameIndex);
		}

        // Retire old generations before partial uploads can publish new directional history.
        resources.FlushHistoryInvalidation();
        ResolveSurfaceLighting(resources, uploader);

		LumOnDebugMode debugMode = config.LumOn.DebugMode;
		if (debugMode is LumOnDebugMode.WorldProbeMetaFlagsHeatmap
			or LumOnDebugMode.WorldProbeOrbsPoints
			or LumOnDebugMode.WorldProbeImportance)
		{
			using var heatmapScope = Profiler.BeginScope("LumOn.WorldProbe.DebugHeatmap", "LumOn");
			UpdateDebugHeatmap(resources);
		}
	}

	public void Dispose()
	{
		capi.Event.UnregisterRenderer(this, EnumRenderStage.Done);
		capi.Event.LeaveWorld -= OnLeaveWorld;
        ReleaseSurfaceLighting();

		traceService?.Dispose();
		traceService = null;
		traceScene = null;
		traceBlockAccessor = null;

		if (scheduler is not null && schedulerAnchorShiftHandler is not null)
		{
			scheduler.AnchorShifted -= schedulerAnchorShiftHandler;
		}

		scheduler = null;
		schedulerAnchorShiftHandler = null;
	}

	private void OnLeaveWorld()
	{
        ReleaseSurfaceLighting();
		scheduler?.ResetAll();

		traceService?.Dispose();
		traceService = null;
		traceScene = null;
		traceBlockAccessor = null;

		startupLogged = false;

		if (clipmapBufferManager.Resources is not null)
		{
			clipmapBufferManager.Resources.ClearAll();
		}
	}

	private void EnsureScheduler(LumOnWorldProbeClipmapGpuResources resources)
	{
		if (scheduler is not null && scheduler.LevelCount == resources.Levels && scheduler.Resolution == resources.Resolution)
		{
			return;
		}

		if (scheduler is not null && schedulerAnchorShiftHandler is not null)
		{
			scheduler.AnchorShifted -= schedulerAnchorShiftHandler;
		}

		scheduler = new LumOnWorldProbeScheduler(resources.Levels, resources.Resolution);

		schedulerAnchorShiftHandler ??= OnProbeAnchorShifted;
		scheduler.AnchorShifted += schedulerAnchorShiftHandler;
	}

	private static bool IsProbeCenterSuppressed(LumOnWorldProbeCenterOccupancy occupancy)
	{
		return occupancy is LumOnWorldProbeCenterOccupancy.Unavailable
			or LumOnWorldProbeCenterOccupancy.OutsideWorldHeight
			or LumOnWorldProbeCenterOccupancy.InsideCollision;
	}

	private void EnsureUnavailableProbeSlots(LumOnWorldProbeClipmapGpuResources resources)
	{
		int slotCount = resources.Levels * resources.Resolution * resources.Resolution * resources.Resolution;
		if (unavailableProbeSlots is null || unavailableProbeSlots.Length != slotCount)
		{
			unavailableProbeSlots = new bool[slotCount];
		}
	}

	private void SetUnavailableProbeSlot(in LumOnWorldProbeUpdateRequest request, bool unavailable)
	{
		if (unavailableProbeSlots is null || scheduler is null
			|| (uint)request.Level >= (uint)scheduler.LevelCount
			|| (uint)request.StorageLinearIndex >= (uint)scheduler.ProbesPerLevel)
		{
			return;
		}

		unavailableProbeSlots[(request.Level * scheduler.ProbesPerLevel) + request.StorageLinearIndex] = unavailable;
	}

	private void ApplyPendingWorldProbeDirtyChunks(double baseSpacing)
	{
		if (scheduler is null)
		{
			return;
		}

		var wpms = capi.ModLoader.GetModSystem<WorldProbeModSystem>();
		if (wpms is null)
		{
			return;
		}

		int chunkSize = GlobalConstants.ChunkSize;
		int levels = scheduler.LevelCount;

		wpms.DrainPendingWorldProbeDirtyChunks(
			onChunk: (cx, cy, cz) =>
			{
				var min = new Vec3d(cx * chunkSize, cy * chunkSize, cz * chunkSize);
				var max = new Vec3d(min.X + chunkSize, min.Y + chunkSize, min.Z + chunkSize);

				for (int level = 0; level < levels; level++)
				{
					scheduler.MarkDirtyWorldAabb(level, min, max, baseSpacing);
                    ClearDirtyProbeHistory(level, new Vector3d(min.X, min.Y, min.Z), new Vector3d(max.X, max.Y, max.Z), baseSpacing);
				}
			},
			overflowCount: out int overflow);

		if (overflow > 0 && scheduler.TryGetLevelParams(0, out var originMin, out _))
		{
			double spacing0 = LumOnClipmapTopology.GetSpacing(baseSpacing, level: 0);
			double size = spacing0 * scheduler.Resolution;
			var min = originMin;
			var max = new Vec3d(min.X + size, min.Y + size, min.Z + size);
			scheduler.MarkDirtyWorldAabb(level: 0, min, max, baseSpacing);
            ClearDirtyProbeHistory(0, new Vector3d(min.X, min.Y, min.Z), new Vector3d(max.X, max.Y, max.Z), baseSpacing);
		}
	}

	private void UpdateRuntimeParams(
		LumOnWorldProbeClipmapGpuResources resources,
		Vec3d playerOriginWorld,
		double baseSpacing)
	{
		if (scheduler is null)
		{
			return;
		}

		int levels = Math.Clamp(resources.Levels, 1, 8);
		int resolution = resources.Resolution;
		float baseSpacingF = (float)Math.Max(1e-6, baseSpacing);

		Span<Vector3> originsSpan = stackalloc Vector3[8];
		Span<Vector3> ringsSpan = stackalloc Vector3[8];

		for (int i = 0; i < 8; i++)
		{
			if (i < levels && scheduler.TryGetLevelParams(i, out var o, out var r))
			{
				originsSpan[i] = new Vector3(
					(float)(o.X - playerOriginWorld.X),
					(float)(o.Y - playerOriginWorld.Y),
					(float)(o.Z - playerOriginWorld.Z));
				ringsSpan[i] = new Vector3(r.X, r.Y, r.Z);
			}
			else
			{
				originsSpan[i] = default;
				ringsSpan[i] = default;
			}
		}

		clipmapBufferManager.UpdateRuntimeParams(
			playerOriginWorld,
			new Vector3((float)playerOriginWorld.X, (float)playerOriginWorld.Y, (float)playerOriginWorld.Z),
			baseSpacingF,
			levels,
			resolution,
			originsSpan,
			ringsSpan);
	}

	private bool TryGetPlayerOriginWorld(out Vec3d playerOriginWorld)
	{
		var camera = readCamera();
		if (camera is null)
		{
			playerOriginWorld = new Vec3d();
			return false;
		}

		playerOriginWorld = new Vec3d(camera.Value.PositionX, camera.Value.PositionY, camera.Value.PositionZ);
		return true;
	}

	private static bool IsWorldProbeCenterInsideSolidBlock(IBlockAccessor blockAccessor, VanillaGraphicsExpanded.Numerics.Vector3d probePosWorld)
	{
		// Backwards-compatible shim; keep the old method name for any debug tooling / reflection-based tests.
		return LumOnWorldProbeSolidBlockCheck.IsProbeCenterInsideSolidBlock(blockAccessor, probePosWorld);
	}

	private void PublishQueuedTraceRaysForDebug(
		LumOnWorldProbeClipmapGpuResources resources,
		double baseSpacing,
		System.Collections.Generic.List<LumOnWorldProbeUpdateRequest> requests)
	{
		if (scheduler is null)
		{
			return;
		}

		int s = Math.Max(1, config.WorldProbeClipmap.OctahedralTileSize);
		int k = Math.Max(1, config.WorldProbeClipmap.AtlasTexelsPerUpdate);
		var dirs = LumOnWorldProbeAtlasDirections.GetDirections(s);
		if (dirs.Length <= 0)
		{
			clipmapBufferManager.ClearDebugTraceRays(frameIndex);
			return;
		}

		const int maxPreviewProbes = 8;
		int raysPerProbe = Math.Min(dirs.Length, k);
		if (raysPerProbe <= 0)
		{
			clipmapBufferManager.ClearDebugTraceRays(frameIndex);
			return;
		}

		int probesToShow = Math.Min(requests.Count, Math.Max(1, LumOnWorldProbeClipmapBufferManager.MaxDebugTraceRays / raysPerProbe));
		probesToShow = Math.Min(probesToShow, maxPreviewProbes);

		var rays = debugQueuedTraceRaysScratch;
		int written = 0;

		int[] texelIndicesScratch = new int[raysPerProbe];
		Span<int> texelIndicesSpan = texelIndicesScratch;

		for (int i = 0; i < probesToShow; i++)
		{
			var req = requests[i];
			if (!scheduler.TryGetLevelParams(req.Level, out var originMinCorner, out _))
			{
				continue;
			}

			double spacing = LumOnClipmapTopology.GetSpacing(baseSpacing, req.Level);
			Vec3d probeCenter = LumOnClipmapTopology.IndexToProbeCenterWorld(req.LocalIndex, originMinCorner, spacing);

			var texelIndices = texelIndicesSpan;
			int texelCount = LumOnWorldProbeAtlasDirectionSlicing.FillTexelIndicesForUpdate(
				frameIndex: frameIndex,
				probeStorageLinearIndex: req.StorageLinearIndex,
				octahedralSize: s,
				texelsPerUpdate: k,
				destination: texelIndices);

			int take = Math.Min(raysPerProbe, texelCount);

			(float rCol, float gCol, float bCol) = i switch
			{
				0 => (1f, 0.2f, 0.2f),
				1 => (0.2f, 1f, 0.2f),
				_ => (0.2f, 0.4f, 1f),
			};

			for (int r = 0; r < take && written < rays.Length; r++)
			{
				int idx = texelIndices[r];
				var d = dirs[idx];
				double rayLength = spacing * DebugTraceRayLengthInProbeSpacings;
				var end = new Vec3d(
					probeCenter.X + d.X * rayLength,
					probeCenter.Y + d.Y * rayLength,
					probeCenter.Z + d.Z * rayLength);

				float a = 1f - (r / Math.Max(1f, take - 1f)) * 0.6f;
				rays[written++] = new LumOnWorldProbeClipmapBufferManager.DebugTraceRay(
					probeCenter,
					end,
					r: rCol,
					g: gCol,
					b: bCol,
					a: a);
			}
		}

		clipmapBufferManager.PublishDebugTraceRays(frameIndex, rays.AsSpan(0, written));
	}

	private void UpdateDebugHeatmap(LumOnWorldProbeClipmapGpuResources resources)
	{
		if (scheduler is null)
		{
			return;
		}

		int levels = Math.Clamp(resources.Levels, 1, 8);
		int resolution = resources.Resolution;
		int probesPerLevel = resolution * resolution * resolution;

		int atlasW = resources.AtlasWidth;
		int atlasH = resources.AtlasHeight;
		int texelCount = atlasW * atlasH;

		lifecycleScratch ??= new LumOnWorldProbeLifecycleState[probesPerLevel];
		if (lifecycleScratch.Length < probesPerLevel)
		{
			lifecycleScratch = new LumOnWorldProbeLifecycleState[probesPerLevel];
		}

		debugStateTexels ??= new ushort[texelCount * 4];
		if (debugStateTexels.Length != texelCount * 4)
		{
			debugStateTexels = new ushort[texelCount * 4];
		}

		const ushort On = ushort.MaxValue;
		const ushort Off = 0;
		const float MaxImportanceFactor = LumOnWorldProbeImportance.IndirectSunlightFactor
			+ LumOnWorldProbeImportance.CardinalSolidNeighborBoost;

		for (int level = 0; level < levels; level++)
		{
			if (!scheduler.TryCopyLifecycleStates(level, lifecycleScratch))
			{
				continue;
			}

			for (int storageLinear = 0; storageLinear < probesPerLevel; storageLinear++)
			{
				int x = storageLinear % resolution;
				int yz = storageLinear / resolution;
				int y = yz % resolution;
				int z = yz / resolution;

				int u = x + z * resolution;
				int v = y + level * resolution;

				int dst = (v * atlasW + u) * 4;

				ushort r = Off;
				ushort g = Off;
				ushort b = Off;
				scheduler.TryGetImportanceFlags(level, storageLinear, out LumOnWorldProbeImportanceFlags importanceFlags);
				float importance = LumOnWorldProbeImportance.ComputeFactor(importanceFlags);
				ushort a = (ushort)Math.Clamp(
					(int)MathF.Round(importance / MaxImportanceFactor * ushort.MaxValue),
					0,
					ushort.MaxValue);

				switch (lifecycleScratch[storageLinear])
				{
					case LumOnWorldProbeLifecycleState.Valid:
						b = On;
						break;
					case LumOnWorldProbeLifecycleState.Stale:
						r = On;
						break;
					case LumOnWorldProbeLifecycleState.Dirty:
						r = On;
						g = On;
						break;
					case LumOnWorldProbeLifecycleState.Queued:
						g = On;
						b = On;
						break;
					case LumOnWorldProbeLifecycleState.InFlight:
						g = On;
						break;
					case LumOnWorldProbeLifecycleState.Disabled:
						r = On;
						b = On;
						break;
				}

				if (unavailableProbeSlots is not null
					&& unavailableProbeSlots[(level * probesPerLevel) + storageLinear])
				{
					r = On;
					g = On;
					b = Off;
				}

				debugStateTexels[dst + 0] = r;
				debugStateTexels[dst + 1] = g;
				debugStateTexels[dst + 2] = b;
				debugStateTexels[dst + 3] = a;
			}
		}

		resources.UploadDebugState0(debugStateTexels);
	}
}
