using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Rendering.Spirv;

/// <summary>Reads the selected entry point's built-in output extent from compiled SPIR-V, independently of debug names.</summary>
internal static class CompiledClipDistance
{
    /// <summary>SPIR-V instructions used for output type, pointer and specialization inspection.</summary>
    private enum Op : ushort { EntryPoint = 15, TypeInt = 21, TypeFloat = 22, TypeArray = 28, TypeStruct = 30, TypePointer = 32,
        Constant = 43, SpecConstant = 50, SpecConstantOp = 52, Function = 54, FunctionEnd = 56,
        FunctionCall = 57, Variable = 59, Store = 62, CopyMemory = 63,
        AccessChain = 65, InBoundsAccessChain = 66, Decorate = 71, MemberDecorate = 72, CopyObject = 83 }
    /// <summary>Supported integer specialization operations from the SPIR-V core grammar.</summary>
    private enum IntegerOp : uint { IAdd = 128, ISub = 130, IMul = 132, UDiv = 134, SDiv = 135,
        UMod = 137, SRem = 138, SMod = 139, ShiftRightLogical = 194, ShiftRightArithmetic = 195,
        ShiftLeftLogical = 196, BitwiseOr = 197, BitwiseXor = 198, BitwiseAnd = 199 }
    private const uint BuiltIn = 11, SpecId = 1, ClipDistance = 3, Output = 3;

    #region Public API
    /// <summary>Returns zero for absent outputs and null for an extent that cannot be verified safely.</summary>
    internal static int? Read(ShaderStageSelection selection, ReadOnlySpan<byte> binary)
    {
        if (binary.Length < 20 || binary.Length % 4 != 0) return null;
        uint[] words = MemoryMarshal.Cast<byte, uint>(binary).ToArray();
        if (words[0] != 0x07230203) return null;
        var definitions = new Dictionary<uint, (Op Code, uint[] Args)>();
        var builtins = new HashSet<uint>();
        var members = new HashSet<(uint Type, uint Member)>();
        var specializations = new Dictionary<uint, uint>();
        var writes = new List<(uint Pointer, uint Function)>();
        var calls = new Dictionary<uint, List<uint>>();
        uint currentFunction = 0, entryFunction = 0;
        HashSet<uint>? interfaces = null;
        for (int offset = 5; offset < words.Length;)
        {
            int count = (int)(words[offset] >> 16);
            if (count < 1 || count > words.Length - offset) return null;
            var code = (Op)(words[offset] & 0xffff);
            uint[] a = words.AsSpan(offset + 1, count - 1).ToArray();
            if (code == Op.EntryPoint && a.Length >= 3)
            {
                ReadOnlySpan<byte> nameBytes = MemoryMarshal.AsBytes(a.AsSpan(2));
                int end = nameBytes.IndexOf((byte)0);
                if (end < 0) return null;
                if (Encoding.UTF8.GetString(nameBytes[..end]) == selection.Stage.EntryPoint)
                {
                    if (interfaces != null || a[0] != ExecutionModel(selection.Stage.Kind)) return null;
                    interfaces = a.Skip(2 + (end + 4) / 4).ToHashSet();
                    entryFunction = a[1];
                }
            }
            else if (code == Op.Decorate && a.Length >= 3)
            {
                if (a[1] == BuiltIn && a[2] == ClipDistance) builtins.Add(a[0]);
                if (a[1] == SpecId) specializations[a[0]] = a[2];
            }
            else if (code == Op.MemberDecorate && a.Length >= 4 && a[2] == BuiltIn && a[3] == ClipDistance)
                members.Add((a[0], a[1]));
            else if (code is Op.TypeInt or Op.TypeFloat or Op.TypeArray or Op.TypeStruct or Op.TypePointer)
            {
                if (a.Length < 1) return null;
                definitions[a[0]] = (code, a);
            }
            else if (code is Op.Constant or Op.SpecConstant or Op.SpecConstantOp or Op.Variable
                or Op.AccessChain or Op.InBoundsAccessChain or Op.CopyObject)
            {
                if (a.Length < 2) return null;
                definitions[a[1]] = (code, a);
            }
            else if (code == Op.Function && a.Length >= 2)
            {
                currentFunction = a[1];
                calls[currentFunction] = [];
            }
            else if (code == Op.FunctionEnd) currentFunction = 0;
            else if (code == Op.FunctionCall && a.Length >= 3 && calls.TryGetValue(currentFunction, out var targets))
                targets.Add(a[2]);
            else if (code is Op.Store or Op.CopyMemory && a.Length >= 2) writes.Add((a[0], currentFunction));
            offset += count;
        }
        if (interfaces == null) return null;
        var reachable = new HashSet<uint>();
        var pending = new Stack<uint>();
        pending.Push(entryFunction);
        while (pending.TryPop(out uint function))
            if (reachable.Add(function) && calls.TryGetValue(function, out var targets))
                foreach (uint target in targets) pending.Push(target);
        int extent = 0;
        foreach (uint variable in interfaces)
        {
            if (!definitions.TryGetValue(variable, out var v) || v.Code != Op.Variable || v.Args.Length < 3) continue;
            if (v.Args[2] != Output) continue;
            if (!definitions.TryGetValue(v.Args[0], out var pointer) || pointer.Code != Op.TypePointer || pointer.Args.Length != 3) return null;
            uint type = pointer.Args[2];
            if (builtins.Contains(variable) && Written(variable, null))
            {
                var size = ArrayExtent(type);
                if (size == null || extent != 0) return null;
                extent = size.Value;
            }
            foreach (var member in members.Where(m => m.Type == type))
            {
                if (!Written(variable, member.Member)) continue;
                if (!definitions.TryGetValue(type, out var structure) || structure.Code != Op.TypeStruct
                    || member.Member >= structure.Args.Length - 1) return null;
                var size = ArrayExtent(structure.Args[member.Member + 1]);
                if (size == null || extent != 0) return null;
                extent = size.Value;
            }
        }
        return extent;

        /// <summary>Excludes implicit unused block members while retaining any statically addressed output write.</summary>
        bool Written(uint variable, uint? member)
        {
            foreach (var write in writes)
            {
                if (!reachable.Contains(write.Function)) continue;
                var indices = new List<uint>();
                var visited = new HashSet<uint>();
                uint pointer = write.Pointer;
                while (pointer != variable && visited.Add(pointer) && definitions.TryGetValue(pointer, out var definition))
                {
                    if (definition.Code is Op.AccessChain or Op.InBoundsAccessChain && definition.Args.Length >= 3)
                    {
                        // Nested chains add indices before the already resolved suffix.
                        indices.InsertRange(0, definition.Args.Skip(3));
                        pointer = definition.Args[2];
                    }
                    else if (definition.Code == Op.CopyObject && definition.Args.Length == 3) pointer = definition.Args[2];
                    else break;
                }
                if (pointer != variable) continue;
                // An aggregate store writes every member; an array-element store writes that output.
                if (member == null || indices.Count == 0 || Constant(indices[0], new HashSet<uint>()) == member) return true;
            }
            return false;
        }

        /// <summary>Accepts only a float array as evidence of the built-in; unknown expression forms fail closed.</summary>
        int? ArrayExtent(uint type)
        {
            if (!definitions.TryGetValue(type, out var array) || array.Code != Op.TypeArray || array.Args.Length != 3
                || !definitions.TryGetValue(array.Args[1], out var scalar) || scalar.Code != Op.TypeFloat
                || scalar.Args.Length != 2 || scalar.Args[1] != 32) return null;
            uint? size = Constant(array.Args[2], new HashSet<uint>());
            return size is > 0 and <= 32 ? (int)size.Value : null;
        }
        /// <summary>Evaluates the selected 32-bit integer specialization DAG with cycle detection.</summary>
        uint? Constant(uint id, HashSet<uint> path)
        {
            if (!path.Add(id) || !definitions.TryGetValue(id, out var value)) return null;
            try
            {
                if (value.Args.Length < 3 || !definitions.TryGetValue(value.Args[0], out var integer)
                    || integer.Code != Op.TypeInt || integer.Args.Length != 3 || integer.Args[1] != 32) return null;
                if (value.Code == Op.SpecConstant && specializations.TryGetValue(id, out uint spec))
                    foreach (var argument in selection.Specializations)
                        if (argument.Id == spec) return argument.Value.Bits;
                if (value.Code is Op.Constant or Op.SpecConstant && value.Args.Length == 3) return value.Args[2];
                if (value.Code != Op.SpecConstantOp || value.Args.Length != 5) return null;
                uint? left = Constant(value.Args[3], path), right = Constant(value.Args[4], path);
                if (left == null || right == null) return null;
                // Integer arithmetic commonly emitted for specialization-dependent array bounds.
                return (IntegerOp)value.Args[2] switch
                {
                    IntegerOp.IAdd => unchecked(left.Value + right.Value), IntegerOp.ISub => unchecked(left.Value - right.Value),
                    IntegerOp.IMul => unchecked(left.Value * right.Value), IntegerOp.UDiv when right != 0 => left.Value / right.Value,
                    IntegerOp.SDiv when right != 0 && !(left == 0x80000000 && right == uint.MaxValue)
                        => unchecked((uint)((int)left.Value / (int)right.Value)),
                    IntegerOp.UMod when right != 0 => left.Value % right.Value,
                    IntegerOp.SRem when right != 0 => unchecked((uint)((long)(int)left.Value % (int)right.Value)),
                    IntegerOp.SMod when right != 0 => SignedModulo((int)left.Value, (int)right.Value),
                    IntegerOp.ShiftRightLogical when right < 32 => left.Value >> (int)right.Value,
                    IntegerOp.ShiftRightArithmetic when right < 32 => unchecked((uint)((int)left.Value >> (int)right.Value)),
                    IntegerOp.ShiftLeftLogical when right < 32 => left.Value << (int)right.Value,
                    IntegerOp.BitwiseOr => left.Value | right.Value,
                    IntegerOp.BitwiseXor => left.Value ^ right.Value,
                    IntegerOp.BitwiseAnd => left.Value & right.Value,
                    _ => null
                };
            }
            finally { path.Remove(id); }
        }
    }
    #endregion

    #region Private
    /// <summary>Implements SPIR-V signed modulo with the divisor's sign without signed overflow.</summary>
    private static uint SignedModulo(int left, int right)
    {
        long remainder = (long)left % right;
        return unchecked((uint)(remainder != 0 && (remainder < 0) != (right < 0) ? remainder + right : remainder));
    }
    /// <summary>Maps shader contract kinds to SPIR-V execution models rather than relying on enum ordering.</summary>
    private static uint ExecutionModel(ShaderStageKind stage) => stage switch
    {
        ShaderStageKind.Vertex => 0, ShaderStageKind.TessellationControl => 1,
        ShaderStageKind.TessellationEvaluation => 2, ShaderStageKind.Geometry => 3,
        ShaderStageKind.Fragment => 4, ShaderStageKind.Compute => 5,
        _ => uint.MaxValue
    };
    #endregion
}
