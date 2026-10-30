using System;

namespace VanillaGraphicsExpanded.Rendering.Integration;

/// <summary>Decides whether the current context meets the renderer's desktop OpenGL requirement.</summary>
internal static class RenderingContextRequirement
{
    #region Public API
    /// <summary>Accepts desktop contexts at or above the required version.</summary>
    internal static bool IsSupported(string? actualVersion) =>
        actualVersion is not null && !actualVersion.Contains("OpenGL ES", StringComparison.OrdinalIgnoreCase)
        && Parse(actualVersion) is { } version && version >= new Version(4, 5);

    /// <summary>Raises a missing or lower launch setting without downgrading a higher requested version.</summary>
    internal static string UpgradeSetting(string? configuredVersion) =>
        Parse(configuredVersion) is { } version && version >= new Version(4, 5) ? configuredVersion! : "4.5";
    #endregion

    #region Private
    /// <summary>Parses the version token, allowing the driver description following a native version string.</summary>
    private static Version? Parse(string? value) =>
        Version.TryParse(value?.Trim().Split(' ', 2)[0], out Version? version) ? version : null;
    #endregion
}
