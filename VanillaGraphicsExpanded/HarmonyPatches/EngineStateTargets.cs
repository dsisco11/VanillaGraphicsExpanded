using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace VanillaGraphicsExpanded.HarmonyPatches;

/// <summary>Discovers supported native calls in built-in game assemblies without resolving unrelated IL dependencies.</summary>
internal static class EngineStateTargets
{
    private static readonly Dictionary<short, OpCode> opcodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.FieldType == typeof(OpCode)).Select(field => (OpCode)field.GetValue(null)!)
        .ToDictionary(opcode => opcode.Value);

    #region Public API
    /// <summary>Limits rewriting to the engine and loaded built-in content assemblies, never arbitrary mods.</summary>
    internal static IEnumerable<Assembly> Assemblies() => AppDomain.CurrentDomain.GetAssemblies()
        .Where(assembly => assembly.GetName().Name is "VintagestoryLib" or "VSEssentials" or "VSSurvivalMod" or "VSCreativeMod");

    /// <summary>Resolves only concrete methods whose bytecode calls an exact mapped native operation.</summary>
    internal static IEnumerable<MethodBase> Discover(Assembly assembly)
    {
        using var stream = File.OpenRead(assembly.Location);
        using var image = new PEReader(stream);
        var metadata = image.GetMetadataReader();
        var tokens = SupportedTokens(assembly.ManifestModule, metadata);
        if (tokens.Count == 0) yield break;

        foreach (var handle in metadata.MethodDefinitions)
        {
            var definition = metadata.GetMethodDefinition(handle);
            if (definition.RelativeVirtualAddress == 0) continue;
            var body = image.GetMethodBody(definition.RelativeVirtualAddress);
            if (!CallsSupportedOperation(body.GetILReader(), tokens)) continue;

            var method = assembly.ManifestModule.ResolveMethod(MetadataTokens.GetToken(handle))!;
            if (method.ContainsGenericParameters)
                throw new NotSupportedException($"Cannot safely patch generic engine state caller: {method}.");
            yield return method;
        }
    }
    #endregion

    #region Private
    /// <summary>Resolves GL member references only, leaving unrelated optional engine dependencies unloaded.</summary>
    private static HashSet<int> SupportedTokens(Module module, MetadataReader metadata)
    {
        var result = new HashSet<int>();
        foreach (var handle in metadata.MemberReferences)
        {
            var member = metadata.GetMemberReference(handle);
            if (member.Parent.Kind != HandleKind.TypeReference || member.GetKind() != MemberReferenceKind.Method) continue;
            var type = metadata.GetTypeReference((TypeReferenceHandle)member.Parent);
            if (metadata.GetString(type.Namespace) != "OpenTK.Graphics.OpenGL" || metadata.GetString(type.Name) != "GL") continue;
            int token = MetadataTokens.GetToken(handle);
            if (module.ResolveMethod(token) is MethodInfo native && EngineStateCallMap.Replacements.ContainsKey(native))
                result.Add(token);
        }
        return result;
    }

    /// <summary>Walks instruction boundaries so bytes inside operands cannot be mistaken for call opcodes.</summary>
    private static bool CallsSupportedOperation(BlobReader reader, HashSet<int> tokens)
    {
        while (reader.RemainingBytes > 0)
        {
            int first = reader.ReadByte();
            short value = first == 0xfe ? unchecked((short)(0xfe00 | reader.ReadByte())) : (short)first;
            var opcode = opcodes[value];
            if (opcode == OpCodes.Call)
            {
                if (tokens.Contains(reader.ReadInt32())) return true;
                continue;
            }

            // Switch tables contain a count followed by that many relative branch offsets.
            int size = opcode.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => checked(reader.ReadInt32() * 4),
                OperandType.InlineBrTarget or OperandType.InlineField or OperandType.InlineI
                    or OperandType.InlineMethod or OperandType.InlineSig or OperandType.InlineString
                    or OperandType.InlineTok or OperandType.InlineType or OperandType.ShortInlineR => 4,
                _ => throw new BadImageFormatException($"Unsupported IL operand kind: {opcode.OperandType}.")
            };
            if (size < 0 || size > reader.RemainingBytes) throw new BadImageFormatException("Truncated engine IL operand.");
            reader.Offset += size;
        }
        return false;
    }
    #endregion
}
