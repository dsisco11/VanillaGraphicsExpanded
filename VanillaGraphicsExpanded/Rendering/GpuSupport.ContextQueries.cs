using System;
using System.Globalization;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Queries context characteristics for capability initialization.</summary>
public static partial class GpuSupport
{
    #region Private
    /// <summary>Reads context identity and version values into an unpublished immutable value.</summary>
    private static GraphicsCapabilities CaptureContextStrings(GraphicsCapabilities capabilities)
    {
        string versionString = string.Empty;
        string vendorString = string.Empty;
        string rendererString = string.Empty;
        string shadingLanguageVersionString = string.Empty;
        Version? apiVersion = default;
        Version? shadingLanguageVersion = default;
        bool isOpenGles = default;
        bool? isSharedContext = default;

        GlDebug.ClearErrors();
        versionString = SafeGetString(StringName.Version);
        vendorString = SafeGetString(StringName.Vendor);
        rendererString = SafeGetString(StringName.Renderer);
        shadingLanguageVersionString = SafeGetString(StringName.ShadingLanguageVersion);

        isOpenGles = versionString.Contains("OpenGL ES", StringComparison.OrdinalIgnoreCase)
            || rendererString.Contains("OpenGL ES", StringComparison.OrdinalIgnoreCase);

        apiVersion = TryParseLeadingVersion(versionString);
        shadingLanguageVersion = TryParseLeadingVersion(shadingLanguageVersionString);

        isSharedContext = TryGetSharedContextFlag();
        GlDebug.ThrowIfErrors();

        return capabilities with
        {
            VersionString = versionString,
            VendorString = vendorString,
            RendererString = rendererString,
            ShadingLanguageVersionString = shadingLanguageVersionString,
            ApiVersion = apiVersion,
            ShadingLanguageVersion = shadingLanguageVersion,
            IsOpenGles = isOpenGles,
            IsSharedContext = isSharedContext
        };
    }

    /// <summary>Reads a context string with an empty fallback when unavailable.</summary>
    private static string SafeGetString(StringName name)
    {
        try
        {
            return GL.GetString(name) ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>Parses the leading numeric OpenGL or shading-language version.</summary>
    private static Version? TryParseLeadingVersion(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        // Typical GL strings: "4.6.0 NVIDIA 552.25", "3.3.0", "OpenGL ES 3.2 ..."
        ReadOnlySpan<char> span = text.AsSpan().TrimStart();

        if (span.StartsWith("OpenGL ES".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            int idx = span.IndexOf(' ');
            if (idx >= 0)
            {
                span = span.Slice(idx).TrimStart();
                idx = span.IndexOf(' ');
                if (idx >= 0)
                {
                    span = span.Slice(idx).TrimStart();
                }
            }
        }

        int end = 0;
        while (end < span.Length)
        {
            char c = span[end];
            if ((uint)(c - '0') <= 9u || c == '.')
            {
                end++;
                continue;
            }

            break;
        }

        if (end == 0)
        {
            return null;
        }

        string versionText = span.Slice(0, end).ToString();
        string[] parts = versionText.Split('.', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 2)
        {
            return null;
        }

        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int major))
        {
            return null;
        }

        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int minor))
        {
            return null;
        }

        int build = 0;
        if (parts.Length >= 3)
        {
            _ = int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out build);
        }

        return new Version(major, minor, build);
    }

    /// <summary>Returns unknown because the engine context API exposes no sharing query.</summary>
    private static bool? TryGetSharedContextFlag()
    {
        // OpenGL has no standard "shared context" query.
        // Vintage Story ships OpenTK 4 split assemblies (OpenTK.Graphics + OpenTK.Windowing.*).
        // The public OpenTK context interfaces available to mods do not expose a shared-context flag.
        // Keep this as "unknown" rather than guessing.
        return null;
    }

    /// <summary>Reads supported context flags without publishing partial capabilities.</summary>
    private static GraphicsCapabilities CaptureContextFlagsAndProfile(GraphicsCapabilities capabilities)
    {
        int contextFlags = default;
        int contextProfileMaskValue = default;
        bool isDebugContext = default;
        bool isForwardCompatibleContext = default;
        bool isRobustAccessContext = default;
        bool isCompatibilityProfile = default;

        GlDebug.ClearErrors();
        // Many context queries were introduced in GL 3.x; avoid emitting GL errors on older contexts.
        if (!IsAtLeast(capabilities.ApiVersion, 3, 0))
        {
            contextFlags = 0;
            contextProfileMaskValue = 0;
            isDebugContext = false;
            isForwardCompatibleContext = false;
            isRobustAccessContext = false;
            isCompatibilityProfile = false;
            return capabilities with
            {
                ContextFlags = contextFlags,
                ContextProfileMaskValue = contextProfileMaskValue,
                IsDebugContext = isDebugContext,
                IsForwardCompatibleContext = isForwardCompatibleContext,
                IsRobustAccessContext = isRobustAccessContext,
                IsCompatibilityProfile = isCompatibilityProfile
            };
        }

        contextFlags = SafeGetInt(GetPName.ContextFlags);

        // GL_CONTEXT_PROFILE_MASK is GL 3.2+.
        contextProfileMaskValue = IsAtLeast(capabilities.ApiVersion, 3, 2)
            ? SafeGetInt(GetPName.ContextProfileMask)
            : 0;

        isDebugContext = (((ContextFlagMask)contextFlags) & ContextFlagMask.ContextFlagDebugBit) != 0;
        isForwardCompatibleContext = (((ContextFlagMask)contextFlags) & ContextFlagMask.ContextFlagForwardCompatibleBit) != 0;

        // Some drivers report robust access via ARB enum; keep this best-effort.
        isRobustAccessContext = (((ContextFlagMask)contextFlags) & (ContextFlagMask)0x00000004) != 0;

        if (contextProfileMaskValue != 0)
        {
            isCompatibilityProfile = (((ContextProfileMask)contextProfileMaskValue) & ContextProfileMask.ContextCompatibilityProfileBit) != 0;
        }
        else
        {
            isCompatibilityProfile = false;
        }
        GlDebug.ThrowIfErrors();

        return capabilities with
        {
            ContextFlags = contextFlags,
            ContextProfileMaskValue = contextProfileMaskValue,
            IsDebugContext = isDebugContext,
            IsForwardCompatibleContext = isForwardCompatibleContext,
            IsRobustAccessContext = isRobustAccessContext,
            IsCompatibilityProfile = isCompatibilityProfile
        };
    }
    #endregion
}
