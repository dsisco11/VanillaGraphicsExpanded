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

    /// <summary>
    /// Attempts to load and cache the current context's extension list.
    /// Safe to call multiple times; returns <c>false</c> if no current context is available.
    /// </summary>
    public static bool TryLoadExtensions()
    {
        return EnsureLoaded() is not null;
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

        HashSet<string>? snapshot = Volatile.Read(ref cached);
        if (snapshot is null)
        {
            snapshot = EnsureLoaded();
        }

        return snapshot is not null && snapshot.Contains(extension);
    }

    private static HashSet<string>? EnsureLoaded()
    {
        HashSet<string>? snapshot = Volatile.Read(ref cached);
        if (snapshot is not null)
        {
            return snapshot;
        }

        lock (Sync)
        {
            snapshot = cached;
            if (snapshot is not null)
            {
                return snapshot;
            }

            HashSet<string>? loaded = TryLoadExtensionsCore();
            if (loaded is not null)
            {
                Volatile.Write(ref cached, loaded);
            }

            return loaded;
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
