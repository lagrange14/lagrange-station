using Content.Server.GameTicking.Rules.Components;
using Content.Server.Maps;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Server.StationEvents.Components;
using Content.Shared.Humanoid;
using Content.Shared.Mobs.Components;
using Content.Shared.Shipyard.Prototypes;
using Robust.Server.GameObjects;
using Robust.Server.Maps;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using System.Numerics;

namespace Content.Server.StationEvents.Events;
public sealed class DistressSignalRule : StationEventSystem<DistressSignalRuleComponent>
{
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly MapLoaderSystem _map = default!;
    [Dependency] private readonly ShuttleSystem _shuttle = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private List<(Entity<TransformComponent> Entity, EntityUid MapUid, Vector2 LocalPosition)> _playerMobs = new();
    private List<DistressSignalObjectiveComponent> _objectives = new();
    private string _designation = default!;
    private bool _failedObjectives = true;

    protected override void Started(EntityUid uid, DistressSignalRuleComponent component, GameRuleComponent gameRule,
        GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        // Convert from randomly-selected file path to a grid in the game.
        var shuttleId = _random.Pick(component.MapList);
        if (!PrototypeManager.TryIndex<VesselPrototype>(shuttleId, out var shuttleProto))
        {
            Log.Error($"Distress signal event failed to load map ID '{shuttleId}'.");
            return;
        }

        var shuttleMap = _mapManager.CreateMap();
        var options = new MapLoadOptions
        {
            LoadMap = true,
        };

        if (!_map.TryLoad(shuttleMap, shuttleProto.ShuttlePath.ToString(), out var gridUids, options))
            return;
        component.GridUid = gridUids[0];
        if (component.GridUid is not EntityUid gridUid)
            return;
        _shuttle.SetIFFColor(gridUid, component.Color);
        var offset = _random.NextVector2(500f, 5000f);
        var mapId = GameTicker.DefaultMap;
        var coords = new MapCoordinates(offset, mapId);
        var location = Spawn(null, coords);

        // Initialize the Station component, giving us access to the randomly-generated name.
        if (PrototypeManager.TryIndex<GameMapPrototype>(shuttleId, out var stationProto))
        {
            var shuttleStation = StationSystem.InitializeNewStation(stationProto.Stations[shuttleId], gridUids);
            var metaData = MetaData((EntityUid) shuttleStation);
            var shipNameParts = metaData.EntityName.Split(' ');
            _designation = shipNameParts[^1];
        }

        // Register all of the objectives attached to the grid.
        var objectiveQuery = AllEntityQuery<DistressSignalObjectiveComponent, TransformComponent>();

        while (objectiveQuery.MoveNext(out var objectiveUid, out var transform))
        {
            if (transform.GridUid == null || transform.MapUid == null || transform.GridUid != gridUid)
                continue;

            objectiveUid.DistressSignalRuleComponent = component;
            objectiveUid.DistressSignalRule = this;
            objectiveUid.GameRule = gameRule;

            AddObjective(objectiveUid);
        }

        // Announce the distress signal to the players.
        var str = Loc.GetString("station-event-distress-signal-start-announcement",
            ("designation", _designation),
            ("x", Math.Round(offset.X)),
            ("y", Math.Round(offset.Y))
        );

        ChatSystem.DispatchGlobalAnnouncement(str, colorOverride: Color.FromHex("#18abf5"));

        // Send the grid to the announced location.
        if (TryComp<ShuttleComponent>(gridUid, out var shuttle))
        {
            _shuttle.FTLTravel(gridUid, shuttle, location, 0, 0);
        }
    }

    public void AddObjective(DistressSignalObjectiveComponent objective)
    {
        _objectives.Add(objective);
    }

    protected override void ActiveTick(EntityUid uid, DistressSignalRuleComponent component, GameRuleComponent gameRule, float frameTime)
    {
        base.ActiveTick(uid, component, gameRule, frameTime);

        // Determine whether a (partially) successful attempt to address the distress signal has been made.
        int successCount = 0;
        int failureCount = 0;

        foreach (var objective in _objectives)
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
                    // Ended(uid, component, gameRule);
                    return;
                }

                failureCount++;
            }
            else if (objective.Completed)
            {
                _failedObjectives = false;
                successCount++;
            }
        }

        if (successCount + failureCount == _objectives.Count)
        {
            _failedObjectives = successCount > 0;
            // Ended(uid, component, gameRule);
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

        // TODO Post-event announcement.
        if (_failedObjectives)
        {

        }
        else
        {

        }
    }
}

