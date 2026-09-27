using System;
using System.Collections.Immutable;
using System.Numerics;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.PBR.Atmosphere;

/// <summary>Schedules bounded atmospheric compute work and reads only fenced, complete generations.</summary>
internal sealed class AtmosphereGpuComputation : IDisposable
{
    internal const int CellsPerDispatch = 64;
    internal const int MaximumOutputBytes = (128 * 96 + 4) * 16;
    private readonly GpuComputePipeline scattering, sky, lighting;
    private readonly GpuShaderStorageBuffer parameters, source;
    private readonly GpuQueue<Vector4> output;
    private GpuFence? batch;
    private Request? admitted, completed;
    private (int Weather, int Quality, int Albedo)? sourceKey;
    private int nextCell;
    private bool disposed;

    /// <summary>Captures one immutable generation and its quantized invalidation key.</summary>
    private readonly record struct Request(Vector3 Sun, float Altitude, int Weather, int Width, int Height, int Quality, int Albedo)
    {
        internal (int, int, int, int, int, int, int, int, int) Key =>
            ((int)MathF.Round(Sun.X * AtmosphereSolarDisk.DirectionResolution), (int)MathF.Round(Sun.Y * AtmosphereSolarDisk.DirectionResolution), (int)MathF.Round(Sun.Z * AtmosphereSolarDisk.DirectionResolution),
                (int)MathF.Round(Altitude * 40), Weather, Width, Height, Quality, Albedo);
    }

    #region Creation and capabilities
    /// <summary>Checks cached compute, binary ingestion and storage limits without probing per sample.</summary>
    internal static bool Supported => GpuSupport.IsInitialized
        && Supports(GpuSupport.SupportsArbComputeShader, GpuSupport.SupportsArbGlSpirv,
            GpuSupport.SupportsArbShaderStorageBufferObject, GpuSupport.MaxComputeWorkGroupInvocations,
            GpuSupport.MaxComputeWorkGroupSize.IsEmpty ? 0 : GpuSupport.MaxComputeWorkGroupSize[0],
            GpuSupport.MaxComputeWorkGroupCount.IsEmpty ? 0 : GpuSupport.MaxComputeWorkGroupCount[0],
            GpuSupport.MaxShaderStorageBufferBindings, GpuSupport.MaxShaderStorageBlockSize);

    /// <summary>Pure admission policy shared with unsupported-device tests; minimum GL 4.3 shared memory suffices.</summary>
    internal static bool Supports(bool compute, bool spirv, bool storage, int invocations, int groupSize,
        int groupCount, int bindings, long blockBytes) => compute && spirv && storage
        && invocations >= 64 && groupSize >= 64 && groupCount >= 192 && bindings >= 3 && blockBytes >= MaximumOutputBytes;

    /// <summary>Creates all programs before taking ownership; failure releases every partially created resource.</summary>
    internal static AtmosphereGpuComputation Create(ICoreAPI api)
    {
        GpuComputePipeline? scattering = null, sky = null, lighting = null;
        try
        {
            if (!GpuComputePipeline.TryCreateFromAssets(api, new ShaderSettings(AtmosphereComputePrograms.Scattering), out scattering, out string log)
                || !GpuComputePipeline.TryCreateFromAssets(api, new ShaderSettings(AtmosphereComputePrograms.Sky), out sky, out log)
                || !GpuComputePipeline.TryCreateFromAssets(api, new ShaderSettings(AtmosphereComputePrograms.Lighting), out lighting, out log))
                throw new InvalidOperationException("Atmosphere compute linking failed: " + log);
            return new(scattering!, sky!, lighting!);
        }
        catch { scattering?.Dispose(); sky?.Dispose(); lighting?.Dispose(); throw; }
    }

    /// <summary>Allocates bounded persistent storage; table capacity covers all admitted quality levels.</summary>
    private AtmosphereGpuComputation(GpuComputePipeline scattering, GpuComputePipeline sky, GpuComputePipeline lighting)
    {
        this.scattering = scattering; this.sky = sky; this.lighting = lighting;
        GpuShaderStorageBuffer? parameters = null, source = null;
        GpuQueue<Vector4>? output = null;
        try
        {
            parameters = GpuShaderStorageBuffer.Create(debugName: "Atmosphere.Parameters");
            source = GpuShaderStorageBuffer.Create(debugName: "Atmosphere.Scattering");
            output = new(MaximumOutputBytes / 16, debugName: "Atmosphere.Output");
            parameters.EnsureCapacity(64, growExponentially: false);
            source.EnsureCapacity(128 * 64 * 16, growExponentially: false);
            this.parameters = parameters; this.source = source; this.output = output;
        }
        catch { parameters?.Dispose(); source?.Dispose(); output?.Dispose(); throw; }
    }
    #endregion

    #region Scheduling and completion
    /// <summary>Finishes admitted work before accepting newer inputs, keeping publication starvation-free.</summary>
    internal AtmosphereLighting? Update(Vector3 sun, float altitude, float clouds, int width, int height, int quality = 0, float groundAlbedo = .1f)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        AtmosphereLighting? ready = null;
        if (output.Pending)
        {
            if (!output.TryRead(ReadLighting, out ready)) return null;
            completed = admitted; admitted = null;
        }
        if (batch is not null)
        {
            var status = batch.Poll();
            if (status == WaitSyncStatus.TimeoutExpired) return ready;
            if (status != WaitSyncStatus.AlreadySignaled && status != WaitSyncStatus.ConditionSatisfied)
                throw new InvalidOperationException("Atmosphere dispatch completion failed.");
            batch.Dispose(); batch = null;
        }
        if (admitted is null)
        {
            if (!float.IsFinite(sun.LengthSquared()) || sun.LengthSquared() < .0001f
                || !float.IsFinite(altitude) || !float.IsFinite(clouds)) return ready;
            var request = new Request(Vector3.Normalize(sun), Math.Clamp(altitude, 0, 99),
                (int)MathF.Round(Math.Clamp(clouds, 0, 1) * 20), Math.Clamp(width, 16, 128),
                Math.Clamp(height, 8, 96), Math.Clamp(quality, 0, 3), AtmosphereSeasonModel.AlbedoBucket(groundAlbedo));
            if (completed?.Key == request.Key) return ready;
            admitted = request; nextCell = 0;
        }
        Dispatch();
        return ready;
    }

    /// <summary>Submits one bounded source-table batch or the dependent sky and integral passes.</summary>
    private void Dispatch()
    {
        var request = admitted!.Value;
        var budget = AtmosphereScatteringBudget.FromQuality(request.Quality);
        Span<Vector4> values = stackalloc Vector4[4];
        values[0] = new(request.Sun, Math.Clamp(request.Altitude, .001f, 99));
        values[1] = new(1f + 7f * (request.Weather / 20f), request.Albedo / 50f, request.Width, request.Height);
        values[2] = new(budget.Width, budget.Height, budget.DirectionSamples, budget.RaySamples);
        values[3] = new(budget.LightSamples, nextCell, AtmosphereSkyMapping.Horizon(request.Altitude), 0);
        parameters.UploadSubData<Vector4>(values, 0, 64);
        parameters.BindBase(0); source.BindBase(1);
        try
        {
            if (sourceKey != (request.Weather, request.Quality, request.Albedo) && nextCell < budget.Width * budget.Height)
            {
                int count = Math.Min(CellsPerDispatch, budget.Width * budget.Height - nextCell);
                scattering.Dispatch(count);
                GpuComputePipeline.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit);
                nextCell += count;
                // A zero-time poll on the next frame gates further work; never drain an entire table in one frame.
                batch = GpuFence.Insert();
                // Flush through the existing fence API with a zero timeout to guarantee progress without waiting.
                var status = batch.Wait(TimeSpan.Zero);
                if (status == WaitSyncStatus.WaitFailed) throw new InvalidOperationException("Atmosphere dispatch submission failed.");
                return;
            }
            sourceKey = (request.Weather, request.Quality, request.Albedo);
            int countOutput = request.Width * request.Height + 4;
            // GpuQueue owns allocation/submission; every output record is overwritten by the two producers.
            output.PrepareGpuWrite(countOutput);
            output.Buffer.BindBase(2);
            sky.Dispatch((request.Width * request.Height + 63) >> 6);
            GpuComputePipeline.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit);
            lighting.Dispatch(1);
            GpuComputePipeline.MemoryBarrier(MemoryBarrierFlags.BufferUpdateBarrierBit);
            output.Submit(countOutput);
        }
        finally
        {
            GpuShaderStorageBuffer.UnbindBase(0); GpuShaderStorageBuffer.UnbindBase(1); GpuShaderStorageBuffer.UnbindBase(2);
        }
    }

    /// <summary>Copies a completed bounded mapping into the same immutable publication used by CPU consumers.</summary>
    private AtmosphereLighting ReadLighting(ReadOnlySpan<Vector4> values)
    {
        var request = admitted!.Value;
        foreach (var value in values)
            if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z))
                throw new InvalidOperationException("Atmospheric GPU transport produced nonfinite radiance.");
        return new(request.Sun, new(values[0].X, values[0].Y, values[0].Z),
            new(values[1].X, values[1].Y, values[1].Z), new(values[2].X, values[2].Y, values[2].Z),
            new(values[3].X, values[3].Y, values[3].Z), ImmutableArray.Create<float>(MemoryMarshal.Cast<Vector4, float>(values[4..])))
            { Width = request.Width, Height = request.Height, HorizonElevation = AtmosphereSkyMapping.Horizon(request.Altitude) };
    }
    #endregion

    #region Lifetime
    /// <summary>Retires in-flight resources through the GPU lifetime owners without publishing or waiting.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; batch?.Dispose(); output.Dispose(); parameters.Dispose(); source.Dispose();
        scattering.Dispose(); sky.Dispose(); lighting.Dispose(); admitted = null;
    }
    #endregion
}
