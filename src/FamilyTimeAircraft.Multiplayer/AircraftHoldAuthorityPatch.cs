using FamilyTimeMultiplayer.Core.Protocol;
using FamilyTimeMultiplayer.Holds;
using HarmonyLib;

namespace FamilyTimeAircraft.Multiplayer;

[HarmonyPatch(typeof(HostHoldRegistry), "TryBeginAgent")]
internal static class AircraftHoldAuthorityPatch
{
    private static bool Prefix(HoldMessage message, ref string? __result)
    {
        if (AircraftRuntime.Current?.IsChildReserved(message.Target.Id) != true) return true;
        __result = "This child is boarding, seated, or being ejected from an aircraft.";
        return false;
    }
}
