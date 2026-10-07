using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Rendering.Spirv;

/// <summary>Validates compiler metadata against the exact captured binary before executable publication.</summary>
internal static class PackagedInterfaceValidation
{
    #region Public API
    /// <summary>Requires a matching binary, stage and structural identity before using compiler declarations.</summary>
    internal static PackagedStageInterface Resolve(ShaderBinaryDigest.Manifest? manifest,
        ShaderStageSelection selection, ReadOnlySpan<byte> binary)
    {
        string path = selection.BinaryPath;
        if (!ShaderBinaryDigest.TryRead(manifest, path, binary.Length, out byte[] digest) ||
            !SHA256.HashData(binary).AsSpan().SequenceEqual(digest))
            throw Failure(path, "missing, malformed or stale binary association");
        var metadata = manifest!.Binaries[path].Interface;
        if (metadata == null || metadata.Version != PackagedShaderInterface.CurrentVersion ||
            string.IsNullOrWhiteSpace(metadata.Extractor) || metadata.Stage != selection.Stage.Kind ||
            metadata.EntryPoint != selection.Stage.EntryPoint || metadata.StructuralKey != selection.Key ||
            metadata.Configuration is not ("Debug" or "Release") || metadata.Interface == null)
            throw Failure(path, "missing or incompatible interface schema/identity");

        var candidate = metadata.Interface;
        if (candidate.Inputs.IsDefault || candidate.Outputs.IsDefault || candidate.ExecutionModes.IsDefault)
            throw Failure(path, "malformed interface declarations");
        ValidateVariables(path, candidate);
        return candidate;
    }
    #endregion

    #region Private
    /// <summary>Rejects malformed numeric shapes before they can weaken linked interface validation.</summary>
    private static void ValidateVariables(string path, PackagedStageInterface candidate)
    {
        foreach (var variable in candidate.Inputs.Concat(candidate.Outputs))
        {
            if (variable == null || variable.Scalar is not (ShaderScalarType.Float or ShaderScalarType.Int or ShaderScalarType.UInt) ||
                variable.BitWidth is not (32 or 64) || variable.BitWidth == 64 && variable.Scalar != ShaderScalarType.Float ||
                variable.VectorSize is < 1 or > 4 || variable.Columns is < 1 or > 4 ||
                variable.Columns > 1 && variable.Scalar != ShaderScalarType.Float ||
                variable.Location > int.MaxValue || variable.Index > 1 || variable.Component > 3 ||
                variable.ArrayDimensions.IsDefault || variable.ArrayDimensions.Any(dimension => dimension == 0))
                throw Failure(path, "malformed numeric interface shape");
            // Bound arithmetic before declaration matching; malformed dimensions must not overflow
            // or manufacture coverage outside the native signed location range.
            long locations = variable.Columns;
            foreach (uint dimension in variable.ArrayDimensions)
            {
                if (locations > int.MaxValue / dimension) throw Failure(path, "numeric interface extent exceeds supported locations");
                locations *= dimension;
            }
            if (variable.Location + locations > (long)int.MaxValue + 1)
                throw Failure(path, "numeric interface location range exceeds supported locations");
        }
        foreach (var mode in candidate.ExecutionModes)
            if (mode == null || mode.Arguments.IsDefault || mode.Arguments.Length != 3 ||
                mode.ArgumentsAreIds && mode.Arguments.Any(argument => argument == 0))
                throw Failure(path, "malformed execution mode");
    }

    /// <summary>Identifies the asset requiring a coherent shader rebuild.</summary>
    private static InvalidDataException Failure(string path, string reason) =>
        new($"Shader interface metadata for '{path}' is invalid: {reason}. Rebuild and deploy matching shader assets.");
    #endregion
}
