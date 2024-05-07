using Content.Server.Abilities;
using Content.Server.Administration.Logs;
using Content.Server.Atmos;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Body.Components;
using Content.Server.Chemistry.Containers.EntitySystems;
using Content.Server.Popups;
using Content.Shared.ActionBlocker;
using Content.Shared.Alert;
using Content.Shared.Atmos;
using Content.Shared.Body.Components;
using Content.Shared.Body.Events;
using Content.Shared.Damage;
using Content.Shared.Database;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Tag;
using JetBrains.Annotations;
using Content.Shared.IdentityManagement;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Physics.Components;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server.Body.Systems;

[UsedImplicitly]
public sealed class RespiratorSystem : EntitySystem
{
    [Dependency] private readonly IAdminLogManager _adminLogger = default!;
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly AlertsSystem _alertsSystem = default!;
    [Dependency] private readonly AtmosphereSystem _atmosSys = default!;
    [Dependency] private readonly BodySystem _bodySystem = default!;
    [Dependency] private readonly DamageableSystem _damageableSys = default!;
    [Dependency] private readonly LungSystem _lungSystem = default!;
    [Dependency] private readonly PopupSystem _popupSystem = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SolutionContainerSystem _solutionContainerSystem = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly ActionBlockerSystem _blocker = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly TagSystem _tag = default!;

    public override void Initialize()
    {
        base.Initialize();

        // We want to process lung reagents before we inhale new reagents.
        UpdatesAfter.Add(typeof(MetabolizerSystem));
        SubscribeLocalEvent<RespiratorComponent, ApplyMetabolicMultiplierEvent>(OnApplyMetabolicMultiplier);
        SubscribeLocalEvent<RespiratorComponent, CPRDoAfterEvent>(OnCPRSuccess);
        // SubscribeLocalEvent<CPRCancelledEvent>(OnCPRCancelled);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<RespiratorComponent, BodyComponent>();
        while (query.MoveNext(out var uid, out var respirator, out var body))
        {
            if (_mobState.IsDead(uid))
            {
                continue;
            }

            respirator.AccumulatedFrametime += frameTime;

            if (respirator.AccumulatedFrametime < respirator.CycleDelay)
                continue;
            respirator.AccumulatedFrametime -= respirator.CycleDelay;
            UpdateSaturation(uid, -respirator.CycleDelay, respirator);

            if (!_mobState.IsIncapacitated(uid) || respirator.BreatheInCritCounter > 0) // cannot breathe in crit.
            {
                switch (respirator.Status)
                {
                    case RespiratorStatus.Inhaling:
                        Inhale(uid, body);
                        respirator.Status = RespiratorStatus.Exhaling;
                        break;
                    case RespiratorStatus.Exhaling:
                        Exhale(uid, body);
                        respirator.Status = RespiratorStatus.Inhaling;
                        break;
                }

                respirator.BreatheInCritCounter = Math.Clamp(respirator.BreatheInCritCounter - 1, 0, 6);
            }

            if (respirator.Saturation < respirator.SuffocationThreshold)
            {
                if (_gameTiming.CurTime >= respirator.LastGaspPopupTime + respirator.GaspPopupCooldown)
                {
                    respirator.LastGaspPopupTime = _gameTiming.CurTime;
                    _popupSystem.PopupEntity(Loc.GetString("lung-behavior-gasp"), uid);
                }

                TakeSuffocationDamage(uid, respirator);
                respirator.SuffocationCycles += 1;
                continue;
            }

            StopSuffocation(uid, respirator);
            respirator.SuffocationCycles = 0;
        }
    }

    public void Inhale(EntityUid uid, BodyComponent? body = null)
    {
        if (!Resolve(uid, ref body, false))
            return;

        var organs = _bodySystem.GetBodyOrganComponents<LungComponent>(uid, body);

        // Inhale gas
        var ev = new InhaleLocationEvent();
        RaiseLocalEvent(uid, ev);

        ev.Gas ??= _atmosSys.GetContainingMixture(uid, false, true);

        if (ev.Gas == null)
        {
            return;
        }

        var actualGas = ev.Gas.RemoveVolume(Atmospherics.BreathVolume);

        var lungRatio = 1.0f / organs.Count;
        var gas = organs.Count == 1 ? actualGas : actualGas.RemoveRatio(lungRatio);
        foreach (var (lung, _) in organs)
        {
            // Merge doesn't remove gas from the giver.
            _atmosSys.Merge(lung.Air, gas);
            _lungSystem.GasToReagent(lung.Owner, lung);
        }
    }

    public void Exhale(EntityUid uid, BodyComponent? body = null)
    {
        if (!Resolve(uid, ref body, false))
            return;

        var organs = _bodySystem.GetBodyOrganComponents<LungComponent>(uid, body);

        // exhale gas

        var ev = new ExhaleLocationEvent();
        RaiseLocalEvent(uid, ev, false);

        if (ev.Gas == null)
        {
            ev.Gas = _atmosSys.GetContainingMixture(uid, false, true);

            // Walls and grids without atmos comp return null. I guess it makes sense to not be able to exhale in walls,
            // but this also means you cannot exhale on some grids.
            ev.Gas ??= GasMixture.SpaceGas;
        }

        var outGas = new GasMixture(ev.Gas.Volume);
        foreach (var (lung, _) in organs)
        {
            _atmosSys.Merge(outGas, lung.Air);
            lung.Air.Clear();

            if (_solutionContainerSystem.ResolveSolution(lung.Owner, lung.SolutionName, ref lung.Solution))
                _solutionContainerSystem.RemoveAllSolution(lung.Solution.Value);
        }

        _atmosSys.Merge(ev.Gas, outGas);
    }

    private void TakeSuffocationDamage(EntityUid uid, RespiratorComponent respirator)
    {
        if (respirator.SuffocationCycles == 2)
            _adminLogger.Add(LogType.Asphyxiation, $"{ToPrettyString(uid):entity} started suffocating");

        if (respirator.SuffocationCycles >= respirator.SuffocationCycleThreshold)
        {
            // TODO: This is not going work with multiple different lungs, if that ever becomes a possibility
            var organs = _bodySystem.GetBodyOrganComponents<LungComponent>(uid);
            foreach (var (comp, _) in organs)
            {
                _alertsSystem.ShowAlert(uid, comp.Alert);
            }
        }

        _damageableSys.TryChangeDamage(uid, respirator.Damage, false, false);
    }

    private void StopSuffocation(EntityUid uid, RespiratorComponent respirator)
    {
        if (respirator.SuffocationCycles >= 2)
            _adminLogger.Add(LogType.Asphyxiation, $"{ToPrettyString(uid):entity} stopped suffocating");

        // TODO: This is not going work with multiple different lungs, if that ever becomes a possibility
        var organs = _bodySystem.GetBodyOrganComponents<LungComponent>(uid);
        foreach (var (comp, _) in organs)
        {
            _alertsSystem.ClearAlert(uid, comp.Alert);
        }

        _damageableSys.TryChangeDamage(uid, respirator.DamageRecovery);
    }

    public void UpdateSaturation(EntityUid uid, float amount,
        RespiratorComponent? respirator = null)
    {
        if (!Resolve(uid, ref respirator, false))
            return;

        respirator.Saturation += amount;
        respirator.Saturation =
            Math.Clamp(respirator.Saturation, respirator.MinSaturation, respirator.MaxSaturation);
    }

    private void OnApplyMetabolicMultiplier(EntityUid uid, RespiratorComponent component,
            ApplyMetabolicMultiplierEvent args)
    {
        if (args.Apply)
        {
            component.CycleDelay *= args.Multiplier;
            component.Saturation *= args.Multiplier;
            component.MaxSaturation *= args.Multiplier;
            component.MinSaturation *= args.Multiplier;
            return;
        }

        // This way we don't have to worry about it breaking if the stasis bed component is destroyed
        component.CycleDelay /= args.Multiplier;
        component.Saturation /= args.Multiplier;
        component.MaxSaturation /= args.Multiplier;
        component.MinSaturation /= args.Multiplier;
        // Reset the accumulator properly
        if (component.AccumulatedFrametime >= component.CycleDelay)
            component.AccumulatedFrametime = component.CycleDelay;
    }

    private void OnCPRSuccess(EntityUid uid, RespiratorComponent component, CPRDoAfterEvent args)
    {
        if (!args.Cancelled && TryComp<RespiratorComponent>(args.Target, out var respirator))
        {
            respirator.BreatheInCritCounter += (int)_random.NextFloat(2, 4);
            args.Repeat = true;

            if (!HasComp<MedicalTrainingComponent>(args.User) && TryComp<PhysicsComponent>(args.Target, out var patientPhysics) && TryComp<PhysicsComponent>(args.User, out var perfPhysics))
            {
                if (perfPhysics.FixturesMass >= patientPhysics.FixturesMass && _random.Prob(0.05f * perfPhysics.FixturesMass / patientPhysics.FixturesMass))
                {
                    _popupSystem.PopupEntity(Loc.GetString("cpr-end-pvs-crack", ("user", args.User), ("target", args.Target)), uid, Shared.Popups.PopupType.MediumCaution);
                    SoundSpecifier crackSound = new SoundPathSpecifier("/Audio/Effects/Chop.ogg");
                    _audio.PlayPvs(crackSound, args.Target.Value);

                    var damage = 3f * (perfPhysics.FixturesMass / patientPhysics.FixturesMass);
                    DamageSpecifier dict = new();
                    dict.DamageDict.Add("Blunt", damage);

                    _damageableSys.TryChangeDamage(args.Target, dict);

                    component.CPRPlayingStream = _audio.Stop(component.CPRPlayingStream);
                    args.Repeat = false;
                }
            }
        }
        else
        {
            component.CPRPlayingStream = _audio.Stop(component.CPRPlayingStream);
            args.Repeat = false;
        }
    }

    /// <summary>
    /// Attempt CPR, which will keep the user breathing even in crit.
    /// As cardiac arrest is currently unsimulated, the damage taken in crit is a function of
    /// respiration alone. This may change in the future.
    /// </summary>
    public void AttemptCPR(EntityUid uid, RespiratorComponent component, InteractHandEvent args)
    {
        bool Check()
        {
            if (_inventory.TryGetSlotEntity(uid, "outerClothing", out var outer))
            {
                _popupSystem.PopupEntity(Loc.GetString("cpr-must-remove", ("clothing", outer)), uid, args.User, Shared.Popups.PopupType.MediumCaution);
                return false;
            }

            if (_inventory.TryGetSlotEntity(uid, "belt", out var belt) && _tag.HasTag(belt.Value, "BeltSlotNotBelt"))
            {
                _popupSystem.PopupEntity(Loc.GetString("cpr-must-remove", ("clothing", belt)), uid, args.User, Shared.Popups.PopupType.MediumCaution);
                return false;
            }

            if (
                TryComp<MobStateComponent>(uid, out var mobState) &&
                mobState.CurrentState != Shared.Mobs.MobState.Critical ||
                !TryComp<RespiratorComponent>(uid, out var respirator)
            )
                return false;

            return true;
        }

        if (args.Handled || !_blocker.CanInteract(args.User, args.Target) || !Check())
            return;

        bool isTrained = TryComp<MedicalTrainingComponent>(args.User, out _);

        if (component.CPRPlayingStream == null)
        {
            component.CPRPlayingStream = _audio.PlayPvs(isTrained ? component.CPRSound : component.CPRWeakSound, uid,
            audioParams: AudioParams.Default.WithVolume(isTrained ? -1f : -3f).WithLoop(true)).Value.Entity;

            _popupSystem.PopupEntity(Loc.GetString("cpr-start-second-person", ("target", Identity.Entity(args.Target, EntityManager))), uid, args.User, Shared.Popups.PopupType.Medium);
            _popupSystem.PopupEntity(Loc.GetString("cpr-start-second-person-patient", ("user", Identity.Entity(args.User, EntityManager))), uid, uid, Shared.Popups.PopupType.Medium);
        }
        else
            component.CPRPlayingStream = _audio.Stop(component.CPRPlayingStream);

        DoAfterArgs doAfterEventArgs = new DoAfterArgs(
            EntityManager,
            args.User,
            TimeSpan.FromSeconds(component.CycleDelay * (isTrained ? 1 : 2.5)),
            new CPRDoAfterEvent(),
            args.Target,
            args.Target,
            null
        )
        {
            // BroadcastFinishedEvent = new CPRSuccessfulEvent(user, uid),
            // BroadcastCancelledEvent = new CPRCancelledEvent(uid),
            BreakOnTargetMove = true,
            BreakOnUserMove = true,
            BreakOnDamage = true,
            // BreakOnStun = true,
            NeedHand = true,
            ExtraCheck = Check,
            AttemptFrequency = AttemptFrequency.StartAndEnd

        };

        _doAfter.TryStartDoAfter(doAfterEventArgs);
    }
}

public sealed class InhaleLocationEvent : EntityEventArgs
{
    public GasMixture? Gas;
}

public sealed class ExhaleLocationEvent : EntityEventArgs
{
    public GasMixture? Gas;
}
