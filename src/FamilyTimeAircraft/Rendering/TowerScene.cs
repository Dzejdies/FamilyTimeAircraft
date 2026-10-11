using System.Collections.Generic;
using FamilyTimeAircraft.Core;
using UnityEngine;

namespace FamilyTimeAircraft.Rendering;

public sealed class TowerScene
{
    private const float DebrisHorizontalVelocity = 4;
    private const float DebrisUpwardVelocity = 5;
    private const float UpperSectionVelocity = 0.35f;
    private const float DebrisMass = 0.8f;
    private const float DebrisAngularVelocity = 2;
    private readonly struct DebrisItem
    {
        public int PairId { get; }
        public float Expires { get; }
        public GameObject Object { get; }
        public DebrisItem(int pairId, float expires, GameObject gameObject)
        { PairId = pairId; Expires = expires; Object = gameObject; }
    }

    private readonly AircraftModels _models;
    private readonly List<DebrisItem> _debris = new();
    public TowerScene(AircraftModels models) => _models = models;

    public void Apply(TowerPair pair, TowerEvent towerEvent)
    {
        if (towerEvent.Kind != TowerEventKind.SectionFailed || Count(pair.Id) >= TowerLayout.MaximumActiveDebrisPerPair) return;
        Spawn(pair, pair.Sections[towerEvent.SectionId]);
    }

    public void Tick()
    {
        for (int index = _debris.Count - 1; index >= 0; index--)
            if (Time.time >= _debris[index].Expires) RemoveAt(index);
    }

    private int Count(int pairId)
    {
        int count = 0;
        foreach (DebrisItem item in _debris) if (item.PairId == pairId) count++;
        return count;
    }

    private void Spawn(TowerPair pair, TowerSectionState section)
    {
        GameObject debris;
        try { debris = _models.InstantiateTowerDebris(section.Id % TowerLayout.DebrisVariants); }
        catch (System.IO.InvalidDataException) { return; }
        debris.transform.SetPositionAndRotation(SectionPosition(pair, section), GameFlightEnvironment.ToUnity(pair.Rotation));
        Rigidbody body = debris.AddComponent<Rigidbody>();
        body.mass = DebrisMass;
        body.linearVelocity = DebrisVelocity(pair, section);
        body.angularVelocity = AngularVelocity(section);
        _debris.Add(new DebrisItem(pair.Id, Time.time + TowerLayout.DebrisLifetimeSeconds, debris));
    }

    private static Vector3 SectionPosition(TowerPair pair, TowerSectionState section)
    {
        float towerOffset = section.TowerIndex == 0 ? -TowerLayout.HorizontalTowerOffset : TowerLayout.HorizontalTowerOffset;
        Vector3 localPosition = new(towerOffset, section.Level * TowerLayout.SectionHeight + TowerLayout.SectionHeight * 0.5f, 0);
        return GameFlightEnvironment.ToUnity(pair.Position) + GameFlightEnvironment.ToUnity(pair.Rotation) * localPosition;
    }

    private static Vector3 DebrisVelocity(TowerPair pair, TowerSectionState section)
    {
        float outward = section.TowerIndex == 0 ? -1 : 1;
        Vector3 localDirection = new Vector3(outward, 0, section.Level % 2 == 0 ? 1 : -1).normalized;
        Vector3 direction = GameFlightEnvironment.ToUnity(pair.Rotation) * localDirection;
        float heightBoost = section.Level * UpperSectionVelocity;
        return direction * DebrisHorizontalVelocity + Vector3.up * (DebrisUpwardVelocity + heightBoost);
    }

    private static Vector3 AngularVelocity(TowerSectionState section) => new(
        section.Level % 2 == 0 ? DebrisAngularVelocity : -DebrisAngularVelocity,
        section.TowerIndex == 0 ? -DebrisAngularVelocity : DebrisAngularVelocity,
        DebrisAngularVelocity);

    private void RemoveAt(int index)
    {
        Object.Destroy(_debris[index].Object);
        _debris.RemoveAt(index);
    }

    public void Dispose()
    {
        for (int index = _debris.Count - 1; index >= 0; index--) RemoveAt(index);
    }
}
