using System;
using FamilyTimeMultiplayer.Core.Protocol;
using FamilyTimeMultiplayer.Game;
using HarmonyLib;

namespace FamilyTimeAircraft.Multiplayer;

[HarmonyPatch(typeof(HostPlayerAuthority), nameof(HostPlayerAuthority.AcceptAddonLook))]
public static class AircraftRemoteLookReceiveProbe
{
    private static float _lastPitch = float.NaN;
    private static bool Prepare() => Array.IndexOf(Environment.GetCommandLineArgs(), "-fta-remote-cursor-smoke") >= 0;

    private static void Postfix(int id, ClientPoseMessage authenticatedLook)
    {
        if (Math.Abs(authenticatedLook.PitchDegrees - _lastPitch) < 1) return;
        _lastPitch = authenticatedLook.PitchDegrees;
        ClientPoseMessage command = HostPlayerAuthority.RequireCommandPose(id);
        AircraftRuntime.Current?.SetNotice("[AircraftRemoteLookEvidence] player=" + id + " receivedYaw=" + authenticatedLook.YawDegrees
            + " receivedPitch=" + authenticatedLook.PitchDegrees + " commandYaw=" + command.YawDegrees + " commandPitch=" + command.PitchDegrees
            + " assembly=" + typeof(HostPlayerAuthority).Assembly.Location);
    }
}
