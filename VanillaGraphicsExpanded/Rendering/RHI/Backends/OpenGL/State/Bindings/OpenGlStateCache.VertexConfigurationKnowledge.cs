using System.Collections.Generic;
namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
/// <summary>Retains observed configuration freshness and certified buffer dependencies without mirroring VAO layouts.</summary>
internal sealed partial class OpenGlStateCache
{
    private readonly Dictionary<int, ulong> vertexConfigurationRevisions = new();
    private readonly Dictionary<int, HashSet<int>> certifiedVertexBuffers = new();
    private readonly Dictionary<int, HashSet<int>> certifiedBufferArrays = new();
    private ulong vertexConfigurationSerial;
    private readonly Dictionary<int, System.Runtime.CompilerServices.ConditionalWeakTable<object, System.Action>> vertexConfigurationConsumers = new();
    #region Internal API
    /// <summary>Weakly observes only the selected original array's configuration, including empty-schema and local arrays.</summary>
    internal void ObserveVertexConfiguration(int array, object owner, System.Action withdraw)
    {
        if (!vertexConfigurationConsumers.TryGetValue(array, out var consumers))
            vertexConfigurationConsumers.Add(array, consumers = new());
        consumers.AddOrUpdate(owner, withdraw);
    }
    /// <summary>Gets only the selected array's observed configuration revision without native queries.</summary>
    internal ulong GetVertexConfigurationRevision(int array) => vertexConfigurationRevisions.TryGetValue(array, out var revision) ? revision : 0;
    /// <summary>Withdraws the affected array's certification before or after a configuration mutation.</summary>
    internal void InvalidateVertexConfiguration(int array)
    {
        vertexConfigurationRevisions[array] = ++vertexConfigurationSerial;
        if (certifiedVertexBuffers.Remove(array, out var buffers))
            foreach (int buffer in buffers)
                if (certifiedBufferArrays.TryGetValue(buffer, out var arrays))
                {
                    arrays.Remove(array);
                    if (arrays.Count == 0) certifiedBufferArrays.Remove(buffer);
                }
        if (vertexConfigurationConsumers.Remove(array, out var consumers))
            foreach (var consumer in consumers) consumer.Value();
    }
    /// <summary>Retains only certified buffer-name dependencies needed to withdraw local receipts on deletion.</summary>
    internal void EstablishVertexConfigurationDependencies(int array, IEnumerable<int> buffers, int elements)
    {
        // Replace only this array's incident relationships; unrelated imported arrays are untouched.
        if (certifiedVertexBuffers.Remove(array, out var previous))
            foreach (int buffer in previous)
                if (certifiedBufferArrays.TryGetValue(buffer, out var arrays))
                {
                    arrays.Remove(array);
                    if (arrays.Count == 0) certifiedBufferArrays.Remove(buffer);
                }
        var selected = new HashSet<int>(buffers) { elements };
        certifiedVertexBuffers.Add(array, selected);
        foreach (int buffer in selected)
        {
            if (!certifiedBufferArrays.TryGetValue(buffer, out var arrays)) certifiedBufferArrays.Add(buffer, arrays = new());
            arrays.Add(array);
        }
    }
    /// <summary>Expires receipts depending on a retired buffer; unrelated staging retirement leaves them unchanged.</summary>
    internal void InvalidateVertexConfigurationsForBuffer(int buffer)
    {
        if (!certifiedBufferArrays.TryGetValue(buffer, out var arrays)) return;
        foreach (int array in System.Linq.Enumerable.ToArray(arrays)) InvalidateVertexConfiguration(array);
    }
    /// <summary>Records the element selector proved by an explicit acquisition without issuing a configuration command.</summary>
    internal void EstablishVertexArrayElementKnowledge(int array, int elements) => elementArrayBufferByVao[array] = elements;
    /// <summary>Withdraws one array's element selection while its native configuration is uncertain.</summary>
    internal void ForgetVertexArrayElementKnowledge(int array) => elementArrayBufferByVao.Remove(array);
    #endregion
}
