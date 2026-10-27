using System;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Preserves exact Ext deletion signatures while forwarding proven native outcomes to the state cache.</summary>
internal static partial class OpenGLRenderingDeletionCalls
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
            cache.CompleteResourceDeletion(GpuResourceKind.Framebuffer, unchecked((int)id), WasDeletedExternal(GpuResourceKind.Framebuffer, unchecked((int)id)));
        }

        /// <summary>Executes Ext.DeleteFramebuffer and reconciles each successful name deletion.</summary>
        public static void DeleteFramebuffer(uint id)
        {
            var cache = EngineStateCalls.CurrentCache;
            cache.PrepareResourceDeletion(GpuResourceKind.Framebuffer, unchecked((int)id));
            GL.Ext.DeleteFramebuffer(id);
            cache.CompleteResourceDeletion(GpuResourceKind.Framebuffer, unchecked((int)id), WasDeletedExternal(GpuResourceKind.Framebuffer, unchecked((int)id)));
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
                cache.CompleteResourceDeletion(GpuResourceKind.Framebuffer, unchecked((int)value), WasDeletedExternal(GpuResourceKind.Framebuffer, unchecked((int)value)));
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
                cache.CompleteResourceDeletion(GpuResourceKind.Framebuffer, unchecked((int)value), WasDeletedExternal(GpuResourceKind.Framebuffer, unchecked((int)value)));
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
                cache.CompleteResourceDeletion(GpuResourceKind.Framebuffer, unchecked((int)value), WasDeletedExternal(GpuResourceKind.Framebuffer, unchecked((int)value)));
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
                cache.CompleteResourceDeletion(GpuResourceKind.Framebuffer, unchecked((int)value), WasDeletedExternal(GpuResourceKind.Framebuffer, unchecked((int)value)));
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
                cache.CompleteResourceDeletion(GpuResourceKind.Framebuffer, unchecked((int)value), WasDeletedExternal(GpuResourceKind.Framebuffer, unchecked((int)value)));
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
                cache.CompleteResourceDeletion(GpuResourceKind.Framebuffer, unchecked((int)value), WasDeletedExternal(GpuResourceKind.Framebuffer, unchecked((int)value)));
        }

















        /// <summary>Executes Ext.DeleteProgramPipeline and reconciles each successful name deletion.</summary>
        public static void DeleteProgramPipeline(int id)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.Ext.DeleteProgramPipeline(id);
            cache.CompleteResourceDeletion(GpuResourceKind.ProgramPipeline, unchecked((int)id), WasDeletedExternal(GpuResourceKind.ProgramPipeline, unchecked((int)id)));
        }

        /// <summary>Executes Ext.DeleteProgramPipeline and reconciles each successful name deletion.</summary>
        public static void DeleteProgramPipeline(uint id)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.Ext.DeleteProgramPipeline(id);
            cache.CompleteResourceDeletion(GpuResourceKind.ProgramPipeline, unchecked((int)id), WasDeletedExternal(GpuResourceKind.ProgramPipeline, unchecked((int)id)));
        }

        /// <summary>Executes Ext.DeleteProgramPipelines and reconciles each successful name deletion.</summary>
        public static void DeleteProgramPipelines(int count, ref int ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.Ext.DeleteProgramPipelines(count, ref ids);
            if (count <= 0) return;
            foreach (var value in MemoryMarshal.CreateReadOnlySpan(ref ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.ProgramPipeline, unchecked((int)value), WasDeletedExternal(GpuResourceKind.ProgramPipeline, unchecked((int)value)));
        }

        /// <summary>Executes Ext.DeleteProgramPipelines and reconciles each successful name deletion.</summary>
        public static void DeleteProgramPipelines(int count, int[] ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.Ext.DeleteProgramPipelines(count, ids);
            if (count <= 0) return;
            foreach (var value in ids.AsSpan(0, Math.Min(count, ids.Length)))
                cache.CompleteResourceDeletion(GpuResourceKind.ProgramPipeline, unchecked((int)value), WasDeletedExternal(GpuResourceKind.ProgramPipeline, unchecked((int)value)));
        }

        /// <summary>Executes Ext.DeleteProgramPipelines and reconciles each successful name deletion.</summary>
        public static void DeleteProgramPipelines(int count, int* ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.Ext.DeleteProgramPipelines(count, ids);
            if (count <= 0) return;
            foreach (var value in new ReadOnlySpan<int>(ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.ProgramPipeline, unchecked((int)value), WasDeletedExternal(GpuResourceKind.ProgramPipeline, unchecked((int)value)));
        }

        /// <summary>Executes Ext.DeleteProgramPipelines and reconciles each successful name deletion.</summary>
        public static void DeleteProgramPipelines(int count, ref uint ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.Ext.DeleteProgramPipelines(count, ref ids);
            if (count <= 0) return;
            foreach (var value in MemoryMarshal.CreateReadOnlySpan(ref ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.ProgramPipeline, unchecked((int)value), WasDeletedExternal(GpuResourceKind.ProgramPipeline, unchecked((int)value)));
        }

        /// <summary>Executes Ext.DeleteProgramPipelines and reconciles each successful name deletion.</summary>
        public static void DeleteProgramPipelines(int count, uint[] ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.Ext.DeleteProgramPipelines(count, ids);
            if (count <= 0) return;
            foreach (var value in ids.AsSpan(0, Math.Min(count, ids.Length)))
                cache.CompleteResourceDeletion(GpuResourceKind.ProgramPipeline, unchecked((int)value), WasDeletedExternal(GpuResourceKind.ProgramPipeline, unchecked((int)value)));
        }

        /// <summary>Executes Ext.DeleteProgramPipelines and reconciles each successful name deletion.</summary>
        public static void DeleteProgramPipelines(int count, uint* ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.Ext.DeleteProgramPipelines(count, ids);
            if (count <= 0) return;
            foreach (var value in new ReadOnlySpan<uint>(ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.ProgramPipeline, unchecked((int)value), WasDeletedExternal(GpuResourceKind.ProgramPipeline, unchecked((int)value)));
        }

















































        #endregion
    }
}
