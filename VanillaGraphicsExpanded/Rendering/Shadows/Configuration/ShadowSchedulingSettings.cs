using System;
using System.Numerics;
using Newtonsoft.Json;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Configuration;

/// <summary>Persisted whole-frame admission, priority and recovery controls; defaults are provisional estimates.</summary>
[JsonObject(MemberSerialization.OptIn)]
public sealed class ShadowSchedulingSettings
{
    [JsonProperty] public bool Enabled { get; set; }
    [JsonProperty] public int MemoryMiB { get; set; } = 256;
    [JsonProperty] public double GpuTargetMilliseconds { get; set; } = 2;
    [JsonProperty] public int MaxShadowedLocalLights { get; set; } = 8;
    [JsonProperty] public double DirectionalMemoryShare { get; set; } = 0.5;
    [JsonProperty] public double DirectionalWorkShare { get; set; } = 0.5;
    [JsonProperty] public string Quality { get; set; } = "Balanced";
    [JsonProperty] public double DirectionalCoverageDegrees { get; set; } = 1;
    [JsonProperty] public Vector3 PointCoverageHalfExtentMetres { get; set; } = new(2);
    [JsonProperty] public double ChallengerMargin { get; set; } = 0.2;
    [JsonProperty] public double MinimumHoldSeconds { get; set; } = 0.5;
    [JsonProperty] public int RecoveryCompleteFrames { get; set; } = 8;
    [JsonProperty] public double MaximumWaitingSeconds { get; set; } = 2;
    [JsonProperty] public double CarriedPreference { get; set; } = 2;
    [JsonProperty] public int DemandRequests { get; set; } = 8192;
    [JsonProperty] public int ReceiverVisits { get; set; } = 65536;
    [JsonProperty] public int InputVisits { get; set; } = 4194304;
    [JsonProperty] public int CoverageChecks { get; set; } = 16384;
    [JsonProperty] public int DrawCalls { get; set; } = 8192;
    [JsonProperty] public int Clears { get; set; } = 1024;
    [JsonProperty] public int Retries { get; set; } = 128;
    [JsonProperty] public int LightCapacity { get; set; } = 102;
    [JsonProperty] public int MaximumDetailReductions { get; set; } = 3;
    [JsonProperty] public double DirectionalTexelMetres { get; set; } = 0.05;
    [JsonProperty] public int PointFaceResolution { get; set; } = 2048;
    [JsonProperty] public int ProjectionHistoryCapacity { get; set; } = 256;
    [JsonProperty] public double PreparationEstimateMilliseconds { get; set; } = 0.1;
    [JsonProperty] public double LightEstimateMilliseconds { get; set; } = 0.05;
    [JsonProperty] public double ViewEstimateMilliseconds { get; set; } = 0.01;
    /// <summary>Reports restored requested controls without interpreting an invalid effective layout as supported.</summary>
    [JsonIgnore] public string? ValidationDiagnostic { get; private set; }

    #region Public API
    /// <summary>Restores unsupported values before a scheduling boundary freezes the policy.</summary>
    internal void Sanitize()
    {
        MemoryMiB = Positive(MemoryMiB, 4096, 256);
        GpuTargetMilliseconds = Number(GpuTargetMilliseconds, 0.001, 1000, 2);
        if(MaxShadowedLocalLights is <0 or >100){MarkInvalid();MaxShadowedLocalLights=8;}
        DirectionalMemoryShare = Number(DirectionalMemoryShare, 0, 1, 0.5);
        DirectionalWorkShare = Number(DirectionalWorkShare, 0, 1, 0.5);
        if(Quality!="Balanced"){MarkInvalid();Quality="Balanced";}
        DirectionalCoverageDegrees = Number(DirectionalCoverageDegrees, 0.0001, 179.999, 1);
        var extent=PointCoverageHalfExtentMetres;
        PointCoverageHalfExtentMetres=new((float)Number(extent.X,0.0001f,1024,2),(float)Number(extent.Y,0.0001f,1024,2),
            (float)Number(extent.Z,0.0001f,1024,2));
        ChallengerMargin = Number(ChallengerMargin, 0, 100, 0.2);
        MinimumHoldSeconds = Number(MinimumHoldSeconds, 0, 60, 0.5);
        MaximumWaitingSeconds = Number(MaximumWaitingSeconds, 0.001, 60, 2);
        CarriedPreference = Number(CarriedPreference, 1, 100, 2);
        RecoveryCompleteFrames = Positive(RecoveryCompleteFrames, 65536, 8);
        DemandRequests = Positive(DemandRequests, 1048576, 8192);
        ReceiverVisits = Positive(ReceiverVisits, 1048576, 65536);
        InputVisits=Positive(InputVisits,16777216,4194304);
        CoverageChecks = Positive(CoverageChecks, 1048576, 16384);
        DrawCalls = Positive(DrawCalls, 1048576, 8192);
        Clears = Positive(Clears, 65536, 1024);
        Retries = Positive(Retries, 65536, 128);
        LightCapacity = Positive(LightCapacity, 102, 102);
        MaximumDetailReductions = Positive(MaximumDetailReductions, 16, 3);
        DirectionalTexelMetres=Number(DirectionalTexelMetres,0.0001,16,0.05);
        PointFaceResolution=Positive(PointFaceResolution,16384,2048);
        if((PointFaceResolution&(PointFaceResolution-1))!=0){MarkInvalid();PointFaceResolution=2048;}
        ProjectionHistoryCapacity=Positive(ProjectionHistoryCapacity,8192,256);
        PreparationEstimateMilliseconds=Number(PreparationEstimateMilliseconds,0.001,1000,0.1);
        LightEstimateMilliseconds=Number(LightEstimateMilliseconds,0.001,1000,0.05);
        ViewEstimateMilliseconds=Number(ViewEstimateMilliseconds,0.001,1000,0.01);
    }

    /// <summary>Separates the active policy from mutable configuration.</summary>
    internal ShadowSchedulingSettings Freeze()
    { var copy = (ShadowSchedulingSettings)MemberwiseClone(); copy.Sanitize(); return copy; }
    #endregion

    #region Private
    /// <summary>Validates a finite bounded integer allowance.</summary>
    private int Positive(int value, int maximum, int fallback)
    {if(value>0 && value<=maximum)return value;MarkInvalid();return fallback;}
    /// <summary>Validates a finite scalar in its documented range.</summary>
    private double Number(double value, double minimum, double maximum, double fallback)
    {if(double.IsFinite(value) && value>=minimum && value<=maximum)return value;MarkInvalid();return fallback;}
    /// <summary>Retains a diagnostic when sanitization restores any malformed requested control.</summary>
    private void MarkInvalid()=>ValidationDiagnostic??="Invalid scheduling controls were restored to supported defaults.";
    #endregion
}
