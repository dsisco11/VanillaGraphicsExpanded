using System;

namespace VanillaGraphicsExpanded.DebugView;

public delegate IDisposable DebugViewRegisterRenderer(DebugViewActivationContext context);

public delegate DebugViewAvailability DebugViewAvailabilityProvider(DebugViewActivationContext context);

public delegate IDebugViewPanel? DebugViewPanelFactory(DebugViewActivationContext context);

public sealed class DebugViewDefinition
{
    public string Id { get; }

    public string Name { get; }

    public string Category { get; }

    public string Description { get; }

    public DebugViewActivationMode ActivationMode { get; }

    public DebugViewAvailabilityProvider? Availability { get; }

    public DebugViewRegisterRenderer RegisterRenderer { get; }

    public DebugViewPanelFactory? CreatePanel { get; }

    public DebugViewDefinition(
        string id,
        string name,
        string category,
        string description,
        DebugViewRegisterRenderer registerRenderer,
        DebugViewActivationMode activationMode = DebugViewActivationMode.Exclusive,
        DebugViewAvailabilityProvider? availability = null,
        DebugViewPanelFactory? createPanel = null)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Id must be non-empty.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name must be non-empty.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(category))
        {
            throw new ArgumentException("Category must be non-empty.", nameof(category));
        }

        Id = id;
        Name = name;
        Category = category;
        Description = description ?? string.Empty;
        RegisterRenderer = registerRenderer ?? throw new ArgumentNullException(nameof(registerRenderer));
        ActivationMode = activationMode;
        Availability = availability;
        CreatePanel = createPanel;
    }

    public DebugViewAvailability GetAvailability(DebugViewActivationContext context)
        => Availability?.Invoke(context) ?? DebugViewAvailability.Available();
}

