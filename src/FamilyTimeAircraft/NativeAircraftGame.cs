using System;
using System.Collections.Generic;
using System.Reflection;
using GAT;
using HarmonyLib;
using UnityEngine;

namespace FamilyTimeAircraft;

internal static class NativeAircraftGame
{
    public static bool HasSingleMaterialConstruction => typeof(BuildPart).GetField("constructionMaterial") != null;

    private static readonly MethodInfo Acquire = AccessTools.Method(typeof(CommandOwnershipService), "TryAcquire")
        ?? throw new MissingMethodException("Native command ownership acquisition is unavailable.");
    private static readonly CommandOwnershipKind MoveKind = (CommandOwnershipKind)Enum.Parse(typeof(CommandOwnershipKind),
        Enum.IsDefined(typeof(CommandOwnershipKind), "MoveLease") ? "MoveLease" : "Follow");

    public static bool IsAvailableChild(Handle child) => Array.Exists(World.commandOwnershipService.FindAvailableWorkers(World.gat.instanceCount,
        additionalFilter: candidate => candidate == child), candidate => candidate == child);

    public static bool ClaimChild(Handle child, out CommandOwnershipToken? token)
    {
        object?[] arguments = Acquire.GetParameters().Length == 3
            ? new object?[] { MoveKind, new[] { child }, null }
            : new object?[] { MoveKind, new[] { child }, true, null };
        bool claimed = (bool)Acquire.Invoke(World.commandOwnershipService, arguments);
        token = arguments[arguments.Length - 1] as CommandOwnershipToken;
        return claimed;
    }

    public static IEnumerable<RigidTransform> Rigids()
    {
        RigidTransformManager manager = World.rigidTransformManager;
        PropertyInfo? count = AccessTools.Property(typeof(RigidTransformManager), "Count");
        MethodInfo? get = AccessTools.Method(typeof(RigidTransformManager), "TryGetManagedRigid", new[] { typeof(int), typeof(RigidTransform).MakeByRefType() });
        if (count == null || get == null)
        {
            foreach (UnityEngine.Object entity in UnityEngine.Object.FindObjectsByType(typeof(RigidTransform), FindObjectsSortMode.None))
                yield return (RigidTransform)(object)entity;
            yield break;
        }
        int length = (int)count.GetValue(manager);
        for (int index = 0; index < length; index++)
        {
            object?[] arguments = { index, null };
            if ((bool)get.Invoke(manager, arguments)) yield return (RigidTransform)arguments[1]!;
        }
    }

    public static void DeleteRigid(RigidTransform rigid)
    {
        MethodInfo method = AccessTools.Method(typeof(RigidTransformManager), "Delete", new[] { typeof(RigidTransform) })
            ?? throw new MissingMethodException("Native rigid deletion is unavailable.");
        method.Invoke(World.rigidTransformManager, new object[] { rigid });
    }

    public static object FindMaterialPrefab(ConstructionMaterial material)
    {
        MethodInfo method = AccessTools.Method(typeof(RigidTransformManager), "TryGetPrefab");
        for (int index = 0; index < World.rigidTransformManager.PrefabCount; index++)
        {
            object?[] arguments = { index, null };
            if (!(bool)method.Invoke(World.rigidTransformManager, arguments)) continue;
            object prefab = arguments[1]!;
            object definition = prefab is GameObject gameObject ? gameObject.GetComponent<RigidTransform>() : prefab;
            object? value = AccessTools.Property(definition.GetType(), "SelfMaterial")?.GetValue(definition)
                ?? AccessTools.Field(definition.GetType(), "selfMaterial")?.GetValue(definition);
            if (value is ConstructionMaterial available && available == material) return prefab;
        }
        throw new InvalidOperationException("Native construction material prefab is missing: " + material);
    }

    public static RigidTransform SpawnRigid(object prefab, Vector3 position)
    {
        MethodInfo method = AccessTools.Method(typeof(RigidTransformManager), "Spawn");
        return (RigidTransform)method.Invoke(World.rigidTransformManager, new[] { prefab, (object)position, Quaternion.identity });
    }

    public static bool Construct(BuildPlanManager.BuildPlan plan, IReadOnlyList<RigidTransform> materials, Vector3 soundPosition)
    {
        MethodInfo method = AccessTools.Method(typeof(BuildPlanManager), "TryConstructBuildPlan");
        object? third = method.GetParameters()[2].ParameterType == typeof(Vector3) ? soundPosition : null;
        return (bool)method.Invoke(World.buildPlanManager, new object?[] { plan, materials, third });
    }
}
