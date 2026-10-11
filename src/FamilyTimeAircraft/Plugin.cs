using System;
using System.IO;
using BepInEx;
using HarmonyLib;

namespace FamilyTimeAircraft;

[BepInPlugin(Guid, Name, Version)]
[BepInDependency("dzejdies.familytime.multiplayer", BepInDependency.DependencyFlags.SoftDependency)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string Guid = "dzejdies.familytime.aircraft";
    public const string Name = "Family Time Aircraft";
    public const string Version = "0.1.3";
    private Harmony? _harmony;

    private void Awake()
    {
        string directory = Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? throw new InvalidOperationException("Aircraft plugin directory is unavailable.");
        AircraftRuntime runtime = gameObject.AddComponent<AircraftRuntime>();
        runtime.enabled = false;
        try { InitializeRuntime(runtime, directory); }
        catch { ReleaseRuntime(runtime); throw; }
        Logger.LogInfo(Name + " " + Version + " ready.");
    }

    private void InitializeRuntime(AircraftRuntime runtime, string directory)
    {
        runtime.Controls = new AircraftControls(Config);
        runtime.Initialize(Logger, Path.Combine(directory, "assets"));
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-fta-smoke") >= 0 || Array.IndexOf(Environment.GetCommandLineArgs(), "-fta-tower-smoke") >= 0)
            gameObject.AddComponent<Diagnostics.AircraftSmokeScenario>().Initialize(runtime);
        _harmony = new Harmony(Guid);
        _harmony.PatchAll(typeof(Plugin).Assembly);
        MultiplayerIntegrationLoader.Register(runtime, directory);
        runtime.enabled = true;
    }

    private void ReleaseRuntime(AircraftRuntime runtime)
    {
        runtime.enabled = false;
        _harmony?.UnpatchSelf();
        foreach (UnityEngine.MonoBehaviour component in gameObject.GetComponents<UnityEngine.MonoBehaviour>())
        {
            string assembly = component.GetType().Assembly.GetName().Name!;
            if (component != this && (assembly == "FamilyTimeAircraft" || assembly == "FamilyTimeAircraft.Multiplayer")) Destroy(component);
        }
    }

    private void OnDestroy() => _harmony?.UnpatchSelf();
}
