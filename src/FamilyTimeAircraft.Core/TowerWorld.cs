using System;
using System.Collections.Generic;
using System.Numerics;

namespace FamilyTimeAircraft.Core;

public sealed partial class AircraftWorld
{
    private readonly Dictionary<int, TowerPair> _towerPairs = new Dictionary<int, TowerPair>();
    private readonly Dictionary<(int PairId, int SectionId), long> _lastTowerImpacts = new Dictionary<(int, int), long>();
    private readonly List<TowerEvent> _towerEvents = new List<TowerEvent>();
    private long _towerEventSequence;
    private int _nextTowerPairId = 1;
    internal long TowerEventSequence => _towerEventSequence;
    internal int NextTowerPairId => _nextTowerPairId;
    public IReadOnlyCollection<TowerPair> TowerPairs => _towerPairs.Values;

    public TowerPair BuildTowerPair(string owner, Vector3 position, Quaternion rotation)
    {
        AircraftRules.RequireOwner(owner);
        AircraftRules.RequirePosition(position);
        AircraftRules.RequireRotation(rotation);
        if (_towerPairs.Count >= TowerLayout.MaximumTowerPairs) throw new InvalidOperationException("Tower pair limit reached.");
        var pair = new TowerPair(_nextTowerPairId++, owner, position, rotation);
        _towerPairs.Add(pair.Id, pair);
        return pair;
    }

    public TowerPair FindTowerPair(int id) => _towerPairs.TryGetValue(id, out TowerPair pair) ? pair : throw new InvalidOperationException("Unknown tower pair.");

    public IReadOnlyList<TowerEvent> ApplyTowerImpact(int towerPairId, int sectionId, float speed, Vector3 direction, Vector3 surfaceNormal, long impactSequence)
    {
        TowerRules.RequireImpact(speed, direction, surfaceNormal);
        if (impactSequence < 1) throw new ArgumentOutOfRangeException(nameof(impactSequence));
        TowerPair pair = FindTowerPair(towerPairId);
        TowerSectionState target = FindSection(pair, sectionId);
        var impactKey = (towerPairId, sectionId);
        if (_lastTowerImpacts.TryGetValue(impactKey, out long lastSequence) && impactSequence <= lastSequence)
            return Array.Empty<TowerEvent>();
        _lastTowerImpacts[impactKey] = impactSequence;
        float strength = TowerRules.ImpactStrength(speed, direction, surfaceNormal);
        TowerDamageResult result = TowerDamageModel.Apply(pair, target.Id, strength);
        return EmitTowerEvents(pair.Id, result);
    }

    public IReadOnlyList<TowerEvent> DrainTowerEvents()
    {
        TowerEvent[] events = _towerEvents.ToArray();
        _towerEvents.Clear();
        return events;
    }

    internal void ClearTowers()
    {
        _towerPairs.Clear();
        _lastTowerImpacts.Clear();
        _towerEvents.Clear();
        _towerEventSequence = 0;
        _nextTowerPairId = 1;
    }

    internal TowerPair RestoreTowerPair(int id, string owner, Vector3 position, Quaternion rotation, float[] integrity, TowerSectionPhase[] phases)
    {
        if (id < 1 || id == int.MaxValue || _towerPairs.Count >= TowerLayout.MaximumTowerPairs || _towerPairs.ContainsKey(id))
            throw new InvalidOperationException("Invalid saved tower pair identity.");
        if (integrity.Length != TowerLayout.SectionsPerPair || phases.Length != TowerLayout.SectionsPerPair)
            throw new InvalidOperationException("Invalid saved tower sections.");
        var pair = new TowerPair(id, owner, position, rotation);
        for (int sectionIndex = 0; sectionIndex < pair.Sections.Count; sectionIndex++)
        {
            if (!AircraftRules.IsFinite(integrity[sectionIndex]) || integrity[sectionIndex] < 0 || integrity[sectionIndex] > TowerRules.InitialIntegrity ||
                !Enum.IsDefined(typeof(TowerSectionPhase), phases[sectionIndex]) || phases[sectionIndex] == TowerSectionPhase.Removed && integrity[sectionIndex] != 0)
                throw new InvalidOperationException("Invalid saved tower section state.");
            pair.Sections[sectionIndex].Integrity = integrity[sectionIndex];
            pair.Sections[sectionIndex].Phase = phases[sectionIndex];
        }
        _towerPairs.Add(pair.Id, pair);
        _nextTowerPairId = Math.Max(_nextTowerPairId, checked(id + 1));
        return pair;
    }

    internal void RestoreTowerEventSequence(long sequence)
    {
        if (sequence < 0) throw new InvalidOperationException("Invalid saved tower event sequence.");
        _towerEventSequence = sequence;
    }

    internal void RestoreNextTowerPairId(int nextId)
    {
        if (nextId < _nextTowerPairId || nextId == int.MaxValue) throw new InvalidOperationException("Invalid saved tower pair sequence.");
        _nextTowerPairId = nextId;
    }

    public bool ApplyTowerEvent(TowerEvent towerEvent)
    {
        if (towerEvent == null || towerEvent.Sequence <= _towerEventSequence) return false;
        if (!Enum.IsDefined(typeof(TowerEventKind), towerEvent.Kind)) throw new ArgumentException("Invalid tower event kind.", nameof(towerEvent));
        TowerPair pair = FindTowerPair(towerEvent.TowerPairId);
        TowerSectionState section = FindSection(pair, towerEvent.SectionId);
        if (section.TowerIndex != towerEvent.TowerIndex || !AircraftRules.IsFinite(towerEvent.Integrity) || towerEvent.Integrity < 0 || towerEvent.Integrity > TowerRules.InitialIntegrity)
            throw new ArgumentException("Invalid tower event.", nameof(towerEvent));
        section.Integrity = towerEvent.Integrity;
        section.Phase = towerEvent.Kind == TowerEventKind.SectionFailed ? TowerSectionPhase.Collapsing : TowerSectionPhase.Damaged;
        _towerEventSequence = towerEvent.Sequence;
        return true;
    }

    private IReadOnlyList<TowerEvent> EmitTowerEvents(int pairId, TowerDamageResult result)
    {
        var events = new List<TowerEvent>(result.DamagedSections.Count);
        foreach (TowerSectionState section in result.DamagedSections)
        {
            TowerEventKind kind = section.Phase == TowerSectionPhase.Collapsing ? TowerEventKind.SectionFailed : TowerEventKind.SectionDamaged;
            var towerEvent = new TowerEvent(++_towerEventSequence, pairId, section.TowerIndex, section.Id, kind, section.Integrity);
            _towerEvents.Add(towerEvent);
            events.Add(towerEvent);
        }
        return events;
    }

    private static TowerSectionState FindSection(TowerPair pair, int sectionId) =>
        sectionId >= 0 && sectionId < pair.Sections.Count ? pair.Sections[sectionId] : throw new ArgumentOutOfRangeException(nameof(sectionId));
}
