using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using VanillaGraphicsExpanded.Rendering.Shadows.Configuration;
using VanillaGraphicsExpanded.Rendering.Shadows.Lights;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Scheduling;

/// <summary>Stable bounded light arbitration with reserved opportunities, borrowing and complete-detail admission.</summary>
internal sealed class ShadowScheduler
{
    private readonly ShadowSchedulingSettings policy;
    private readonly Dictionary<ShadowSourceKey, History> history = new();
    private double previousTime = -1;
    private long previousFrame;
    private double timingScale = 1;

    /// <summary>Retains only selection history, never native geometry or an extension of temporal eligibility.</summary>
    private sealed class History
    {
        internal double Waiting, SelectedAt;
        internal bool Selected;
        internal int Detail;
        internal double LastEstimatedMilliseconds=0.1;
        internal bool RetentionEligible;
        internal double LastRefreshAt;
        internal bool Refreshed;
    }

    #region Public API
    /// <summary>Freezes persisted priority controls at a scheduling boundary.</summary>
    internal ShadowScheduler(ShadowSchedulingSettings settings) => policy = settings.Freeze();

    /// <summary>Consumes an already available old timing result; callers never wait for current-frame queries.</summary>
    internal void ObserveTiming(double estimated, double measured)
    {
        if (!double.IsFinite(estimated) || estimated <= 0 || !double.IsFinite(measured) || measured < 0)
            throw new ArgumentOutOfRangeException(nameof(measured));
        timingScale = Math.Clamp(timingScale * 0.875 + Math.Clamp(measured / estimated, 0.125, 8) * 0.125, 0.125, 8);
    }

    /// <summary>Orders preparation using stable admission history and prior estimates, without certifying current coverage.</summary>
    internal ImmutableArray<ShadowSourceKey> PrioritizeInputs(double now,IReadOnlyList<ShadowSourcePriority> inputs)
    {
        if(!double.IsFinite(now) || now<previousTime || inputs.Count>policy.LightCapacity)
            throw new ArgumentOutOfRangeException(nameof(now));
        var offers=inputs.Select(input=>
        {
            history.TryGetValue(input.Source,out var state);
            return new ShadowLightOffer(input.Source,input.Contribution,input.Carried,state?.RetentionEligible??false,
                [new(0,default,state?.LastEstimatedMilliseconds??0.1)],IncumbentReady:state?.RetentionEligible??false);
        }).ToArray();
        return ImmutableArray.CreateRange(offers.OrderByDescending(offer=>Held(offer,now)).ThenByDescending(offer=>Score(offer,now))
            .ThenBy(offer=>StableKey(offer.Source),StringComparer.Ordinal).Select(offer=>offer.Source));
    }

    /// <summary>Admits complete minimum-detail sets before refining; every attempt shares the supplied frame ledger.</summary>
    internal ImmutableArray<ShadowLightDecision> Select(long frame, double now, IReadOnlyList<ShadowLightOffer> offers,
        ShadowFrameBudget budget)
    {
        if (frame <= previousFrame || !double.IsFinite(now) || now < previousTime || offers.Count > policy.LightCapacity)
            throw new ArgumentOutOfRangeException(nameof(frame));
        var keys = new HashSet<ShadowSourceKey>();
        foreach (var offer in offers)
        {
            if (!offer.Source.IsKnown || !keys.Add(offer.Source) || !double.IsFinite(offer.Contribution) || offer.Contribution < 0 ||
                offer.Details.IsDefaultOrEmpty || offer.Details.Length > policy.MaximumDetailReductions + 1)
                throw new ArgumentException("Offers require unique stable sources and bounded complete alternatives.", nameof(offers));
            foreach (var detail in offer.Details)
                if (!double.IsFinite(detail.EstimatedMilliseconds) || detail.EstimatedMilliseconds < 0 ||
                    !detail.Cost.Fits(budget.Limit))
                {
                    // Oversized alternatives may be rejected, but negative costs are never meaningful.
                    if (detail.Cost.Counters().Any(value => value < 0) || !double.IsFinite(detail.EstimatedMilliseconds) || detail.EstimatedMilliseconds < 0)
                        throw new ArgumentException("Invalid detail cost.", nameof(offers));
                }
        }
        double elapsed = previousTime < 0 ? 0 : now - previousTime;
        previousTime = now; previousFrame = frame;
        foreach (var key in history.Keys.Where(key => !keys.Contains(key)).ToArray()) history.Remove(key);
        foreach (var offer in offers)
        {
            if (!history.TryGetValue(offer.Source, out var state)) history.Add(offer.Source, state = new(){LastRefreshAt=now});
            state.Waiting = state.Refreshed ? 0 : Math.Min(policy.MaximumWaitingSeconds, state.Waiting + elapsed);
        }
        var sorted = offers.OrderByDescending(offer=>Held(offer,now)).ThenByDescending(offer => Score(offer, now))
            .ThenBy(offer => StableKey(offer.Source), StringComparer.Ordinal).ToArray();
        var chosen = new Dictionary<ShadowSourceKey, int>();
        var retainedOnly=new HashSet<ShadowSourceKey>();
        int localCount = 0;
        double milliseconds = policy.PreparationEstimateMilliseconds*timingScale, directionalMs = 0, localMs = 0;
        long directionalBytes = 0, localBytes = 0;
        long directionalTiles=0,localTiles=0;
        // Reserve each category's unused share first. Each celestial source is visited independently.
        foreach (bool directional in new[] { true, false })
            foreach (var offer in sorted.Where(offer => Directional(offer.Source) == directional)) Admit(offer, true);
        // Borrow only after both categories had their reserved opportunity. Fixed allocations remain charged.
        foreach (var offer in sorted) if (!chosen.ContainsKey(offer.Source)) Admit(offer, false);
        // Refresh pressure cannot erase an affordable read-only retained result.
        // Its actual coverage and finite temporal eligibility are still checked on GPU.
        foreach(var offer in sorted)
        {
            if(chosen.ContainsKey(offer.Source) || offer.Contribution==0 || !offer.RetentionEligible || offer.RetainedCost is not { } cost ||
                (!Directional(offer.Source) && localCount>=policy.MaxShadowedLocalLights))continue;
            if(!budget.TryCharge(new(Retries:1)) || !budget.TryCharge(cost))continue;
            chosen.Add(offer.Source,0);retainedOnly.Add(offer.Source);
            if(!Directional(offer.Source))localCount++;
        }
        // Refinement reuses a complete selected light, charging the incremental producer cost only.
        foreach (var offer in sorted)
        {
            if (!chosen.TryGetValue(offer.Source, out int current) || retainedOnly.Contains(offer.Source)) continue;
            for (int next = current + 1; next < offer.Details.Length; next++)
            {
                if (!budget.TryCharge(new(Retries: 1))) break;
                var a = offer.Details[current]; var b = offer.Details[next];
                var delta = b.Cost.IncreaseOver(a.Cost);
                double time = Math.Max(0,b.EstimatedMilliseconds - a.EstimatedMilliseconds) * timingScale;
                if (milliseconds + time > policy.GpuTargetMilliseconds || !budget.TryCharge(delta)) break;
                milliseconds += time; current = next; chosen[offer.Source] = next;
            }
        }
        var result = ImmutableArray.CreateBuilder<ShadowLightDecision>(offers.Count);
        foreach (var offer in offers)
        {
            var state = history[offer.Source];
            bool selected = chosen.TryGetValue(offer.Source, out int index);
            if (selected && !state.Selected) state.SelectedAt = now;
            state.Selected = selected;
            state.Detail = selected ? offer.Details[index].Detail : -1;
            state.RetentionEligible=offer.IncumbentReady;
            if(selected)state.LastEstimatedMilliseconds=offer.Details[index].EstimatedMilliseconds;
            bool refresh=!retainedOnly.Contains(offer.Source);
            result.Add(new(offer.Source, state.Detail, selected, selected ? refresh?"Admitted":"RetainedRefreshDeferred" : "BudgetOrLocalLimit",refresh,
                state.Waiting,Math.Max(0,now-state.LastRefreshAt)));
            state.Refreshed=selected&&refresh;
            if(state.Refreshed)state.LastRefreshAt=now;
        }
        return result.MoveToImmutable();

        /// <summary>Reserves a minimum complete set atomically against category and aggregate limits.</summary>
        void Admit(ShadowLightOffer offer, bool reserved)
        {
            bool directional = Directional(offer.Source);
            if (offer.Contribution == 0 || (!directional && localCount >= policy.MaxShadowedLocalLights)) return;
            var detail = offer.Details[0];
            double ms = detail.EstimatedMilliseconds * timingScale;
            double share = directional ? policy.DirectionalWorkShare : 1 - policy.DirectionalWorkShare;
            double usedMs = directional ? directionalMs : localMs;
            long usedBytes = directional ? directionalBytes : localBytes;
            long usedTiles=directional?directionalTiles:localTiles;
            double memoryShare = directional ? policy.DirectionalMemoryShare : 1 - policy.DirectionalMemoryShare;
            if (reserved && (usedMs + ms > policy.GpuTargetMilliseconds * share ||
                usedBytes + detail.Cost.Bytes > budget.Limit.Bytes * memoryShare ||
                usedTiles+detail.Cost.Tiles>budget.Limit.Tiles*memoryShare)) return;
            if (!budget.TryCharge(new(Retries: 1))) return;
            if (milliseconds + ms > policy.GpuTargetMilliseconds || !budget.TryCharge(detail.Cost)) return;
            chosen.Add(offer.Source, 0); milliseconds += ms;
            if (directional) { directionalMs += ms; directionalBytes += detail.Cost.Bytes;directionalTiles+=detail.Cost.Tiles; }
            else { localMs += ms; localBytes += detail.Cost.Bytes;localTiles+=detail.Cost.Tiles;localCount++; }
        }
    }
    #endregion

    #region Private
    /// <summary>Combines visible contribution/cost, carried preference and bounded waiting with valid incumbent protection.</summary>
    private double Score(ShadowLightOffer offer, double now)
    {
        var state = history.TryGetValue(offer.Source,out var previous)?previous:new History();
        double score = Math.Clamp(offer.Contribution / Math.Max(0.001, offer.Details[0].EstimatedMilliseconds * timingScale),0,1e200);
        if (offer.Carried) score *= policy.CarriedPreference;
        score *= 1 + state.Waiting / policy.MaximumWaitingSeconds;
        if (state.Selected && offer.IncumbentReady)
            score *= 1 + policy.ChallengerMargin;
        return score;
    }

    /// <summary>Protects valid incumbents by ordering, rather than an arbitrary finite score multiplier.</summary>
    private bool Held(ShadowLightOffer offer,double now)=>history.TryGetValue(offer.Source,out var state) && state.Selected &&
        offer.IncumbentReady && now-state.SelectedAt<policy.MinimumHoldSeconds;

    /// <summary>Recognizes independent celestial reservations without conflating their readiness.</summary>
    private static bool Directional(ShadowSourceKey source) => source.Kind is ShadowSourceKind.Sun or ShadowSourceKind.Moon;

    /// <summary>Uses complete stable identity for deterministic ties independent of frame-list order.</summary>
    private static string StableKey(ShadowSourceKey source) =>
        $"{source.World:X16}:{source.Dimension:X8}:{(int)source.Kind:X8}:{source.Owner:X16}:{source.Lifetime:X16}:{source.Slot:X8}:{source.X:X8}:{source.Y:X8}:{source.Z:X8}";

    #endregion
}
