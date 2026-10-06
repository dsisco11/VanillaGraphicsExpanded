using ShaderBuildTool.Spirv;

namespace ShaderBuildTool.Tests;

/// <summary>Exercises aliases accessing the same compiler result while other jobs publish it.</summary>
public sealed class ShaderVariantCacheConcurrencyTests
{
    #region Public API
    /// <summary>Parallel readers and writers never observe a partial entry or deny an atomic replacement.</summary>
    [Fact]
    public void SameKeyReadersAndWritersPreservePublishedBytes()
    {
        string root = Path.Combine(Path.GetTempPath(), "vge-cache-sharing-" + Guid.NewGuid().ToString("N"));
        try
        {
            var cache = new ShaderVariantCache(root, "fixture-compiler");
            byte[] bytes = Enumerable.Range(0, 131072).Select(index => (byte)index).ToArray();
            var digest = ShaderVariantCache.Digest(bytes);
            string key = cache.Key("shared source", "vertex", "main");
            cache.Store(key, bytes, digest);
            // Different catalog selections can resolve to the same content key. Exercise their
            // overlapping reads and replacements without depending on an external shader compiler.
            Parallel.For(0, 512, new ParallelOptions { MaxDegreeOfParallelism = 8 }, index =>
            {
                if (index % 2 == 0) cache.Store(key, bytes, digest);
                else
                {
                    Assert.True(cache.TryRead(key, out var actual, out var actualDigest));
                    Assert.Equal(digest, actualDigest);
                    Assert.Equal(bytes, actual);
                }
            });
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }
    #endregion
}
