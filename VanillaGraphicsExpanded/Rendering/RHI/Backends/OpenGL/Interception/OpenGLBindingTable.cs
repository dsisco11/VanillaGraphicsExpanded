using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Validates and accesses one installed OpenTK native dispatch table by entrypoint name.</summary>
internal sealed class OpenGLBindingTable
{
    private static readonly Lazy<OpenGLBindingTable> legacy = new(() => new(typeof(OpenTK.Graphics.OpenGL.GL)));
    private static readonly Lazy<OpenGLBindingTable> core = new(() => new(typeof(OpenTK.Graphics.OpenGL4.GL)));
    private readonly FieldInfo pointers;
    private readonly FieldInfo names;
    private readonly Dictionary<string, int> indices = new(StringComparer.Ordinal);
    private nint[] currentPointers = [];
    private string[]? expectedNames;

    #region Internal API
    /// <summary>Identifies the generated binding root without assuming experimental numeric slots.</summary>
    internal Type Root { get; }

    /// <summary>Shares dispatch-layout knowledge only; native resource/cache state remains context-local elsewhere.</summary>
    internal static OpenGLBindingTable For(Type root) => root == typeof(OpenTK.Graphics.OpenGL.GL) ? legacy.Value
        : root == typeof(OpenTK.Graphics.OpenGL4.GL) ? core.Value
        : throw new NotSupportedException($"Unsupported OpenTK binding root: {root.FullName}.");

    /// <summary>Validates the installed generated layout before any table entry is replaced.</summary>
    internal OpenGLBindingTable(Type root)
    {
        Root = root;
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
        pointers = root.GetField("EntryPoints", flags)
            ?? throw new NotSupportedException($"{root.FullName} has no native dispatch table.");
        names = root.GetField("EntryPointNames", flags)
            ?? throw new NotSupportedException($"{root.FullName} has no native entrypoint names.");
        if (pointers.FieldType != typeof(nint[]) || names.FieldType != typeof(string[]))
            throw new NotSupportedException($"Unsupported OpenTK dispatch layout: {root.FullName}.");
        var captured = Capture();
        expectedNames = (string[])captured.Names.Clone();
        for (int index = 0; index < captured.Names.Length; index++)
            if (!indices.TryAdd(captured.Names[index], index))
                throw new NotSupportedException($"Duplicate OpenTK entrypoint: {captured.Names[index]}.");
    }

    /// <summary>Captures the current array, including a replacement published by a binding loader.</summary>
    internal (nint[] Pointers, string[] Names) Capture()
    {
        var table = (nint[]?)pointers.GetValue(null);
        var entries = (string[]?)names.GetValue(null);
        if (table is null || entries is null || table.Length != entries.Length)
            throw new NotSupportedException($"Inconsistent OpenTK dispatch layout: {Root.FullName}.");
        if (expectedNames is not null && !entries.AsSpan().SequenceEqual(expectedNames))
            throw new NotSupportedException($"OpenTK entrypoint ordering changed: {Root.FullName}.");
        Volatile.Write(ref currentPointers, table);
        return (table, entries);
    }

    /// <summary>Resolves one entry in the current table and returns its atomically published pointer.</summary>
    internal nint Read(string entrypoint)
    {
        if (!indices.TryGetValue(entrypoint, out int index))
            throw new NotSupportedException($"{Root.FullName} does not expose {entrypoint}.");
        var table = Volatile.Read(ref currentPointers);
        return Volatile.Read(ref table[index]);
    }
    #endregion
}
