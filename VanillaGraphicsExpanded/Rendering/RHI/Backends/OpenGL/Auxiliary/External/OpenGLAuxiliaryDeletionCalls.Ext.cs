using System;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
/// <summary>Preserves exact external auxiliary deletion arguments and per-object outcomes.</summary>
internal static partial class OpenGLAuxiliaryDeletionCalls
{
    /// <summary>Contains exact Ext deletion overloads.</summary>
    internal static unsafe class Ext
    {
        #region Public API
        /// <summary>Executes Ext.DeleteMemoryObject and reconciles each successful name deletion.</summary>
        public static void DeleteMemoryObject(int id)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.Ext.DeleteMemoryObject(id);
            cache.CompleteResourceDeletion(GpuResourceKind.MemoryObject, unchecked((int)id), WasDeletedExternal(GpuResourceKind.MemoryObject, unchecked((int)id)));
        }

        /// <summary>Executes Ext.DeleteMemoryObject and reconciles each successful name deletion.</summary>
        public static void DeleteMemoryObject(uint id)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.Ext.DeleteMemoryObject(id);
            cache.CompleteResourceDeletion(GpuResourceKind.MemoryObject, unchecked((int)id), WasDeletedExternal(GpuResourceKind.MemoryObject, unchecked((int)id)));
        }

        /// <summary>Executes Ext.DeleteMemoryObjects and reconciles each successful name deletion.</summary>
        public static void DeleteMemoryObjects(int count, ref int ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.Ext.DeleteMemoryObjects(count, ref ids);
            if (count <= 0) return;
            foreach (var value in MemoryMarshal.CreateReadOnlySpan(ref ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.MemoryObject, unchecked((int)value), WasDeletedExternal(GpuResourceKind.MemoryObject, unchecked((int)value)));
        }

        /// <summary>Executes Ext.DeleteMemoryObjects and reconciles each successful name deletion.</summary>
        public static void DeleteMemoryObjects(int count, int[] ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.Ext.DeleteMemoryObjects(count, ids);
            if (count <= 0) return;
            foreach (var value in ids.AsSpan(0, Math.Min(count, ids.Length)))
                cache.CompleteResourceDeletion(GpuResourceKind.MemoryObject, unchecked((int)value), WasDeletedExternal(GpuResourceKind.MemoryObject, unchecked((int)value)));
        }

        /// <summary>Executes Ext.DeleteMemoryObjects and reconciles each successful name deletion.</summary>
        public static void DeleteMemoryObjects(int count, int* ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.Ext.DeleteMemoryObjects(count, ids);
            if (count <= 0) return;
            foreach (var value in new ReadOnlySpan<int>(ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.MemoryObject, unchecked((int)value), WasDeletedExternal(GpuResourceKind.MemoryObject, unchecked((int)value)));
        }

        /// <summary>Executes Ext.DeleteMemoryObjects and reconciles each successful name deletion.</summary>
        public static void DeleteMemoryObjects(int count, ref uint ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.Ext.DeleteMemoryObjects(count, ref ids);
            if (count <= 0) return;
            foreach (var value in MemoryMarshal.CreateReadOnlySpan(ref ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.MemoryObject, unchecked((int)value), WasDeletedExternal(GpuResourceKind.MemoryObject, unchecked((int)value)));
        }

        /// <summary>Executes Ext.DeleteMemoryObjects and reconciles each successful name deletion.</summary>
        public static void DeleteMemoryObjects(int count, uint[] ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.Ext.DeleteMemoryObjects(count, ids);
            if (count <= 0) return;
            foreach (var value in ids.AsSpan(0, Math.Min(count, ids.Length)))
                cache.CompleteResourceDeletion(GpuResourceKind.MemoryObject, unchecked((int)value), WasDeletedExternal(GpuResourceKind.MemoryObject, unchecked((int)value)));
        }

        /// <summary>Executes Ext.DeleteMemoryObjects and reconciles each successful name deletion.</summary>
        public static void DeleteMemoryObjects(int count, uint* ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.Ext.DeleteMemoryObjects(count, ids);
            if (count <= 0) return;
            foreach (var value in new ReadOnlySpan<uint>(ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.MemoryObject, unchecked((int)value), WasDeletedExternal(GpuResourceKind.MemoryObject, unchecked((int)value)));
        }

        /// <summary>Executes Ext.DeleteSemaphore and reconciles each successful name deletion.</summary>
        public static void DeleteSemaphore(int id)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.Ext.DeleteSemaphore(id);
            cache.CompleteResourceDeletion(GpuResourceKind.Semaphore, unchecked((int)id), WasDeletedExternal(GpuResourceKind.Semaphore, unchecked((int)id)));
        }

        /// <summary>Executes Ext.DeleteSemaphore and reconciles each successful name deletion.</summary>
        public static void DeleteSemaphore(uint id)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.Ext.DeleteSemaphore(id);
            cache.CompleteResourceDeletion(GpuResourceKind.Semaphore, unchecked((int)id), WasDeletedExternal(GpuResourceKind.Semaphore, unchecked((int)id)));
        }

        /// <summary>Executes Ext.DeleteSemaphores and reconciles each successful name deletion.</summary>
        public static void DeleteSemaphores(int count, ref int ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.Ext.DeleteSemaphores(count, ref ids);
            if (count <= 0) return;
            foreach (var value in MemoryMarshal.CreateReadOnlySpan(ref ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.Semaphore, unchecked((int)value), WasDeletedExternal(GpuResourceKind.Semaphore, unchecked((int)value)));
        }

        /// <summary>Executes Ext.DeleteSemaphores and reconciles each successful name deletion.</summary>
        public static void DeleteSemaphores(int count, int[] ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.Ext.DeleteSemaphores(count, ids);
            if (count <= 0) return;
            foreach (var value in ids.AsSpan(0, Math.Min(count, ids.Length)))
                cache.CompleteResourceDeletion(GpuResourceKind.Semaphore, unchecked((int)value), WasDeletedExternal(GpuResourceKind.Semaphore, unchecked((int)value)));
        }

        /// <summary>Executes Ext.DeleteSemaphores and reconciles each successful name deletion.</summary>
        public static void DeleteSemaphores(int count, int* ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.Ext.DeleteSemaphores(count, ids);
            if (count <= 0) return;
            foreach (var value in new ReadOnlySpan<int>(ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.Semaphore, unchecked((int)value), WasDeletedExternal(GpuResourceKind.Semaphore, unchecked((int)value)));
        }

        /// <summary>Executes Ext.DeleteSemaphores and reconciles each successful name deletion.</summary>
        public static void DeleteSemaphores(int count, ref uint ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.Ext.DeleteSemaphores(count, ref ids);
            if (count <= 0) return;
            foreach (var value in MemoryMarshal.CreateReadOnlySpan(ref ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.Semaphore, unchecked((int)value), WasDeletedExternal(GpuResourceKind.Semaphore, unchecked((int)value)));
        }

        /// <summary>Executes Ext.DeleteSemaphores and reconciles each successful name deletion.</summary>
        public static void DeleteSemaphores(int count, uint[] ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.Ext.DeleteSemaphores(count, ids);
            if (count <= 0) return;
            foreach (var value in ids.AsSpan(0, Math.Min(count, ids.Length)))
                cache.CompleteResourceDeletion(GpuResourceKind.Semaphore, unchecked((int)value), WasDeletedExternal(GpuResourceKind.Semaphore, unchecked((int)value)));
        }

        /// <summary>Executes Ext.DeleteSemaphores and reconciles each successful name deletion.</summary>
        public static void DeleteSemaphores(int count, uint* ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.Ext.DeleteSemaphores(count, ids);
            if (count <= 0) return;
            foreach (var value in new ReadOnlySpan<uint>(ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.Semaphore, unchecked((int)value), WasDeletedExternal(GpuResourceKind.Semaphore, unchecked((int)value)));
        }
        #endregion
    }
}
