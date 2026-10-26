using System;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Preserves exact Ext deletion signatures while forwarding proven native outcomes to the state cache.</summary>
internal static partial class EngineStateDeletionCalls
{
    /// <summary>Contains the Ext native deletion adapters.</summary>
    internal static unsafe class Ext
    {
        #region Public API
        /// <summary>Executes Ext.DeleteFramebuffer and reconciles each successful name deletion.</summary>
        public static void DeleteFramebuffer(int id)
        {
            var cache = EngineStateCalls.CurrentCache;
            cache.PrepareResourceDeletion(GpuResourceKind.Framebuffer, unchecked((int)id));
            GL.Ext.DeleteFramebuffer(id);
            cache.CompleteResourceDeletion(GpuResourceKind.Framebuffer, unchecked((int)id));
        }

        /// <summary>Executes Ext.DeleteFramebuffer and reconciles each successful name deletion.</summary>
        public static void DeleteFramebuffer(uint id)
        {
            var cache = EngineStateCalls.CurrentCache;
            cache.PrepareResourceDeletion(GpuResourceKind.Framebuffer, unchecked((int)id));
            GL.Ext.DeleteFramebuffer(id);
            cache.CompleteResourceDeletion(GpuResourceKind.Framebuffer, unchecked((int)id));
        }

        /// <summary>Executes Ext.DeleteFramebuffers and reconciles each successful name deletion.</summary>
        public static void DeleteFramebuffers(int count, ref int ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            if (count > 0) foreach (var value in MemoryMarshal.CreateReadOnlySpan(ref ids, count))
                cache.PrepareResourceDeletion(GpuResourceKind.Framebuffer, unchecked((int)value));
            GL.Ext.DeleteFramebuffers(count, ref ids);
            if (count <= 0) return;
            foreach (var value in MemoryMarshal.CreateReadOnlySpan(ref ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.Framebuffer, unchecked((int)value));
        }

        /// <summary>Executes Ext.DeleteFramebuffers and reconciles each successful name deletion.</summary>
        public static void DeleteFramebuffers(int count, int[] ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            if (count > 0) foreach (var value in ids.AsSpan(0, Math.Min(count, ids.Length)))
                cache.PrepareResourceDeletion(GpuResourceKind.Framebuffer, unchecked((int)value));
            GL.Ext.DeleteFramebuffers(count, ids);
            if (count <= 0) return;
            foreach (var value in ids.AsSpan(0, Math.Min(count, ids.Length)))
                cache.CompleteResourceDeletion(GpuResourceKind.Framebuffer, unchecked((int)value));
        }

        /// <summary>Executes Ext.DeleteFramebuffers and reconciles each successful name deletion.</summary>
        public static void DeleteFramebuffers(int count, int* ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            if (count > 0) foreach (var value in new ReadOnlySpan<int>(ids, count))
                cache.PrepareResourceDeletion(GpuResourceKind.Framebuffer, unchecked((int)value));
            GL.Ext.DeleteFramebuffers(count, ids);
            if (count <= 0) return;
            foreach (var value in new ReadOnlySpan<int>(ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.Framebuffer, unchecked((int)value));
        }

        /// <summary>Executes Ext.DeleteFramebuffers and reconciles each successful name deletion.</summary>
        public static void DeleteFramebuffers(int count, ref uint ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            if (count > 0) foreach (var value in MemoryMarshal.CreateReadOnlySpan(ref ids, count))
                cache.PrepareResourceDeletion(GpuResourceKind.Framebuffer, unchecked((int)value));
            GL.Ext.DeleteFramebuffers(count, ref ids);
            if (count <= 0) return;
            foreach (var value in MemoryMarshal.CreateReadOnlySpan(ref ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.Framebuffer, unchecked((int)value));
        }

        /// <summary>Executes Ext.DeleteFramebuffers and reconciles each successful name deletion.</summary>
        public static void DeleteFramebuffers(int count, uint[] ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            if (count > 0) foreach (var value in ids.AsSpan(0, Math.Min(count, ids.Length)))
                cache.PrepareResourceDeletion(GpuResourceKind.Framebuffer, unchecked((int)value));
            GL.Ext.DeleteFramebuffers(count, ids);
            if (count <= 0) return;
            foreach (var value in ids.AsSpan(0, Math.Min(count, ids.Length)))
                cache.CompleteResourceDeletion(GpuResourceKind.Framebuffer, unchecked((int)value));
        }

        /// <summary>Executes Ext.DeleteFramebuffers and reconciles each successful name deletion.</summary>
        public static void DeleteFramebuffers(int count, uint* ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            if (count > 0) foreach (var value in new ReadOnlySpan<uint>(ids, count))
                cache.PrepareResourceDeletion(GpuResourceKind.Framebuffer, unchecked((int)value));
            GL.Ext.DeleteFramebuffers(count, ids);
            if (count <= 0) return;
            foreach (var value in new ReadOnlySpan<uint>(ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.Framebuffer, unchecked((int)value));
        }

        /// <summary>Executes Ext.DeleteMemoryObject and reconciles each successful name deletion.</summary>
        public static void DeleteMemoryObject(int id)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.Ext.DeleteMemoryObject(id);
            cache.CompleteResourceDeletion(GpuResourceKind.MemoryObject, unchecked((int)id));
        }

        /// <summary>Executes Ext.DeleteMemoryObject and reconciles each successful name deletion.</summary>
        public static void DeleteMemoryObject(uint id)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.Ext.DeleteMemoryObject(id);
            cache.CompleteResourceDeletion(GpuResourceKind.MemoryObject, unchecked((int)id));
        }

        /// <summary>Executes Ext.DeleteMemoryObjects and reconciles each successful name deletion.</summary>
        public static void DeleteMemoryObjects(int count, ref int ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.Ext.DeleteMemoryObjects(count, ref ids);
            if (count <= 0) return;
            foreach (var value in MemoryMarshal.CreateReadOnlySpan(ref ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.MemoryObject, unchecked((int)value));
        }

        /// <summary>Executes Ext.DeleteMemoryObjects and reconciles each successful name deletion.</summary>
        public static void DeleteMemoryObjects(int count, int[] ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.Ext.DeleteMemoryObjects(count, ids);
            if (count <= 0) return;
            foreach (var value in ids.AsSpan(0, Math.Min(count, ids.Length)))
                cache.CompleteResourceDeletion(GpuResourceKind.MemoryObject, unchecked((int)value));
        }

        /// <summary>Executes Ext.DeleteMemoryObjects and reconciles each successful name deletion.</summary>
        public static void DeleteMemoryObjects(int count, int* ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.Ext.DeleteMemoryObjects(count, ids);
            if (count <= 0) return;
            foreach (var value in new ReadOnlySpan<int>(ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.MemoryObject, unchecked((int)value));
        }

        /// <summary>Executes Ext.DeleteMemoryObjects and reconciles each successful name deletion.</summary>
        public static void DeleteMemoryObjects(int count, ref uint ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.Ext.DeleteMemoryObjects(count, ref ids);
            if (count <= 0) return;
            foreach (var value in MemoryMarshal.CreateReadOnlySpan(ref ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.MemoryObject, unchecked((int)value));
        }

        /// <summary>Executes Ext.DeleteMemoryObjects and reconciles each successful name deletion.</summary>
        public static void DeleteMemoryObjects(int count, uint[] ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.Ext.DeleteMemoryObjects(count, ids);
            if (count <= 0) return;
            foreach (var value in ids.AsSpan(0, Math.Min(count, ids.Length)))
                cache.CompleteResourceDeletion(GpuResourceKind.MemoryObject, unchecked((int)value));
        }

        /// <summary>Executes Ext.DeleteMemoryObjects and reconciles each successful name deletion.</summary>
        public static void DeleteMemoryObjects(int count, uint* ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.Ext.DeleteMemoryObjects(count, ids);
            if (count <= 0) return;
            foreach (var value in new ReadOnlySpan<uint>(ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.MemoryObject, unchecked((int)value));
        }

        /// <summary>Executes Ext.DeleteProgramPipeline and reconciles each successful name deletion.</summary>
        public static void DeleteProgramPipeline(int id)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.Ext.DeleteProgramPipeline(id);
            cache.CompleteResourceDeletion(GpuResourceKind.ProgramPipeline, unchecked((int)id));
        }

        /// <summary>Executes Ext.DeleteProgramPipeline and reconciles each successful name deletion.</summary>
        public static void DeleteProgramPipeline(uint id)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.Ext.DeleteProgramPipeline(id);
            cache.CompleteResourceDeletion(GpuResourceKind.ProgramPipeline, unchecked((int)id));
        }

        /// <summary>Executes Ext.DeleteProgramPipelines and reconciles each successful name deletion.</summary>
        public static void DeleteProgramPipelines(int count, ref int ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.Ext.DeleteProgramPipelines(count, ref ids);
            if (count <= 0) return;
            foreach (var value in MemoryMarshal.CreateReadOnlySpan(ref ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.ProgramPipeline, unchecked((int)value));
        }

        /// <summary>Executes Ext.DeleteProgramPipelines and reconciles each successful name deletion.</summary>
        public static void DeleteProgramPipelines(int count, int[] ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.Ext.DeleteProgramPipelines(count, ids);
            if (count <= 0) return;
            foreach (var value in ids.AsSpan(0, Math.Min(count, ids.Length)))
                cache.CompleteResourceDeletion(GpuResourceKind.ProgramPipeline, unchecked((int)value));
        }

        /// <summary>Executes Ext.DeleteProgramPipelines and reconciles each successful name deletion.</summary>
        public static void DeleteProgramPipelines(int count, int* ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.Ext.DeleteProgramPipelines(count, ids);
            if (count <= 0) return;
            foreach (var value in new ReadOnlySpan<int>(ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.ProgramPipeline, unchecked((int)value));
        }

        /// <summary>Executes Ext.DeleteProgramPipelines and reconciles each successful name deletion.</summary>
        public static void DeleteProgramPipelines(int count, ref uint ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.Ext.DeleteProgramPipelines(count, ref ids);
            if (count <= 0) return;
            foreach (var value in MemoryMarshal.CreateReadOnlySpan(ref ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.ProgramPipeline, unchecked((int)value));
        }

        /// <summary>Executes Ext.DeleteProgramPipelines and reconciles each successful name deletion.</summary>
        public static void DeleteProgramPipelines(int count, uint[] ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.Ext.DeleteProgramPipelines(count, ids);
            if (count <= 0) return;
            foreach (var value in ids.AsSpan(0, Math.Min(count, ids.Length)))
                cache.CompleteResourceDeletion(GpuResourceKind.ProgramPipeline, unchecked((int)value));
        }

        /// <summary>Executes Ext.DeleteProgramPipelines and reconciles each successful name deletion.</summary>
        public static void DeleteProgramPipelines(int count, uint* ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.Ext.DeleteProgramPipelines(count, ids);
            if (count <= 0) return;
            foreach (var value in new ReadOnlySpan<uint>(ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.ProgramPipeline, unchecked((int)value));
        }

















        /// <summary>Executes Ext.DeleteSemaphore and reconciles each successful name deletion.</summary>
        public static void DeleteSemaphore(int id)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.Ext.DeleteSemaphore(id);
            cache.CompleteResourceDeletion(GpuResourceKind.Semaphore, unchecked((int)id));
        }

        /// <summary>Executes Ext.DeleteSemaphore and reconciles each successful name deletion.</summary>
        public static void DeleteSemaphore(uint id)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.Ext.DeleteSemaphore(id);
            cache.CompleteResourceDeletion(GpuResourceKind.Semaphore, unchecked((int)id));
        }

        /// <summary>Executes Ext.DeleteSemaphores and reconciles each successful name deletion.</summary>
        public static void DeleteSemaphores(int count, ref int ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.Ext.DeleteSemaphores(count, ref ids);
            if (count <= 0) return;
            foreach (var value in MemoryMarshal.CreateReadOnlySpan(ref ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.Semaphore, unchecked((int)value));
        }

        /// <summary>Executes Ext.DeleteSemaphores and reconciles each successful name deletion.</summary>
        public static void DeleteSemaphores(int count, int[] ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.Ext.DeleteSemaphores(count, ids);
            if (count <= 0) return;
            foreach (var value in ids.AsSpan(0, Math.Min(count, ids.Length)))
                cache.CompleteResourceDeletion(GpuResourceKind.Semaphore, unchecked((int)value));
        }

        /// <summary>Executes Ext.DeleteSemaphores and reconciles each successful name deletion.</summary>
        public static void DeleteSemaphores(int count, int* ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.Ext.DeleteSemaphores(count, ids);
            if (count <= 0) return;
            foreach (var value in new ReadOnlySpan<int>(ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.Semaphore, unchecked((int)value));
        }

        /// <summary>Executes Ext.DeleteSemaphores and reconciles each successful name deletion.</summary>
        public static void DeleteSemaphores(int count, ref uint ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.Ext.DeleteSemaphores(count, ref ids);
            if (count <= 0) return;
            foreach (var value in MemoryMarshal.CreateReadOnlySpan(ref ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.Semaphore, unchecked((int)value));
        }

        /// <summary>Executes Ext.DeleteSemaphores and reconciles each successful name deletion.</summary>
        public static void DeleteSemaphores(int count, uint[] ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.Ext.DeleteSemaphores(count, ids);
            if (count <= 0) return;
            foreach (var value in ids.AsSpan(0, Math.Min(count, ids.Length)))
                cache.CompleteResourceDeletion(GpuResourceKind.Semaphore, unchecked((int)value));
        }

        /// <summary>Executes Ext.DeleteSemaphores and reconciles each successful name deletion.</summary>
        public static void DeleteSemaphores(int count, uint* ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.Ext.DeleteSemaphores(count, ids);
            if (count <= 0) return;
            foreach (var value in new ReadOnlySpan<uint>(ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.Semaphore, unchecked((int)value));
        }

















        #endregion
    }
}
