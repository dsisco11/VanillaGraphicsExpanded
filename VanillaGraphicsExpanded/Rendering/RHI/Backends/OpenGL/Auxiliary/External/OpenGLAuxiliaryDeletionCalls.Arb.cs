using System;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
/// <summary>Preserves exact external auxiliary deletion arguments and per-object outcomes.</summary>
internal static partial class OpenGLAuxiliaryDeletionCalls
{
    /// <summary>Contains exact Arb deletion overloads.</summary>
    internal static unsafe class Arb
    {
        #region Public API
        /// <summary>Executes Arb.DeleteQueries and reconciles each successful name deletion.</summary>
        public static void DeleteQueries(int count, ref int ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.Arb.DeleteQueries(count, ref ids);
            if (count <= 0) return;
            foreach (var value in MemoryMarshal.CreateReadOnlySpan(ref ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.Query, unchecked((int)value), WasDeletedExternal(GpuResourceKind.Query, unchecked((int)value)));
        }

        /// <summary>Executes Arb.DeleteQueries and reconciles each successful name deletion.</summary>
        public static void DeleteQueries(int count, int[] ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.Arb.DeleteQueries(count, ids);
            if (count <= 0) return;
            foreach (var value in ids.AsSpan(0, Math.Min(count, ids.Length)))
                cache.CompleteResourceDeletion(GpuResourceKind.Query, unchecked((int)value), WasDeletedExternal(GpuResourceKind.Query, unchecked((int)value)));
        }

        /// <summary>Executes Arb.DeleteQueries and reconciles each successful name deletion.</summary>
        public static void DeleteQueries(int count, int* ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.Arb.DeleteQueries(count, ids);
            if (count <= 0) return;
            foreach (var value in new ReadOnlySpan<int>(ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.Query, unchecked((int)value), WasDeletedExternal(GpuResourceKind.Query, unchecked((int)value)));
        }

        /// <summary>Executes Arb.DeleteQueries and reconciles each successful name deletion.</summary>
        public static void DeleteQueries(int count, ref uint ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.Arb.DeleteQueries(count, ref ids);
            if (count <= 0) return;
            foreach (var value in MemoryMarshal.CreateReadOnlySpan(ref ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.Query, unchecked((int)value), WasDeletedExternal(GpuResourceKind.Query, unchecked((int)value)));
        }

        /// <summary>Executes Arb.DeleteQueries and reconciles each successful name deletion.</summary>
        public static void DeleteQueries(int count, uint[] ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.Arb.DeleteQueries(count, ids);
            if (count <= 0) return;
            foreach (var value in ids.AsSpan(0, Math.Min(count, ids.Length)))
                cache.CompleteResourceDeletion(GpuResourceKind.Query, unchecked((int)value), WasDeletedExternal(GpuResourceKind.Query, unchecked((int)value)));
        }

        /// <summary>Executes Arb.DeleteQueries and reconciles each successful name deletion.</summary>
        public static void DeleteQueries(int count, uint* ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.Arb.DeleteQueries(count, ids);
            if (count <= 0) return;
            foreach (var value in new ReadOnlySpan<uint>(ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.Query, unchecked((int)value), WasDeletedExternal(GpuResourceKind.Query, unchecked((int)value)));
        }

        /// <summary>Executes Arb.DeleteQuery and reconciles each successful name deletion.</summary>
        public static void DeleteQuery(int id)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.Arb.DeleteQuery(id);
            cache.CompleteResourceDeletion(GpuResourceKind.Query, unchecked((int)id), WasDeletedExternal(GpuResourceKind.Query, unchecked((int)id)));
        }

        /// <summary>Executes Arb.DeleteQuery and reconciles each successful name deletion.</summary>
        public static void DeleteQuery(uint id)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.Arb.DeleteQuery(id);
            cache.CompleteResourceDeletion(GpuResourceKind.Query, unchecked((int)id), WasDeletedExternal(GpuResourceKind.Query, unchecked((int)id)));
        }
        #endregion
    }
}
