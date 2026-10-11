using System;
using System.Collections.Generic;
using FamilyTimeAircraft.Core;
using UnityEngine;

namespace FamilyTimeAircraft.Rendering;

public sealed class AircraftScene : IDisposable
{
    private const float SurfaceRefreshInterval = 0.25f;
    private readonly AircraftModels _models;
    private readonly IFlightEnvironment _environment;
    private readonly AircraftParachuteScene _parachutes;
    private readonly Dictionary<int, (Airfield Field, float Height, float Expires)> _runwaySurfaces = new();
    private readonly Dictionary<int, GameObject> _planes = new();
    private readonly Dictionary<int, bool> _wreckStates = new();
    private readonly Dictionary<int, GameObject> _runways = new();
    private readonly HashSet<int> _present = new();

    public AircraftScene(AircraftModels models, IFlightEnvironment environment)
    {
        _models = models;
        _environment = environment;
        _parachutes = new AircraftParachuteScene(models);
    }

    public void Draw(AircraftWorld world)
    {
        _present.Clear();
        foreach (Aircraft aircraft in world.Aircrafts)
        {
            _present.Add(aircraft.Id);
            DrawAircraft(aircraft);
        }
        RemoveMissing(_planes, id => _wreckStates.Remove(id));
        _present.Clear();
        foreach (Airfield runway in world.Airfields)
        {
            _present.Add(runway.Id);
            DrawRunway(runway);
        }
        RemoveMissing(_runways, id => _runwaySurfaces.Remove(id));
        _parachutes.Draw(world.Ejections);
    }

    private void DrawAircraft(Aircraft aircraft)
    {
        bool wrecked = aircraft.Phase == FlightPhase.Wrecked;
        if (!_planes.TryGetValue(aircraft.Id, out GameObject plane) || _wreckStates[aircraft.Id] != wrecked)
        {
            if (plane != null) UnityEngine.Object.Destroy(plane);
            plane = _models.Instantiate(wrecked ? "wreck" : "aircraft");
            _planes[aircraft.Id] = plane;
            _wreckStates[aircraft.Id] = wrecked;
        }
        plane.transform.SetPositionAndRotation(GameFlightEnvironment.ToUnity(aircraft.Position), GameFlightEnvironment.ToUnity(aircraft.Rotation));
        Transform propeller = plane.transform.Find("Propeller");
        if (propeller != null && !wrecked && aircraft.Phase != FlightPhase.Parked)
            propeller.Rotate(Vector3.forward, 1800 * Time.deltaTime, Space.Self);
    }

    private void DrawRunway(Airfield airfield)
    {
        if (!_runways.TryGetValue(airfield.Id, out GameObject runway))
        {
            runway = _models.Instantiate("runway");
            _runways.Add(airfield.Id, runway);
        }
        Vector3 position = GameFlightEnvironment.ToUnity(airfield.Position);
        position.y = RunwaySurfaceHeight(airfield) + AircraftModels.RunwaySurfaceSeparation - AircraftModels.RunwayModelElevation;
        runway.transform.SetPositionAndRotation(position, GameFlightEnvironment.ToUnity(airfield.Rotation));
    }

    private float RunwaySurfaceHeight(Airfield airfield)
    {
        float now = Time.unscaledTime;
        if (_runwaySurfaces.TryGetValue(airfield.Id, out var cached) && cached.Field == airfield && now < cached.Expires) return cached.Height;
        float height = float.NegativeInfinity;
        foreach (System.Numerics.Vector3 point in airfield.TerrainSamples()) height = Math.Max(height, _environment.GroundHeight(point));
        _runwaySurfaces[airfield.Id] = (airfield, height, now + SurfaceRefreshInterval);
        return height;
    }

    private void RemoveMissing(Dictionary<int, GameObject> entities, Action<int> removeMetadata)
    {
        var removed = new List<int>();
        foreach (KeyValuePair<int, GameObject> entry in entities)
            if (!_present.Contains(entry.Key)) removed.Add(entry.Key);
        foreach (int id in removed)
        {
            UnityEngine.Object.Destroy(entities[id]);
            entities.Remove(id);
            removeMetadata(id);
        }
    }

    public void Dispose()
    {
        _parachutes.Dispose();
        foreach (GameObject entity in _planes.Values) UnityEngine.Object.Destroy(entity);
        foreach (GameObject entity in _runways.Values) UnityEngine.Object.Destroy(entity);
        _planes.Clear();
        _runways.Clear();
        _wreckStates.Clear();
        _runwaySurfaces.Clear();
    }
}
