using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace VanillaGraphicsExpanded.HarmonyPatches;

/// <summary>Resolves concrete value-type instantiations of engine methods containing supported native calls.</summary>
internal static class EngineGenericStateTargets
{
    #region Internal API
    /// <summary>Returns distinct concrete targets, including calls made through a generic virtual base declaration.</summary>
    internal static IEnumerable<MethodInfo> Discover(IReadOnlyCollection<Assembly> assemblies, IReadOnlyCollection<MethodInfo> definitions)
    {
        if (definitions.Count == 0) yield break;
        var declarations = new Dictionary<MethodInfo, List<MethodInfo>>();
        foreach (var definition in definitions)
        {
            AddDeclaration(declarations, definition, definition);
            if (definition.IsVirtual) AddDeclaration(declarations, definition.GetBaseDefinition(), definition);
        }
        var seen = new HashSet<MethodInfo>();
        foreach (var assembly in assemblies)
        {
            using var stream = File.OpenRead(assembly.Location);
            using var image = new PEReader(stream);
            var metadata = image.GetMetadataReader();
            var module = assembly.ManifestModule;
            var references = FindDeclarations(module, metadata, declarations);
            for (int row = 1; row <= metadata.GetTableRowCount(TableIndex.MethodSpec); row++)
            {
                var handle = MetadataTokens.MethodSpecificationHandle(row);
                var specification = metadata.GetMethodSpecification(handle);
                if (!references.TryGetValue(MetadataTokens.GetToken(specification.Method), out var owners)) continue;

                // Resolve only references to eligible engine methods. Unrelated signatures can
                // require optional dependencies, so this is deliberately not a whole-module resolve.
                MethodInfo instance;
                try { instance = (MethodInfo)module.ResolveMethod(MetadataTokens.GetToken(handle))!; }
                catch (ArgumentException exception)
                {
                    throw new NotSupportedException($"Engine state caller has unresolved generic arguments in {assembly.GetName().Name}: {owners[0]}.", exception);
                }
                var arguments = instance.GetGenericArguments();
                if (instance.ContainsGenericParameters || arguments.Any(argument => !argument.IsValueType))
                    throw new NotSupportedException($"Engine state interception requires a concrete value-type instantiation: {instance}.");

                // Value-type specializations have distinct bodies. Never patch a shared reference-
                // type body or an open definition and assume that all instantiations are covered.
                foreach (var owner in owners)
                {
                    var target = owner.MakeGenericMethod(arguments);
                    if (seen.Add(target)) yield return target;
                }
            }
        }
    }
    #endregion

    #region Private
    /// <summary>Associates exact direct and virtual declarations with the native-calling implementation.</summary>
    private static void AddDeclaration(Dictionary<MethodInfo, List<MethodInfo>> declarations, MethodInfo declaration, MethodInfo owner)
    {
        if (!declarations.TryGetValue(declaration, out var owners)) declarations.Add(declaration, owners = []);
        if (!owners.Contains(owner)) owners.Add(owner);
    }

    /// <summary>Indexes matching definitions and member references without loading unrelated engine signatures.</summary>
    private static Dictionary<int, List<MethodInfo>> FindDeclarations(Module module, MetadataReader metadata,
        Dictionary<MethodInfo, List<MethodInfo>> declarations)
    {
        var result = new Dictionary<int, List<MethodInfo>>();
        foreach (var (declaration, owners) in declarations)
            if (declaration.Module == module) result.Add(declaration.MetadataToken, owners);
        var names = declarations.Keys.Select(method => (method.DeclaringType!.FullName, method.Name)).ToHashSet();
        foreach (var handle in metadata.MemberReferences)
        {
            var member = metadata.GetMemberReference(handle);
            if (member.Parent.Kind != HandleKind.TypeReference || member.GetKind() != MemberReferenceKind.Method) continue;
            if (!names.Contains((TypeName(metadata, (TypeReferenceHandle)member.Parent), metadata.GetString(member.Name)))) continue;
            int token = MetadataTokens.GetToken(handle);
            if (module.ResolveMethod(token) is MethodInfo method && declarations.TryGetValue(method, out var owners))
                result.Add(token, owners);
        }
        return result;
    }

    /// <summary>Builds reflection's qualified type name from metadata, including nested declaring types.</summary>
    private static string TypeName(MetadataReader metadata, TypeReferenceHandle handle)
    {
        var type = metadata.GetTypeReference(handle);
        string name = metadata.GetString(type.Name);
        if (type.ResolutionScope.Kind == HandleKind.TypeReference)
            return TypeName(metadata, (TypeReferenceHandle)type.ResolutionScope) + "+" + name;
        string space = metadata.GetString(type.Namespace);
        return space.Length == 0 ? name : space + "." + name;
    }
    #endregion
}
