namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Owns one independent shader-test scene's engine targets and production terrain attachments.</summary>
internal sealed class ShaderSceneInputs : IDisposable
{
    private EngineTerrainBuffers? engine;
    private GBufferTextures? terrain;
    private bool disposed;
    public EngineTerrainBuffers Engine => engine ?? throw new InvalidOperationException("Size the scene before borrowing inputs.");
    public GBufferTextures Terrain => terrain ?? throw new InvalidOperationException("Size the scene before borrowing inputs.");

    #region Allocation
    /// <summary>Retains allocations at unchanged dimensions; resizing retires borrowed resources as one scene set.</summary>
    public void EnsureSize(int width, int height)
    {
        ObjectDisposedException.ThrowIf(disposed,this);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (engine?.Primary.Width == width && engine.Primary.Height == height) return;

        // Prepare replacements before retiring the previous scene, preserving it if allocation fails.
        var replacementEngine = new EngineTerrainBuffers(width,height);
        GBufferTextures replacementTerrain;
        try { replacementTerrain = new(width,height); }
        catch { replacementEngine.Dispose(); throw; }
        terrain?.Dispose(); engine?.Dispose();
        engine = replacementEngine; terrain = replacementTerrain;
    }
    #endregion

    #region Lifetime
    /// <summary>Releases owned targets; callers borrow attachments and must not dispose them separately.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        terrain?.Dispose(); engine?.Dispose();
        terrain = null; engine = null;
    }
    #endregion
}
