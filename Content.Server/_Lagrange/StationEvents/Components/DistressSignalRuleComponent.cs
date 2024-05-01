using Content.Server.StationEvents.Events;
using Content.Shared.Storage;

namespace Content.Server.StationEvents.Components;

[RegisterComponent, Access(typeof(DistressSignalRule))]
public sealed partial class DistressSignalRuleComponent : Component
{
    /// <summary>
    /// Path to the grid that gets bluspaced in
    /// </summary>
    [DataField("mapList")]
    public string[] MapList = [];

    /// <summary>
    /// The color of your thing. the name should be set by the mapper when mapping.
    /// </summary>
    [DataField("color")]
    public Color Color = new Color(0, 127, 255);

    /// <summary>
    /// The grid in question, set after starting the event
    /// </summary>
    [DataField("gridUid")]
    public EntityUid? GridUid = null;
}
