using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;

namespace FamilyTimeAircraft.Core;

public static partial class WorldRecord
{
    public const string SectionId = "ftair.aircraft";
    public const int Version = 3;
    public const int MaximumBytes = 1024 * 1024;
    private const int Magic = 0x31544146;

    public static void Write(Stream stream, AircraftWorld world)
    {
        using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        writer.Write(Magic); writer.Write(Version); writer.Write(world.EventSequence);
        writer.Write(world.NextAircraftId); writer.Write(world.NextAirfieldId);
        writer.Write(world.Airfields.Count);
        foreach (Airfield field in world.Airfields.OrderBy(item => item.Id)) WriteAirfield(writer, field);
        writer.Write(world.Aircrafts.Count);
        foreach (Aircraft plane in world.Aircrafts.OrderBy(item => item.Id)) WriteAircraft(writer, plane);
        writer.Write(world.Ejections.Count);
        foreach (EjectedOccupant passenger in world.Ejections.OrderBy(item => item.Occupant.Key)) WriteEjection(writer, passenger);
        WriteDropPlans(writer, world);
        WriteTowers(writer, world);
        if (stream.CanSeek && stream.Position > MaximumBytes) throw new InvalidDataException("Aircraft world exceeds the save size limit.");
    }

    public static AircraftWorld Read(Stream stream)
    {
        if (stream.CanSeek && stream.Length - stream.Position > MaximumBytes) throw new InvalidDataException("Aircraft world exceeds the save size limit.");
        using var reader = new BinaryReader(stream, Encoding.UTF8, true);
        if (reader.ReadInt32() != Magic) throw new InvalidDataException("Unsupported aircraft world format.");
        int version = reader.ReadInt32();
        if (version < 1 || version > Version) throw new InvalidDataException("Unsupported aircraft world format.");
        var world = new AircraftWorld { EventSequence = reader.ReadInt64(), NextAircraftId = ReadId(reader), NextAirfieldId = ReadId(reader) };
        int fields = ReadCount(reader, AircraftRules.MaximumAirfields);
        for (int i = 0; i < fields; i++) ReadAirfield(reader, world);
        int aircraft = ReadCount(reader, AircraftRules.MaximumAircraft);
        for (int i = 0; i < aircraft; i++) ReadAircraft(reader, world, version);
        int passengers = ReadCount(reader, AircraftRules.MaximumAircraft * (AircraftRules.PassengerCapacity + 1));
        for (int i = 0; i < passengers; i++) ReadEjection(reader, world, version);
        if (version >= 2) ReadDropPlans(reader, world);
        if (version >= 3) ReadTowers(reader, world);
        ValidateWorld(world);
        if (stream.CanSeek && stream.Position != stream.Length) throw new InvalidDataException("Aircraft world has unexpected trailing bytes.");
        return world;
    }

    internal static void WriteVector(BinaryWriter writer, Vector3 value) { writer.Write(value.X); writer.Write(value.Y); writer.Write(value.Z); }
    internal static Vector3 ReadVector(BinaryReader reader)
    {
        var value = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        if (!AircraftRules.IsFinite(value)) throw new InvalidDataException("Invalid aircraft vector.");
        return value;
    }
    internal static void WriteOccupant(BinaryWriter writer, Occupant occupant) { writer.Write((byte)occupant.Kind); writer.Write(occupant.Id); WriteText(writer, occupant.Owner); }
    internal static Occupant ReadOccupant(BinaryReader reader) => new Occupant((OccupantKind)reader.ReadByte(), reader.ReadInt32(), ReadText(reader));

    private static void WriteAirfield(BinaryWriter writer, Airfield field)
    {
        writer.Write(field.Id); WriteText(writer, field.Owner); WriteVector(writer, field.Position);
        WriteRotation(writer, field.Rotation); writer.Write(field.ReservedBy ?? 0);
    }

    private static void ReadAirfield(BinaryReader reader, AircraftWorld world)
    {
        var field = new Airfield(ReadId(reader), ReadText(reader), ReadVector(reader), ReadRotation(reader));
        field.ReservedBy = ReadOptionalId(reader);
        if (world.Runways.ContainsKey(field.Id)) throw new InvalidDataException("Duplicate airfield identity.");
        world.Runways.Add(field.Id, field);
    }

    private static void WriteAircraft(BinaryWriter writer, Aircraft plane)
    {
        writer.Write(plane.Id); WriteText(writer, plane.Owner); WriteVector(writer, plane.Position);
        WriteRotation(writer, plane.Rotation); WriteVector(writer, plane.Velocity); writer.Write((byte)plane.Phase); writer.Write(plane.IsOnGround);
        writer.Write(plane.TargetAirfieldId ?? 0); writer.Write(plane.OriginAirfieldId ?? 0);
        writer.Write(plane.CrashTarget.HasValue); if (plane.CrashTarget.HasValue) WriteVector(writer, plane.CrashTarget.Value);
        writer.Write(plane.EmergencyLandingPoint.HasValue); if (plane.EmergencyLandingPoint.HasValue) WriteVector(writer, plane.EmergencyLandingPoint.Value);
        WriteInput(writer, plane.Input); writer.Write(plane.Pitch); writer.Write(plane.Roll); writer.Write(plane.Yaw);
        writer.Write(plane.Cabin.Count);
        foreach (var seat in plane.Cabin.OrderBy(item => item.Key)) { writer.Write(seat.Key); WriteOccupant(writer, seat.Value); }
        writer.Write(plane.PilotGroundTarget.HasValue);
        if (plane.PilotGroundTarget.HasValue) { WriteVector(writer, plane.PilotGroundTarget.Value); WriteText(writer, plane.PilotGroundOwner!); writer.Write(plane.PilotReturnPending); }
    }

    private static void ReadAircraft(BinaryReader reader, AircraftWorld world, int version)
    {
        var plane = new Aircraft(ReadId(reader), ReadText(reader), ReadVector(reader), ReadRotation(reader));
        plane.Velocity = ReadVector(reader); plane.Phase = (FlightPhase)reader.ReadByte(); plane.IsOnGround = reader.ReadBoolean();
        plane.TargetAirfieldId = ReadOptionalId(reader); plane.OriginAirfieldId = ReadOptionalId(reader);
        plane.CrashTarget = reader.ReadBoolean() ? ReadVector(reader) : (Vector3?)null;
        plane.EmergencyLandingPoint = reader.ReadBoolean() ? ReadVector(reader) : (Vector3?)null;
        plane.Input = ReadInput(reader); plane.Pitch = ReadAngle(reader); plane.Roll = ReadAngle(reader); plane.Yaw = ReadAngle(reader);
        int count = ReadCount(reader, AircraftRules.PassengerCapacity + 1);
        for (int i = 0; i < count; i++) ReadSeat(reader, plane);
        if (version >= 2 && reader.ReadBoolean()) { plane.PilotGroundTarget = ReadVector(reader); plane.PilotGroundOwner = AircraftRules.RequireOwner(ReadText(reader)); plane.PilotReturnPending = reader.ReadBoolean(); }
        if (world.Planes.ContainsKey(plane.Id)) throw new InvalidDataException("Duplicate aircraft identity.");
        world.Planes.Add(plane.Id, plane);
    }

    private static void ReadSeat(BinaryReader reader, Aircraft plane)
    {
        int seat = reader.ReadInt32();
        if (seat < 0 || seat > AircraftRules.PassengerCapacity || plane.Cabin.ContainsKey(seat)) throw new InvalidDataException("Invalid or duplicate aircraft seat.");
        plane.Cabin.Add(seat, ReadOccupant(reader));
    }

    private static void WriteEjection(BinaryWriter writer, EjectedOccupant passenger)
    {
        WriteOccupant(writer, passenger.Occupant); writer.Write(passenger.AircraftId);
        WriteVector(writer, passenger.Position); WriteVector(writer, passenger.Velocity); writer.Write(passenger.Elapsed);
        writer.Write(passenger.ParachuteTarget.HasValue);
        if (passenger.ParachuteTarget.HasValue) WriteVector(writer, passenger.ParachuteTarget.Value);
    }

    private static void ReadEjection(BinaryReader reader, AircraftWorld world, int version)
    {
        var passenger = new EjectedOccupant(ReadOccupant(reader), ReadId(reader), ReadVector(reader), ReadVector(reader));
        passenger.Elapsed = reader.ReadSingle();
        if (version >= 2 && reader.ReadBoolean()) passenger.ParachuteTarget = ReadVector(reader);
        if (!AircraftRules.IsFinite(passenger.Elapsed) || passenger.Elapsed < 0 || passenger.Elapsed > (passenger.HasParachute ? AircraftRules.ParachuteMaximumElapsed : AircraftRules.EjectionTimeout) || passenger.Velocity.Length() > 100 || world.FlyingOccupants.ContainsKey(passenger.Occupant.Key))
            throw new InvalidDataException("Invalid ejected passenger state.");
        world.FlyingOccupants.Add(passenger.Occupant.Key, passenger);
    }

    private static void ValidateWorld(AircraftWorld world)
    {
        if (world.EventSequence < 0 || world.Aircrafts.Any(plane => plane.Id >= world.NextAircraftId) || world.Airfields.Any(field => field.Id >= world.NextAirfieldId)) throw new InvalidDataException("Invalid aircraft sequence state.");
        var keys = world.Aircrafts.SelectMany(plane => plane.Seats.Values).Select(passenger => passenger.Key).Concat(world.Ejections.Select(passenger => passenger.Occupant.Key)).ToArray();
        if (keys.Distinct().Count() != keys.Length) throw new InvalidDataException("An occupant has conflicting aircraft assignments.");
        foreach (Aircraft plane in world.Aircrafts) { ValidateAircraft(plane, world); ValidatePilotGroundOrder(plane); }
        ValidateDropPlans(world);
        foreach (Airfield field in world.Airfields) if (field.ReservedBy.HasValue && !world.Planes.ContainsKey(field.ReservedBy.Value)) throw new InvalidDataException("Runway reservation refers to a missing aircraft.");
        foreach (EjectedOccupant passenger in world.Ejections) if (!world.Planes.ContainsKey(passenger.AircraftId)) throw new InvalidDataException("An ejected passenger refers to a missing aircraft.");
    }

    private static void WriteTowers(BinaryWriter writer, AircraftWorld world)
    {
        writer.Write(world.TowerEventSequence);
        writer.Write(world.NextTowerPairId);
        writer.Write(world.TowerPairs.Count);
        foreach (TowerPair pair in world.TowerPairs.OrderBy(item => item.Id))
        {
            writer.Write(pair.Id);
            WriteText(writer, pair.Owner);
            WriteVector(writer, pair.Position);
            WriteRotation(writer, pair.Rotation);
            writer.Write(pair.Sections.Count);
            foreach (TowerSectionState section in pair.Sections.OrderBy(item => item.Id))
            {
                writer.Write(section.Id);
                writer.Write(section.TowerIndex);
                writer.Write(section.Level);
                writer.Write(section.Integrity);
                writer.Write((byte)section.Phase);
            }
        }
    }

    private static void ReadTowers(BinaryReader reader, AircraftWorld world)
    {
        long eventSequence = reader.ReadInt64();
        int nextPairId = ReadId(reader);
        int pairCount = ReadCount(reader, TowerLayout.MaximumTowerPairs);
        for (int pairIndex = 0; pairIndex < pairCount; pairIndex++) ReadTowerPair(reader, world);
        try
        {
            world.RestoreTowerEventSequence(eventSequence);
            world.RestoreNextTowerPairId(nextPairId);
        }
        catch (InvalidOperationException error) { throw new InvalidDataException("Invalid tower sequence state.", error); }
    }

    private static void ReadTowerPair(BinaryReader reader, AircraftWorld world)
    {
        int id = ReadId(reader);
        string owner = AircraftRules.RequireOwner(ReadText(reader));
        Vector3 position = ReadVector(reader);
        Quaternion rotation = ReadRotation(reader);
        int sectionCount = ReadCount(reader, TowerLayout.SectionsPerPair);
        if (sectionCount != TowerLayout.SectionsPerPair) throw new InvalidDataException("Invalid tower section count.");
        var integrity = new float[sectionCount];
        var phases = new TowerSectionPhase[sectionCount];
        for (int index = 0; index < sectionCount; index++) ReadTowerSection(reader, index, integrity, phases);
        try { world.RestoreTowerPair(id, owner, position, rotation, integrity, phases); }
        catch (ArgumentException error) { throw new InvalidDataException("Invalid tower pair record.", error); }
        catch (InvalidOperationException error) { throw new InvalidDataException("Invalid tower pair record.", error); }
    }

    private static void ReadTowerSection(BinaryReader reader, int expectedId, float[] integrity, TowerSectionPhase[] phases)
    {
        int id = reader.ReadInt32();
        int towerIndex = reader.ReadInt32();
        int level = reader.ReadInt32();
        float sectionIntegrity = reader.ReadSingle();
        var phase = (TowerSectionPhase)reader.ReadByte();
        if (id != expectedId || towerIndex != expectedId / TowerLayout.SectionsPerTower || level != expectedId % TowerLayout.SectionsPerTower ||
            !AircraftRules.IsFinite(sectionIntegrity) || sectionIntegrity < 0 || sectionIntegrity > TowerRules.InitialIntegrity || !Enum.IsDefined(typeof(TowerSectionPhase), phase))
            throw new InvalidDataException("Invalid tower section record.");
        integrity[expectedId] = sectionIntegrity;
        phases[expectedId] = phase;
    }

    private static void ValidateAircraft(Aircraft plane, AircraftWorld world)
    {
        if (!Enum.IsDefined(typeof(FlightPhase), plane.Phase) || plane.Velocity.Length() > AircraftRules.MaximumSpeed + 0.1f) throw new InvalidDataException("Invalid aircraft motion state.");
        if (plane.TargetAirfieldId.HasValue && !world.Runways.ContainsKey(plane.TargetAirfieldId.Value) || plane.OriginAirfieldId.HasValue && !world.Runways.ContainsKey(plane.OriginAirfieldId.Value)) throw new InvalidDataException("Aircraft refers to a missing runway.");
        if ((plane.Phase == FlightPhase.Taxiing || plane.Phase == FlightPhase.TakingOff) && !plane.OriginAirfieldId.HasValue) throw new InvalidDataException("Departure lacks a runway.");
        if ((plane.Phase == FlightPhase.Cruising || plane.Phase == FlightPhase.Approaching || plane.Phase == FlightPhase.Landing) && !plane.TargetAirfieldId.HasValue && !plane.CrashTarget.HasValue) throw new InvalidDataException("Flight lacks a destination.");
        if (plane.Phase == FlightPhase.CrashApproach && !plane.CrashTarget.HasValue || plane.Phase == FlightPhase.Wrecked && plane.Seats.Count > 0) throw new InvalidDataException("Invalid crashed aircraft state.");
        if (plane.Phase == FlightPhase.Unpiloted && (plane.Seats.ContainsKey(0) || plane.TargetAirfieldId.HasValue || plane.CrashTarget.HasValue)) throw new InvalidDataException("Unpiloted flight cannot retain a pilot or autopilot target.");
        if (plane.Phase == FlightPhase.EmergencyLanding && !plane.EmergencyLandingPoint.HasValue) throw new InvalidDataException("Emergency landing lacks a target.");
    }

    internal static void WriteInput(BinaryWriter writer, FlightInput input) { writer.Write(input.Throttle); writer.Write(input.Pitch); writer.Write(input.Yaw); writer.Write(input.Roll); writer.Write(input.Brake); }
    internal static FlightInput ReadInput(BinaryReader reader) => new FlightInput(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadBoolean());
    private static void WriteRotation(BinaryWriter writer, Quaternion value) { writer.Write(value.X); writer.Write(value.Y); writer.Write(value.Z); writer.Write(value.W); }
    private static Quaternion ReadRotation(BinaryReader reader)
    {
        var value = new Quaternion(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        AircraftRules.RequireRotation(value); return value;
    }
    private static float ReadAngle(BinaryReader reader)
    {
        float value = reader.ReadSingle(); if (!AircraftRules.IsFinite(value)) throw new InvalidDataException("Invalid aircraft orientation."); return value;
    }
    internal static void WriteText(BinaryWriter writer, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length > 2048) throw new InvalidDataException("Aircraft text exceeds its limit.");
        writer.Write(bytes.Length); writer.Write(bytes);
    }
    internal static string ReadText(BinaryReader reader)
    {
        int length = ReadCount(reader, 2048);
        byte[] bytes = reader.ReadBytes(length);
        if (bytes.Length != length) throw new EndOfStreamException("Truncated aircraft text.");
        return new UTF8Encoding(false, true).GetString(bytes);
    }
    internal static int ReadCount(BinaryReader reader, int maximum)
    {
        int value = reader.ReadInt32(); if (value < 0 || value > maximum) throw new InvalidDataException("Aircraft collection exceeds its limit."); return value;
    }
    private static int ReadId(BinaryReader reader)
    {
        int value = reader.ReadInt32(); if (value < 1 || value == int.MaxValue) throw new InvalidDataException("Invalid aircraft identity."); return value;
    }
    private static int? ReadOptionalId(BinaryReader reader)
    {
        int value = reader.ReadInt32(); if (value < 0 || value == int.MaxValue) throw new InvalidDataException("Invalid aircraft reference."); return value == 0 ? (int?)null : value;
    }
}
