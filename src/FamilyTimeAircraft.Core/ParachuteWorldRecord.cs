using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FamilyTimeAircraft.Core;

public static partial class WorldRecord
{
    private static void WriteDropPlans(BinaryWriter writer, AircraftWorld world)
    {
        writer.Write(world.DropPlans.Count);
        foreach (PendingParachuteDrop plan in world.DropPlans.Values.OrderBy(plan => plan.AircraftId))
        {
            writer.Write(plan.AircraftId); WriteText(writer, plan.Owner); WriteVector(writer, plan.Origin); writer.Write(plan.UntilNext);
            writer.Write(plan.Passengers.Count);
            foreach (ParachuteDropPassenger passenger in plan.Passengers) { writer.Write(passenger.ChildId); WriteVector(writer, passenger.Target); }
        }
    }

    private static void ReadDropPlans(BinaryReader reader, AircraftWorld world)
    {
        int count = ReadCount(reader, AircraftRules.MaximumAircraft);
        for (int index = 0; index < count; index++)
        {
            int aircraftId = ReadId(reader); string owner = ReadText(reader); var origin = ReadVector(reader); float delay = reader.ReadSingle();
            int passengers = ReadCount(reader, AircraftRules.PassengerCapacity);
            var queue = new List<ParachuteDropPassenger>();
            for (int child = 0; child < passengers; child++) queue.Add(new ParachuteDropPassenger(ReadChildId(reader), ReadVector(reader)));
            if (world.DropPlans.ContainsKey(aircraftId)) throw new InvalidDataException("Duplicate parachute drop plan.");
            world.DropPlans.Add(aircraftId, new PendingParachuteDrop(aircraftId, owner, origin, queue) { UntilNext = delay });
        }
    }

    private static int ReadChildId(BinaryReader reader)
    {
        int childId = reader.ReadInt32();
        if (childId < 0 || childId >= ushort.MaxValue) throw new InvalidDataException("Invalid parachute child identity.");
        return childId;
    }

    private static void ValidatePilotGroundOrder(Aircraft plane)
    {
        if (!plane.PilotGroundTarget.HasValue) return;
        if (plane.CrashTarget.HasValue || !plane.Seats.TryGetValue(0, out Occupant pilot) || pilot.Kind != OccupantKind.Child || pilot.Owner != plane.PilotGroundOwner)
            throw new InvalidDataException("Pilot ground order lacks its owned child pilot.");
    }

    private static void ValidateDropPlans(AircraftWorld world)
    {
        foreach (PendingParachuteDrop plan in world.DropPlans.Values)
        {
            if (!world.Planes.TryGetValue(plan.AircraftId, out Aircraft plane) || plane.IsOnGround || plane.Phase == FlightPhase.Wrecked || !plane.Seats.ContainsKey(0))
                throw new InvalidDataException("Parachute drop lacks an airborne aircraft and pilot.");
            if (!AircraftRules.IsFinite(plan.UntilNext) || plan.UntilNext < 0 || plan.UntilNext > AircraftRules.ParachuteDropInterval || plan.Passengers.Count == 0)
                throw new InvalidDataException("Invalid parachute drop timing.");
            if (plan.Passengers.Select(child => child.ChildId).Distinct().Count() != plan.Passengers.Count)
                throw new InvalidDataException("Duplicate child in parachute drop.");
            foreach (ParachuteDropPassenger child in plan.Passengers)
                if (!plane.Seats.Any(seat => seat.Key > 0 && seat.Value.Kind == OccupantKind.Child && seat.Value.Id == child.ChildId && seat.Value.Owner == plan.Owner))
                    throw new InvalidDataException("Parachute drop refers to an unavailable child.");
        }
    }
}
