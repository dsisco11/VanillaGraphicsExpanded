using System;
using System.Diagnostics;

using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// Helper wrapper for configuring a VAO vertex buffer binding + associated vertex attributes.
/// This is not an OpenGL object; it represents state stored inside a VAO.
/// </summary>
internal struct GpuVertexAttribBinding
{
    private readonly GpuVao? owner;
    private readonly (nint Handle, long Generation) context;
    private ulong metadataRevision;
    private ulong localEpoch;
    private GpuVertexBindingFields knownFacts;

    private readonly int vaoId;
    private readonly int bindingIndex;

    private int vboId;
    private nint baseOffsetBytes;
    private int strideBytes;
    private int divisor;

    #region Public API
    #region Identity and binding facts
    /// <summary>
    /// Gets the VAO id this binding config targets.
    /// </summary>
    public int VaoId => vaoId;

    /// <summary>
    /// Gets the binding index within the VAO.
    /// </summary>
    public int BindingIndex => bindingIndex;

    /// <summary>
    /// Gets the buffer id bound to this binding (if known; primarily for fallback paths).
    /// </summary>
    public int BufferId { get { RequireCurrentFacts(GpuVertexBindingFields.Buffer); return vboId; } }

    /// <summary>
    /// Gets the base byte offset for this binding (if known; primarily for fallback paths).
    /// </summary>
    public nint BaseOffsetBytes { get { RequireCurrentFacts(GpuVertexBindingFields.Offset); return baseOffsetBytes; } }

    /// <summary>
    /// Gets the stride (in bytes) for this binding (if known; primarily for fallback paths).
    /// </summary>
    public int StrideBytes { get { RequireCurrentFacts(GpuVertexBindingFields.Stride); return strideBytes; } }

    /// <summary>
    /// Gets the instance divisor for this binding (0 for per-vertex).
    /// </summary>
    public int Divisor { get { RequireCurrentFacts(GpuVertexBindingFields.Divisor); return divisor; } }

    /// <summary>Captures the owning wrapper so a retained helper cannot target a reused native name.</summary>
    public GpuVertexAttribBinding(GpuVao owner, int bindingIndex)
        : this(owner, bindingIndex, vboId: 0, baseOffsetBytes: 0, strideBytes: 0, divisor: 0, discover: true)
    {
    }

    #endregion

    #region Configuration
    /// <summary>
    /// Binds a vertex buffer to this binding index within the VAO.
    /// </summary>
    public GpuVertexAttribBinding BindVertexBuffer(int vboId, nint offsetBytes, int strideBytes)
    {
        RequireCurrentFacts(GpuVertexBindingFields.Divisor);
        if (vaoId == 0)
        {
            Debug.WriteLine("[GpuVertexAttribBinding] Attempted to configure binding on invalid VAO (0)");
            return this;
        }

        if (vboId == 0)
        {
            Debug.WriteLine("[GpuVertexAttribBinding] Attempted to bind invalid VBO id (0)");
            return this;
        }

        if (strideBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(strideBytes), strideBytes, "Stride must be >= 0.");
        }

        if (!Dsa.TryVertexArrayVertexBuffer(vaoId, bindingIndex, vboId, offsetBytes, strideBytes))
        {
            // Legacy emulation: bind VAO + bind ARRAY_BUFFER. Offsets are applied per-attribute via VertexAttribPointer.
            StateCache.Current.BindVertexArray(vaoId);
            StateCache.Current.BindBuffer(BufferTarget.ArrayBuffer, vboId);
        }

        return new GpuVertexAttribBinding(owner!, bindingIndex, vboId, offsetBytes, strideBytes, divisor);
    }

    /// <summary>
    /// Sets the instance divisor for this binding index (0 for per-vertex, 1 for per-instance, etc.).
    /// </summary>
    public GpuVertexAttribBinding SetDivisor(int divisor)
    {
        RequireCurrentFacts(GpuVertexBindingFields.Buffer | GpuVertexBindingFields.Offset | GpuVertexBindingFields.Stride);
        if (vaoId == 0)
        {
            return this;
        }

        if (divisor < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(divisor), divisor, "Divisor must be >= 0.");
        }

        if (!Dsa.TryVertexArrayBindingDivisor(vaoId, bindingIndex, divisor))
        {
            // Legacy emulation is per-attrib (VertexAttribDivisor) and is applied when configuring each attrib.
        }

        return new GpuVertexAttribBinding(owner!, bindingIndex, vboId, baseOffsetBytes, strideBytes, divisor);
    }

    /// <summary>
    /// Configures a floating-point vertex attribute to read from this binding index.
    /// </summary>
    public void SetFloatAttrib(
        int attribIndex,
        int size,
        VertexAttribPointerType type,
        bool normalized,
        int relativeOffsetBytes)
    {
        RequireCurrentFacts();
        if (vaoId == 0)
        {
            return;
        }

        if (relativeOffsetBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(relativeOffsetBytes), relativeOffsetBytes, "Relative offset must be >= 0.");
        }

        if (Dsa.TryVertexArrayAttribFormatFloat(vaoId, attribIndex, size, type, normalized, relativeOffsetBytes)
            && Dsa.TryVertexArrayAttribBinding(vaoId, attribIndex, bindingIndex)
            && Dsa.TryEnableVertexArrayAttrib(vaoId, attribIndex))
        {
            CaptureRevision();
            return;
        }

        // Legacy emulation: requires a bound ARRAY_BUFFER.
        if (vboId == 0)
        {
            Debug.WriteLine("[GpuVertexAttribBinding] Legacy attrib pointer requires BindVertexBuffer() first");
            return;
        }

        nint absoluteOffset = baseOffsetBytes + relativeOffsetBytes;

        StateCache.Current.BindVertexArray(vaoId);
        StateCache.Current.BindBuffer(BufferTarget.ArrayBuffer, vboId);
        StateCache.Current.EnableVertexAttribArray(attribIndex);
        StateCache.Current.VertexAttribPointer(attribIndex, size, type, normalized, strideBytes, (IntPtr)absoluteOffset);
        StateCache.Current.VertexAttribDivisor(attribIndex, divisor);
        CaptureRevision();
    }

    /// <summary>
    /// Configures an integer vertex attribute to read from this binding index.
    /// </summary>
    public void SetIntAttrib(
        int attribIndex,
        int size,
        VertexAttribIntegerType type,
        int relativeOffsetBytes)
    {
        RequireCurrentFacts();
        if (vaoId == 0)
        {
            return;
        }

        if (relativeOffsetBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(relativeOffsetBytes), relativeOffsetBytes, "Relative offset must be >= 0.");
        }

        if (Dsa.TryVertexArrayAttribFormatInt(vaoId, attribIndex, size, type, relativeOffsetBytes)
            && Dsa.TryVertexArrayAttribBinding(vaoId, attribIndex, bindingIndex)
            && Dsa.TryEnableVertexArrayAttrib(vaoId, attribIndex))
        {
            CaptureRevision();
            return;
        }

        // Legacy emulation: requires a bound ARRAY_BUFFER.
        if (vboId == 0)
        {
            Debug.WriteLine("[GpuVertexAttribBinding] Legacy attrib pointer requires BindVertexBuffer() first");
            return;
        }

        nint absoluteOffset = baseOffsetBytes + relativeOffsetBytes;

        StateCache.Current.BindVertexArray(vaoId);
        StateCache.Current.BindBuffer(BufferTarget.ArrayBuffer, vboId);
        StateCache.Current.EnableVertexAttribArray(attribIndex);
        StateCache.Current.VertexAttribIPointer(attribIndex, size, type, strideBytes, (IntPtr)absoluteOffset);
        StateCache.Current.VertexAttribDivisor(attribIndex, divisor);
        CaptureRevision();
    }

    /// <summary>Configures a double attribute through the same revision-aware binding contract.</summary>
    public void SetDoubleAttrib(int attribIndex, int size, int relativeOffsetBytes)
    {
        RequireCurrentFacts();
        if (vaoId == 0) return;
        ArgumentOutOfRangeException.ThrowIfNegative(relativeOffsetBytes);
        var cache = StateCache.Current;
        if (GpuSupport.Graphics.SupportsArbDirectStateAccess)
        {
            cache.VertexArrayAttribLFormat(vaoId, attribIndex, size, VertexAttribDoubleType.Double, relativeOffsetBytes);
            cache.VertexArrayAttribBinding(vaoId, attribIndex, bindingIndex);
            cache.EnableVertexArrayAttrib(vaoId, attribIndex);
        }
        else
        {
            if (vboId == 0) throw new InvalidOperationException("Bind a vertex buffer before configuring double attributes.");
            cache.BindVertexArray(vaoId);
            cache.BindBuffer(BufferTarget.ArrayBuffer, vboId);
            cache.EnableVertexAttribArray(attribIndex);
            cache.VertexAttribLPointer(attribIndex, size, VertexAttribDoubleType.Double, strideBytes,
                checked(baseOffsetBytes + relativeOffsetBytes));
            cache.VertexAttribDivisor(attribIndex, divisor);
        }
        CaptureRevision();
    }

    /// <summary>Disables a vertex attribute within this VAO.</summary>
    public void DisableAttrib(int attribIndex)
    {
        RequireCurrentFacts();
        if (vaoId == 0)
        {
            return;
        }

        if (!Dsa.TryDisableVertexArrayAttrib(vaoId, attribIndex))
        {
            StateCache.Current.BindVertexArray(vaoId);
            StateCache.Current.DisableVertexAttribArray(attribIndex);
        }
        CaptureRevision();
    }

    #endregion
    #endregion

    #region Private
    /// <summary>Copies authored binding values with their owner's current metadata revision.</summary>
    private GpuVertexAttribBinding(GpuVao owner, int bindingIndex, int vboId, nint baseOffsetBytes, int strideBytes, int divisor, bool discover = false)
    {
        this.owner = owner;
        context = Integration.RenderContextRegistry.Current();
        metadataRevision = owner.ResourceManager?.GetResourceRegistration(owner)?.MetadataRevision ?? 0;
        localEpoch = StateCache.Current.VertexConfigurationEpoch;
        knownFacts = discover && GpuSupport.Graphics.SupportsArbDirectStateAccess
            ? GpuVertexBindingFields.None : GpuVertexBindingFields.All;
        vaoId = owner.VertexArrayId;
        this.bindingIndex = bindingIndex;
        this.vboId = vboId;
        this.baseOffsetBytes = baseOffsetBytes;
        this.strideBytes = strideBytes;
        this.divisor = divisor;
        RequireCurrentFacts(GpuVertexBindingFields.All);
    }

    /// <summary>Checks original ownership and acquires only unknown fields needed by this operation.</summary>
    private void RequireCurrentFacts(GpuVertexBindingFields required = GpuVertexBindingFields.None)
    {
        if (owner is null) return;
        ObjectDisposedException.ThrowIf(!owner.IsValid || owner.VertexArrayId != vaoId, owner);
        if (context != Integration.RenderContextRegistry.Current())
            throw new InvalidOperationException("The vertex binding belongs to another native context.");
        var manager = owner.ResourceManager;
        var entry = manager?.GetResourceRegistration(owner);
        if (entry is not null && !manager!.CanObserveNativeMetadata)
            throw new InvalidOperationException("The vertex array belongs to another resource context.");
        if (entry is not null && !manager!.IsResourceMetadataAvailable(owner))
            throw new InvalidOperationException("Vertex array metadata is unavailable.");
        bool changed = entry is not null ? entry.MetadataRevision != metadataRevision
            : localEpoch != StateCache.Current.VertexConfigurationEpoch;
        // Fallback descriptions are authored per attribute, not native binding-index mirrors.
        // A changed revision cannot be recovered reliably; retained copies must be rejected.
        if (!GpuSupport.Graphics.SupportsArbDirectStateAccess)
        {
            if (changed) throw new InvalidOperationException("The vertex binding changed; obtain and configure a new binding helper.");
            return;
        }
        if (changed) knownFacts = GpuVertexBindingFields.None;
        GpuVertexBindingFields missing = required & ~knownFacts;
        GpuResourceManager.AcquireVertexBindingFacts(owner, vaoId, bindingIndex, context, missing,
            ref vboId, ref baseOffsetBytes, ref strideBytes, ref divisor);
        knownFacts |= missing;
        CaptureRevision();
    }

    /// <summary>Accepts completed commands without certifying retained copies or unknown fields.</summary>
    private void CaptureRevision()
    {
        metadataRevision = owner?.ResourceManager?.GetResourceRegistration(owner)?.MetadataRevision ?? 0;
        localEpoch = StateCache.Current.VertexConfigurationEpoch;
    }
    #endregion

    /// <summary>Selects named configuration commands using the shared capabilities.</summary>
    private static class Dsa
    {

        /// <summary>Uses the named vertex API when supported; native and publication failures propagate.</summary>
        public static bool TryVertexArrayVertexBuffer(int vaoId, int bindingIndex, int vboId, nint offsetBytes, int strideBytes)
        {
            if (!GpuSupport.Graphics.SupportsArbDirectStateAccess)
            {
                return false;
            }

            StateCache.Current.VertexArrayVertexBuffer(vaoId, bindingIndex, vboId, (IntPtr)offsetBytes, strideBytes);
            return true;
        }

        /// <summary>Uses the named vertex API when supported; native and publication failures propagate.</summary>
        public static bool TryVertexArrayAttribBinding(int vaoId, int attribIndex, int bindingIndex)
        {
            if (!GpuSupport.Graphics.SupportsArbDirectStateAccess)
            {
                return false;
            }

            StateCache.Current.VertexArrayAttribBinding(vaoId, attribIndex, bindingIndex);
            return true;
        }

        /// <summary>Uses the named vertex API when supported; native and publication failures propagate.</summary>
        public static bool TryVertexArrayAttribFormatFloat(
            int vaoId,
            int attribIndex,
            int size,
            VertexAttribPointerType type,
            bool normalized,
            int relativeOffsetBytes)
        {
            if (!GpuSupport.Graphics.SupportsArbDirectStateAccess)
            {
                return false;
            }

            VertexAttribType attribType = (VertexAttribType)(int)type;
            StateCache.Current.VertexArrayAttribFormat(vaoId, attribIndex, size, attribType, normalized, relativeOffsetBytes);
            return true;
        }

        /// <summary>Uses the named vertex API when supported; native and publication failures propagate.</summary>
        public static bool TryVertexArrayAttribFormatInt(
            int vaoId,
            int attribIndex,
            int size,
            VertexAttribIntegerType type,
            int relativeOffsetBytes)
        {
            if (!GpuSupport.Graphics.SupportsArbDirectStateAccess)
            {
                return false;
            }

            VertexAttribIType attribType = (VertexAttribIType)(int)type;
            StateCache.Current.VertexArrayAttribIFormat(vaoId, attribIndex, size, attribType, relativeOffsetBytes);
            return true;
        }

        /// <summary>Uses the named vertex API when supported; native and publication failures propagate.</summary>
        public static bool TryEnableVertexArrayAttrib(int vaoId, int attribIndex)
        {
            if (!GpuSupport.Graphics.SupportsArbDirectStateAccess)
            {
                return false;
            }

            StateCache.Current.EnableVertexArrayAttrib(vaoId, attribIndex);
            return true;
        }

        /// <summary>Uses the named vertex API when supported; native and publication failures propagate.</summary>
        public static bool TryDisableVertexArrayAttrib(int vaoId, int attribIndex)
        {
            if (!GpuSupport.Graphics.SupportsArbDirectStateAccess)
            {
                return false;
            }

            StateCache.Current.DisableVertexArrayAttrib(vaoId, attribIndex);
            return true;
        }

        /// <summary>Uses the named vertex API when supported; native and publication failures propagate.</summary>
        public static bool TryVertexArrayBindingDivisor(int vaoId, int bindingIndex, int divisor)
        {
            if (!GpuSupport.Graphics.SupportsArbDirectStateAccess)
            {
                return false;
            }

            StateCache.Current.VertexArrayBindingDivisor(vaoId, bindingIndex, divisor);
            return true;
        }
    }
}
