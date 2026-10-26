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

        /// <summary>Executes Arb.DeleteQueries and reconciles each successful name deletion.</summary>
        public static void DeleteQueries(int count, ref int ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.Arb.DeleteQueries(count, ref ids);
            if (count <= 0) return;
            foreach (var value in MemoryMarshal.CreateReadOnlySpan(ref ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.Query, unchecked((int)value));
        }

        /// <summary>Executes Arb.DeleteQueries and reconciles each successful name deletion.</summary>
        public static void DeleteQueries(int count, int[] ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.Arb.DeleteQueries(count, ids);
            if (count <= 0) return;
            foreach (var value in ids.AsSpan(0, Math.Min(count, ids.Length)))
                cache.CompleteResourceDeletion(GpuResourceKind.Query, unchecked((int)value));
        }

        /// <summary>Executes Arb.DeleteQueries and reconciles each successful name deletion.</summary>
        public static void DeleteQueries(int count, int* ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.Arb.DeleteQueries(count, ids);
            if (count <= 0) return;
            foreach (var value in new ReadOnlySpan<int>(ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.Query, unchecked((int)value));
        }

        /// <summary>Executes Arb.DeleteQueries and reconciles each successful name deletion.</summary>
        public static void DeleteQueries(int count, ref uint ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.Arb.DeleteQueries(count, ref ids);
            if (count <= 0) return;
            foreach (var value in MemoryMarshal.CreateReadOnlySpan(ref ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.Query, unchecked((int)value));
        }

        /// <summary>Executes Arb.DeleteQueries and reconciles each successful name deletion.</summary>
        public static void DeleteQueries(int count, uint[] ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.Arb.DeleteQueries(count, ids);
            if (count <= 0) return;
            foreach (var value in ids.AsSpan(0, Math.Min(count, ids.Length)))
                cache.CompleteResourceDeletion(GpuResourceKind.Query, unchecked((int)value));
        }

        /// <summary>Executes Arb.DeleteQueries and reconciles each successful name deletion.</summary>
        public static void DeleteQueries(int count, uint* ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.Arb.DeleteQueries(count, ids);
            if (count <= 0) return;
            foreach (var value in new ReadOnlySpan<uint>(ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.Query, unchecked((int)value));
        }

        /// <summary>Executes Arb.DeleteQuery and reconciles each successful name deletion.</summary>
        public static void DeleteQuery(int id)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.Arb.DeleteQuery(id);
            cache.CompleteResourceDeletion(GpuResourceKind.Query, unchecked((int)id));
        }

        /// <summary>Executes Arb.DeleteQuery and reconciles each successful name deletion.</summary>
        public static void DeleteQuery(uint id)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.Arb.DeleteQuery(id);
            cache.CompleteResourceDeletion(GpuResourceKind.Query, unchecked((int)id));
        }

        #endregion
    }
}
