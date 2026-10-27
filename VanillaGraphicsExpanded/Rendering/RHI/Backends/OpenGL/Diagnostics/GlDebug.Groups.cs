using System;
using OpenTK.Graphics.OpenGL4;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Balances optional native debug groups.</summary>
internal static partial class GlDebug
{
#if DEBUG
    private const bool DebugGroupsEnabled = true;
#else
    private const bool DebugGroupsEnabled = false;
#endif

    #region Public API
    /// <summary>Starts an optional debug group for the supplied diagnostic name.</summary>
    public static GroupScope Group(string name)
    {
        return new GroupScope(name);
    }

    /// <summary>Balances an optional native debug group even while an operation unwinds.</summary>
    public readonly struct GroupScope : IDisposable
    {
        private readonly bool active;

        /// <summary>Starts a supported group in Debug builds only.</summary>
        public GroupScope(string name)
        {
#if DEBUG
            if (!DebugGroupsEnabled || string.IsNullOrWhiteSpace(name) || !SupportsKhrDebug())
            {
                active = false;
                return;
            }

            try
            {
                GL.PushDebugGroup(DebugSourceExternal.DebugSourceApplication, 0, -1, name);
                active = true;
            }
            catch
            {
                active = false;
            }
#else
            active = false;
#endif
        }

        /// <summary>Closes only the group successfully started by this scope.</summary>
        public void Dispose()
        {
#if DEBUG
            if (!active)
            {
                return;
            }

            try
            {
                GL.PopDebugGroup();
            }
            catch
            {
            }
#endif
        }
    }
    #endregion

}
