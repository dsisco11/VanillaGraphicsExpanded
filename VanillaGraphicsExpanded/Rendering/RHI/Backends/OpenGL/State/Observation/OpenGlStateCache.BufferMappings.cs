using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Owns executed buffer mapping activity independently of canonical storage descriptors.</summary>
internal sealed partial class OpenGlStateCache
{
    private readonly Dictionary<int, bool> bufferMappings = new();

    #region Internal API
    /// <summary>Records creation or allocation's guaranteed unmapped state in the originating cache.</summary>
    internal void EstablishBufferUnmapped(int id) => bufferMappings[id] = false;

    /// <summary>Reports established execution activity without probing a native descriptor.</summary>
    internal bool TryGetBufferMapping(int id, out bool mapped) => bufferMappings.TryGetValue(id, out mapped);

    /// <summary>Resolves only the selector required by a mapping command and handles EXT realization.</summary>
    internal int ResolveObservedBuffer(int selector, bool named, bool extCreation)
    {
        if (!named && !IsBufferTargetSupported((BufferTarget)selector)) return 0;
        int id = named ? selector : GetBoundBuffer((BufferTarget)selector);
        if (extCreation) RealizeNativeBuffer(id, true);
        return id;
    }
    /// <summary>Records successful named mapping from the backend saved-original route.</summary>
    internal void RecordBufferMapped(int id) => bufferMappings[id] = true;

    /// <summary>Distinguishes a rejected unknown unmap from known mapping termination.</summary>
    internal void RecordBufferUnmapped(int id, bool result)
    {
        if (result || bufferMappings.ContainsKey(id)) bufferMappings[id] = false;
    }
    #endregion
}
