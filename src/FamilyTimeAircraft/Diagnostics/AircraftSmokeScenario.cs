using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using FamilyTimeAircraft.Core;
using GAT;
using HarmonyLib;
using UnityEngine;

namespace FamilyTimeAircraft.Diagnostics;

[DefaultExecutionOrder(10000)]
public sealed class AircraftSmokeScenario : MonoBehaviour
{
    private AircraftRuntime _runtime = null!;
    private Vector3? _capturePoint;
    private Vector3 _captureOffset = new(16, 10, 16);
    public void Initialize(AircraftRuntime runtime)
    {
        Application.runInBackground = true;
        _runtime = runtime;
        UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering += SetCaptureCamera;
        StartCoroutine(Run());
    }

    private void SetCaptureCamera(UnityEngine.Rendering.ScriptableRenderContext context, Camera camera)
    {
        if (camera == Camera.main) LateUpdate();
    }

    private void OnDestroy()
    {
        UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering -= SetCaptureCamera;
        AircraftKeyboardFixture.ReleaseKeyboard();
    }

    private void LateUpdate()
    {
        AircraftKeyboardFixture.ApplyNativeCameraAim();
        if (!_capturePoint.HasValue || Camera.main == null) return;
        Vector3 target = _capturePoint.Value + Vector3.up * 2;
        Vector3 viewpoint = _capturePoint.Value + _captureOffset;
        World.playerController.TeleportTo(viewpoint, (target - viewpoint).normalized);
        World.gat.SetInstanceMatrix(World.playerController.PossessedHandle, Matrix4x4.TRS(viewpoint, Quaternion.LookRotation(target - viewpoint), Vector3.one), false, false, false);
        Camera.main.transform.SetPositionAndRotation(viewpoint, Quaternion.LookRotation(target - viewpoint));
    }

    private IEnumerator Run()
    {
        yield return BeginNativeGame();
        while (!AircraftRuntime.IsGameReady) yield return null;
        yield return new WaitForSecondsRealtime(2);
        Screen.SetResolution(1280, 720, false);
        if (World.promptUI != null) World.promptUI.Consent(false);
        if (!_runtime.IsAuthoritative) yield break;
        yield return VerifyNativeBuildMenu();
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-fta-tower-smoke") >= 0)
        {
            yield return new AircraftTowerSmokeScenario(_runtime).Run();
            Check("scenario-complete", true);
            yield break;
        }
        try { RunConstruction(); }
        catch (Exception error) { _runtime.SetNotice("[AircraftTest] FAIL " + error); throw; }
        yield return null;
        Check("materials-consumed", _materials.TrueForAll(material => !System.Linq.Enumerable.Contains(NativeAircraftGame.Rigids(), material)));
        using var bytes = new MemoryStream();
        WorldRecord.Write(bytes, _runtime.World);
        bytes.Position = 0;
        AircraftWorld reloaded = WorldRecord.Read(bytes);
        Check("save-roundtrip", reloaded.Aircrafts.Count == _runtime.World.Aircrafts.Count);
        yield return VerifyNativeSaveLoad();
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-fta-controls-smoke") >= 0)
        {
            yield return new AircraftControlsSmokeScenario(_runtime).Run();
            Check("scenario-complete", true);
            yield break;
        }
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-fta-impact-smoke") >= 0)
        {
            yield return new AircraftImpactSmokeScenario(_runtime).Run();
            Check("scenario-complete", true);
            yield break;
        }
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-fta-parachute-smoke") >= 0)
        {
            var parachutes = new AircraftParachuteSmokeScenario(_runtime, (point, offset) => { _capturePoint = point; _captureOffset = offset; });
            yield return parachutes.Run();
            Check("scenario-complete", true);
            yield break;
        }
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-fta-bailout-smoke") >= 0)
        {
            yield return new AircraftBailoutSmokeScenario(_runtime).Run();
            Check("scenario-complete", true);
            yield break;
        }
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-fta-runway-smoke") >= 0)
        {
            var runway = new AircraftRunwaySmokeScenario(_runtime, (point, offset) => { _capturePoint = point; _captureOffset = offset; });
            yield return runway.Run();
            Check("scenario-complete", true);
            yield break;
        }
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-fta-ejection-restore-smoke") >= 0) yield return VerifyEjectedChildClaimRestore();
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-fta-manual-smoke") >= 0) yield return VerifyManualTakeoff();
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-fta-autopilot-smoke") >= 0) yield return VerifyAutopilotDestination();
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-fta-visual-smoke") >= 0)
        {
            Aircraft plane = System.Linq.Enumerable.First(_runtime.World.Aircrafts);
            _runtime.World.BuildAircraft(plane.Owner, plane.Position + System.Numerics.Vector3.UnitY * 40, plane.Rotation);
        }
        foreach (Aircraft plane in System.Linq.Enumerable.Reverse(_runtime.World.Aircrafts))
        {
            Vector3 point = GameFlightEnvironment.ToUnity(plane.Position);
            Vector3 viewpoint = point + new Vector3(16, 5, 14);
            World.playerController.TeleportTo(viewpoint, (point + Vector3.up * 2 - viewpoint).normalized);
            _capturePoint = point;
            break;
        }
        _runtime.GetComponent<AircraftMenu>().SendMessage("Toggle");
        yield return new WaitForSecondsRealtime(2);
        foreach (Camera camera in Camera.allCameras)
            _runtime.SetNotice("[AircraftCameraEvidence] " + camera.name + " pos=" + camera.transform.position + " mask=" + camera.cullingMask + " target=" + camera.WorldToViewportPoint(_capturePoint!.Value));
        string directory = Path.Combine(Application.persistentDataPath, "fta_shots");
        Directory.CreateDirectory(directory);
        ScreenCapture.CaptureScreenshot(Path.Combine(directory, "aircraft-smoke.png"));
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-fta-visual-smoke") >= 0)
        {
            yield return new WaitForEndOfFrame();
            Rendering.AircraftExplosion.Play(_capturePoint!.Value);
            yield return new WaitForSecondsRealtime(0.08f);
            ScreenCapture.CaptureScreenshot(Path.Combine(directory, "aircraft-explosion-early.png"));
            yield return new WaitForSecondsRealtime(0.42f);
            ScreenCapture.CaptureScreenshot(Path.Combine(directory, "aircraft-explosion-middle.png"));
            yield return new WaitForSecondsRealtime(0.7f);
            ScreenCapture.CaptureScreenshot(Path.Combine(directory, "aircraft-explosion-late.png"));
        }
        Check("scenario-complete", true);
    }

    private IEnumerator VerifyNativeBuildMenu()
    {
        BuildPartUI[] menus = UnityEngine.Object.FindObjectsByType<BuildPartUI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (menus.Length == 0) throw new InvalidOperationException("The native building menu is unavailable.");
        BuildPart expected = World.buildPartManager.BuildPartTemplates[_runtime.Blueprints.AircraftIndex];
        BuildPart? selected = null;
        BuildPartUI menu = menus[0];
        menu.PickBuildPartFor(part => selected = part);
        yield return null;
        yield return null;
        var buttons = (List<BuildPartButton>)AccessTools.Field(typeof(BuildPartUI), "_buttonInstances").GetValue(menu);
        foreach (BuildPartButton button in buttons)
        {
            if ((BuildPart)AccessTools.Field(typeof(BuildPartButton), "_buildPart").GetValue(button) != expected) continue;
            Check("native-aircraft-slot-four", button.transform.GetSiblingIndex() == 3);
            ((UnityEngine.UI.Button)AccessTools.Field(typeof(BuildPartButton), "button").GetValue(button)).onClick.Invoke();
            Check("native-aircraft-button-selection", selected == expected);
            yield break;
        }
        throw new InvalidOperationException("The native aircraft blueprint button is missing.");
    }

    private IEnumerator BeginNativeGame()
    {
        yield return new WaitForSecondsRealtime(3);
        System.Type? sessionType = typeof(World).Assembly.GetType("family.GameSession");
        if (sessionType == null) yield break;
        object? session = AccessTools.Property(sessionType, "Instance")?.GetValue(null);
        if (session == null) yield break;
        string? state = AccessTools.Property(sessionType, "State")?.GetValue(session)?.ToString();
        while (state == "PreparingTitle" || GAT.World.worldSerializer?.IsBusy == true)
        {
            _runtime.SetNotice("[AircraftTest] waiting for native title: " + state);
            yield return new WaitForSecondsRealtime(2);
            state = AccessTools.Property(sessionType, "State")?.GetValue(session)?.ToString();
        }
        _runtime.SetNotice("[AircraftTest] native session starting from " + state);
        if (state == "Playing" || state == "Starting" || state == "Loading") yield break;
        System.Reflection.MethodInfo start = AccessTools.Method(sessionType, "StartNewGame");
        System.Type modeType = start.GetParameters()[1].ParameterType;
        start.Invoke(session, new object[] { 91, System.Enum.ToObject(modeType, 0), false });
    }

    private readonly List<RigidTransform> _materials = new();

    private IEnumerator VerifyNativeSaveLoad()
    {
        string saveName = "fta_smoke_diagnostic_" + System.Diagnostics.Process.GetCurrentProcess().Id;
        Vector3Int pendingPosition = CreatePendingProject();
        Aircraft savedAircraft = System.Linq.Enumerable.First(_runtime.World.Aircrafts);
        _runtime.World.Board(savedAircraft.Id, new Occupant(OccupantKind.Player, 99, savedAircraft.Owner), 8);
        Check("native-save-remote-player-fixture", savedAircraft.Seats.ContainsKey(8));
        int aircraftCount = _runtime.World.Aircrafts.Count;
        byte[] buildState = _runtime.CaptureBuildState();
        bool? saved = null;
        World.worldSerializer.Save(saveName, success => saved = success);
        yield return WaitForOperation(() => saved.HasValue);
        Check("native-save-completed", saved == true);
        _runtime.World.Clear();
        bool? loaded = null;
        World.worldSerializer.Load(saveName, success => loaded = success);
        yield return WaitForOperation(() => loaded.HasValue);
        Check("native-load-completed", loaded == true);
        while (!AircraftRuntime.IsGameReady) yield return null;
        yield return null;
        Check("native-aircraft-restored", _runtime.World.Aircrafts.Count == aircraftCount);
        Check("native-save-stale-player-removed", !System.Linq.Enumerable.Any(_runtime.World.Aircrafts, plane => System.Linq.Enumerable.Any(plane.Seats.Values, occupant => occupant.Kind == OccupantKind.Player && occupant.Id == 99)));
        Check("native-build-state-restored", System.Linq.Enumerable.SequenceEqual(buildState, _runtime.CaptureBuildState()));
        Check("native-pending-project-restored", World.buildPlanManager.IsBuildPlanActive(FindPlan(pendingPosition)));
    }

    private IEnumerator VerifyManualTakeoff()
    {
        Aircraft aircraft = System.Linq.Enumerable.First(_runtime.World.Aircrafts);
        Vector3 origin = GameFlightEnvironment.ToUnity(aircraft.Position);
        PrepareFlightCorridor(origin);
        yield return new WaitForSecondsRealtime(1);
        World.playerController.TeleportTo(origin + Vector3.right * 7, Vector3.forward);
        yield return new WaitForSecondsRealtime(1);
        AircraftMenu menu = _runtime.GetComponent<AircraftMenu>();
        menu.enabled = false;
        _runtime.Dispatch(new AircraftCommand(AircraftCommandKind.BoardPlayer, aircraft.Id, 0));
        Check("manual-pilot-boarded", aircraft.Seats.ContainsKey(0));
        float deadline = Time.realtimeSinceStartup + 20;
        while (aircraft.Position.Y < origin.y + 5 && aircraft.Phase != FlightPhase.Wrecked && Time.realtimeSinceStartup < deadline)
        {
            _runtime.Dispatch(new AircraftCommand(AircraftCommandKind.ManualControl, aircraft.Id, input: new FlightInput(1, 0.2f, 0, 0, false)));
            yield return new WaitForSecondsRealtime(0.05f);
        }
        Check("manual-airborne", aircraft.Phase == FlightPhase.Manual && aircraft.Position.Y >= origin.y + 5);
        Check("manual-airborne-avatar-seat", Vector3.Distance(World.playerController.CachedInstancePosition, GameFlightEnvironment.ToUnity(aircraft.SeatPosition(0))) < 2);
        menu.enabled = true;
    }

    private IEnumerator VerifyEjectedChildClaimRestore()
    {
        Aircraft aircraft = System.Linq.Enumerable.First(_runtime.World.Aircrafts);
        Handle child = World.commandOwnershipService.FindAvailableWorkers(World.gat.instanceCount, GameFlightEnvironment.ToUnity(aircraft.Position), 50, _ => true)[0];
        _runtime.World.Board(aircraft.Id, new Occupant(OccupantKind.Child, child.stableIndex, aircraft.Owner), 0);
        ReloadAircraftWorld();
        aircraft = _runtime.World.FindAircraft(aircraft.Id);
        AccessTools.Property(typeof(Aircraft), nameof(Aircraft.Phase)).SetValue(aircraft, FlightPhase.Manual);
        AccessTools.Property(typeof(Aircraft), nameof(Aircraft.Velocity)).SetValue(aircraft, -System.Numerics.Vector3.UnitY * 20);
        _runtime.World.Tick(0.02f, _runtime.Environment);
        Check("ejected-child-fixture-created", _runtime.World.Ejections.Count == 1);
        foreach (AircraftEvent aircraftEvent in _runtime.World.DrainEvents()) _runtime.RenderEvent(aircraftEvent);
        ReloadAircraftWorld();
        bool free = (bool)AccessTools.Method(typeof(CommandOwnershipService), "IsAvailableWorker", new[] { typeof(Handle) }).Invoke(World.commandOwnershipService, new object[] { child });
        Check("ejected-child-native-claim-restored", !free);
        float deadline = Time.realtimeSinceStartup + 12;
        while (_runtime.World.Ejections.Count > 0 && Time.realtimeSinceStartup < deadline) yield return null;
        Check("restored-ejected-child-landed", _runtime.World.Ejections.Count == 0);
        free = (bool)AccessTools.Method(typeof(CommandOwnershipService), "IsAvailableWorker", new[] { typeof(Handle) }).Invoke(World.commandOwnershipService, new object[] { child });
        Check("restored-ejected-child-native-claim-released", free);
        var velocity = new Vector3[1];
        World.gat.GetInstanceVelocityBuffer().GetData(velocity, 0, World.gat.ResolveHandle(child), 1);
        _runtime.SetNotice("[AircraftTest] landed-native-velocity=" + velocity[0]);
        Check("restored-ejected-child-velocity-cleared", velocity[0].sqrMagnitude < 0.0001f);
    }

    private void ReloadAircraftWorld()
    {
        using var saved = new MemoryStream();
        WorldRecord.Write(saved, _runtime.World);
        saved.Position = 0;
        _runtime.ApplySnapshot(WorldRecord.Read(saved));
    }

    private static void PrepareFlightCorridor(Vector3 origin) => AircraftTestEnvironment.PrepareFlightCorridor(origin);

    private IEnumerator VerifyAutopilotDestination()
    {
        Aircraft plane = System.Linq.Enumerable.First(_runtime.World.Aircrafts);
        Vector3 origin = GameFlightEnvironment.ToUnity(plane.Position);
        Vector3 destination = origin + new Vector3(-320, 0, 80);
        ClearFlightWater();
        PrepareRunway(origin);
        PrepareRunway(destination);
        yield return new WaitForSecondsRealtime(1);
        Airfield departure = _runtime.World.BuildAirfield(plane.Owner, plane.Position, System.Numerics.Quaternion.Identity);
        Airfield arrival = _runtime.World.BuildAirfield(plane.Owner, GameFlightEnvironment.ToCore(destination), System.Numerics.Quaternion.Identity);
        Check("autopilot-native-departure-clear", _runtime.Environment.IsRunwayClear(departure));
        Check("autopilot-native-destination-clear", _runtime.Environment.IsRunwayClear(arrival));
        Handle child = World.commandOwnershipService.FindAvailableWorkers(World.gat.instanceCount, origin, 50, _ => true)[0];
        _runtime.World.Board(plane.Id, new Occupant(OccupantKind.Child, child.stableIndex, plane.Owner), 0);
        _runtime.World.OrderFlight(plane.Id, arrival.Id, plane.Owner, _runtime.Environment);
        float deadline = Time.realtimeSinceStartup + 75;
        float nextEvidence = 0;
        while (plane.Phase != FlightPhase.Parked && plane.Phase != FlightPhase.Wrecked && Time.realtimeSinceStartup < deadline)
        {
            if (Time.realtimeSinceStartup >= nextEvidence)
            {
                _runtime.SetNotice("[AircraftFlightEvidence] phase=" + plane.Phase + " position=" + plane.Position + " ground=" + _runtime.Environment.GroundHeight(plane.Position) + " velocity=" + plane.Velocity);
                nextEvidence = Time.realtimeSinceStartup + 5;
            }
            yield return null;
        }
        _runtime.SetNotice("[AircraftFlightEvidence] finalphase=" + plane.Phase + " position=" + plane.Position + " ground=" + _runtime.Environment.GroundHeight(plane.Position));
        Check("autopilot-native-destination-landed", plane.Phase == FlightPhase.Parked && System.Numerics.Vector3.Distance(plane.Position, arrival.Position) < 5);
    }

    private static void PrepareRunway(Vector3 origin) => AircraftTestEnvironment.PrepareRunway(origin);

    private static void ClearFlightWater() => AircraftTestEnvironment.ClearWater();

    private Vector3Int CreatePendingProject()
    {
        var position = Vector3Int.RoundToInt(World.playerController.CachedInstancePosition + Vector3.right * 40);
        position.y = Mathf.RoundToInt(World.heightfield.GetWorldHeight(position.x, position.z));
        Check("native-pending-project-created", World.buildPlanManager.CreateBuildPlan(_runtime.Blueprints.AircraftIndex, position, BuildPart.Rotation.R_0, 0.01f));
        if (!NativeAircraftGame.HasSingleMaterialConstruction) return position;
        BuildPlanManager.BuildPlan plan = FindPlan(position);
        RigidTransform log = NativeAircraftGame.SpawnRigid(NativeAircraftGame.FindMaterialPrefab(ConstructionMaterial.LOG), position + Vector3.up);
        log.SetFrozen(true);
        Check("native-partial-material-delivered", NativeAircraftGame.Construct(plan, new[] { log }, position));
        Check("native-partial-project-progress", _runtime.Projects.Require(plan).Delivered == 1);
        return position;
    }

    private static IEnumerator WaitForOperation(Func<bool> completed)
    {
        const float operationTimeout = 90;
        float deadline = Time.realtimeSinceStartup + operationTimeout;
        while (!completed() && Time.realtimeSinceStartup < deadline) yield return null;
        if (!completed()) throw new TimeoutException("The native aircraft save/load operation timed out.");
    }

    private void RunConstruction()
    {
        Vector3 player = World.playerController.CachedInstancePosition;
        var position = Vector3Int.RoundToInt(player + Vector3.right * 20);
        position.y = Mathf.RoundToInt(World.heightfield.GetWorldHeight(position.x, position.z));
        bool created = World.buildPlanManager.CreateBuildPlan(_runtime.Blueprints.AircraftIndex, position, BuildPart.Rotation.R_0, 0.01f);
        Check("native-blueprint-created", created);
        if (!created) throw new InvalidOperationException("Aircraft diagnostic cannot create native project.");
        BuildPlanManager.BuildPlan plan = FindPlan(position);
        BuildPart template = World.buildPartManager.BuildPartTemplates[plan.templateIndex];
        SpawnIngredients(_runtime.Blueprints.RecipeFor(plan.templateIndex), position);
        Check("native-material-construction", NativeAircraftGame.Construct(plan, _materials, position));
        Check("aircraft-created", _runtime.World.Aircrafts.Count > 0);
        Check("project-removed", !World.buildPlanManager.IsBuildPlanActive(plan));
    }

    private BuildPlanManager.BuildPlan FindPlan(Vector3Int position)
    {
        var plans = (List<BuildPlanManager.BuildPlan>)AccessTools.Field(typeof(BuildPlanManager), "activeBuildPlans").GetValue(World.buildPlanManager);
        return plans.Find(plan => plan.gridPosition == position && plan.templateIndex == _runtime.Blueprints.AircraftIndex)
            ?? throw new InvalidOperationException("Native aircraft project is missing.");
    }

    private void SpawnIngredients(ConstructionRequirement[] recipe, Vector3 position)
    {
        foreach (ConstructionRequirement requirement in recipe)
        {
            _runtime.SetNotice("[AircraftTest] spawning " + requirement.amount + " " + requirement.req);
            object prefab = NativeAircraftGame.FindMaterialPrefab(requirement.req);
            for (int count = 0; count < requirement.amount; count++)
            {
                RigidTransform rigid = NativeAircraftGame.SpawnRigid(prefab, position + Vector3.up);
                if (rigid == null) throw new InvalidOperationException("Native ingredient spawn failed.");
                rigid.SetFrozen(true);
                _materials.Add(rigid);
            }
        }
    }

    private void Check(string name, bool passed)
    {
        _runtime.SetNotice("[AircraftTest] " + name + "=" + (passed ? "PASS" : "FAIL"));
        if (!passed) throw new InvalidOperationException("Aircraft diagnostic failed: " + name);
    }
}

[HarmonyPatch(typeof(Debug), nameof(Debug.LogException), new[] { typeof(Exception) })]
internal static class AircraftSmokeExceptionLoggingPatch
{
    private static bool Prepare() => Array.IndexOf(Environment.GetCommandLineArgs(), "-fta-smoke") >= 0 || Array.IndexOf(Environment.GetCommandLineArgs(), "-fta-tower-smoke") >= 0;
    private static void Prefix(Exception exception) => AircraftRuntime.Current?.SetNotice("[AircraftTest] native exception: " + exception);
}
