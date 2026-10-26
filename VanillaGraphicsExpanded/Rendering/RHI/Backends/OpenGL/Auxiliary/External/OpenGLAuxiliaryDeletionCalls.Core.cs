using System;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
/// <summary>Preserves exact external auxiliary deletion arguments and per-object outcomes.</summary>
internal static partial class OpenGLAuxiliaryDeletionCalls
{
    /// <summary>Contains exact Core deletion overloads.</summary>
    internal static unsafe class Core
    {
        #region Public API
        /// <summary>Executes Core.DeleteQueries and reconciles each successful name deletion.</summary>
        public static void DeleteQueries(int count, ref int ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteQueries(count, ref ids);
            if (count <= 0) return;
            foreach (var value in MemoryMarshal.CreateReadOnlySpan(ref ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.Query, unchecked((int)value), WasDeletedExternal(GpuResourceKind.Query, unchecked((int)value)));
        }

        /// <summary>Executes Core.DeleteQueries and reconciles each successful name deletion.</summary>
        public static void DeleteQueries(int count, int[] ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteQueries(count, ids);
            if (count <= 0) return;
            foreach (var value in ids.AsSpan(0, Math.Min(count, ids.Length)))
                cache.CompleteResourceDeletion(GpuResourceKind.Query, unchecked((int)value), WasDeletedExternal(GpuResourceKind.Query, unchecked((int)value)));
        }

        /// <summary>Executes Core.DeleteQueries and reconciles each successful name deletion.</summary>
        public static void DeleteQueries(int count, int* ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteQueries(count, ids);
            if (count <= 0) return;
            foreach (var value in new ReadOnlySpan<int>(ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.Query, unchecked((int)value), WasDeletedExternal(GpuResourceKind.Query, unchecked((int)value)));
        }

        /// <summary>Executes Core.DeleteQueries and reconciles each successful name deletion.</summary>
        public static void DeleteQueries(int count, ref uint ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteQueries(count, ref ids);
            if (count <= 0) return;
            foreach (var value in MemoryMarshal.CreateReadOnlySpan(ref ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.Query, unchecked((int)value), WasDeletedExternal(GpuResourceKind.Query, unchecked((int)value)));
        }

        /// <summary>Executes Core.DeleteQueries and reconciles each successful name deletion.</summary>
        public static void DeleteQueries(int count, uint[] ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteQueries(count, ids);
            if (count <= 0) return;
            foreach (var value in ids.AsSpan(0, Math.Min(count, ids.Length)))
                cache.CompleteResourceDeletion(GpuResourceKind.Query, unchecked((int)value), WasDeletedExternal(GpuResourceKind.Query, unchecked((int)value)));
        }

        /// <summary>Executes Core.DeleteQueries and reconciles each successful name deletion.</summary>
        public static void DeleteQueries(int count, uint* ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteQueries(count, ids);
            if (count <= 0) return;
            foreach (var value in new ReadOnlySpan<uint>(ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.Query, unchecked((int)value), WasDeletedExternal(GpuResourceKind.Query, unchecked((int)value)));
        }

        /// <summary>Executes Core.DeleteQuery and reconciles each successful name deletion.</summary>
        public static void DeleteQuery(int id)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteQuery(id);
            cache.CompleteResourceDeletion(GpuResourceKind.Query, unchecked((int)id), WasDeletedExternal(GpuResourceKind.Query, unchecked((int)id)));
        }

        /// <summary>Executes Core.DeleteQuery and reconciles each successful name deletion.</summary>
        public static void DeleteQuery(uint id)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteQuery(id);
            cache.CompleteResourceDeletion(GpuResourceKind.Query, unchecked((int)id), WasDeletedExternal(GpuResourceKind.Query, unchecked((int)id)));
        }

        /// <summary>Executes Core.DeleteSync and reconciles each successful name deletion.</summary>
        public static void DeleteSync(IntPtr id)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteSync(id);
            cache.CompleteResourceDeletion(GpuResourceKind.Sync, (nint)id, WasDeletedExternal(GpuResourceKind.Sync, (nint)id));
        }

        /// <summary>Executes Core.DeleteTransformFeedback and reconciles each successful name deletion.</summary>
        public static void DeleteTransformFeedback(int id)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteTransformFeedback(id);
            cache.CompleteResourceDeletion(GpuResourceKind.TransformFeedback, unchecked((int)id), WasDeletedExternal(GpuResourceKind.TransformFeedback, unchecked((int)id)));
        }

        /// <summary>Executes Core.DeleteTransformFeedback and reconciles each successful name deletion.</summary>
        public static void DeleteTransformFeedback(uint id)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteTransformFeedback(id);
            cache.CompleteResourceDeletion(GpuResourceKind.TransformFeedback, unchecked((int)id), WasDeletedExternal(GpuResourceKind.TransformFeedback, unchecked((int)id)));
        }

        /// <summary>Executes Core.DeleteTransformFeedbacks and reconciles each successful name deletion.</summary>
        public static void DeleteTransformFeedbacks(int count, ref int ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteTransformFeedbacks(count, ref ids);
            if (count <= 0) return;
            foreach (var value in MemoryMarshal.CreateReadOnlySpan(ref ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.TransformFeedback, unchecked((int)value), WasDeletedExternal(GpuResourceKind.TransformFeedback, unchecked((int)value)));
        }

        /// <summary>Executes Core.DeleteTransformFeedbacks and reconciles each successful name deletion.</summary>
        public static void DeleteTransformFeedbacks(int count, int[] ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteTransformFeedbacks(count, ids);
            if (count <= 0) return;
            foreach (var value in ids.AsSpan(0, Math.Min(count, ids.Length)))
                cache.CompleteResourceDeletion(GpuResourceKind.TransformFeedback, unchecked((int)value), WasDeletedExternal(GpuResourceKind.TransformFeedback, unchecked((int)value)));
        }

        /// <summary>Executes Core.DeleteTransformFeedbacks and reconciles each successful name deletion.</summary>
        public static void DeleteTransformFeedbacks(int count, int* ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteTransformFeedbacks(count, ids);
            if (count <= 0) return;
            foreach (var value in new ReadOnlySpan<int>(ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.TransformFeedback, unchecked((int)value), WasDeletedExternal(GpuResourceKind.TransformFeedback, unchecked((int)value)));
        }

        /// <summary>Executes Core.DeleteTransformFeedbacks and reconciles each successful name deletion.</summary>
        public static void DeleteTransformFeedbacks(int count, ref uint ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteTransformFeedbacks(count, ref ids);
            if (count <= 0) return;
            foreach (var value in MemoryMarshal.CreateReadOnlySpan(ref ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.TransformFeedback, unchecked((int)value), WasDeletedExternal(GpuResourceKind.TransformFeedback, unchecked((int)value)));
        }

        /// <summary>Executes Core.DeleteTransformFeedbacks and reconciles each successful name deletion.</summary>
        public static void DeleteTransformFeedbacks(int count, uint[] ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteTransformFeedbacks(count, ids);
            if (count <= 0) return;
            foreach (var value in ids.AsSpan(0, Math.Min(count, ids.Length)))
                cache.CompleteResourceDeletion(GpuResourceKind.TransformFeedback, unchecked((int)value), WasDeletedExternal(GpuResourceKind.TransformFeedback, unchecked((int)value)));
        }

        /// <summary>Executes Core.DeleteTransformFeedbacks and reconciles each successful name deletion.</summary>
        public static void DeleteTransformFeedbacks(int count, uint* ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteTransformFeedbacks(count, ids);
            if (count <= 0) return;
            foreach (var value in new ReadOnlySpan<uint>(ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.TransformFeedback, unchecked((int)value), WasDeletedExternal(GpuResourceKind.TransformFeedback, unchecked((int)value)));
        }
        #endregion
    }
}
