using System;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Preserves exact Arb deletion signatures while forwarding proven native outcomes to the state cache.</summary>
internal static partial class EngineStateDeletionCalls
{
    /// <summary>Contains the Arb native deletion adapters.</summary>
    internal static unsafe class Arb
    {
        #region Public API


        /// <summary>Executes Arb.DeleteObject and reconciles each successful name deletion.</summary>
        public static void DeleteObject(int id)
        {
            var cache = EngineStateCalls.CurrentCache;
            var kind = GL.IsShader(unchecked((int)id)) ? GpuResourceKind.Shader : GpuResourceKind.Program;
            cache.PrepareResourceDeletion(kind, unchecked((int)id));
            GL.Arb.DeleteObject(id);
            cache.CompleteResourceDeletion(kind, unchecked((int)id));
        }

        /// <summary>Executes Arb.DeleteObject and reconciles each successful name deletion.</summary>
        public static void DeleteObject(uint id)
        {
            var cache = EngineStateCalls.CurrentCache;
            var kind = GL.IsShader(unchecked((int)id)) ? GpuResourceKind.Shader : GpuResourceKind.Program;
            cache.PrepareResourceDeletion(kind, unchecked((int)id));
            GL.Arb.DeleteObject(id);
            cache.CompleteResourceDeletion(kind, unchecked((int)id));
        }

















        #endregion
    }
}
