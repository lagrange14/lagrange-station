using Content.Server.GameTicking.Rules.Components;
using Content.Server.StationEvents.Events;

namespace Content.Server.StationEvents.Components;

[RegisterComponent]
public partial class DistressSignalObjectiveComponent : Component
{
    [DataField("distressSignalRuleComponent")]
    public DistressSignalRuleComponent DistressSignalRuleComponent = default!;

    [DataField("distressSignalRule")]
    public DistressSignalRule DistressSignalRule = default!;

    [DataField("gameRuleComponent")]
    public GameRuleComponent GameRule = default!;

    /// <summary>
    /// If true, failure of this objective ends the event immediately.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("critical")]
    public bool Critical = false;

    /// <summary>
    /// If true, this objective's conditions have been met.
    /// </summary>
    [DataField("completed")]
    public bool Completed = false;

    /// <summary>
    /// If true, this objective can no longer be completed.
    /// </summary>
    [DataField("failed")]
    public bool Failed = false;
}
