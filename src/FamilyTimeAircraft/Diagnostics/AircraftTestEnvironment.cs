using System;
using System.Reflection;
using GAT;
using HarmonyLib;
using UnityEngine;

namespace FamilyTimeAircraft.Diagnostics;

public static class AircraftTestEnvironment
{
    private const int RunwayHalfWidth = 10;
    private const int RunwayHalfLength = 42;
    private const int WaterNavigationResolution = 32;

    public static void PrepareRunway(Vector3 center) => PrepareTerrain(center, RunwayHalfWidth, RunwayHalfLength, RunwayHalfLength);
    public static void PrepareRunwaySquare(Vector3 center) => PrepareTerrain(center, RunwayHalfLength, RunwayHalfLength, RunwayHalfLength);
    public static void PrepareFlightCorridor(Vector3 center) => PrepareTerrain(center, 12, 50, 180);

    public static void ClearWater()
    {
        WaterManager manager = World.waterManager;
        manager.enabled = false;
        MethodInfo clear = AccessTools.Method(typeof(WaterManager), "ClearWaterTexture");
        clear.Invoke(manager, new object[] { AccessTools.Field(typeof(WaterManager), "waterLedger").GetValue(manager) });
        foreach (RenderTexture texture in (RenderTexture[])AccessTools.Field(typeof(WaterManager), "waterWindow").GetValue(manager))
            clear.Invoke(manager, new object[] { texture });
        ClearWaterNavigation();
    }

    private static void ClearWaterNavigation()
    {
        MethodInfo? update = AccessTools.Method(typeof(NavManager), "UpdateWaterNavigation");
        if (update == null) return;
        Type chunk = update.GetParameters()[4].ParameterType.GetElementType()!;
        int chunkCount = WaterNavigationResolution * WaterNavigationResolution;
        update.Invoke(World.navManager, new object[] { 0, 0, WaterNavigationResolution, 0, Array.CreateInstance(chunk, chunkCount) });
    }

    private static void PrepareTerrain(Vector3 center, int halfWidth, int backLength, int frontLength)
    {
        byte elevation = Heightfield.WorldHeightToRawHeight(center.y);
        int centerX = Mathf.RoundToInt(center.x);
        int centerZ = Mathf.RoundToInt(center.z);
        for (int x = centerX - halfWidth; x <= centerX + halfWidth; x++)
            for (int z = centerZ - backLength; z <= centerZ + frontLength; z++)
            {
                World.heightfield.SetRawHeight(x, z, elevation);
                World.propChunkManager.RemoveProp(new Unity.Mathematics.uint2(unchecked((uint)x), unchecked((uint)z)));
            }
    }
}
