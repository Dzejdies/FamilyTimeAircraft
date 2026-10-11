using System;
using FamilyTimeAircraft.Core;
using CoreVector3 = System.Numerics.Vector3;

namespace FamilyTimeAircraft;

public sealed partial class AircraftRuntime
{
    public Func<int>? CommandPlayerId { get; set; }

    public void OrderAircraftChildrenToGoTo(CoreVector3 target)
    {
        if (!IsAuthoritative || !IsGameReady) return;
        int playerId = CommandPlayerId?.Invoke() ?? LocalPlayerId;
        string owner = CommandOwner?.Invoke() ?? Owner;
        foreach (Aircraft plane in World.Aircrafts)
        {
            if (!AircraftRules.CanControlAircraft(plane, owner)) continue;
            try { OrderAircraftGoTo(plane, playerId, owner, target); }
            catch (InvalidOperationException exception) { SetNotice(exception.Message); }
        }
    }

    private void OrderAircraftGoTo(Aircraft plane, int playerId, string owner, CoreVector3 target)
    {
        var children = World.AircraftChildrenForGoTo(plane.Id, owner);
        if (children.Count == 0) return;
        foreach (Occupant child in children)
            if (CanCommandChild?.Invoke(playerId, child.Id) == false)
                throw new InvalidOperationException("You cannot give this aircraft child a go-to command.");
        World.OrderParachuteDrop(plane.Id, target, owner, Environment);
        SetNotice("Aircraft children will go to the selected point. The child pilot lands first.");
    }
}
