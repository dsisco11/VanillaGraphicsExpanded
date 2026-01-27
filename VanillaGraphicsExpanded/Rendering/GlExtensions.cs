using System;
using System.Collections.Generic;
using System.Threading;

using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// Helper for querying OpenGL extension support using the current context.
/// Results are cached on first successful query and reused for subsequent calls.
/// </summary>
internal static class GlExtensions
{
    private static readonly object Sync = new();
    private static HashSet<string>? cached;
    private static string? cachedContextKey;

    /// <summary>
    /// Attempts to load and cache the current context's extension list.
    /// Safe to call multiple times; returns <c>false</c> if no current context is available.
    /// </summary>
    public static bool TryLoadExtensions()
    {
        if (!TryGetContextKey(out string contextKey))
        {
            return false;
        }

        return EnsureLoaded(contextKey) is not null;
    }

    /// <summary>
    /// Returns <c>true</c> when the current OpenGL context reports support for the given extension string.
    /// </summary>
    public static bool Supports(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return false;
        }

        if (!TryGetContextKey(out string contextKey))
        {
            return false;
        }

        HashSet<string>? snapshot = Volatile.Read(ref cached);
        string? keySnapshot = Volatile.Read(ref cachedContextKey);

        if (snapshot is null || !string.Equals(keySnapshot, contextKey, StringComparison.Ordinal))
        {
            snapshot = EnsureLoaded(contextKey);
        }

        return snapshot is not null && snapshot.Contains(extension);
    }

    private static HashSet<string>? EnsureLoaded(string contextKey)
    {
        HashSet<string>? snapshot = Volatile.Read(ref cached);
        if (snapshot is not null)
        {
            string? keySnapshot = Volatile.Read(ref cachedContextKey);
            if (string.Equals(keySnapshot, contextKey, StringComparison.Ordinal))
            {
                return snapshot;
            }
        }

        lock (Sync)
        {
            snapshot = cached;
            if (snapshot is not null)
            {
                if (string.Equals(cachedContextKey, contextKey, StringComparison.Ordinal))
                {
                    return snapshot;
                }
            }

            HashSet<string>? loaded = TryLoadExtensionsCore();
            if (loaded is not null)
            {
                Volatile.Write(ref cached, loaded);
                Volatile.Write(ref cachedContextKey, contextKey);
            }

            return loaded;
        }
    }

    internal static bool TryGetContextKey(out string key)
    {
        try
        {
            string version = GL.GetString(StringName.Version) ?? string.Empty;
            string vendor = GL.GetString(StringName.Vendor) ?? string.Empty;
            string renderer = GL.GetString(StringName.Renderer) ?? string.Empty;

            if (version.Length == 0 && vendor.Length == 0 && renderer.Length == 0)
            {
                key = string.Empty;
                return false;
            }

            key = string.Concat(version, "|", vendor, "|", renderer);
            return true;
        }
        catch
        {
            key = string.Empty;
            return false;
        }
    }

    private static HashSet<string>? TryLoadExtensionsCore()
    {
        try
        {
            var set = new HashSet<string>(StringComparer.Ordinal);

            // Core profile path (GL 3+): indexed extension strings.
            GL.GetInteger(GetPName.NumExtensions, out int count);
            if (count > 0)
            {
                for (int i = 0; i < count; i++)
                {
                    string ext = GL.GetString(StringNameIndexed.Extensions, i);
                    if (!string.IsNullOrEmpty(ext))
                    {
                        set.Add(ext);
                    }
                }

                return set;
            }

            // Compatibility fallback: space-separated extension string.
            string extString = GL.GetString(StringName.Extensions) ?? string.Empty;
            if (extString.Length == 0)
            {
                return set;
            }

            foreach (string ext in extString.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                set.Add(ext);
            }

            return set;
        }
        catch
        {
            // Best-effort: the current thread may not have a current context.
            return null;
        }
    }
}
