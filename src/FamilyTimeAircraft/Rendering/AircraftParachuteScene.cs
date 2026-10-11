using System;
using System.Collections.Generic;
using FamilyTimeAircraft.Core;
using UnityEngine;

namespace FamilyTimeAircraft.Rendering;

public sealed class AircraftParachuteScene : IDisposable
{
    private const float DeploymentSeconds = 0.35f;
    private const float MinimumDeploymentScale = 0.15f;
    private const float HarnessAttachmentHeight = 0.9f;
    private readonly AircraftModels _models;
    private readonly Dictionary<string, GameObject> _canopies = new();
    private readonly HashSet<string> _present = new();

    public AircraftParachuteScene(AircraftModels models) => _models = models;

    public void Draw(IEnumerable<EjectedOccupant> occupants)
    {
        _present.Clear();
        foreach (EjectedOccupant occupant in occupants)
        {
            if (!occupant.ParachuteTarget.HasValue) continue;
            _present.Add(occupant.Occupant.Key);
            DrawOccupant(occupant);
        }
        RemoveLandedCanopies();
    }

    private void DrawOccupant(EjectedOccupant occupant)
    {
        string key = occupant.Occupant.Key;
        if (!_canopies.TryGetValue(key, out GameObject canopy))
        {
            canopy = _models.Instantiate("parachute");
            _canopies.Add(key, canopy);
        }
        Vector3 heading = GameFlightEnvironment.ToUnity(occupant.Velocity);
        heading.y = 0;
        Quaternion rotation = heading.sqrMagnitude > 0.01f ? Quaternion.LookRotation(heading) : Quaternion.identity;
        canopy.transform.SetPositionAndRotation(GameFlightEnvironment.ToUnity(occupant.Position) + Vector3.up * HarnessAttachmentHeight, rotation);
        canopy.transform.localScale = Vector3.one * Mathf.Lerp(MinimumDeploymentScale, 1, Mathf.Clamp01(occupant.Elapsed / DeploymentSeconds));
    }

    private void RemoveLandedCanopies()
    {
        var removed = new List<string>();
        foreach (string key in _canopies.Keys)
            if (!_present.Contains(key)) removed.Add(key);
        foreach (string key in removed)
        {
            UnityEngine.Object.Destroy(_canopies[key]);
            _canopies.Remove(key);
        }
    }

    public void Dispose()
    {
        foreach (GameObject canopy in _canopies.Values) UnityEngine.Object.Destroy(canopy);
        _canopies.Clear();
        _present.Clear();
    }
}
