using System;
using System.Collections.Generic;
using FamilyTimeAircraft.Core;
using GAT;
using HarmonyLib;

namespace FamilyTimeAircraft.Construction;

public sealed class AircraftConstructionProjects
{
    public sealed class Project
    {
        public string Owner { get; }
        public int Delivered { get; internal set; }
        public Project(string owner, int delivered = 0) { Owner = owner; Delivered = delivered; }
    }

    private readonly AircraftRuntime _runtime;
    private readonly Dictionary<BuildPlanManager.BuildPlan, Project> _projects = new();
    public AircraftConstructionProjects(AircraftRuntime runtime) => _runtime = runtime;
    public void Track(BuildPlanManager.BuildPlan plan, string owner, int delivered = 0) => _projects[plan] = new Project(owner, delivered);
    public Project Require(BuildPlanManager.BuildPlan plan) => _projects.TryGetValue(plan, out Project project)
        ? project : throw new InvalidOperationException("Aircraft project has no authoritative owner.");
    public void Remove(BuildPlanManager.BuildPlan plan) => _projects.Remove(plan);
    public void Clear() => _projects.Clear();

    public ConstructionMaterial NextMaterial(BuildPlanManager.BuildPlan plan)
    {
        int remaining = Require(plan).Delivered;
        foreach (ConstructionRequirement requirement in _runtime.Blueprints.RecipeFor(plan.templateIndex))
        {
            if (remaining < requirement.amount) return requirement.req;
            remaining -= requirement.amount;
        }
        return ConstructionMaterial.NONE;
    }

    public bool Deliver(BuildPlanManager manager, BuildPlanManager.BuildPlan plan, IReadOnlyList<RigidTransform> materials)
    {
        if (!_runtime.IsAuthoritative || !manager.IsBuildPlanActive(plan)) return false;
        Project project = Require(plan);
        bool supplied = false;
        foreach (RigidTransform rigid in materials)
        {
            if (NextMaterial(plan) == ConstructionMaterial.NONE) break;
            if (rigid == null || rigid.SelfMaterial != NextMaterial(plan)) continue;
            NativeAircraftGame.DeleteRigid(rigid);
            project.Delivered++;
            supplied = true;
        }
        if (NextMaterial(plan) == ConstructionMaterial.NONE)
        {
            bool complete = manager.CompleteBuildPlan(plan);
            if (complete) manager.PlayConstructionSound(plan.gridPosition);
            return complete;
        }
        return supplied;
    }
}

[HarmonyPatch(typeof(BuildPlanManager), nameof(BuildPlanManager.CreateBuildPlan))]
internal static class TrackAircraftProjectOwnerPatch
{
    private static void Postfix(int templateIndex, UnityEngine.Vector3Int gridPosition, BuildPart.Rotation rotation, bool __result)
    {
        AircraftRuntime? runtime = AircraftRuntime.Current;
        if (!__result || runtime == null || !runtime.Blueprints.IsCustom(templateIndex)) return;
        var plans = (List<BuildPlanManager.BuildPlan>)AccessTools.Field(typeof(BuildPlanManager), "activeBuildPlans").GetValue(GAT.World.buildPlanManager);
        BuildPlanManager.BuildPlan? plan = plans.Find(candidate => candidate.templateIndex == templateIndex && candidate.gridPosition == gridPosition && candidate.rotation == rotation);
        if (plan != null) runtime.Projects.Track(plan, runtime.CommandOwner?.Invoke() ?? runtime.Owner);
    }
}

[HarmonyPatch(typeof(BuildPlanManager), nameof(BuildPlanManager.TryConstructBuildPlan))]
internal static class DeliverAircraftMaterialsPatch
{
    private static bool Prefix(BuildPlanManager __instance, BuildPlanManager.BuildPlan buildPlan, IReadOnlyList<RigidTransform> materials, ref bool __result)
    {
        AircraftRuntime? runtime = AircraftRuntime.Current;
        if (runtime == null || !NativeAircraftGame.HasSingleMaterialConstruction || !runtime.Blueprints.IsCustom(buildPlan.templateIndex)) return true;
        __result = runtime.Projects.Deliver(__instance, buildPlan, materials);
        return false;
    }
}

[HarmonyPatch]
internal static class AircraftNextMaterialPatch
{
    private static bool Prepare() => NativeAircraftGame.HasSingleMaterialConstruction;
    private static System.Reflection.MethodBase TargetMethod() => AccessTools.Method(typeof(ConstructionScheduler), "TryGetRequirement");
    private static bool Prefix(BuildPlanManager.BuildPlan plan, ref ConstructionMaterial requirement, ref bool __result)
    {
        AircraftRuntime? runtime = AircraftRuntime.Current;
        if (runtime == null || !runtime.Blueprints.IsCustom(plan.templateIndex)) return true;
        requirement = runtime.Projects.NextMaterial(plan);
        __result = true;
        return false;
    }
}

[HarmonyPatch]
internal static class ContinueAircraftConstructionPatch
{
    private static bool Prepare() => NativeAircraftGame.HasSingleMaterialConstruction;
    private static System.Reflection.MethodBase TargetMethod() => AccessTools.Method(typeof(ConstructionScheduler), "OnMaterialDelivered");
    private static void Postfix(ConstructionScheduler __instance, object job)
    {
        AircraftRuntime? runtime = AircraftRuntime.Current;
        if (runtime == null || !runtime.IsAuthoritative) return;
        var plan = (BuildPlanManager.BuildPlan)AccessTools.Field(job.GetType(), "buildPlan").GetValue(job);
        if (runtime.Blueprints.IsCustom(plan.templateIndex) && GAT.World.buildPlanManager.IsBuildPlanActive(plan))
            AccessTools.Method(typeof(ConstructionScheduler), "TryStartConstructionCommand").Invoke(__instance, new object[] { plan });
    }
}

[HarmonyPatch(typeof(BuildPlanManager), nameof(BuildPlanManager.TryGetConstructionMaterials))]
internal static class SelectAircraftConstructionMaterialPatch
{
    private static bool Prepare() => NativeAircraftGame.HasSingleMaterialConstruction;
    private static bool Prefix(BuildPlanManager.BuildPlan buildPlan, IReadOnlyList<RigidTransform> candidates, List<RigidTransform> materials, ref bool __result)
    {
        AircraftRuntime? runtime = AircraftRuntime.Current;
        if (runtime == null || !runtime.Blueprints.IsCustom(buildPlan.templateIndex)) return true;
        materials.Clear();
        ConstructionMaterial required = runtime.Projects.NextMaterial(buildPlan);
        __result = required == ConstructionMaterial.NONE;
        if (__result || candidates == null) return false;
        foreach (RigidTransform candidate in candidates)
            if (candidate != null && candidate.SelfMaterial == required)
            {
                materials.Add(candidate);
                __result = true;
                break;
            }
        return false;
    }
}

[HarmonyPatch(typeof(BuildPlanManager), nameof(BuildPlanManager.CreateBuildPlan))]
internal static class ValidateAircraftRunwayPlacementPatch
{
    private static bool Prefix(int templateIndex, UnityEngine.Vector3Int gridPosition, BuildPart.Rotation rotation, ref bool __result)
    {
        AircraftRuntime? runtime = AircraftRuntime.Current;
        if (runtime != null && templateIndex == runtime.Blueprints.TowerPairIndex && !runtime.IsAuthoritative)
        {
            if (runtime.SubmitTowerPlacement == null)
            {
                runtime.SetNotice("The host connection is required to place Aster Twin Spires.");
                __result = false;
                return false;
            }
            runtime.SubmitTowerPlacement(new TowerPlacementRequest(gridPosition.x, gridPosition.z, (byte)rotation));
            __result = false;
            return false;
        }
        if (runtime == null || !runtime.IsAuthoritative || templateIndex != runtime.Blueprints.RunwayIndex && templateIndex != runtime.Blueprints.TowerPairIndex) return true;
        UnityEngine.Matrix4x4 placement = GAT.World.buildPartManager.BuildPartTemplates[templateIndex].GetPlacementMatrix(gridPosition, rotation);
        GameFlightEnvironment environment = (GameFlightEnvironment)runtime.Environment;
        bool valid = templateIndex == runtime.Blueprints.RunwayIndex
            ? environment.CanPlaceRunway(GameFlightEnvironment.ToCore(placement.GetPosition()), GameFlightEnvironment.ToCore(placement.rotation))
            : runtime.World.TowerPairs.Count < TowerLayout.MaximumTowerPairs && environment.CanPlaceTowerPair(GameFlightEnvironment.ToCore(placement.GetPosition()), GameFlightEnvironment.ToCore(placement.rotation));
        if (valid) return true;
        runtime.SetNotice(templateIndex == runtime.Blueprints.RunwayIndex
            ? "An airfield requires flat, dry and unobstructed terrain."
            : "Aster Twin Spires require flat, dry and unobstructed terrain.");
        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(BuildPlanManager), nameof(BuildPlanManager.RemoveBuildPlan))]
internal static class RemoveAircraftProjectPatch
{
    private static void Postfix(BuildPlanManager.BuildPlan buildPlan)
    {
        AircraftRuntime? runtime = AircraftRuntime.Current;
        if (runtime != null && runtime.Blueprints.IsCustom(buildPlan.templateIndex)) runtime.Projects.Remove(buildPlan);
    }
}
