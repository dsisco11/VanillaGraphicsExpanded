using System.Reflection;

namespace VanillaGraphicsExpanded.Tests.Architecture;

/// <summary>Inspects declared payload storage through project types, containers and inheritance without constructing objects.</summary>
internal sealed class ContractPayloadGraph(Func<Type, bool> ownsType, Func<Type, bool> opaqueOwner)
{
    private readonly HashSet<Type> visited = [];
    private readonly Dictionary<Type, string> paths = [];

    #region Internal API
    /// <summary>Returns the first field/property path reaching each declared payload type.</summary>
    internal IReadOnlyDictionary<Type, string> Inspect(IEnumerable<Type> roots)
    {
        foreach (var root in roots) Visit(root, root.Name);
        return paths;
    }
    #endregion

    #region Private
    /// <summary>Unwraps containers before following instance storage; opaque execution owners are checked at their own boundary.</summary>
    private void Visit(Type type, string path)
    {
        paths.TryAdd(type, path);
        if (!visited.Add(type)) return;
        if (type.HasElementType) Visit(type.GetElementType()!, path + "[]");
        if (type.IsGenericType)
            foreach (var argument in type.GetGenericArguments()) Visit(argument, path + "<" + argument.Name + ">");
        if (type.IsGenericParameter || type.IsEnum || !ownsType(type) || opaqueOwner(type)) return;

        // Inspect both authored properties and private backing fields. A semantic outer record
        // must not hide a native enum inside another record, collection key or inherited member.
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        foreach (var property in type.GetProperties(flags)) Visit(property.PropertyType, path + "." + property.Name);
        foreach (var field in type.GetFields(flags)) Visit(field.FieldType, path + "." + field.Name);
        if (type.BaseType is { } parent && ownsType(parent)) Visit(parent, path + ":" + parent.Name);
    }
    #endregion
}
