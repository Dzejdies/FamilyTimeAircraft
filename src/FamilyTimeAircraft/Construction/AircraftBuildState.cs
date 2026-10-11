using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using GAT;
using HarmonyLib;
using UnityEngine;

namespace FamilyTimeAircraft.Construction;

public sealed class AircraftBuildState
{
    private const int MaximumPlans = 256;
    private const int MaximumOwnerBytes = 2048;
    private readonly AircraftRuntime _runtime;
    private byte[]? _pending;
    private bool _restoreMissing;

    private readonly struct Entry
    {
        public int Kind { get; }
        public Vector3Int Position { get; }
        public BuildPart.Rotation Rotation { get; }
        public string Owner { get; }
        public int Delivered { get; }
        public Entry(int kind, Vector3Int position, BuildPart.Rotation rotation, string owner, int delivered)
        { Kind = kind; Position = position; Rotation = rotation; Owner = owner; Delivered = delivered; }
    }

    public AircraftBuildState(AircraftRuntime runtime) => _runtime = runtime;
    private static List<BuildPlanManager.BuildPlan> Plans => GAT.World.buildPlanManager != null
        ? (List<BuildPlanManager.BuildPlan>)AccessTools.Field(typeof(BuildPlanManager), "activeBuildPlans").GetValue(GAT.World.buildPlanManager)
        : new List<BuildPlanManager.BuildPlan>();

    public byte[] Capture()
    {
        var entries = new List<BuildPlanManager.BuildPlan>();
        foreach (BuildPlanManager.BuildPlan plan in Plans)
            if (_runtime.Blueprints.IsCustom(plan.templateIndex)) entries.Add(plan);
        if (entries.Count > MaximumPlans) throw new InvalidOperationException("Too many aircraft construction projects.");
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        writer.Write(3);
        writer.Write(entries.Count);
        foreach (BuildPlanManager.BuildPlan plan in entries) WriteEntry(writer, plan);
        writer.Write(_runtime.ReservedChildIds.Count);
        foreach (int id in _runtime.ReservedChildIds) writer.Write(id);
        return stream.ToArray();
    }

    private void WriteEntry(BinaryWriter writer, BuildPlanManager.BuildPlan plan)
    {
        AircraftConstructionProjects.Project project = _runtime.Projects.Require(plan);
        int kind = plan.templateIndex == _runtime.Blueprints.AircraftIndex ? 0
            : plan.templateIndex == _runtime.Blueprints.RunwayIndex ? 1 : 2;
        writer.Write(kind);
        writer.Write(plan.gridPosition.x); writer.Write(plan.gridPosition.y); writer.Write(plan.gridPosition.z);
        writer.Write((int)plan.rotation);
        byte[] owner = Encoding.UTF8.GetBytes(project.Owner);
        writer.Write(owner.Length); writer.Write(owner); writer.Write(project.Delivered);
    }

    public void Apply(byte[] data, bool restoreMissing)
    {
        Read(data, out _);
        _pending = (byte[])data.Clone();
        _restoreMissing = restoreMissing;
        Tick();
    }

    public void Tick()
    {
        if (_pending == null || !AircraftRuntime.IsGameReady) return;
        List<Entry> entries = Read(_pending, out int[] reservedIds);
        _runtime.ApplyReservedChildIds(reservedIds);
        bool missing = false;
        var existing = new List<BuildPlanManager.BuildPlan>(Plans);
        foreach (BuildPlanManager.BuildPlan plan in existing)
            if (_runtime.Blueprints.IsCustom(plan.templateIndex) && !MatchesAny(entries, plan))
            {
                GAT.World.buildPlanManager.RemoveBuildPlan(plan);
                _runtime.Projects.Remove(plan);
            }
        foreach (Entry entry in entries) if (!ApplyEntry(entry)) missing = true;
        if (!missing) _pending = null;
    }

    private bool ApplyEntry(Entry entry)
    {
        int template = TemplateFor(entry.Kind);
        BuildPlanManager.BuildPlan? plan = Plans.Find(candidate => candidate.templateIndex == template && candidate.gridPosition == entry.Position && candidate.rotation == entry.Rotation);
        if (plan == null && _restoreMissing)
        {
            if (!GAT.World.buildPlanManager.CreateBuildPlan(template, entry.Position, entry.Rotation, 0.01f))
                throw new InvalidDataException("Cannot restore aircraft construction project.");
            plan = Plans.Find(candidate => candidate.templateIndex == template && candidate.gridPosition == entry.Position && candidate.rotation == entry.Rotation);
        }
        if (plan == null) return false;
        _runtime.Projects.Track(plan, entry.Owner, entry.Delivered);
        return true;
    }

    private bool MatchesAny(List<Entry> entries, BuildPlanManager.BuildPlan plan) => entries.Exists(entry =>
        TemplateFor(entry.Kind) == plan.templateIndex && entry.Position == plan.gridPosition && entry.Rotation == plan.rotation);

    private int TemplateFor(int kind) => kind switch
    {
        0 => _runtime.Blueprints.AircraftIndex,
        1 => _runtime.Blueprints.RunwayIndex,
        2 => _runtime.Blueprints.TowerPairIndex,
        _ => throw new InvalidDataException("Unknown aircraft construction project kind.")
    };

    private static List<Entry> Read(byte[] data, out int[] reservedIds)
    {
        if (data == null || data.Length > 1024 * 1024) throw new InvalidDataException("Aircraft construction state exceeds limits.");
        using var stream = new MemoryStream(data, false);
        using var reader = new BinaryReader(stream);
        int version = reader.ReadInt32();
        if (version < 1 || version > 3) throw new InvalidDataException("Unknown aircraft construction state version.");
        int count = reader.ReadInt32();
        if (count < 0 || count > MaximumPlans) throw new InvalidDataException("Too many aircraft construction projects.");
        var entries = new List<Entry>();
        for (int index = 0; index < count; index++) entries.Add(ReadEntry(reader, version));
        reservedIds = version >= 2 ? ReadReservedChildren(reader) : Array.Empty<int>();
        if (stream.Position != stream.Length) throw new InvalidDataException("Trailing aircraft construction state.");
        return entries;
    }

    private static int[] ReadReservedChildren(BinaryReader reader)
    {
        int count = reader.ReadInt32();
        if (count < 0 || count > Core.AircraftRules.MaximumAircraft * 9) throw new InvalidDataException("Too many aircraft child reservations.");
        var ids = new int[count];
        var unique = new HashSet<int>();
        for (int index = 0; index < count; index++)
        {
            int id = reader.ReadInt32();
            if ((uint)id >= ushort.MaxValue || !unique.Add(id)) throw new InvalidDataException("Invalid aircraft child reservation identity.");
            ids[index] = id;
        }
        return ids;
    }

    private static Entry ReadEntry(BinaryReader reader, int version)
    {
        int kind = reader.ReadInt32();
        var position = new Vector3Int(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
        int rotation = reader.ReadInt32();
        int length = reader.ReadInt32();
        if ((uint)kind > (version >= 3 ? 2u : 1u) || (uint)rotation > 3 || length < 1 || length > MaximumOwnerBytes
            || Math.Abs((long)position.x) >= 2048 || Math.Abs((long)position.z) >= 2048 || position.y < -16 || position.y > 256)
            throw new InvalidDataException("Invalid aircraft project placement or owner.");
        byte[] owner = reader.ReadBytes(length);
        if (owner.Length != length) throw new EndOfStreamException();
        string ownerName = Core.AircraftRules.RequireOwner(Encoding.UTF8.GetString(owner));
        int delivered = reader.ReadInt32();
        int maximumDelivered = kind == 2 ? 72 : 48;
        if (delivered < 0 || delivered > maximumDelivered) throw new InvalidDataException("Invalid aircraft project material progress.");
        return new Entry(kind, position, (BuildPart.Rotation)rotation, ownerName, delivered);
    }

    public void Clear() { _pending = null; _restoreMissing = false; }
}
