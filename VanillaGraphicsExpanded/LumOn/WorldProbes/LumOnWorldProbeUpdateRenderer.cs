using System;
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
internal sealed class LumOnWorldProbeUpdateRenderer : IRenderer, IDisposable
{
	private const double RenderOrderValue = 0.9999;
	private const int RenderRangeValue = 1;

	private readonly ICoreClientAPI capi;
	private readonly VgeConfig config;
	private readonly LumOnWorldProbeClipmapBufferManager clipmapBufferManager;

	private LumOnWorldProbeScheduler? scheduler;
	private Action<LumOnWorldProbeScheduler.WorldProbeAnchorShiftEvent>? schedulerAnchorShiftHandler;

	private LumOnWorldProbeTraceService? traceService;
	private BlockAccessorWorldProbeTraceScene? traceScene;
	private IBlockAccessor? traceBlockAccessor;

	private readonly System.Collections.Generic.List<LumOnWorldProbeTraceResult> traceResults = new();

	// Debug-only CPU->GPU heatmap buffer for world-probe lifecycle visualization.
	private LumOnWorldProbeLifecycleState[]? lifecycleScratch;
	private ushort[]? debugStateTexels;

	private readonly LumOnWorldProbeClipmapBufferManager.DebugTraceRay[] debugQueuedTraceRaysScratch =
		new LumOnWorldProbeClipmapBufferManager.DebugTraceRay[LumOnWorldProbeClipmapBufferManager.MaxDebugTraceRays];

	private readonly float[] modelViewMatrix = new float[16];
	private readonly float[] invModelViewMatrix = new float[16];

	private bool startupLogged;
	private int frameIndex;

	public double RenderOrder => RenderOrderValue;

	public int RenderRange => RenderRangeValue;

	public LumOnWorldProbeUpdateRenderer(
		ICoreClientAPI capi,
		VgeConfig config,
		LumOnWorldProbeClipmapBufferManager clipmapBufferManager)
	{
		this.capi = capi ?? throw new ArgumentNullException(nameof(capi));
		this.config = config ?? throw new ArgumentNullException(nameof(config));
		this.clipmapBufferManager = clipmapBufferManager ?? throw new ArgumentNullException(nameof(clipmapBufferManager));

		capi.Event.RegisterRenderer(this, EnumRenderStage.Done, "vge_worldprobe_update");
		capi.Event.LeaveWorld += OnLeaveWorld;

		capi.Logger.Notification("[VGE] World-probe update renderer registered (Done @ {0})", RenderOrderValue);
	}

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
		if (scheduler is null)
		{
			return;
		}

		if (!TryGetCameraPositions(out Vec3d camPosWorld, out Vec3d camPosMatrixSpace))
		{
			return;
		}

		var cfg = config.WorldProbeClipmap;
		double baseSpacing = Math.Max(1e-6, cfg.ClipmapBaseSpacing);
		int wpTileSize = cfg.OctahedralTileSize;
		int wpTexelsPerUpdate = cfg.AtlasTexelsPerUpdate;

		using (Profiler.BeginScope("LumOn.WorldProbe.Schedule.UpdateOrigins", "LumOn"))
		{
			scheduler.UpdateOrigins(camPosWorld, baseSpacing);
		}

		ApplyPendingWorldProbeDirtyChunks(baseSpacing);

		UpdateRuntimeParams(resources, camPosWorld, camPosMatrixSpace, baseSpacing);

		// World-space tracing requires the game world to be ready.
		traceBlockAccessor ??= capi.World?.BlockAccessor;
		var worldAccessor = traceBlockAccessor;
		if (worldAccessor is null)
		{
			return;
		}

		var mainThreadAccessor = capi.World?.BlockAccessor;
		if (mainThreadAccessor is null)
		{
			return;
		}

		traceScene ??= new BlockAccessorWorldProbeTraceScene(worldAccessor);
		traceService ??= new LumOnWorldProbeTraceService(
			traceScene,
			maxQueuedWorkItems: 2048,
			tryClaim: (req, frame) => scheduler.TryClaim(req, frame));

		int[] perLevelBudgets = cfg.PerLevelProbeUpdateBudget ?? Array.Empty<int>();
		System.Collections.Generic.List<LumOnWorldProbeUpdateRequest> requests;
		using (Profiler.BeginScope("LumOn.WorldProbe.Schedule.BuildList", "LumOn"))
		{
			requests = scheduler.BuildUpdateList(
				frameIndex,
				camPosWorld,
				baseSpacing,
				perLevelBudgets,
				cfg.TraceMaxProbesPerFrame,
				cfg.UploadBudgetBytesPerFrame);
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

		if (config.LumOn.DebugMode == LumOnDebugMode.WorldProbeOrbsPoints)
		{
			PublishQueuedTraceRaysForDebug(resources, baseSpacing, requests);
		}
		else
		{
			clipmapBufferManager.ClearDebugTraceRays(frameIndex);
		}

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

				if (LumOnWorldProbeSolidBlockCheck.IsProbeCenterInsideSolidBlock(mainThreadAccessor, probePosWorld))
				{
					// Probes whose centers lie inside solid collision should not contribute (likely invalid data).
					// Probes are re-checked automatically when their ring-buffer slot is re-used on anchor shifts.
					scheduler.Disable(req);
					continue;
				}

				double maxDist = spacing * resources.Resolution;
				var item = new LumOnWorldProbeTraceWorkItem(frameIndex, req, probePosWorld, maxDist, wpTileSize, wpTexelsPerUpdate);
				if (!traceService.TryEnqueue(item))
				{
					scheduler.Unqueue(req);
				}
			}
		}

		using (Profiler.BeginScope("LumOn.WorldProbe.Trace.Drain", "LumOn"))
		{
			traceResults.Clear();
			while (traceService.TryDequeueResult(out var res))
			{
				if (res.Success)
				{
					traceResults.Add(res);
					scheduler.Complete(res.Request, frameIndex, success: true);
				}
				else
				{
					bool aborted = res.FailureReason == WorldProbeTraceFailureReason.Aborted;
					scheduler.Complete(res.Request, frameIndex, success: false, aborted);
				}
			}
		}

		if (traceResults.Count > 0)
		{
			using var uploadScope = Profiler.BeginScope("LumOn.WorldProbe.Upload", "LumOn");
			_ = uploader.Upload(resources, traceResults, cfg.UploadBudgetBytesPerFrame);
		}

		LumOnDebugMode debugMode = config.LumOn.DebugMode;
		if (debugMode == LumOnDebugMode.WorldProbeMetaFlagsHeatmap || debugMode == LumOnDebugMode.WorldProbeOrbsPoints)
		{
			using var heatmapScope = Profiler.BeginScope("LumOn.WorldProbe.DebugHeatmap", "LumOn");
			UpdateDebugHeatmap(resources);
		}
	}

	public void Dispose()
	{
		capi.Event.LeaveWorld -= OnLeaveWorld;

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

		schedulerAnchorShiftHandler ??= evt => clipmapBufferManager.NotifyAnchorShifted(in evt);
		scheduler.AnchorShifted += schedulerAnchorShiftHandler;
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
		}
	}

	private void UpdateRuntimeParams(
		LumOnWorldProbeClipmapGpuResources resources,
		Vec3d camPosWorld,
		Vec3d camPosMatrixSpace,
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
					(float)(o.X - camPosWorld.X),
					(float)(o.Y - camPosWorld.Y),
					(float)(o.Z - camPosWorld.Z));
				ringsSpan[i] = new Vector3(r.X, r.Y, r.Z);
			}
			else
			{
				originsSpan[i] = default;
				ringsSpan[i] = default;
			}
		}

		clipmapBufferManager.UpdateRuntimeParams(
			camPosWorld,
			new Vector3((float)camPosMatrixSpace.X, (float)camPosMatrixSpace.Y, (float)camPosMatrixSpace.Z),
			baseSpacingF,
			levels,
			resolution,
			originsSpan,
			ringsSpan);
	}

	private bool TryGetCameraPositions(out Vec3d cameraPosWorld, out Vec3d cameraPosMatrixSpace)
	{
		var player = capi.World?.Player;
		if (player?.Entity is null)
		{
			cameraPosWorld = new Vec3d();
			cameraPosMatrixSpace = new Vec3d();
			return false;
		}

		cameraPosWorld = player.Entity.CameraPos;

		Array.Copy(capi.Render.CameraMatrixOriginf, modelViewMatrix, 16);
		Array.Copy(modelViewMatrix, invModelViewMatrix, 16);
		MatrixHelper.Invert(invModelViewMatrix, invModelViewMatrix);

		cameraPosMatrixSpace = new Vec3d(invModelViewMatrix[12], invModelViewMatrix[13], invModelViewMatrix[14]);
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
				var end = new Vec3d(
					probeCenter.X + d.X * (spacing * resources.Resolution),
					probeCenter.Y + d.Y * (spacing * resources.Resolution),
					probeCenter.Z + d.Z * (spacing * resources.Resolution));

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
				ushort a = On;

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

				debugStateTexels[dst + 0] = r;
				debugStateTexels[dst + 1] = g;
				debugStateTexels[dst + 2] = b;
				debugStateTexels[dst + 3] = a;
			}
		}

		resources.UploadDebugState0(debugStateTexels);
	}
}
