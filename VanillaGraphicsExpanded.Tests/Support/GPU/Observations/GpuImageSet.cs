using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.Support.GPU.Observations;

/// <summary>Owns independent attachment snapshots for comparisons across draws and resource replacements.</summary>
internal sealed class GpuImageSet : IDisposable
{
    private readonly List<GpuImageSnapshot> images = [];
    private bool disposed;

    #region Public API
    /// <summary>Attempts retirement of every captured attachment even if one owner reports a cleanup failure.</summary>
    public void Dispose()
    {
        if (disposed) return;
        List<Exception>? failures = null;
        foreach (var image in images)
        {
            try { image.Dispose(); }
            catch (Exception error) { (failures ??= []).Add(error); }
        }
        images.Clear(); disposed = true;
        if (failures is not null) throw new AggregateException("Attachment snapshot cleanup failed.", failures);
    }
    #endregion

    #region Internal API
    /// <summary>Gets the exact number of independently retained attachments.</summary>
    internal int Count => images.Count;
    /// <summary>Gets a retained GPU image, without transferring pixels or ownership to its consumer.</summary>
    internal GpuImageSnapshot this[int index]
    {
        get { ObjectDisposedException.ThrowIf(disposed, this); return images[index]; }
    }

    /// <summary>Copies each supplied attachment and cleans every completed partial copy if construction fails.</summary>
    internal GpuImageSet(IEnumerable<GpuTexture> sources, bool activeAttachment = false)
    {
        try { foreach (var source in sources) images.Add(new(source, activeAttachment: activeAttachment)); }
        catch (Exception failure)
        {
            try { Dispose(); }
            catch (Exception cleanup) { throw new AggregateException(failure, cleanup); }
            throw;
        }
    }
    #endregion
}
