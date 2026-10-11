using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System;
using GAT;
using HarmonyLib;
using UnityEngine;

namespace FamilyTimeAircraft.Construction;

[HarmonyPatch(typeof(BuildPartUI), "RebuildEntries")]
internal static class AircraftFourthBuildSlotPatch
{
    private const int AircraftMenuIndex = 3;
    private static void Postfix(BuildPartUI __instance)
    {
        AppendMissingNativeEntries(__instance);
        Reorder(__instance);
        __instance.StartCoroutine(ReorderAfterNativeCleanup(__instance));
    }

    private static void AppendMissingNativeEntries(BuildPartUI menu)
    {
        List<BuildPartButton> buttons = ButtonInstances(menu);
        IReadOnlyList<BuildPart> templates = World.buildPartManager.BuildPartTemplates;
        for (int index = 0; index < templates.Count; index++)
            if (IsVisibleNativeTemplate(index, templates[index]) && !HasButton(buttons, templates[index]))
                AddNativeButton(menu, buttons, templates[index]);
    }

    private static bool IsVisibleNativeTemplate(int index, BuildPart template)
    {
        AircraftRuntime? runtime = AircraftRuntime.Current;
        return template != null && !template.hideFromBlueprintTool && (runtime == null || !runtime.Blueprints.IsCustom(index));
    }

    private static bool HasButton(List<BuildPartButton> buttons, BuildPart template)
    {
        foreach (BuildPartButton button in buttons)
            if (button != null && (BuildPart)AccessTools.Field(typeof(BuildPartButton), "_buildPart").GetValue(button) == template) return true;
        return false;
    }

    private static void AddNativeButton(BuildPartUI menu, List<BuildPartButton> buttons, BuildPart template)
    {
        var prefab = (BuildPartButton)AccessTools.Field(typeof(BuildPartUI), "buildPartButtonPrefab").GetValue(menu);
        var requirements = (ConstructionRequirementUI)AccessTools.Field(typeof(BuildPartUI), "buildPartRequirementPrefab").GetValue(menu);
        var container = (UnityEngine.UI.GridLayoutGroup)AccessTools.Field(typeof(BuildPartUI), "gridLayoutContainer").GetValue(menu);
        var callback = (Action<BuildPart>)AccessTools.Method(typeof(BuildPartUI), "HandleSelection").CreateDelegate(typeof(Action<BuildPart>), menu);
        BuildPartButton button = UnityEngine.Object.Instantiate(prefab, container.transform);
        button.Initialize(template, requirements, callback);
        button.SetInteractable(!IsBlueprintTutorialActive() || HasPlankRequirement(template));
        button.gameObject.SetActive(true);
        buttons.Add(button);
    }

    private static bool IsBlueprintTutorialActive() => World.tutorialManager != null
        && World.tutorialManager.ActiveTutorial.ToString() == "BLUEPRINT_MENU";

    private static bool HasPlankRequirement(BuildPart template)
    {
        var requirements = (ConstructionRequirement[])AccessTools.Field(typeof(BuildPart), "constructionRequirements").GetValue(template);
        if (requirements == null) return false;
        foreach (ConstructionRequirement requirement in requirements)
            if (requirement != null && requirement.req == ConstructionMaterial.PLANK) return true;
        return false;
    }

    private static List<BuildPartButton> ButtonInstances(BuildPartUI menu) =>
        (List<BuildPartButton>)AccessTools.Field(typeof(BuildPartUI), "_buttonInstances").GetValue(menu);

    private static IEnumerator ReorderAfterNativeCleanup(BuildPartUI menu)
    {
        yield return null;
        if (menu != null) Reorder(menu);
    }

    private static void Reorder(BuildPartUI menu)
    {
        AircraftRuntime? runtime = AircraftRuntime.Current;
        if (runtime == null || runtime.Blueprints.AircraftIndex < 0) return;
        BuildPart aircraft = World.buildPartManager.BuildPartTemplates[runtime.Blueprints.AircraftIndex];
        var buttons = (List<BuildPartButton>)AccessTools.Field(typeof(BuildPartUI), "_buttonInstances").GetValue(menu);
        foreach (BuildPartButton button in buttons)
            if (button != null && (BuildPart)AccessTools.Field(typeof(BuildPartButton), "_buildPart").GetValue(button) == aircraft)
            {
                button.transform.SetSiblingIndex(AircraftMenuIndex);
                return;
            }
    }
}

[HarmonyPatch]
internal static class UnlockNativeAircraftBlueprintPatch
{
    private static bool Prepare() => AccessTools.Method(typeof(BuildPartManager), "IsBlueprintUnlocked") != null;
    private static MethodBase TargetMethod() => AccessTools.Method(typeof(BuildPartManager), "IsBlueprintUnlocked");
    private static bool Prefix(BuildPart buildPart, ref bool __result)
    {
        AircraftRuntime? runtime = AircraftRuntime.Current;
        if (runtime == null || buildPart == null) return true;
        int index = -1;
        for (int candidate = 0; candidate < World.buildPartManager.BuildPartTemplates.Count; candidate++)
            if (World.buildPartManager.BuildPartTemplates[candidate] == buildPart) { index = candidate; break; }
        if (!runtime.Blueprints.IsCustom(index)) return true;
        __result = true;
        return false;
    }
}

[HarmonyPatch(typeof(BuildPartButton), nameof(BuildPartButton.Initialize))]
internal static class AircraftNativeRecipeIconsPatch
{
    private static void Postfix(BuildPartButton __instance, BuildPart buildPart, ConstructionRequirementUI requirementPrefab)
    {
        AircraftRuntime? runtime = AircraftRuntime.Current;
        if (runtime == null || buildPart == null || requirementPrefab == null) return;
        int index = FindCustomIndex(runtime, buildPart);
        if (index < 0) return;
        var instances = (List<ConstructionRequirementUI>)AccessTools.Field(typeof(BuildPartButton), "_requirementInstances").GetValue(__instance);
        foreach (ConstructionRequirementUI entry in instances) if (entry != null) UnityEngine.Object.Destroy(entry.gameObject);
        instances.Clear();
        var container = (UnityEngine.UI.HorizontalLayoutGroup)AccessTools.Field(typeof(BuildPartButton), "requirementsContainer").GetValue(__instance);
        foreach (ConstructionRequirement recipe in runtime.Blueprints.RecipeFor(index))
        {
            ConstructionRequirementUI icon = UnityEngine.Object.Instantiate(requirementPrefab, container.transform);
            icon.Initialize(World.buildPartIcons.GetMaterialSprite(recipe.req), recipe.amount);
            icon.gameObject.SetActive(true);
            instances.Add(icon);
        }
    }

    private static int FindCustomIndex(AircraftRuntime runtime, BuildPart part)
    {
        foreach (int index in new[] { runtime.Blueprints.AircraftIndex, runtime.Blueprints.RunwayIndex, runtime.Blueprints.TowerPairIndex })
            if (index >= 0 && World.buildPartManager.BuildPartTemplates[index] == part) return index;
        return -1;
    }
}
