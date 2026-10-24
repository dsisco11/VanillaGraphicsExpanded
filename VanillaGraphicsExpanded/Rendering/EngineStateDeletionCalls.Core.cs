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

        /// <summary>Executes Core.DeleteQueries and reconciles each successful name deletion.</summary>
        public static void DeleteQueries(int count, ref int ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteQueries(count, ref ids);
            if (count <= 0) return;
            foreach (var value in MemoryMarshal.CreateReadOnlySpan(ref ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.Query, unchecked((int)value));
        }

        /// <summary>Executes Core.DeleteQueries and reconciles each successful name deletion.</summary>
        public static void DeleteQueries(int count, int[] ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteQueries(count, ids);
            if (count <= 0) return;
            foreach (var value in ids.AsSpan(0, Math.Min(count, ids.Length)))
                cache.CompleteResourceDeletion(GpuResourceKind.Query, unchecked((int)value));
        }

        /// <summary>Executes Core.DeleteQueries and reconciles each successful name deletion.</summary>
        public static void DeleteQueries(int count, int* ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteQueries(count, ids);
            if (count <= 0) return;
            foreach (var value in new ReadOnlySpan<int>(ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.Query, unchecked((int)value));
        }

        /// <summary>Executes Core.DeleteQueries and reconciles each successful name deletion.</summary>
        public static void DeleteQueries(int count, ref uint ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteQueries(count, ref ids);
            if (count <= 0) return;
            foreach (var value in MemoryMarshal.CreateReadOnlySpan(ref ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.Query, unchecked((int)value));
        }

        /// <summary>Executes Core.DeleteQueries and reconciles each successful name deletion.</summary>
        public static void DeleteQueries(int count, uint[] ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteQueries(count, ids);
            if (count <= 0) return;
            foreach (var value in ids.AsSpan(0, Math.Min(count, ids.Length)))
                cache.CompleteResourceDeletion(GpuResourceKind.Query, unchecked((int)value));
        }

        /// <summary>Executes Core.DeleteQueries and reconciles each successful name deletion.</summary>
        public static void DeleteQueries(int count, uint* ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteQueries(count, ids);
            if (count <= 0) return;
            foreach (var value in new ReadOnlySpan<uint>(ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.Query, unchecked((int)value));
        }

        /// <summary>Executes Core.DeleteQuery and reconciles each successful name deletion.</summary>
        public static void DeleteQuery(int id)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteQuery(id);
            cache.CompleteResourceDeletion(GpuResourceKind.Query, unchecked((int)id));
        }

        /// <summary>Executes Core.DeleteQuery and reconciles each successful name deletion.</summary>
        public static void DeleteQuery(uint id)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteQuery(id);
            cache.CompleteResourceDeletion(GpuResourceKind.Query, unchecked((int)id));
        }

        /// <summary>Executes Core.DeleteRenderbuffer and reconciles each successful name deletion.</summary>
        public static void DeleteRenderbuffer(int id)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteRenderbuffer(id);
            cache.CompleteResourceDeletion(GpuResourceKind.Renderbuffer, unchecked((int)id));
        }

        /// <summary>Executes Core.DeleteRenderbuffer and reconciles each successful name deletion.</summary>
        public static void DeleteRenderbuffer(uint id)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteRenderbuffer(id);
            cache.CompleteResourceDeletion(GpuResourceKind.Renderbuffer, unchecked((int)id));
        }

        /// <summary>Executes Core.DeleteRenderbuffers and reconciles each successful name deletion.</summary>
        public static void DeleteRenderbuffers(int count, ref int ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteRenderbuffers(count, ref ids);
            if (count <= 0) return;
            foreach (var value in MemoryMarshal.CreateReadOnlySpan(ref ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.Renderbuffer, unchecked((int)value));
        }

        /// <summary>Executes Core.DeleteRenderbuffers and reconciles each successful name deletion.</summary>
        public static void DeleteRenderbuffers(int count, int[] ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteRenderbuffers(count, ids);
            if (count <= 0) return;
            foreach (var value in ids.AsSpan(0, Math.Min(count, ids.Length)))
                cache.CompleteResourceDeletion(GpuResourceKind.Renderbuffer, unchecked((int)value));
        }

        /// <summary>Executes Core.DeleteRenderbuffers and reconciles each successful name deletion.</summary>
        public static void DeleteRenderbuffers(int count, int* ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteRenderbuffers(count, ids);
            if (count <= 0) return;
            foreach (var value in new ReadOnlySpan<int>(ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.Renderbuffer, unchecked((int)value));
        }

        /// <summary>Executes Core.DeleteRenderbuffers and reconciles each successful name deletion.</summary>
        public static void DeleteRenderbuffers(int count, ref uint ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteRenderbuffers(count, ref ids);
            if (count <= 0) return;
            foreach (var value in MemoryMarshal.CreateReadOnlySpan(ref ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.Renderbuffer, unchecked((int)value));
        }

        /// <summary>Executes Core.DeleteRenderbuffers and reconciles each successful name deletion.</summary>
        public static void DeleteRenderbuffers(int count, uint[] ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteRenderbuffers(count, ids);
            if (count <= 0) return;
            foreach (var value in ids.AsSpan(0, Math.Min(count, ids.Length)))
                cache.CompleteResourceDeletion(GpuResourceKind.Renderbuffer, unchecked((int)value));
        }

        /// <summary>Executes Core.DeleteRenderbuffers and reconciles each successful name deletion.</summary>
        public static void DeleteRenderbuffers(int count, uint* ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteRenderbuffers(count, ids);
            if (count <= 0) return;
            foreach (var value in new ReadOnlySpan<uint>(ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.Renderbuffer, unchecked((int)value));
        }

        /// <summary>Executes Core.DeleteSampler and reconciles each successful name deletion.</summary>
        public static void DeleteSampler(int id)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteSampler(id);
            cache.CompleteResourceDeletion(GpuResourceKind.Sampler, unchecked((int)id));
        }

        /// <summary>Executes Core.DeleteSampler and reconciles each successful name deletion.</summary>
        public static void DeleteSampler(uint id)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteSampler(id);
            cache.CompleteResourceDeletion(GpuResourceKind.Sampler, unchecked((int)id));
        }

        /// <summary>Executes Core.DeleteSamplers and reconciles each successful name deletion.</summary>
        public static void DeleteSamplers(int count, ref int ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteSamplers(count, ref ids);
            if (count <= 0) return;
            foreach (var value in MemoryMarshal.CreateReadOnlySpan(ref ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.Sampler, unchecked((int)value));
        }

        /// <summary>Executes Core.DeleteSamplers and reconciles each successful name deletion.</summary>
        public static void DeleteSamplers(int count, int[] ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteSamplers(count, ids);
            if (count <= 0) return;
            foreach (var value in ids.AsSpan(0, Math.Min(count, ids.Length)))
                cache.CompleteResourceDeletion(GpuResourceKind.Sampler, unchecked((int)value));
        }

        /// <summary>Executes Core.DeleteSamplers and reconciles each successful name deletion.</summary>
        public static void DeleteSamplers(int count, int* ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteSamplers(count, ids);
            if (count <= 0) return;
            foreach (var value in new ReadOnlySpan<int>(ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.Sampler, unchecked((int)value));
        }

        /// <summary>Executes Core.DeleteSamplers and reconciles each successful name deletion.</summary>
        public static void DeleteSamplers(int count, ref uint ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteSamplers(count, ref ids);
            if (count <= 0) return;
            foreach (var value in MemoryMarshal.CreateReadOnlySpan(ref ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.Sampler, unchecked((int)value));
        }

        /// <summary>Executes Core.DeleteSamplers and reconciles each successful name deletion.</summary>
        public static void DeleteSamplers(int count, uint[] ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteSamplers(count, ids);
            if (count <= 0) return;
            foreach (var value in ids.AsSpan(0, Math.Min(count, ids.Length)))
                cache.CompleteResourceDeletion(GpuResourceKind.Sampler, unchecked((int)value));
        }

        /// <summary>Executes Core.DeleteSamplers and reconciles each successful name deletion.</summary>
        public static void DeleteSamplers(int count, uint* ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteSamplers(count, ids);
            if (count <= 0) return;
            foreach (var value in new ReadOnlySpan<uint>(ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.Sampler, unchecked((int)value));
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

        /// <summary>Executes Core.DeleteSync and reconciles each successful name deletion.</summary>
        public static void DeleteSync(IntPtr id)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteSync(id);
            cache.CompleteResourceDeletion(GpuResourceKind.Sync, (nint)id);
        }

        /// <summary>Executes Core.DeleteTexture and reconciles each successful name deletion.</summary>
        public static void DeleteTexture(int id)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteTexture(id);
            cache.CompleteResourceDeletion(GpuResourceKind.Texture, unchecked((int)id));
        }

        /// <summary>Executes Core.DeleteTexture and reconciles each successful name deletion.</summary>
        public static void DeleteTexture(uint id)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteTexture(id);
            cache.CompleteResourceDeletion(GpuResourceKind.Texture, unchecked((int)id));
        }

        /// <summary>Executes Core.DeleteTextures and reconciles each successful name deletion.</summary>
        public static void DeleteTextures(int count, ref int ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteTextures(count, ref ids);
            if (count <= 0) return;
            foreach (var value in MemoryMarshal.CreateReadOnlySpan(ref ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.Texture, unchecked((int)value));
        }

        /// <summary>Executes Core.DeleteTextures and reconciles each successful name deletion.</summary>
        public static void DeleteTextures(int count, int[] ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteTextures(count, ids);
            if (count <= 0) return;
            foreach (var value in ids.AsSpan(0, Math.Min(count, ids.Length)))
                cache.CompleteResourceDeletion(GpuResourceKind.Texture, unchecked((int)value));
        }

        /// <summary>Executes Core.DeleteTextures and reconciles each successful name deletion.</summary>
        public static void DeleteTextures(int count, int* ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteTextures(count, ids);
            if (count <= 0) return;
            foreach (var value in new ReadOnlySpan<int>(ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.Texture, unchecked((int)value));
        }

        /// <summary>Executes Core.DeleteTextures and reconciles each successful name deletion.</summary>
        public static void DeleteTextures(int count, ref uint ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteTextures(count, ref ids);
            if (count <= 0) return;
            foreach (var value in MemoryMarshal.CreateReadOnlySpan(ref ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.Texture, unchecked((int)value));
        }

        /// <summary>Executes Core.DeleteTextures and reconciles each successful name deletion.</summary>
        public static void DeleteTextures(int count, uint[] ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteTextures(count, ids);
            if (count <= 0) return;
            foreach (var value in ids.AsSpan(0, Math.Min(count, ids.Length)))
                cache.CompleteResourceDeletion(GpuResourceKind.Texture, unchecked((int)value));
        }

        /// <summary>Executes Core.DeleteTextures and reconciles each successful name deletion.</summary>
        public static void DeleteTextures(int count, uint* ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteTextures(count, ids);
            if (count <= 0) return;
            foreach (var value in new ReadOnlySpan<uint>(ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.Texture, unchecked((int)value));
        }

        /// <summary>Executes Core.DeleteTransformFeedback and reconciles each successful name deletion.</summary>
        public static void DeleteTransformFeedback(int id)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteTransformFeedback(id);
            cache.CompleteResourceDeletion(GpuResourceKind.TransformFeedback, unchecked((int)id));
        }

        /// <summary>Executes Core.DeleteTransformFeedback and reconciles each successful name deletion.</summary>
        public static void DeleteTransformFeedback(uint id)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteTransformFeedback(id);
            cache.CompleteResourceDeletion(GpuResourceKind.TransformFeedback, unchecked((int)id));
        }

        /// <summary>Executes Core.DeleteTransformFeedbacks and reconciles each successful name deletion.</summary>
        public static void DeleteTransformFeedbacks(int count, ref int ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteTransformFeedbacks(count, ref ids);
            if (count <= 0) return;
            foreach (var value in MemoryMarshal.CreateReadOnlySpan(ref ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.TransformFeedback, unchecked((int)value));
        }

        /// <summary>Executes Core.DeleteTransformFeedbacks and reconciles each successful name deletion.</summary>
        public static void DeleteTransformFeedbacks(int count, int[] ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteTransformFeedbacks(count, ids);
            if (count <= 0) return;
            foreach (var value in ids.AsSpan(0, Math.Min(count, ids.Length)))
                cache.CompleteResourceDeletion(GpuResourceKind.TransformFeedback, unchecked((int)value));
        }

        /// <summary>Executes Core.DeleteTransformFeedbacks and reconciles each successful name deletion.</summary>
        public static void DeleteTransformFeedbacks(int count, int* ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteTransformFeedbacks(count, ids);
            if (count <= 0) return;
            foreach (var value in new ReadOnlySpan<int>(ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.TransformFeedback, unchecked((int)value));
        }

        /// <summary>Executes Core.DeleteTransformFeedbacks and reconciles each successful name deletion.</summary>
        public static void DeleteTransformFeedbacks(int count, ref uint ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteTransformFeedbacks(count, ref ids);
            if (count <= 0) return;
            foreach (var value in MemoryMarshal.CreateReadOnlySpan(ref ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.TransformFeedback, unchecked((int)value));
        }

        /// <summary>Executes Core.DeleteTransformFeedbacks and reconciles each successful name deletion.</summary>
        public static void DeleteTransformFeedbacks(int count, uint[] ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteTransformFeedbacks(count, ids);
            if (count <= 0) return;
            foreach (var value in ids.AsSpan(0, Math.Min(count, ids.Length)))
                cache.CompleteResourceDeletion(GpuResourceKind.TransformFeedback, unchecked((int)value));
        }

        /// <summary>Executes Core.DeleteTransformFeedbacks and reconciles each successful name deletion.</summary>
        public static void DeleteTransformFeedbacks(int count, uint* ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteTransformFeedbacks(count, ids);
            if (count <= 0) return;
            foreach (var value in new ReadOnlySpan<uint>(ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.TransformFeedback, unchecked((int)value));
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
