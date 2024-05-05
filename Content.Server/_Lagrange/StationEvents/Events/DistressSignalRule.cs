using Content.Server.GameTicking.Rules.Components;
using Content.Server.Maps;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Server.StationEvents.Components;
using Content.Shared.Humanoid;
using Content.Shared.Mobs.Components;
using Robust.Server.GameObjects;
using Robust.Server.Maps;
using Robust.Shared.Map;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using System.Numerics;

namespace Content.Server.StationEvents.Events;
public sealed class DistressSignalRule : StationEventSystem<DistressSignalRuleComponent>
{
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly MapLoaderSystem _map = default!;
    [Dependency] private readonly ShuttleSystem _shuttle = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private readonly int _objectiveCompleteDelay = 15;

    private List<(Entity<TransformComponent> Entity, EntityUid MapUid, Vector2 LocalPosition)> _playerMobs = new();

    protected override void Started(EntityUid uid, DistressSignalRuleComponent component, GameRuleComponent gameRule,
        GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        // Convert from randomly-selected file path to a grid in the game.
        component.MapId = _random.Pick(component.MapList);
        if (!PrototypeManager.TryIndex<GameMapPrototype>(component.MapId, out var shuttleProto))
        {
            Log.Error($"Distress signal event failed to load map ID '{component.MapId}'.");
            GameTicker.EndGameRule(uid);
            return;
        }

        var shuttleMap = _mapManager.CreateMap();
        var options = new MapLoadOptions
        {
            LoadMap = true,
        };

        if (!_map.TryLoad(shuttleMap, shuttleProto.MapPath.ToString(), out var gridUids, options))
        {
            Log.Error($"Distress signal map '{shuttleMap}' failed to load.");
            GameTicker.EndGameRule(uid);
            return;
        }
        component.GridUid = gridUids[0];
        if (component.GridUid is not EntityUid gridUid)
        {
            Log.Error($"Distress signal rule component's GridUid was not correctly set.");
            GameTicker.EndGameRule(uid);
            return;
        }
        _shuttle.SetIFFColor(gridUid, component.Color);
        var offset = _random.NextVector2(500f, 5000f);
        var mapId = GameTicker.DefaultMap;
        var coords = new MapCoordinates(offset, mapId);
        var location = Spawn(null, coords);

        // Initialize the Station component, giving us access to the randomly-generated name.
        var shuttleStation = StationSystem.InitializeNewStation(shuttleProto.Stations[component.MapId], gridUids);
        var metaData = MetaData((EntityUid) shuttleStation);
        var shipNameParts = metaData.EntityName.Split(' ');
        component.Designation = shipNameParts[^1];

        // Announce the distress signal to the players.
        var str = Loc.GetString("station-event-distress-signal-start-announcement",
            ("designation", component.Designation),
            ("x", Math.Round(offset.X)),
            ("y", Math.Round(offset.Y))
        );

        ChatSystem.DispatchGlobalAnnouncement(str, sender: "Automated", colorOverride: Color.FromHex("#18abf5"));

        // Send the grid to the announced location.
        if (TryComp<ShuttleComponent>(gridUid, out var shuttle))
        {
            _shuttle.FTLTravel(gridUid, shuttle, location, 0, 0);
        }
    }

    public void AddObjective(DistressSignalObjectiveComponent objective, DistressSignalRuleComponent component)
    {
        objective.DistressSignalRuleComponent = component;
        component.Objectives.Add(objective);
    }

    protected override void ActiveTick(EntityUid uid, DistressSignalRuleComponent component, GameRuleComponent gameRule, float frameTime)
    {
        base.ActiveTick(uid, component, gameRule, frameTime);

        if (component.TimerRunning)
            return;

        if (component.Objectives.Count == 0)
        {
            Log.Error($"Distress signal '{component.MapId}' lacks objectives and was thus aborted.");
            if (component.GridUid is not null)
                Del(component.GridUid);
            GameTicker.EndGameRule(uid);
            return;
        }

        // Determine whether a (partially) successful attempt to address the distress signal has been made.
        int successCount = 0;
        int failureCount = 0;

        foreach (var objective in component.Objectives)
        {
            if (objective is null)
            {
                failureCount++;
                continue;
            }

            if (objective.Failed)
            {
                if (objective.Critical)
                {
                    component.ObjectivesCompleted = false;
                    GameTicker.EndGameRule(uid);
                    return;
                }

                failureCount++;
            }
            else if (objective.Completed)
            {
                component.ObjectivesCompleted = true;
                successCount++;
            }
        }

        if (successCount + failureCount >= component.Objectives.Count)
        {
            component.ObjectivesCompleted = successCount > 0;

            // Ensure the objectives *stay* completed.
            component.TimerRunning = true;
            Timer.Spawn(TimeSpan.FromSeconds(_objectiveCompleteDelay), () =>
            {
                component.TimerRunning = false;
                foreach (var objective in component.Objectives)
                {
                    if (objective is not null && !objective.Completed && !objective.Failed)
                        return;
                }
                GameTicker.EndGameRule(uid);
            });
        }
    }

    protected override void Ended(EntityUid uid, DistressSignalRuleComponent component, GameRuleComponent gameRule, GameRuleEndedEvent args)
    {
        base.Ended(uid, component, gameRule, args);

        if (!EntityManager.TryGetComponent<TransformComponent>(component.GridUid, out var gridTransform))
        {
            Log.Error("Distress signal grid was missing transform component.");
            return;
        }

        if (gridTransform.GridUid is not EntityUid gridUid)
        {
            Log.Error("Distress signal has no associated grid.");
            return;
        }

        if (component.Designation is null)
        {
            Log.Error("Distress signal has no designation defined.");
            return;
        }

        var mobQuery = AllEntityQuery<HumanoidAppearanceComponent, MobStateComponent, TransformComponent>();
        _playerMobs.Clear();

        while (mobQuery.MoveNext(out var mobUid, out _, out _, out var xform))
        {
            if (xform.GridUid == null || xform.MapUid == null || xform.GridUid != gridUid)
                continue;

            // Can't parent directly to map as it runs grid traversal.
            _playerMobs.Add(((mobUid, xform), xform.MapUid.Value, _transform.GetWorldPosition(xform)));
            _transform.DetachParentToNull(mobUid, xform);
        }

        // Deletion has to happen before grid traversal re-parents players.
        Del(gridUid);

        foreach (var mob in _playerMobs)
        {
            _transform.SetCoordinates(mob.Entity.Owner, new EntityCoordinates(mob.MapUid, mob.LocalPosition));
        }

        var str = "";

        if (component.ObjectivesCompleted)
        {
            str = Loc.GetString("station-event-distress-signal-pass-announcement",
                ("designation", component.Designation)
            );
        }
        else
        {
            str = Loc.GetString("station-event-distress-signal-fail-announcement",
                ("designation", component.Designation)
            );
        }

        ChatSystem.DispatchGlobalAnnouncement(str, sender: "Automated", colorOverride: Color.FromHex("#18abf5"));
    }
}

