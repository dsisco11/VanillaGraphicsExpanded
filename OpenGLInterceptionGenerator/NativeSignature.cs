namespace OpenGLInterceptionGenerator;

/// <summary>Describes a selected native ABI independently of its handwritten observer.</summary>
internal sealed class NativeSignature
{
    internal string Name { get; }
    internal string Result { get; }
    internal string[] Parameters { get; }

    #region Internal API
    /// <summary>Retains the selected entrypoint's audited native signature.</summary>
    internal NativeSignature(string name, string result, string[] parameters)
    {
        Name = name;
        Result = result;
        Parameters = parameters;
    }

    #endregion
}
