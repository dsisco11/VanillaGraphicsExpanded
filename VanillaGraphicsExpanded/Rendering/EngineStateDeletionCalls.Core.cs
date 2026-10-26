using System;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Preserves exact Core deletion signatures while forwarding proven native outcomes to the state cache.</summary>
internal static partial class EngineStateDeletionCalls
{
    /// <summary>Contains the Core native deletion adapters.</summary>
    internal static unsafe class Core
    {
        #region Public API


        /// <summary>Executes Core.DeleteFramebuffer and reconciles each successful name deletion.</summary>
        public static void DeleteFramebuffer(int id)
        {
            var cache = EngineStateCalls.CurrentCache;
            cache.PrepareResourceDeletion(GpuResourceKind.Framebuffer, unchecked((int)id));
            GL.DeleteFramebuffer(id);
            cache.CompleteResourceDeletion(GpuResourceKind.Framebuffer, unchecked((int)id));
        }

        /// <summary>Executes Core.DeleteFramebuffer and reconciles each successful name deletion.</summary>
        public static void DeleteFramebuffer(uint id)
        {
            var cache = EngineStateCalls.CurrentCache;
            cache.PrepareResourceDeletion(GpuResourceKind.Framebuffer, unchecked((int)id));
            GL.DeleteFramebuffer(id);
            cache.CompleteResourceDeletion(GpuResourceKind.Framebuffer, unchecked((int)id));
        }

        /// <summary>Executes Core.DeleteFramebuffers and reconciles each successful name deletion.</summary>
        public static void DeleteFramebuffers(int count, ref int ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            if (count > 0) foreach (var value in MemoryMarshal.CreateReadOnlySpan(ref ids, count))
                cache.PrepareResourceDeletion(GpuResourceKind.Framebuffer, unchecked((int)value));
            GL.DeleteFramebuffers(count, ref ids);
            if (count <= 0) return;
            foreach (var value in MemoryMarshal.CreateReadOnlySpan(ref ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.Framebuffer, unchecked((int)value));
        }

        /// <summary>Executes Core.DeleteFramebuffers and reconciles each successful name deletion.</summary>
        public static void DeleteFramebuffers(int count, int[] ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            if (count > 0) foreach (var value in ids.AsSpan(0, Math.Min(count, ids.Length)))
                cache.PrepareResourceDeletion(GpuResourceKind.Framebuffer, unchecked((int)value));
            GL.DeleteFramebuffers(count, ids);
            if (count <= 0) return;
            foreach (var value in ids.AsSpan(0, Math.Min(count, ids.Length)))
                cache.CompleteResourceDeletion(GpuResourceKind.Framebuffer, unchecked((int)value));
        }

        /// <summary>Executes Core.DeleteFramebuffers and reconciles each successful name deletion.</summary>
        public static void DeleteFramebuffers(int count, int* ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            if (count > 0) foreach (var value in new ReadOnlySpan<int>(ids, count))
                cache.PrepareResourceDeletion(GpuResourceKind.Framebuffer, unchecked((int)value));
            GL.DeleteFramebuffers(count, ids);
            if (count <= 0) return;
            foreach (var value in new ReadOnlySpan<int>(ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.Framebuffer, unchecked((int)value));
        }

        /// <summary>Executes Core.DeleteFramebuffers and reconciles each successful name deletion.</summary>
        public static void DeleteFramebuffers(int count, ref uint ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            if (count > 0) foreach (var value in MemoryMarshal.CreateReadOnlySpan(ref ids, count))
                cache.PrepareResourceDeletion(GpuResourceKind.Framebuffer, unchecked((int)value));
            GL.DeleteFramebuffers(count, ref ids);
            if (count <= 0) return;
            foreach (var value in MemoryMarshal.CreateReadOnlySpan(ref ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.Framebuffer, unchecked((int)value));
        }

        /// <summary>Executes Core.DeleteFramebuffers and reconciles each successful name deletion.</summary>
        public static void DeleteFramebuffers(int count, uint[] ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            if (count > 0) foreach (var value in ids.AsSpan(0, Math.Min(count, ids.Length)))
                cache.PrepareResourceDeletion(GpuResourceKind.Framebuffer, unchecked((int)value));
            GL.DeleteFramebuffers(count, ids);
            if (count <= 0) return;
            foreach (var value in ids.AsSpan(0, Math.Min(count, ids.Length)))
                cache.CompleteResourceDeletion(GpuResourceKind.Framebuffer, unchecked((int)value));
        }

        /// <summary>Executes Core.DeleteFramebuffers and reconciles each successful name deletion.</summary>
        public static void DeleteFramebuffers(int count, uint* ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            if (count > 0) foreach (var value in new ReadOnlySpan<uint>(ids, count))
                cache.PrepareResourceDeletion(GpuResourceKind.Framebuffer, unchecked((int)value));
            GL.DeleteFramebuffers(count, ids);
            if (count <= 0) return;
            foreach (var value in new ReadOnlySpan<uint>(ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.Framebuffer, unchecked((int)value));
        }

        /// <summary>Executes Core.DeleteProgram and reconciles each successful name deletion.</summary>
        public static void DeleteProgram(int id)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteProgram(id);
            cache.CompleteResourceDeletion(GpuResourceKind.Program, unchecked((int)id));
        }

        /// <summary>Executes Core.DeleteProgram and reconciles each successful name deletion.</summary>
        public static void DeleteProgram(uint id)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteProgram(id);
            cache.CompleteResourceDeletion(GpuResourceKind.Program, unchecked((int)id));
        }

        /// <summary>Executes Core.DeleteProgramPipeline and reconciles each successful name deletion.</summary>
        public static void DeleteProgramPipeline(int id)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteProgramPipeline(id);
            cache.CompleteResourceDeletion(GpuResourceKind.ProgramPipeline, unchecked((int)id));
        }

        /// <summary>Executes Core.DeleteProgramPipeline and reconciles each successful name deletion.</summary>
        public static void DeleteProgramPipeline(uint id)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteProgramPipeline(id);
            cache.CompleteResourceDeletion(GpuResourceKind.ProgramPipeline, unchecked((int)id));
        }

        /// <summary>Executes Core.DeleteProgramPipelines and reconciles each successful name deletion.</summary>
        public static void DeleteProgramPipelines(int count, ref int ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteProgramPipelines(count, ref ids);
            if (count <= 0) return;
            foreach (var value in MemoryMarshal.CreateReadOnlySpan(ref ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.ProgramPipeline, unchecked((int)value));
        }

        /// <summary>Executes Core.DeleteProgramPipelines and reconciles each successful name deletion.</summary>
        public static void DeleteProgramPipelines(int count, int[] ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteProgramPipelines(count, ids);
            if (count <= 0) return;
            foreach (var value in ids.AsSpan(0, Math.Min(count, ids.Length)))
                cache.CompleteResourceDeletion(GpuResourceKind.ProgramPipeline, unchecked((int)value));
        }

        /// <summary>Executes Core.DeleteProgramPipelines and reconciles each successful name deletion.</summary>
        public static void DeleteProgramPipelines(int count, int* ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteProgramPipelines(count, ids);
            if (count <= 0) return;
            foreach (var value in new ReadOnlySpan<int>(ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.ProgramPipeline, unchecked((int)value));
        }

        /// <summary>Executes Core.DeleteProgramPipelines and reconciles each successful name deletion.</summary>
        public static void DeleteProgramPipelines(int count, ref uint ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteProgramPipelines(count, ref ids);
            if (count <= 0) return;
            foreach (var value in MemoryMarshal.CreateReadOnlySpan(ref ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.ProgramPipeline, unchecked((int)value));
        }

        /// <summary>Executes Core.DeleteProgramPipelines and reconciles each successful name deletion.</summary>
        public static void DeleteProgramPipelines(int count, uint[] ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteProgramPipelines(count, ids);
            if (count <= 0) return;
            foreach (var value in ids.AsSpan(0, Math.Min(count, ids.Length)))
                cache.CompleteResourceDeletion(GpuResourceKind.ProgramPipeline, unchecked((int)value));
        }

        /// <summary>Executes Core.DeleteProgramPipelines and reconciles each successful name deletion.</summary>
        public static void DeleteProgramPipelines(int count, uint* ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteProgramPipelines(count, ids);
            if (count <= 0) return;
            foreach (var value in new ReadOnlySpan<uint>(ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.ProgramPipeline, unchecked((int)value));
        }

















































        /// <summary>Executes Core.DeleteShader and reconciles each successful name deletion.</summary>
        public static void DeleteShader(int id)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteShader(id);
            cache.CompleteResourceDeletion(GpuResourceKind.Shader, unchecked((int)id));
        }

        /// <summary>Executes Core.DeleteShader and reconciles each successful name deletion.</summary>
        public static void DeleteShader(uint id)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteShader(id);
            cache.CompleteResourceDeletion(GpuResourceKind.Shader, unchecked((int)id));
        }



































        /// <summary>Executes Core.DeleteVertexArray and reconciles each successful name deletion.</summary>
        public static void DeleteVertexArray(int id)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteVertexArray(id);
            cache.CompleteResourceDeletion(GpuResourceKind.VertexArray, unchecked((int)id));
        }

        /// <summary>Executes Core.DeleteVertexArray and reconciles each successful name deletion.</summary>
        public static void DeleteVertexArray(uint id)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteVertexArray(id);
            cache.CompleteResourceDeletion(GpuResourceKind.VertexArray, unchecked((int)id));
        }

        /// <summary>Executes Core.DeleteVertexArrays and reconciles each successful name deletion.</summary>
        public static void DeleteVertexArrays(int count, ref int ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteVertexArrays(count, ref ids);
            if (count <= 0) return;
            foreach (var value in MemoryMarshal.CreateReadOnlySpan(ref ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.VertexArray, unchecked((int)value));
        }

        /// <summary>Executes Core.DeleteVertexArrays and reconciles each successful name deletion.</summary>
        public static void DeleteVertexArrays(int count, int[] ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteVertexArrays(count, ids);
            if (count <= 0) return;
            foreach (var value in ids.AsSpan(0, Math.Min(count, ids.Length)))
                cache.CompleteResourceDeletion(GpuResourceKind.VertexArray, unchecked((int)value));
        }

        /// <summary>Executes Core.DeleteVertexArrays and reconciles each successful name deletion.</summary>
        public static void DeleteVertexArrays(int count, int* ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteVertexArrays(count, ids);
            if (count <= 0) return;
            foreach (var value in new ReadOnlySpan<int>(ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.VertexArray, unchecked((int)value));
        }

        /// <summary>Executes Core.DeleteVertexArrays and reconciles each successful name deletion.</summary>
        public static void DeleteVertexArrays(int count, ref uint ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteVertexArrays(count, ref ids);
            if (count <= 0) return;
            foreach (var value in MemoryMarshal.CreateReadOnlySpan(ref ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.VertexArray, unchecked((int)value));
        }

        /// <summary>Executes Core.DeleteVertexArrays and reconciles each successful name deletion.</summary>
        public static void DeleteVertexArrays(int count, uint[] ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteVertexArrays(count, ids);
            if (count <= 0) return;
            foreach (var value in ids.AsSpan(0, Math.Min(count, ids.Length)))
                cache.CompleteResourceDeletion(GpuResourceKind.VertexArray, unchecked((int)value));
        }

        /// <summary>Executes Core.DeleteVertexArrays and reconciles each successful name deletion.</summary>
        public static void DeleteVertexArrays(int count, uint* ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteVertexArrays(count, ids);
            if (count <= 0) return;
            foreach (var value in new ReadOnlySpan<uint>(ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.VertexArray, unchecked((int)value));
        }

        #endregion
    }
}
