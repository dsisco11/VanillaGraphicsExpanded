using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Microsoft.CodeAnalysis;

namespace OpenGLInterceptionGenerator.Bindings;

/// <summary>Derives native signatures from the exact OpenTK PE reference supplied by the compiler.</summary>
internal static class OpenTKSignatures
{
    #region Internal API
    /// <summary>Inspects supported GL roots and their Core/Ext/Arb owners without running static constructors.</summary>
    internal static Dictionary<string, NativeSignature> Read(Compilation compilation, IEnumerable<string> selectedNames,
        IEnumerable<DriverCall> driverCalls, out Dictionary<string, (string EntryPoint, bool ScalarAddress)> driverEntries)
    {
        driverEntries = new(StringComparer.Ordinal);
        var requested = new HashSet<string>(driverCalls.Select(call => call.BindingKey), StringComparer.Ordinal);
        var selected = new HashSet<string>(selectedNames, StringComparer.Ordinal);
        var reference = compilation.References.OfType<PortableExecutableReference>().SingleOrDefault(reference =>
            compilation.GetAssemblyOrModuleSymbol(reference) is IAssemblySymbol assembly && assembly.Name == "OpenTK.Graphics");
        if (reference?.FilePath is not { } path) throw new FormatException("A file-backed OpenTK.Graphics compiler reference is required.");
        // The compiler reference is the authority. Reading PE metadata never loads OpenTK into the analyzer host.
#pragma warning disable RS1035
        using var stream = File.OpenRead(path);
#pragma warning restore RS1035
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        var result = new Dictionary<string, NativeSignature>(StringComparer.Ordinal);
        foreach (var rootNamespace in new[] { "OpenTK.Graphics.OpenGL", "OpenTK.Graphics.OpenGL4" })
        {
            var root = reader.TypeDefinitions.SingleOrDefault(handle =>
                reader.GetString(reader.GetTypeDefinition(handle).Namespace) == rootNamespace
                && reader.GetString(reader.GetTypeDefinition(handle).Name) == "GL");
            if (root.IsNil) throw new FormatException("Unsupported OpenTK layout: missing " + rootNamespace + ".GL.");
            var definition = reader.GetTypeDefinition(root);
            var names = ReadNames(pe, reader, definition);
            foreach (var owner in new[] { root }.Concat(definition.GetNestedTypes().Where(handle =>
                reader.GetString(reader.GetTypeDefinition(handle).Name) is "Core" or "Ext" or "Arb")))
                ReadMethods(pe, reader, reader.GetTypeDefinition(owner), names, selected, result,
                    rootNamespace + ".GL" + (owner == root ? "" : "." + reader.GetString(reader.GetTypeDefinition(owner).Name)), requested, driverEntries);
        }
        return result;
    }
    #endregion

    #region Private
    /// <summary>Recovers explicit name-array assignments and rejects layouts whose indices cannot be proven.</summary>
    private static string[] ReadNames(PEReader pe, MetadataReader reader, TypeDefinition root)
    {
        var initializer = root.GetMethods().SingleOrDefault(handle => reader.GetString(reader.GetMethodDefinition(handle).Name) == ".cctor");
        if (initializer.IsNil) throw new FormatException("OpenTK GL has no binding-name initializer.");
        var instructions = Instructions(pe, reader.GetMethodDefinition(initializer));
        var entries = new Dictionary<int, string>();
        for (int i = 2; i < instructions.Count; i++)
        {
            var current = instructions[i];
            if (current.Code == OpCodes.Ldstr && instructions[i - 2].Code == OpCodes.Dup
                && BindingInstructions.IsInteger(instructions[i - 1].Code)
                && i + 1 < instructions.Count && instructions[i + 1].Code == OpCodes.Stelem_Ref)
                entries.Add(instructions[i - 1].Value, reader.GetUserString(MetadataTokens.UserStringHandle(current.Value & 0x00ffffff)));
            if (current.Code == OpCodes.Stsfld && FieldName(reader, current.Value) == "EntryPointNames")
            {
                if (entries.Count == 0 || entries.Keys.Min() != 0 || entries.Keys.Max() != entries.Count - 1
                    || entries.Values.Any(name => !name.StartsWith("gl", StringComparison.Ordinal))
                    || entries.Values.Distinct().Count() != entries.Count)
                    throw new FormatException("Unsupported OpenTK binding-name array layout.");
                return Enumerable.Range(0, entries.Count).Select(index => entries[index]).ToArray();
            }
        }
        throw new FormatException("OpenTK EntryPointNames initialization could not be resolved.");
    }

    /// <summary>Associates native table loads with calli signatures, independent of managed overload marshalling.</summary>
    private static void ReadMethods(PEReader pe, MetadataReader reader, TypeDefinition owner, string[] names, HashSet<string> selected,
        Dictionary<string, NativeSignature> signatures, string ownerName, HashSet<string> requested,
        Dictionary<string, (string EntryPoint, bool ScalarAddress)> driverEntries)
    {
        foreach (var handle in owner.GetMethods())
        {
            var method = reader.GetMethodDefinition(handle);
            if ((method.Attributes & MethodAttributes.MemberAccessMask) != MethodAttributes.Public
                || (method.Attributes & MethodAttributes.Static) == 0 || method.GetGenericParameters().Count != 0) continue;
            var instructions = Instructions(pe, method);
            var calls = instructions.Where(instruction => instruction.Code == OpCodes.Calli).ToArray();
            if (calls.Length == 0) continue;
            var slots = Enumerable.Range(0, Math.Max(0, instructions.Count - 1)).Where(index =>
                instructions[index].Code == OpCodes.Ldsfld && FieldName(reader, instructions[index].Value) == "EntryPoints"
                && BindingInstructions.IsInteger(instructions[index + 1].Code)).Select(index => instructions[index + 1].Value).ToArray();
            if (calls.Length != 1 || slots.Length != 1 || slots[0] < 0 || slots[0] >= names.Length)
                throw new FormatException("Unsupported OpenTK native dispatch layout: " + reader.GetString(method.Name));
            var managed = method.DecodeSignature(new ManagedSignatureProvider(), null);
            string bindingKey = ownerName + "." + reader.GetString(method.Name)
                + "(" + string.Join(",", managed.ParameterTypes) + ")";
            bool driverRequested = requested.Contains(bindingKey);
            if (!selected.Contains(names[slots[0]]) && !driverRequested) continue;
            var decoded = reader.GetStandaloneSignature((StandaloneSignatureHandle)MetadataTokens.Handle(calls[0].Value))
                .DecodeMethodSignature(new NativeAbiProvider(), null);
            if (decoded.Header.CallingConvention != SignatureCallingConvention.StdCall)
                throw new FormatException("Unsupported OpenTK native calling convention.");
            string name = names[slots[0]];
            var candidate = new NativeSignature(name, decoded.ReturnType, decoded.ParameterTypes.ToArray());
            if (signatures.TryGetValue(name, out var existing)
                && (existing.Result != candidate.Result || !existing.Parameters.SequenceEqual(candidate.Parameters)))
                throw new FormatException("Conflicting OpenTK native signatures: " + name);
            signatures[name] = candidate;
            if (driverRequested)
            {
                bool scalarAddress = decoded.ParameterTypes.Length == 2
                    && instructions.Any(instruction => instruction.Code == OpCodes.Ldc_I4_1)
                    && instructions.Any(instruction =>
                        (instruction.Code == OpCodes.Ldarga || instruction.Code == OpCodes.Ldarga_S) && instruction.Value == 0);
                if (driverEntries.TryGetValue(bindingKey, out var previous) && previous.EntryPoint != name)
                    throw new FormatException("Conflicting OpenTK overload entrypoints: " + bindingKey);
                driverEntries[bindingKey] = (name, scalarAddress || previous.ScalarAddress);
            }
        }
    }

    /// <summary>Reads a method body directly from its PE RVA, including convenience overloads.</summary>
    private static List<(OpCode Code, int Value)> Instructions(PEReader pe, MethodDefinition method)
        => method.RelativeVirtualAddress == 0 ? new() : BindingInstructions.Read(pe.GetMethodBody(method.RelativeVirtualAddress).GetILBytes()!);

    /// <summary>Resolves field tokens without using runtime reflection or initializing OpenTK.</summary>
    private static string FieldName(MetadataReader reader, int token)
    {
        var handle = MetadataTokens.Handle(token);
        return handle.Kind switch
        {
            HandleKind.FieldDefinition => reader.GetString(reader.GetFieldDefinition((FieldDefinitionHandle)handle).Name),
            HandleKind.MemberReference => reader.GetString(reader.GetMemberReference((MemberReferenceHandle)handle).Name),
            _ => throw new FormatException("Invalid OpenTK native table field token.")
        };
    }
    #endregion
}
