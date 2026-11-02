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
    private (int, int, int, int, int, int, int, int, int)? admittedKey;
    private bool disposed;
    private readonly AtmosphereLookup lookup = new();
    private readonly Func<AtmosphereBuildRequest, CancellationToken, Task<AtmosphereLighting>> produce;

    #region Construction
    /// <summary>Uses the real background integrator unless an explicit producer controls completion.</summary>
    internal AtmosphereComputation(Func<AtmosphereBuildRequest, CancellationToken, Task<AtmosphereLighting>>? producer = null)
    {
        produce = producer ?? BuildAsync;
    }
    #endregion

    /// <summary>Exposes the admitted completion dependency without transferring publication ownership.</summary>
    internal Task<AtmosphereLighting>? Pending => pending;

    #region Scheduling
    /// <summary>Consumes finished work without blocking, then admits the latest inputs if they changed.</summary>
    internal AtmosphereLighting? Update(Vector3 sun, float altitude, float clouds, int width, int height, int quality = 0, float groundAlbedo = .1f)
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
        width = Math.Clamp(width, 16, AtmosphereLookup.DefaultWidth * 4);
        height = Math.Clamp(height, 8, AtmosphereLookup.DefaultHeight * 4);
        quality = Math.Clamp(quality, 0, 3);
        var key = ((int)MathF.Round(sun.X * AtmosphereSolarDisk.DirectionResolution), (int)MathF.Round(sun.Y * AtmosphereSolarDisk.DirectionResolution),
            (int)MathF.Round(sun.Z * AtmosphereSolarDisk.DirectionResolution), (int)MathF.Round(Math.Clamp(altitude, 0, 99) * 40),
            (int)MathF.Round(Math.Clamp(clouds, 0, 1) * 20), width, height, quality, AtmosphereSeasonModel.AlbedoBucket(groundAlbedo));
        if (admittedKey == key) return ready;
        admittedKey = key;
        var token = cancellation.Token;
        // Freeze the admitted inputs. Completion is consumed only by a later Update, even if a
        // producer completes synchronously; changing requests cannot starve an admitted build.
        try
        {
            pending = produce(new(sun, altitude, clouds, width, height, quality, groundAlbedo), token)
                ?? throw new InvalidOperationException("The atmosphere producer returned no completion dependency.");
        }
        catch { admittedKey = null; throw; }
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

    #region Private
    /// <summary>Keeps numerical state on its background worker and returns only its completed immutable publication.</summary>
    private Task<AtmosphereLighting> BuildAsync(AtmosphereBuildRequest request, CancellationToken token)
    {
        return Task.Run(() =>
        {
            // The worker owns all mutable integration state and never reads game or GPU objects.
            lookup.Update(request.Sun, request.Altitude, request.Clouds, complete: true,
                width: request.Width, height: request.Height, cancellationToken: token,
                quality: request.Quality, groundAlbedo: request.GroundAlbedo);
            return lookup.Current!;
        }, token);
    }
    #endregion
}
