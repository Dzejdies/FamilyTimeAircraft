using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FamilyTimeAircraft.Core;
using GAT;
using HarmonyLib;
using UnityEngine;
using NVector = System.Numerics.Vector3;

namespace FamilyTimeAircraft.Diagnostics;

public sealed class AircraftImpactSmokeScenario
{
    private const float TimeoutSeconds = 75;
    private readonly AircraftRuntime _runtime;
    private Aircraft _plane = null!;
    private AircraftBuildingFixture _building = null!;
    private Vector3 _target;

    public AircraftImpactSmokeScenario(AircraftRuntime runtime) => _runtime = runtime;

    public IEnumerator Run()
    {
        _plane = _runtime.World.Aircrafts.First();
        Vector3 origin = GameFlightEnvironment.ToUnity(_plane.Position);
        AircraftTestEnvironment.ClearWater();
        AircraftTestEnvironment.PrepareFlightCorridor(origin);
        yield return new WaitForSecondsRealtime(1);
        _building = AircraftBuildingFixture.Create(origin);
        _target = _building.TargetPoint;
        _runtime.SetNotice("[AircraftImpactEvidence] template=" + _building.TemplateIndex + " target=" + _target + " grid=" + _building.TargetPosition);
        Report("impact-native-wall-collision-present", _building.TargetCollisionPresent);
        Report("impact-native-wall-renderer-present", _building.TargetRenderersPresent);
        Report("impact-native-neighbor-present", _building.NeighborCollisionPresent && _building.NeighborRenderersPresent);
        Airfield departure = _runtime.World.BuildAirfield(_plane.Owner, _plane.Position, System.Numerics.Quaternion.Identity);
        Handle child = World.commandOwnershipService.FindAvailableWorkers(World.gat.instanceCount, origin, 50, _ => true)[0];
        _runtime.World.Board(_plane.Id, new Occupant(OccupantKind.Child, child.stableIndex, _plane.Owner), 0);
        Report("impact-departure-runway-clear", _runtime.Environment.IsRunwayClear(departure));
        _runtime.World.OrderCrash(_plane.Id, GameFlightEnvironment.ToCore(_target), _plane.Owner, _runtime.Environment);
        yield return AwaitCrash();
        Report("impact-native-wall-collision-removed", !_building.TargetCollisionPresent);
        Report("impact-native-wall-renderer-removed", _building.TargetRenderersRemoved);
        Report("impact-native-neighbor-retained", _building.NeighborCollisionPresent && _building.NeighborRenderersPresent);
        var environment = (GameFlightEnvironment)_runtime.Environment;
        Report("impact-duplicate-contact-applies-zero", environment.ImpactDamage.Apply(_plane.Position) == 0);
        NVector terrain = _plane.Position + new NVector(20, 0, 0);
        terrain.Y = environment.GroundHeight(terrain);
        environment.IsBlocked(terrain + NVector.UnitY, terrain - NVector.UnitY, AircraftRules.CollisionRadius);
        Report("impact-terrain-contact-removes-no-buildings", environment.ImpactDamage.Apply(terrain) == 0 && _building.NeighborCollisionPresent);
    }

    private IEnumerator AwaitCrash()
    {
        float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
        float nextEvidence = 0;
        while (_plane.Phase != FlightPhase.Wrecked && Time.realtimeSinceStartup < deadline)
        {
            if (Time.realtimeSinceStartup >= nextEvidence)
            {
                _runtime.SetNotice("[AircraftImpactEvidence] phase=" + _plane.Phase + " position=" + _plane.Position + " target=" + _target);
                nextEvidence = Time.realtimeSinceStartup + 5;
            }
            yield return null;
        }
        _runtime.SetNotice("[AircraftImpactEvidence] final=" + _plane.Phase + " position=" + _plane.Position + " target=" + _target
            + " distance=" + Vector3.Distance(GameFlightEnvironment.ToUnity(_plane.Position), _target));
        Report("impact-native-aircraft-crashed", _plane.Phase == FlightPhase.Wrecked);
    }

    private void Report(string name, bool passed)
    {
        _runtime.SetNotice("[AircraftTest] " + name + "=" + (passed ? "PASS" : "FAIL"));
        if (!passed) throw new InvalidOperationException("Native aircraft impact check failed: " + name);
    }
}
