using Microsoft.CodeAnalysis;
using System.Linq;

namespace OpenGLInterceptionGenerator;

/// <summary>Retains the compiler-selected OpenTK overload needed by one backend driver call.</summary>
internal sealed class DriverCall
{
    internal IMethodSymbol Method { get; }
    internal string Family { get; }
    internal Location Location { get; }
    internal string BindingKey => Method.ContainingType.ToDisplayString() + "." + Method.MetadataName
        + "(" + string.Join(",", Method.Parameters.Select(parameter => parameter.Type.ToDisplayString()
            + (parameter.RefKind == RefKind.None ? "" : "&"))) + ")";

    #region Internal API
    /// <summary>Captures the checked managed overload without maintaining another signature declaration.</summary>
    internal DriverCall(IMethodSymbol method, string family, Location location)
    {
        Method = method;
        Family = family;
        Location = location;
    }
    #endregion
}
