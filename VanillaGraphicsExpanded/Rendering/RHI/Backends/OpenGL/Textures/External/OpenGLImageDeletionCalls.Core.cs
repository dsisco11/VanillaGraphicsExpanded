using System;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Preserves exact external image deletion overloads and native per-name outcomes.</summary>
internal static partial class OpenGLImageDeletionCalls
{
    /// <summary>Contains exact Core deletion signatures.</summary>
    internal static unsafe class Core
    {
        #region Public API
        /// <summary>Executes Core.DeleteRenderbuffer and reconciles each successful name deletion.</summary>
        public static void DeleteRenderbuffer(int id)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteRenderbuffer(id);
            cache.CompleteResourceDeletion(GpuResourceKind.Renderbuffer, unchecked((int)id), unchecked((int)id) == 0 || !GL.IsRenderbuffer(unchecked((int)id)));
        }

        /// <summary>Executes Core.DeleteRenderbuffer and reconciles each successful name deletion.</summary>
        public static void DeleteRenderbuffer(uint id)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteRenderbuffer(id);
            cache.CompleteResourceDeletion(GpuResourceKind.Renderbuffer, unchecked((int)id), unchecked((int)id) == 0 || !GL.IsRenderbuffer(unchecked((int)id)));
        }

        /// <summary>Executes Core.DeleteRenderbuffers and reconciles each successful name deletion.</summary>
        public static void DeleteRenderbuffers(int count, ref int ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteRenderbuffers(count, ref ids);
            if (count <= 0) return;
            foreach (var value in MemoryMarshal.CreateReadOnlySpan(ref ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.Renderbuffer, unchecked((int)value), unchecked((int)value) == 0 || !GL.IsRenderbuffer(unchecked((int)value)));
        }

        /// <summary>Executes Core.DeleteRenderbuffers and reconciles each successful name deletion.</summary>
        public static void DeleteRenderbuffers(int count, int[] ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteRenderbuffers(count, ids);
            if (count <= 0) return;
            foreach (var value in ids.AsSpan(0, Math.Min(count, ids.Length)))
                cache.CompleteResourceDeletion(GpuResourceKind.Renderbuffer, unchecked((int)value), unchecked((int)value) == 0 || !GL.IsRenderbuffer(unchecked((int)value)));
        }

        /// <summary>Executes Core.DeleteRenderbuffers and reconciles each successful name deletion.</summary>
        public static void DeleteRenderbuffers(int count, int* ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteRenderbuffers(count, ids);
            if (count <= 0) return;
            foreach (var value in new ReadOnlySpan<int>(ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.Renderbuffer, unchecked((int)value), unchecked((int)value) == 0 || !GL.IsRenderbuffer(unchecked((int)value)));
        }

        /// <summary>Executes Core.DeleteRenderbuffers and reconciles each successful name deletion.</summary>
        public static void DeleteRenderbuffers(int count, ref uint ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteRenderbuffers(count, ref ids);
            if (count <= 0) return;
            foreach (var value in MemoryMarshal.CreateReadOnlySpan(ref ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.Renderbuffer, unchecked((int)value), unchecked((int)value) == 0 || !GL.IsRenderbuffer(unchecked((int)value)));
        }

        /// <summary>Executes Core.DeleteRenderbuffers and reconciles each successful name deletion.</summary>
        public static void DeleteRenderbuffers(int count, uint[] ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteRenderbuffers(count, ids);
            if (count <= 0) return;
            foreach (var value in ids.AsSpan(0, Math.Min(count, ids.Length)))
                cache.CompleteResourceDeletion(GpuResourceKind.Renderbuffer, unchecked((int)value), unchecked((int)value) == 0 || !GL.IsRenderbuffer(unchecked((int)value)));
        }

        /// <summary>Executes Core.DeleteRenderbuffers and reconciles each successful name deletion.</summary>
        public static void DeleteRenderbuffers(int count, uint* ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteRenderbuffers(count, ids);
            if (count <= 0) return;
            foreach (var value in new ReadOnlySpan<uint>(ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.Renderbuffer, unchecked((int)value), unchecked((int)value) == 0 || !GL.IsRenderbuffer(unchecked((int)value)));
        }

        /// <summary>Executes Core.DeleteSampler and reconciles each successful name deletion.</summary>
        public static void DeleteSampler(int id)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteSampler(id);
            cache.CompleteResourceDeletion(GpuResourceKind.Sampler, unchecked((int)id), unchecked((int)id) == 0 || !GL.IsSampler(unchecked((int)id)));
        }

        /// <summary>Executes Core.DeleteSampler and reconciles each successful name deletion.</summary>
        public static void DeleteSampler(uint id)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteSampler(id);
            cache.CompleteResourceDeletion(GpuResourceKind.Sampler, unchecked((int)id), unchecked((int)id) == 0 || !GL.IsSampler(unchecked((int)id)));
        }

        /// <summary>Executes Core.DeleteSamplers and reconciles each successful name deletion.</summary>
        public static void DeleteSamplers(int count, ref int ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteSamplers(count, ref ids);
            if (count <= 0) return;
            foreach (var value in MemoryMarshal.CreateReadOnlySpan(ref ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.Sampler, unchecked((int)value), unchecked((int)value) == 0 || !GL.IsSampler(unchecked((int)value)));
        }

        /// <summary>Executes Core.DeleteSamplers and reconciles each successful name deletion.</summary>
        public static void DeleteSamplers(int count, int[] ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteSamplers(count, ids);
            if (count <= 0) return;
            foreach (var value in ids.AsSpan(0, Math.Min(count, ids.Length)))
                cache.CompleteResourceDeletion(GpuResourceKind.Sampler, unchecked((int)value), unchecked((int)value) == 0 || !GL.IsSampler(unchecked((int)value)));
        }

        /// <summary>Executes Core.DeleteSamplers and reconciles each successful name deletion.</summary>
        public static void DeleteSamplers(int count, int* ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteSamplers(count, ids);
            if (count <= 0) return;
            foreach (var value in new ReadOnlySpan<int>(ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.Sampler, unchecked((int)value), unchecked((int)value) == 0 || !GL.IsSampler(unchecked((int)value)));
        }

        /// <summary>Executes Core.DeleteSamplers and reconciles each successful name deletion.</summary>
        public static void DeleteSamplers(int count, ref uint ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteSamplers(count, ref ids);
            if (count <= 0) return;
            foreach (var value in MemoryMarshal.CreateReadOnlySpan(ref ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.Sampler, unchecked((int)value), unchecked((int)value) == 0 || !GL.IsSampler(unchecked((int)value)));
        }

        /// <summary>Executes Core.DeleteSamplers and reconciles each successful name deletion.</summary>
        public static void DeleteSamplers(int count, uint[] ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteSamplers(count, ids);
            if (count <= 0) return;
            foreach (var value in ids.AsSpan(0, Math.Min(count, ids.Length)))
                cache.CompleteResourceDeletion(GpuResourceKind.Sampler, unchecked((int)value), unchecked((int)value) == 0 || !GL.IsSampler(unchecked((int)value)));
        }

        /// <summary>Executes Core.DeleteSamplers and reconciles each successful name deletion.</summary>
        public static void DeleteSamplers(int count, uint* ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteSamplers(count, ids);
            if (count <= 0) return;
            foreach (var value in new ReadOnlySpan<uint>(ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.Sampler, unchecked((int)value), unchecked((int)value) == 0 || !GL.IsSampler(unchecked((int)value)));
        }

        /// <summary>Executes Core.DeleteTexture and reconciles each successful name deletion.</summary>
        public static void DeleteTexture(int id)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteTexture(id);
            cache.CompleteResourceDeletion(GpuResourceKind.Texture, unchecked((int)id), unchecked((int)id) == 0 || !GL.IsTexture(unchecked((int)id)));
        }

        /// <summary>Executes Core.DeleteTexture and reconciles each successful name deletion.</summary>
        public static void DeleteTexture(uint id)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteTexture(id);
            cache.CompleteResourceDeletion(GpuResourceKind.Texture, unchecked((int)id), unchecked((int)id) == 0 || !GL.IsTexture(unchecked((int)id)));
        }

        /// <summary>Executes Core.DeleteTextures and reconciles each successful name deletion.</summary>
        public static void DeleteTextures(int count, ref int ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteTextures(count, ref ids);
            if (count <= 0) return;
            foreach (var value in MemoryMarshal.CreateReadOnlySpan(ref ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.Texture, unchecked((int)value), unchecked((int)value) == 0 || !GL.IsTexture(unchecked((int)value)));
        }

        /// <summary>Executes Core.DeleteTextures and reconciles each successful name deletion.</summary>
        public static void DeleteTextures(int count, int[] ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteTextures(count, ids);
            if (count <= 0) return;
            foreach (var value in ids.AsSpan(0, Math.Min(count, ids.Length)))
                cache.CompleteResourceDeletion(GpuResourceKind.Texture, unchecked((int)value), unchecked((int)value) == 0 || !GL.IsTexture(unchecked((int)value)));
        }

        /// <summary>Executes Core.DeleteTextures and reconciles each successful name deletion.</summary>
        public static void DeleteTextures(int count, int* ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteTextures(count, ids);
            if (count <= 0) return;
            foreach (var value in new ReadOnlySpan<int>(ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.Texture, unchecked((int)value), unchecked((int)value) == 0 || !GL.IsTexture(unchecked((int)value)));
        }

        /// <summary>Executes Core.DeleteTextures and reconciles each successful name deletion.</summary>
        public static void DeleteTextures(int count, ref uint ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteTextures(count, ref ids);
            if (count <= 0) return;
            foreach (var value in MemoryMarshal.CreateReadOnlySpan(ref ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.Texture, unchecked((int)value), unchecked((int)value) == 0 || !GL.IsTexture(unchecked((int)value)));
        }

        /// <summary>Executes Core.DeleteTextures and reconciles each successful name deletion.</summary>
        public static void DeleteTextures(int count, uint[] ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteTextures(count, ids);
            if (count <= 0) return;
            foreach (var value in ids.AsSpan(0, Math.Min(count, ids.Length)))
                cache.CompleteResourceDeletion(GpuResourceKind.Texture, unchecked((int)value), unchecked((int)value) == 0 || !GL.IsTexture(unchecked((int)value)));
        }

        /// <summary>Executes Core.DeleteTextures and reconciles each successful name deletion.</summary>
        public static void DeleteTextures(int count, uint* ids)
        {
            var cache = EngineStateCalls.CurrentCache;
            GL.DeleteTextures(count, ids);
            if (count <= 0) return;
            foreach (var value in new ReadOnlySpan<uint>(ids, count))
                cache.CompleteResourceDeletion(GpuResourceKind.Texture, unchecked((int)value), unchecked((int)value) == 0 || !GL.IsTexture(unchecked((int)value)));
        }
        #endregion
    }
}
