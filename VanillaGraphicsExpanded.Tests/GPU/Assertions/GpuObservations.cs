namespace VanillaGraphicsExpanded.Tests.GPU.Assertions;

/// <summary>Composes explicit fixture observation services without owning subject resources or dispatching assertion modes.</summary>
internal sealed class GpuObservations
{
    #region Internal API
    /// <summary>Provides whole-domain expected-value comparison operations.</summary>
    internal GpuImageComparisons Comparisons { get; }
    /// <summary>Provides finite domain facts, counts, rank coverage and numerical moments.</summary>
    internal GpuImageStatistics Statistics { get; }
    /// <summary>Provides independently authored domain predicate operations.</summary>
    internal GpuImagePredicates Predicates { get; }
    /// <summary>Provides deterministic original-texel selection operations.</summary>
    internal GpuImageSelection Selection { get; }
    /// <summary>Provides independently authored history/input projections.</summary>
    internal GpuImageTransform Projections { get; }
    /// <summary>Retains production executable ownership in the fixture rather than in observation consumers.</summary>
    internal GpuObservationPrograms Programs { get; }

    /// <summary>Connects services to the fixture's already selected context and explicit executable owner.</summary>
    internal GpuObservations(GpuObservationPrograms programs)
    {
        Programs = programs;
        Projections = new(programs);
        Comparisons = new(programs);
        Statistics = new(programs, Projections);
        Predicates = new(programs);
        Selection = new(programs, Statistics, Predicates, Projections);
    }
    #endregion
}
