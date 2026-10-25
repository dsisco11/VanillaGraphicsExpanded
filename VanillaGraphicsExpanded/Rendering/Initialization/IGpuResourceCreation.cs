namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Defines a compiler-checked resource-family backend for synchronous manager creation.</summary>
/// <remarks>Implementations consume a frozen value request and publish no wrapper snapshots.</remarks>
internal interface IGpuResourceCreation<TInitialization, TMetadata>
    where TInitialization : struct where TMetadata : struct
{
    /// <summary>Identifies the single native object owned by this creation operation.</summary>
    GpuResourceKind Kind { get; }

    /// <summary>Validates required inputs and conditional defaults before native allocation.</summary>
    void Validate(in TInitialization initialization);

    /// <summary>Dispatches through StateCache and returns established metadata, or a detected failure.</summary>
    /// <remarks>Assign the newly owned ID as soon as it is allocated, including on exceptional exit.
    /// Do not return borrowed dependencies in this output. Null does not detect silent native errors.</remarks>
    TMetadata? Initialize(StateCache cache, in TInitialization initialization, out nint id);
}
