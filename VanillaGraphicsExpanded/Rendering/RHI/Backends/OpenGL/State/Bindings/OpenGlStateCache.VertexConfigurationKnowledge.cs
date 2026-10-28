using System.Collections.Generic;
namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
/// <summary>Retains observed configuration freshness and certified buffer dependencies without mirroring VAO layouts.</summary>
internal sealed partial class OpenGlStateCache
{
    private readonly Dictionary<int, ulong> vertexConfigurationRevisions = new();
    private readonly Dictionary<int, HashSet<int>> certifiedVertexBuffers = new();
    private ulong vertexConfigurationSerial;
    #region Internal API
    /// <summary>Gets only the selected array's observed configuration revision without native queries.</summary>
    internal ulong GetVertexConfigurationRevision(int array) => vertexConfigurationRevisions.TryGetValue(array, out var revision) ? revision : 0;
    /// <summary>Withdraws the affected array's certification before or after a configuration mutation.</summary>
    internal void InvalidateVertexConfiguration(int array)
    {
        vertexConfigurationRevisions[array] = ++vertexConfigurationSerial;
        certifiedVertexBuffers.Remove(array);
    }
    /// <summary>Retains only certified buffer-name dependencies needed to withdraw local receipts on deletion.</summary>
    internal void EstablishVertexConfigurationDependencies(int array, IEnumerable<int> buffers, int elements)
    {
        certifiedVertexBuffers[array] = new(buffers) { elements };
    }
    /// <summary>Expires receipts depending on a retired buffer; unrelated staging retirement leaves them unchanged.</summary>
    internal void InvalidateVertexConfigurationsForBuffer(int buffer)
    {
        foreach (var pair in certifiedVertexBuffers)
            if (pair.Value.Contains(buffer)) vertexConfigurationRevisions[pair.Key] = ++vertexConfigurationSerial;
    }
    /// <summary>Records the element selector proved by an explicit acquisition without issuing a configuration command.</summary>
    internal void EstablishVertexArrayElementKnowledge(int array, int elements) => elementArrayBufferByVao[array] = elements;
    /// <summary>Withdraws one array's element selection while its native configuration is uncertain.</summary>
    internal void ForgetVertexArrayElementKnowledge(int array) => elementArrayBufferByVao.Remove(array);
    #endregion
}
