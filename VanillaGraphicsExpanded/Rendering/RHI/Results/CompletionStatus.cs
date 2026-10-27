namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Distinguishes actual GPU completion from CPU submission and terminal failure.</summary>
public enum CompletionStatus { Pending, Ready, Failed, Cancelled }
