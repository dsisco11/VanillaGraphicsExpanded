using System;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace VanillaGraphicsExpanded.PBR.Atmosphere;

/// <summary>Owns one background lookup build; only the render thread admits and consumes work.</summary>
internal sealed class AtmosphereComputation : IDisposable
{
    private readonly CancellationTokenSource cancellation = new();
    private Task<AtmosphereLighting>? pending;
    private (int, int, int, int, int, int, int)? admittedKey;
    private bool disposed;

    /// <summary>Exposes the admitted completion dependency without transferring publication ownership.</summary>
    internal Task<AtmosphereLighting>? Pending => pending;

    #region Scheduling
    /// <summary>Consumes finished work without blocking, then admits the latest inputs if they changed.</summary>
    internal AtmosphereLighting? Update(Vector3 sun, float altitude, float clouds, int width, int height)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        AtmosphereLighting? ready = null;
        if (pending is { IsCompleted: true })
        {
            var completed = pending;
            pending = null;
            try { ready = completed.GetAwaiter().GetResult(); }
            catch { admittedKey = null; throw; }
        }
        if (pending is not null) return ready;
        if (!float.IsFinite(sun.LengthSquared()) || sun.LengthSquared() < .0001f
            || !float.IsFinite(altitude) || !float.IsFinite(clouds)) return ready;
        sun = Vector3.Normalize(sun);
        width = Math.Clamp(width, 16, AtmosphereLookup.DefaultWidth << 3);
        height = Math.Clamp(height, 8, AtmosphereLookup.DefaultHeight << 3);
        var key = ((int)MathF.Round(sun.X * 256), (int)MathF.Round(sun.Y * 256),
            (int)MathF.Round(sun.Z * 256), (int)MathF.Round(Math.Clamp(altitude, 0, 99) * 40),
            (int)MathF.Round(Math.Clamp(clouds, 0, 1) * 20), width, height);
        if (admittedKey == key) return ready;
        admittedKey = key;
        var token = cancellation.Token;
        // The worker owns all mutable integration state and never reads game or GPU objects.
        // Finish admitted work to avoid starvation; the next render update captures the newest inputs.
        pending = Task.Run(() =>
        {
            var lookup = new AtmosphereLookup();
            lookup.Update(sun, altitude, clouds, complete: true, width: width, height: height,
                cancellationToken: token);
            return lookup.Current!;
        }, token);
        // Observe faults even if world teardown abandons this task before the render thread polls it.
        _ = pending.ContinueWith(task => { _ = task.Exception; }, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return ready;
    }
    #endregion

    #region Lifetime
    /// <summary>Cancels between SIMD batches without waiting on the render thread or publishing old-world results.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        cancellation.Cancel();
        cancellation.Dispose();
        pending = null;
    }
    #endregion
}
