using Content.Server.StationEvents.Events;
using Content.Shared.Storage;

namespace Content.Server.StationEvents.Components;

[RegisterComponent, Access(typeof(DistressSignalRule))]
public sealed partial class DistressSignalRuleComponent : Component
{
    /// <summary>
    /// List of vessels in distress that may be spawned.
    /// </summary>
    [DataField("mapList")]
    public string[] MapList = [];

    /// <summary>
    /// ID of the selected map.
    /// </summary>
    [DataField("mapId")]
    public string? MapId = default!;

    /// <summary>
    /// List of objectives registered with this distress signal.
    /// </summary>
    [ViewVariables]
    public List<DistressSignalObjectiveComponent?> Objectives { get; } = new();

    /// <summary>
    /// Whether or not the distress signal's objectives have been completed successfully.
    /// </summary>
    [DataField("objectivesCompleted")]
    public bool ObjectivesCompleted = false;

    /// <summary>
    /// Randomly-generated name of the distressed vessel.
    /// </summary>
    [DataField("designation")]
    public string? Designation = default!;

    /// <summary>
    /// The color of your thing. the name should be set by the mapper when mapping.
    /// </summary>
    [DataField("color")]
    public Color Color = new Color(0, 127, 255);

    /// <summary>
    /// The grid in question, set after starting the event.
    /// </summary>
    [DataField("gridUid")]
    public EntityUid? GridUid = default!;

    /// <summary>
    /// Whether or not the component is in a debounce state while it waits for objectives to *stay* completed.
    /// </summary>
    [DataField("timerRunning")]
    public bool TimerRunning = false;
}
