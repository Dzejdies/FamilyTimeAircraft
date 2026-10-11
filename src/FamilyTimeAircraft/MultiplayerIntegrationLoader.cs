using System;
using System.IO;
using System.Reflection;
using BepInEx.Bootstrap;

namespace FamilyTimeAircraft;

internal static class MultiplayerIntegrationLoader
{
    private const string MultiplayerGuid = "dzejdies.familytime.multiplayer";
    private static readonly Version MinimumVersion = new(0, 1, 14);

    public static void Register(AircraftRuntime runtime, string pluginDirectory)
    {
        if (!Chainloader.PluginInfos.TryGetValue(MultiplayerGuid, out BepInEx.PluginInfo info)) return;
        if (info.Metadata.Version < MinimumVersion)
            throw new InvalidOperationException("Family Time Aircraft requires Family Time Multiplayer 0.1.14 or newer.");
        string path = Path.Combine(pluginDirectory, "integrations", "FamilyTimeAircraft.Multiplayer.dll");
        if (!File.Exists(path)) throw new FileNotFoundException("The aircraft multiplayer integration is missing.", path);
        Assembly assembly = Assembly.LoadFrom(path);
        Type bridge = assembly.GetType("FamilyTimeAircraft.Multiplayer.AircraftMultiplayerBridge", true);
        MethodInfo register = bridge.GetMethod("Register", BindingFlags.Public | BindingFlags.Static)
            ?? throw new MissingMethodException("Aircraft multiplayer integration has no Register entrypoint.");
        register.Invoke(null, new object[] { runtime });
    }
}
