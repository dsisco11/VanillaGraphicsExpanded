namespace ShaderContractGenerator.Tests;

/// <summary>Supplies context-free capture contracts to the generator's isolated compilation tests.</summary>
internal static class RuntimeCaptureSupport
{
    internal const string Source = """
    namespace VanillaGraphicsExpanded.Rendering
    {
        public static class ShaderInputSnapshot
        {
            public static bool Equal<T>(T[] left, T[] right) => System.Linq.Enumerable.SequenceEqual(left ?? System.Array.Empty<T>(), right ?? System.Array.Empty<T>());
            public static T Copy<T>(T value) => value is System.Array array ? (T)(object)array.Clone() : value;
        }
        public class GpuTexture { public static implicit operator GpuTextureRef(GpuTexture value) => new(value); }
        public readonly record struct GpuTextureRef(object Resource);
        public readonly record struct GpuBufferRef(object Resource);
        public class GpuUniformBuffer { public static implicit operator GpuBufferRef(GpuUniformBuffer value) => new(value); }
        public class GpuShaderStorageBuffer { public static implicit operator GpuBufferRef(GpuShaderStorageBuffer value) => new(value); }
        public class GpuAtomicCounterBuffer { public static implicit operator GpuBufferRef(GpuAtomicCounterBuffer value) => new(value); }
        public readonly record struct GpuStorageBufferBinding(GpuShaderStorageBuffer Buffer, int OffsetBytes = 0, int SizeBytes = 0);
        public class CpuUniformBuffer
        {
            private RHI.RHIUniformSnapshot snapshot = new(0);
            public void Edit(int version) => snapshot = new(version);
            public RHI.RHIUniformSnapshot Capture() => snapshot;
        }
        public abstract class GpuComputeProgram : Shaders.GpuProgram { }
    }
    namespace VanillaGraphicsExpanded.Rendering.RHI
    {
        public readonly record struct RHIShaderRef(object Resource);
        public enum BufferRole { Uniform, ShaderStorage, AtomicCounter }
        public enum ImageAccess { ReadOnly, WriteOnly, ReadWrite }
        public enum TextureFormat { Rgba32f, R32f }
        public sealed record RHIUniformSnapshot(int Version);
        public readonly record struct RHIImageBinding(VanillaGraphicsExpanded.Rendering.GpuTextureRef Texture, int Level = 0, bool Layered = false, int Layer = 0, ImageAccess Access = ImageAccess.ReadWrite, TextureFormat? Format = null);
        public readonly record struct RHISamplerBinding(ulong Identity, VanillaGraphicsExpanded.Rendering.GpuTextureRef Texture, int Element = 0);
        public readonly struct RHIImageInput
        {
            public RHIImageBinding View { get; }
            public RHIImageInput(ulong identity, RHIImageBinding view) { View = view; }
            public RHIImageInput(ulong identity, VanillaGraphicsExpanded.Rendering.GpuTextureRef texture) { View = new(texture); }
        }
        public readonly record struct RHIBufferBinding(ulong Identity, BufferRole Role, VanillaGraphicsExpanded.Rendering.GpuBufferRef Buffer, int Offset = 0, int Length = 0, bool Whole = true);
        public readonly record struct RHIUniformBinding(ulong Identity, RHIUniformSnapshot Snapshot);
        public sealed class RHIResourceBindings
        {
            public RHIShaderRef Shader { get; }
            public RHISamplerBinding[] Samplers { get; }
            public RHIImageInput[] Images { get; }
            public RHIBufferBinding[] Buffers { get; }
            public RHIUniformBinding[] Uniforms { get; }
            public RHIResourceBindings(RHIShaderRef shader, System.Collections.Generic.IEnumerable<RHISamplerBinding> samplers = null,
                System.Collections.Generic.IEnumerable<RHIImageInput> images = null, System.Collections.Generic.IEnumerable<RHIBufferBinding> buffers = null,
                System.Collections.Generic.IEnumerable<RHIUniformBinding> uniforms = null)
            {
                Shader = shader; Samplers = System.Linq.Enumerable.ToArray(samplers ?? System.Array.Empty<RHISamplerBinding>());
                Images = System.Linq.Enumerable.ToArray(images ?? System.Array.Empty<RHIImageInput>());
                Buffers = System.Linq.Enumerable.ToArray(buffers ?? System.Array.Empty<RHIBufferBinding>());
                Uniforms = System.Linq.Enumerable.ToArray(uniforms ?? System.Array.Empty<RHIUniformBinding>());
            }
        }
    }
    namespace VanillaGraphicsExpanded.Rendering.Shaders
    {
        public abstract class GpuProgram : System.IDisposable
        {
            private bool retired;
            public VanillaGraphicsExpanded.Rendering.RHI.RHIShaderRef Shader { get; set; } = new(new object());
            protected virtual VanillaGraphicsExpanded.Rendering.RHI.RHIResourceBindings CaptureBindingsCore() => new(Shader);
            public VanillaGraphicsExpanded.Rendering.RHI.RHIResourceBindings CaptureBindings() { RequireInputMutation(); return CaptureBindingsCore(); }
            protected void RequireInputMutation() { if (retired) throw new System.ObjectDisposedException("shader"); }
            public void Dispose() { retired = true; }
        }
    }
    """;
}
