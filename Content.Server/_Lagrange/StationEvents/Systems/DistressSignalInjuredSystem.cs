using Content.Server.Cloning.Components;
using Content.Server.StationEvents.Components;
using Content.Server.StationEvents.Events;
using Content.Shared.Atmos.Rotting;
using Content.Shared.Cloning;
using Content.Shared.Damage;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;

namespace Content.Server.StationEvents.Systems;

[Access(typeof(DistressSignalObjectiveSystem))]
public sealed class DistressSignalInjuredSystem : DistressSignalObjectiveSystem
{
    public override void Initialize()
    {
        SubscribeLocalEvent<DistressSignalInjuredComponent, CloningEvent>(OnVictimCloned);
    }

    private void OnVictimCloned(EntityUid uid, DistressSignalInjuredComponent component, CloningEvent ev)
    {
        if (component.DistressSignalRuleComponent is null)
        {
            Log.Error($"Distress signal objective attached to '{uid}', which was cloned without a rule component set.");
            return;
        }

        _ruleSystem.AddObjective(component, component.DistressSignalRuleComponent);
    }

    public override bool Failed(EntityUid uid)
    {
        // As long as the body still exists, the patient can still be saved.
        // The real failure case is the event timer running out.
        return false;
    }

    public override bool Completed(EntityUid uid)
    {
        // The patient must be alive.
        if (!TryComp<MobStateComponent>(uid, out var mobState) || mobState.CurrentState != MobState.Alive)
        {
            return false;
        }

        // The patient must be uninjured.
        if (!TryComp<DamageableComponent>(uid, out var damage) || damage.Damage.Any())
        {
            return false;
        }

        // The patient must be returned to their shuttle (so they can be deleted when the event is over).
        if (
            !TryComp<TransformComponent>(uid, out var transform) ||
            !TryComp<DistressSignalInjuredComponent>(uid, out var objective) ||
            objective.DistressSignalRuleComponent is null ||
            transform.GridUid != objective.DistressSignalRuleComponent.GridUid
        )
        {
            return false;
        }

        return true;
    }
}
