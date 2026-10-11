using System;
using System.Collections;
using System.Collections.Generic;
using FamilyTimeAircraft.Core;
using FamilyTimeAircraft.Construction;
using GAT;
using HarmonyLib;
using UnityEngine;

namespace FamilyTimeAircraft.Diagnostics;

public sealed class AircraftTowerSmokeScenario
{
    private readonly AircraftRuntime _runtime;

    public AircraftTowerSmokeScenario(AircraftRuntime runtime) => _runtime = runtime;

    public IEnumerator Run()
    {
        Check("tower-blueprint-registered", _runtime.Blueprints.TowerPairIndex >= 0);
        BuildPartUI menu = FindMenu();
        menu.PickBuildPartFor(_ => { });
        yield return null;
        yield return null;
        Check("aircraft-slot-remains-four", FindAircraftButtonIndex(menu) == 3);
        Check("tower-blueprint-visible", HasBlueprintButton(menu, _runtime.Blueprints.TowerPairIndex));
        _runtime.SetNotice("[TowerTest] nativeTemplates=" + _runtime.Blueprints.NativeTemplateCount + " visibleNative=" + _runtime.Blueprints.VisibleNativeTemplateCount + " menus=" + DescribeMenus());
        Check("native-menu-items-retained", GetButtonCount(menu) == _runtime.Blueprints.VisibleNativeTemplateCount + 3);
        Check("native-blueprints-retained", _runtime.Blueprints.NativeTemplateCount > 0 && _runtime.Blueprints.NativeTemplatesPreserved());
        Check("native-blueprint-visibility-retained", _runtime.Blueprints.NativeVisibilityPreserved());
        menu.Close();
        Check("tower-sections-registered", _runtime.Blueprints.TowerSectionTemplateIndices.Count == TowerLayout.SectionsPerPair);
        Vector3Int position = FindClearPlacement();
        AircraftTestEnvironment.PrepareRunwaySquare(position);
        AircraftTestEnvironment.ClearWater();
        yield return null;
        yield return null;
        GameFlightEnvironment environment = (GameFlightEnvironment)_runtime.Environment;
        bool validTerrain = environment.CanPlaceTowerPair(GameFlightEnvironment.ToCore(position), System.Numerics.Quaternion.Identity);
        if (!validTerrain) _runtime.SetNotice("[TowerTest] placement rejected: " + environment.TowerPlacementFailure);
        Check("tower-placement-terrain-valid", validTerrain);
        Check("tower-plan-created", World.buildPlanManager.CreateBuildPlan(_runtime.Blueprints.TowerPairIndex, position, BuildPart.Rotation.R_0, 0.01f));
        BuildPlanManager.BuildPlan plan = FindPlan(position);
        _runtime.Projects.Track(plan, _runtime.Owner);
        byte[] savedProgress = _runtime.CaptureBuildState();
        _runtime.RestoreBuildState(savedProgress);
        Check("tower-project-restored", World.buildPlanManager.IsBuildPlanActive(plan));
        SpawnIngredients(AircraftBlueprints.TowerPairRecipe, position);
        Check("tower-native-construction", NativeAircraftGame.Construct(plan, _materials, position));
        Check("tower-pair-created", _runtime.World.TowerPairs.Count == 1);
        Check("tower-section-state-created", System.Linq.Enumerable.Single(_runtime.World.TowerPairs).Sections.Count == TowerLayout.SectionsPerPair);
        Check("tower-native-sections-created", HasEveryNativeTowerSection(position, BuildPart.Rotation.R_0));
        CheckTowerImpact(position);
        yield return null;
    }

    private readonly List<RigidTransform> _materials = new();

    private static BuildPartUI FindMenu()
    {
        BuildPartUI[] menus = UnityEngine.Object.FindObjectsByType<BuildPartUI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (BuildPartUI menu in menus) if (menu.gameObject.activeInHierarchy) return menu;
        return menus[0];
    }

    private int FindAircraftButtonIndex(BuildPartUI menu)
    {
        var buttons = (List<BuildPartButton>)AccessTools.Field(typeof(BuildPartUI), "_buttonInstances").GetValue(menu);
        foreach (BuildPartButton button in buttons)
            if ((BuildPart)AccessTools.Field(typeof(BuildPartButton), "_buildPart").GetValue(button) == World.buildPartManager.BuildPartTemplates[_runtime.Blueprints.AircraftIndex])
                return button.transform.GetSiblingIndex();
        return -1;
    }

    private static bool HasBlueprintButton(BuildPartUI menu, int templateIndex)
    {
        var buttons = (List<BuildPartButton>)AccessTools.Field(typeof(BuildPartUI), "_buttonInstances").GetValue(menu);
        foreach (BuildPartButton button in buttons)
            if (button != null && (BuildPart)AccessTools.Field(typeof(BuildPartButton), "_buildPart").GetValue(button) == World.buildPartManager.BuildPartTemplates[templateIndex]) return true;
        return false;
    }

    private static int GetButtonCount(BuildPartUI menu) =>
        ((List<BuildPartButton>)AccessTools.Field(typeof(BuildPartUI), "_buttonInstances").GetValue(menu)).Count;

    private static string DescribeMenus()
    {
        var descriptions = new List<string>();
        foreach (BuildPartUI menu in UnityEngine.Object.FindObjectsByType<BuildPartUI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            descriptions.Add(menu.gameObject.activeSelf + "/" + menu.gameObject.activeInHierarchy + ":" + GetButtonCount(menu));
        return string.Join(",", descriptions);
    }

    private Vector3Int FindClearPlacement()
    {
        Vector3Int position = Vector3Int.RoundToInt(World.playerController.CachedInstancePosition + Vector3.right * 40);
        position.y = Mathf.RoundToInt(World.heightfield.GetWorldHeight(position.x, position.z));
        return position;
    }

    private BuildPlanManager.BuildPlan FindPlan(Vector3Int position)
    {
        var plans = (List<BuildPlanManager.BuildPlan>)AccessTools.Field(typeof(BuildPlanManager), "activeBuildPlans").GetValue(World.buildPlanManager);
        return plans.Find(plan => plan.gridPosition == position && plan.templateIndex == _runtime.Blueprints.TowerPairIndex)
            ?? throw new InvalidOperationException("Native tower project is missing.");
    }

    private bool HasEveryNativeTowerSection(Vector3Int anchor, BuildPart.Rotation rotation)
    {
        for (int tower = 0; tower < TowerLayout.TowerCount; tower++)
            for (int level = 0; level < TowerLayout.SectionsPerTower; level++)
            {
                int offset = tower == 0 ? -TowerLayout.HorizontalTowerOffset : TowerLayout.HorizontalTowerOffset;
                var sectionPosition = anchor + new Vector3Int(offset, level * TowerLayout.SectionHeight, 0);
                if (!World.buildPartManager.TryGetBuildPart(sectionPosition, rotation, out BuildPart part) ||
                    part != World.buildPartManager.BuildPartTemplates[_runtime.Blueprints.TowerSectionTemplateIndex(tower, level)]) return false;
            }
        return true;
    }

    private void CheckTowerImpact(Vector3Int anchor)
    {
        Vector3 towerFace = anchor + new Vector3(-TowerLayout.HorizontalTowerOffset + 3, 22.5f, 0);
        System.Numerics.Vector3 start = GameFlightEnvironment.ToCore(towerFace);
        System.Numerics.Vector3 end = GameFlightEnvironment.ToCore(towerFace + Vector3.left * 8);
        GameFlightEnvironment environment = (GameFlightEnvironment)_runtime.Environment;
        bool collisionDetected = environment.IsBlocked(start, end, AircraftRules.CollisionRadius);
        var pair = System.Linq.Enumerable.Single(_runtime.World.TowerPairs);
        TowerSectionState section = pair.Sections[TowerLayout.SectionsPerTower - 1];
        environment.ImpactDamage.Apply(start - System.Numerics.Vector3.UnitY);
        Check("tower-aircraft-collision-detected", collisionDetected);
        Check("tower-impact-collapses-contacted-section", section.Phase == TowerSectionPhase.Collapsing && section.Integrity == 0);
        Vector3Int sectionPosition = anchor + new Vector3Int(-TowerLayout.HorizontalTowerOffset, (TowerLayout.SectionsPerTower - 1) * TowerLayout.SectionHeight, 0);
        Check("tower-impact-removes-native-section", !World.buildPartManager.TryGetBuildPart(sectionPosition, BuildPart.Rotation.R_0, out _));
    }

    private void SpawnIngredients(ConstructionRequirement[] recipe, Vector3 position)
    {
        foreach (ConstructionRequirement requirement in recipe)
            for (int count = 0; count < requirement.amount; count++)
            {
                RigidTransform rigid = NativeAircraftGame.SpawnRigid(NativeAircraftGame.FindMaterialPrefab(requirement.req), position + Vector3.up);
                rigid.SetFrozen(true);
                _materials.Add(rigid);
            }
    }

    private void Check(string name, bool passed)
    {
        _runtime.SetNotice("[AircraftTest] " + name + "=" + (passed ? "PASS" : "FAIL"));
        if (!passed) throw new InvalidOperationException("Aircraft tower diagnostic failed: " + name);
    }
}
