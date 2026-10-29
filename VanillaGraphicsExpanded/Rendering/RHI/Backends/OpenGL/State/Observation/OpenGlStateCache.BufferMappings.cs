using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Owns executed buffer mapping activity independently of canonical storage descriptors.</summary>
internal sealed partial class OpenGlStateCache
{
    private readonly Dictionary<int, BufferMapping> bufferMappings = new();

    /// <summary>Retains whether an established mapping permits persistent GPU access.</summary>
    private readonly record struct BufferMapping(bool Mapped, bool Persistent);

    #region Internal API
    /// <summary>Records creation or allocation's guaranteed unmapped state in the originating cache.</summary>
    internal void EstablishBufferUnmapped(int id) => bufferMappings[id] = new(false, false);

    /// <summary>Reports established execution activity without probing a native descriptor.</summary>
    internal bool TryGetBufferMapping(int id, out bool mapped, out bool persistent)
    {
        bool known = bufferMappings.TryGetValue(id, out var state);
        mapped = state.Mapped; persistent = state.Persistent;
        return known;
    }

    /// <summary>Resolves only the selector required by a mapping command and handles EXT realization.</summary>
    internal int ResolveObservedBuffer(int selector, bool named, bool extCreation)
    {
        bool? targetAccepted = named ? true : ClassifyBufferTarget((BufferTarget)selector);
        if (targetAccepted == false) return 0;
        if (targetAccepted is null)
        {
            // A FALSE unmap can be target rejection, while a valid unmap may report corruption.
            // Do not query an optional selector whose availability has not been captured.
            if (TryGetCachedBoundBuffer((BufferTarget)selector, out int known)) bufferMappings.Remove(known);
            return 0;
        }
        int id = named ? selector : GetBoundBuffer((BufferTarget)selector);
        if (extCreation) RealizeNativeBuffer(id, true);
        return id;
    }
    /// <summary>Records successful named mapping from the backend saved-original route.</summary>
    internal void RecordBufferMapped(int id, bool persistent = false) => bufferMappings[id] = new(true, persistent);

    /// <summary>Distinguishes a rejected unknown unmap from known mapping termination.</summary>
    internal void RecordBufferUnmapped(int id, bool result)
    {
        if (result || bufferMappings.ContainsKey(id)) bufferMappings[id] = new(false, false);
    }
    #endregion
}
