namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Distinguishes actual GPU completion from CPU submission and terminal failure.</summary>
public enum RHICompletionStatus { Pending, Ready, Failed, Cancelled }
