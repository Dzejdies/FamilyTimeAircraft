using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FamilyTimeAircraft.Core;
using FamilyTimeAircraft.Rendering;
using GAT;
using HarmonyLib;
using UnityEngine;

namespace FamilyTimeAircraft.Diagnostics;

public sealed class AircraftRunwaySmokeScenario
{
    private const float TerrainAboveOrigin = 0.25f;
    private const float MinimumSurfaceGap = 0.05f;
    private const float SurfaceTolerance = 0.001f;
    private readonly AircraftRuntime _runtime;
    private readonly Action<Vector3, Vector3> _setCamera;
    private bool _failed;

    public AircraftRunwaySmokeScenario(AircraftRuntime runtime, Action<Vector3, Vector3> setCamera)
    {
        _runtime = runtime;
        _setCamera = setCamera;
    }

    public IEnumerator Run()
    {
        Vector3 center = GameFlightEnvironment.ToUnity(_runtime.World.Aircrafts.First().Position) + new Vector3(150, 0, 150);
        center.y = Heightfield.RawHeightToWorldHeight(Heightfield.WorldHeightToRawHeight(World.heightfield.GetWorldHeight(center.x, center.z)));
        AircraftTestEnvironment.ClearWater();
        foreach (int angle in new[] { 0, 90 }) yield return VerifyOrientation(center + Vector3.right * angle, angle);
        yield return VerifyFootprintEdge(center);
        VerifyIndependentRendererLifetimes();
        if (_failed) throw new InvalidOperationException("Native runway terrain separation diagnostic failed.");
    }

    private IEnumerator VerifyOrientation(Vector3 center, int angle)
    {
        AircraftTestEnvironment.PrepareRunwaySquare(center);
        yield return new WaitForSecondsRealtime(1);
        Vector3 origin = center - Vector3.up * TerrainAboveOrigin;
        Quaternion rotation = Quaternion.Euler(0, angle, 0);
        Check("runway-native-placement-" + angle, ((GameFlightEnvironment)_runtime.Environment).CanPlaceRunway(GameFlightEnvironment.ToCore(origin), GameFlightEnvironment.ToCore(rotation)));
        _runtime.World.BuildAirfield("solo", GameFlightEnvironment.ToCore(origin), GameFlightEnvironment.ToCore(rotation));
        yield return null;
        yield return null;
        VerifySurfaceHeights(origin, angle);
        foreach (Vector3 offset in new[] { new Vector3(55, 30, 55), new Vector3(-45, 12, 45) })
        {
            _setCamera(origin, offset);
            yield return new WaitForSecondsRealtime(1);
            Capture("runway-" + angle + "-" + (offset.y > 20 ? "high" : "low"));
        }
    }

    private void VerifySurfaceHeights(Vector3 origin, int angle)
    {
        GameObject runway = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None)
            .Where(transform => transform.name == "FamilyTimeAircraft.runway").OrderBy(transform => Vector3.Distance(transform.position, origin)).First().gameObject;
        MeshFilter surface = runway.GetComponentsInChildren<MeshFilter>().Single(filter => filter.name == "Runway");
        float sceneTop = surface.transform.TransformPoint(surface.sharedMesh.bounds.max).y;
        BuildPart template = World.buildPartManager.BuildPartTemplates[_runtime.Blueprints.RunwayIndex];
        Mesh preview = template.meshMaterials.Select(pair => pair.mesh).Single(mesh => mesh.bounds.size.x > 15 && mesh.bounds.size.z > 79);
        float previewTop = origin.y + preview.bounds.max.y;
        float ground = World.heightfield.GetWorldHeight(origin.x, origin.z);
        _runtime.SetNotice("[AircraftRunwayEvidence] angle=" + angle + " origin=" + origin + " ground=" + ground + " sceneTop=" + sceneTop + " blueprintTop=" + previewTop);
        Check("runway-scene-surface-separated-" + angle, sceneTop - ground >= MinimumSurfaceGap - SurfaceTolerance);
        Check("runway-native-blueprint-surface-separated-" + angle, previewTop - ground >= MinimumSurfaceGap - SurfaceTolerance);
    }

    private IEnumerator VerifyFootprintEdge(Vector3 center)
    {
        World.heightfield.SetRawHeight(Mathf.RoundToInt(center.x + 7), Mathf.RoundToInt(center.z + 39), Heightfield.WorldHeightToRawHeight(center.y + 1));
        yield return new WaitForSecondsRealtime(1);
        Check("runway-full-footprint-edge-rejected", !((GameFlightEnvironment)_runtime.Environment).CanPlaceRunway(GameFlightEnvironment.ToCore(center), System.Numerics.Quaternion.Identity));
    }

    private static void Capture(string name)
    {
        string directory = Path.Combine(Application.persistentDataPath, "fta_shots");
        Directory.CreateDirectory(directory);
        ScreenCapture.CaptureScreenshot(Path.Combine(directory, name + ".png"));
    }

    private void VerifyIndependentRendererLifetimes()
    {
        Aircraft plane = _runtime.World.Aircrafts.First();
        var runways = (Dictionary<int, Airfield>)AccessTools.Field(typeof(AircraftWorld), "Runways").GetValue(_runtime.World);
        var scene = (AircraftScene)AccessTools.Field(typeof(AircraftRuntime), "_scene").GetValue(_runtime);
        Check("runway-shared-renderer-id-fixture", runways.ContainsKey(plane.Id));
        runways.Remove(plane.Id);
        try
        {
            scene.Draw(_runtime.World);
            scene.Draw(_runtime.World);
            var planes = (Dictionary<int, GameObject>)AccessTools.Field(typeof(AircraftScene), "_planes").GetValue(scene);
            Check("runway-removal-preserves-aircraft-renderer", planes.TryGetValue(plane.Id, out GameObject rendered) && rendered != null);
        }
        catch (KeyNotFoundException) { Check("runway-removal-preserves-aircraft-renderer", false); }
    }

    private void Check(string name, bool passed)
    {
        _runtime.SetNotice("[AircraftTest] " + name + "=" + (passed ? "PASS" : "FAIL"));
        _failed |= !passed;
    }
}
