using System;
using System.Collections.Generic;
using System.Numerics;

namespace FamilyTimeAircraft.Core;

public enum TowerSectionPhase : byte { Intact, Damaged, Collapsing, Removed }
public enum TowerEventKind : byte { SectionDamaged, SectionFailed }

public sealed class TowerSectionState
{
    internal TowerSectionState(int id, int towerIndex, int level)
    {
        Id = id;
        TowerIndex = towerIndex;
        Level = level;
        Integrity = TowerRules.InitialIntegrity;
    }

    public int Id { get; }
    public int TowerIndex { get; }
    public int Level { get; }
    public float Integrity { get; internal set; }
    public TowerSectionPhase Phase { get; internal set; }
}

public sealed class TowerPair
{
    internal TowerPair(int id, string owner, Vector3 position, Quaternion rotation)
    {
        Id = id;
        Owner = owner;
        Position = position;
        Rotation = rotation;
        Sections = Array.AsReadOnly(TowerLayout.CreateSections());
    }

    public int Id { get; }
    public string Owner { get; }
    public Vector3 Position { get; }
    public Quaternion Rotation { get; }
    public IReadOnlyList<TowerSectionState> Sections { get; }
}

public sealed class TowerEvent
{
    internal TowerEvent(long sequence, int towerPairId, int towerIndex, int sectionId, TowerEventKind kind, float integrity)
    {
        Sequence = sequence;
        TowerPairId = towerPairId;
        TowerIndex = towerIndex;
        SectionId = sectionId;
        Kind = kind;
        Integrity = integrity;
    }

    public long Sequence { get; }
    public int TowerPairId { get; }
    public int TowerIndex { get; }
    public int SectionId { get; }
    public TowerEventKind Kind { get; }
    public float Integrity { get; }
}

internal readonly struct TowerDamageResult
{
    internal TowerDamageResult(IReadOnlyList<TowerSectionState> damagedSections)
    {
        DamagedSections = damagedSections;
    }

    internal IReadOnlyList<TowerSectionState> DamagedSections { get; }
}
