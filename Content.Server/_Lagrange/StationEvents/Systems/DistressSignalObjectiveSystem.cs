using Content.Server.GameTicking.Rules.Components;
using Content.Server.StationEvents.Components;
using Content.Server.StationEvents.Events;

namespace Content.Server.StationEvents.Systems;
public abstract class DistressSignalObjectiveSystem : EntitySystem
{
    [Dependency] protected readonly DistressSignalRule _ruleSystem = default!;
    public override void Update(float frameTime)
    {
        var objectiveQuery = EntityQuery<DistressSignalInjuredComponent>();
        foreach (var objective in objectiveQuery)
        {
            // The definition of "failed" in this context is "no longer possible to complete".
            if (objective.Failed)
                continue;

            // Register the objective with a specific distress signal.
            if (objective.DistressSignalRuleComponent is null)
            {
                if (!TryComp<TransformComponent>(objective.Owner, out var transform))
                {
                    Log.Error($"Distress signal objective is attached to '{objective.Owner.Id}', which lacks a transform component.");
                    objective.Failed = true;
                    continue;
                }

                if (transform.GridUid == null)
                {
                    Log.Error($"Distress signal objective is attached to '{objective.Owner.Id}', which is not located on any grid.");
                    objective.Failed = true;
                    continue;
                }

                var ruleQuery = AllEntityQuery<DistressSignalRuleComponent>();
                while (ruleQuery.MoveNext(out var ruleComponent))
                {
                    if (ruleComponent.GridUid == transform.GridUid)
                    {
                        _ruleSystem.AddObjective(objective, ruleComponent);
                        break;
                    }
                }

                if (objective.DistressSignalRuleComponent is null)
                {
                    Log.Error($"Distress signal objective attached to '{objective.Owner.Id}' could not find a matching distress signal.");
                    objective.Failed = true;
                    continue;
                }
            }

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
