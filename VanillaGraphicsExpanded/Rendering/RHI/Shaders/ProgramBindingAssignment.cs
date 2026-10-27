namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Describes one authored external executable slot assignment, resolved against canonical reflection.</summary>
internal readonly record struct ProgramBindingAssignment(ProgramResourceKind Kind, string Name, int Slot, bool Required);
