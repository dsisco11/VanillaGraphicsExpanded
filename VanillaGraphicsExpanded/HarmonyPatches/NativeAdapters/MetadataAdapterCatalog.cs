using System.Collections.Generic;
using System.Reflection;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters;

/// <summary>Declares the native owner of each compiler-checked adapter family.</summary>
internal static class MetadataAdapterCatalog
{
    #region Internal API
    /// <summary>Registers explicitly selected native owners through exact managed signatures.</summary>
    internal static void Register(Dictionary<MethodInfo, MethodInfo> mappings)
    {
        NativeAdapterSignatures.Register(mappings, typeof(GL.Arb), typeof(BufferStorage.Arb));
        NativeAdapterSignatures.Register(mappings, typeof(GL), typeof(BufferStorage.Core));
        NativeAdapterSignatures.Register(mappings, typeof(GL.Ext), typeof(BufferStorage.Ext));
        NativeAdapterSignatures.Register(mappings, typeof(GL.Arb), typeof(BufferTextures.Arb));
        NativeAdapterSignatures.Register(mappings, typeof(GL), typeof(BufferTextures.Core));
        NativeAdapterSignatures.Register(mappings, typeof(GL.Ext), typeof(BufferTextures.Ext));
        NativeAdapterSignatures.Register(mappings, typeof(GL.Arb), typeof(FramebufferRouting.Arb));
        NativeAdapterSignatures.Register(mappings, typeof(GL), typeof(FramebufferRouting.Core));
        NativeAdapterSignatures.Register(mappings, typeof(GL.Ext), typeof(FramebufferRouting.Ext));
        NativeAdapterSignatures.Register(mappings, typeof(GL.Arb), typeof(Framebuffers.Arb));
        NativeAdapterSignatures.Register(mappings, typeof(GL), typeof(Framebuffers.Core));
        NativeAdapterSignatures.Register(mappings, typeof(GL.Ext), typeof(Framebuffers.Ext));
        NativeAdapterSignatures.Register(mappings, typeof(GL.Arb), typeof(Programs.Arb));
        NativeAdapterSignatures.Register(mappings, typeof(GL), typeof(Programs.Core));
        NativeAdapterSignatures.Register(mappings, typeof(GL.Ext), typeof(Programs.Ext));
        NativeAdapterSignatures.Register(mappings, typeof(GL), typeof(Renderbuffers.Core));
        NativeAdapterSignatures.Register(mappings, typeof(GL.Ext), typeof(Renderbuffers.Ext));
        NativeAdapterSignatures.Register(mappings, typeof(GL.Arb), typeof(Textures.Arb));
        NativeAdapterSignatures.Register(mappings, typeof(GL), typeof(Textures.Core));
        NativeAdapterSignatures.Register(mappings, typeof(GL.Ext), typeof(Textures.Ext));
        NativeAdapterSignatures.Register(mappings, typeof(GL.Arb), typeof(VertexArrays.Arb));
        NativeAdapterSignatures.Register(mappings, typeof(GL), typeof(VertexArrays.Core));
        NativeAdapterSignatures.Register(mappings, typeof(GL.Ext), typeof(VertexArrays.Ext));
    }
    #endregion
}
