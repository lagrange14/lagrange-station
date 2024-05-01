using Content.Server.GameTicking.Rules.Components;
using Content.Server.StationEvents.Components;
using Content.Server.StationEvents.Events;

namespace Content.Server.StationEvents.Systems;
public abstract class DistressSignalObjectiveSystem : EntitySystem
{
    public override void Update(float frameTime)
    {
        var objectiveQuery = EntityQuery<DistressSignalInjuredComponent>();
        foreach (var objective in objectiveQuery)
        {
            if (objective.Failed)
                continue;

            if (Failed(objective.Owner))
            {
                objective.Failed = true;
                continue;
            }

            if (Completed(objective.Owner))
            {
                objective.Completed = true;
                continue;
            }
        }
    }

    /// <summary>
    /// Checks whether this objective can no longer be completed.
    /// </summary>
    public abstract bool Failed(EntityUid uid);

    /// <summary>
    /// Checks whether this objective has been completed.
    /// </summary>
    public abstract bool Completed(EntityUid uid);
}
