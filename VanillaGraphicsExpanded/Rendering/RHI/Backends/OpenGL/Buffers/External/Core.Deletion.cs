using System;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Preserves exact external buffer-deletion overloads and per-name outcomes.</summary>
internal static partial class OpenGLBufferDeletionCalls
{
    /// <summary>Contains compiler-checked Core deletion adapters.</summary>
    internal static unsafe class Core
    {
        #region Public API
        /// <summary>Executes Core.DeleteBuffer and reconciles each successful name deletion.</summary>
        public static void DeleteBuffer(int id)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteBuffer(id);
            cache.CompleteResourceDeletion(GpuResourceKind.Buffer, unchecked((int)id));
        }

        /// <summary>Executes Core.DeleteBuffer and reconciles each successful name deletion.</summary>
        public static void DeleteBuffer(uint id)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteBuffer(id);
            cache.CompleteResourceDeletion(GpuResourceKind.Buffer, unchecked((int)id));
        }

        /// <summary>Executes Core.DeleteBuffers and reconciles each successful name deletion.</summary>
        public static void DeleteBuffers(int count, ref int ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteBuffers(count, ref ids);
            if (count <= 0) return;
            foreach (var value in MemoryMarshal.CreateReadOnlySpan(ref ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.Buffer, unchecked((int)value));
        }

        /// <summary>Executes Core.DeleteBuffers and reconciles each successful name deletion.</summary>
        public static void DeleteBuffers(int count, int[] ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteBuffers(count, ids);
            if (count <= 0) return;
            foreach (var value in ids.AsSpan(0, Math.Min(count, ids.Length)))
                cache.CompleteResourceDeletion(GpuResourceKind.Buffer, unchecked((int)value));
        }

        /// <summary>Executes Core.DeleteBuffers and reconciles each successful name deletion.</summary>
        public static void DeleteBuffers(int count, int* ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteBuffers(count, ids);
            if (count <= 0) return;
            foreach (var value in new ReadOnlySpan<int>(ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.Buffer, unchecked((int)value));
        }

        /// <summary>Executes Core.DeleteBuffers and reconciles each successful name deletion.</summary>
        public static void DeleteBuffers(int count, ref uint ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteBuffers(count, ref ids);
            if (count <= 0) return;
            foreach (var value in MemoryMarshal.CreateReadOnlySpan(ref ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.Buffer, unchecked((int)value));
        }

        /// <summary>Executes Core.DeleteBuffers and reconciles each successful name deletion.</summary>
        public static void DeleteBuffers(int count, uint[] ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteBuffers(count, ids);
            if (count <= 0) return;
            foreach (var value in ids.AsSpan(0, Math.Min(count, ids.Length)))
                cache.CompleteResourceDeletion(GpuResourceKind.Buffer, unchecked((int)value));
        }

        /// <summary>Executes Core.DeleteBuffers and reconciles each successful name deletion.</summary>
        public static void DeleteBuffers(int count, uint* ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteBuffers(count, ids);
            if (count <= 0) return;
            foreach (var value in new ReadOnlySpan<uint>(ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.Buffer, unchecked((int)value));
        }
        #endregion
    }
}
