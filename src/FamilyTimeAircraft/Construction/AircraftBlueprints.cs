using System;
using System.Collections.Generic;
using System.IO;
using FamilyTimeAircraft.Rendering;
using GAT;
using HarmonyLib;
using FamilyTimeAircraft.Core;
using UnityEngine;

namespace FamilyTimeAircraft.Construction;

public sealed class AircraftBlueprints
{
    private const string AircraftName = "Family Time Aircraft";
    private const string RunwayName = "Family Time Airfield";
    private const string TowerPairName = "Aster Twin Spires";
    private readonly AircraftModels _models;
    private readonly string _assetDirectory;
    private BuildPartManager? _registered;
    public int AircraftIndex { get; private set; } = -1;
    public int RunwayIndex { get; private set; } = -1;
    public int TowerPairIndex { get; private set; } = -1;
    public int TowerFoundationIndex { get; private set; } = -1;
    private readonly List<int> _towerSectionTemplateIndices = new();
    private readonly List<BuildPart> _nativeTemplates = new();
    private readonly List<bool> _nativeVisibility = new();
    public IReadOnlyList<int> TowerSectionTemplateIndices => _towerSectionTemplateIndices.AsReadOnly();
    public int NativeTemplateCount => _nativeTemplates.Count;
    public int VisibleNativeTemplateCount => _nativeVisibility.FindAll(visible => visible).Count;

    public AircraftBlueprints(AircraftModels models, string assetDirectory)
    {
        _models = models;
        _assetDirectory = assetDirectory;
    }

    public void Register()
    {
        BuildPartManager manager = World.buildPartManager;
        if (manager == null || manager == _registered) return;
        var templates = (List<BuildPart>)AccessTools.Field(typeof(BuildPartManager), "buildPartTemplates").GetValue(manager);
        _nativeTemplates.Clear();
        _nativeTemplates.AddRange(templates);
        _nativeVisibility.Clear();
        foreach (BuildPart template in templates) _nativeVisibility.Add(!template.hideFromBlueprintTool);
        AircraftIndex = AddTemplate(templates, "aircraft", AircraftName, AircraftRecipe, new Vector3(12, 3, 11));
        RunwayIndex = AddTemplate(templates, "runway", RunwayName, RunwayRecipe, new Vector3(16, 0.2f, 80));
        TowerPairIndex = AddTemplate(templates, "tower-pair", TowerPairName, TowerPairRecipe, new Vector3(15.4f, 24, 8));
        TowerFoundationIndex = AddTemplate(templates, "tower-pair", TowerPairName + " Foundation", Array.Empty<ConstructionRequirement>(),
            new Vector3(15.4f, 0.24f, 6.8f), _models.TowerFoundationParts(), true);
        RegisterTowerSectionTemplates(templates);
        _registered = manager;
    }

    public bool IsCustom(int index) => index >= 0 && (index == AircraftIndex || index == RunwayIndex || index == TowerPairIndex);
    public bool IsTowerSectionTemplate(int index) => _towerSectionTemplateIndices.Contains(index);
    public bool NativeTemplatesPreserved()
    {
        IReadOnlyList<BuildPart> current = World.buildPartManager.BuildPartTemplates;
        if (current.Count < _nativeTemplates.Count) return false;
        for (int index = 0; index < _nativeTemplates.Count; index++)
            if (current[index] != _nativeTemplates[index]) return false;
        return true;
    }
    public bool NativeVisibilityPreserved()
    {
        IReadOnlyList<BuildPart> current = World.buildPartManager.BuildPartTemplates;
        if (current.Count < _nativeVisibility.Count) return false;
        for (int index = 0; index < _nativeVisibility.Count; index++)
            if (_nativeVisibility[index] != !current[index].hideFromBlueprintTool) return false;
        return true;
    }
    public int TowerSectionTemplateIndex(int towerIndex, int sectionIndex) => _towerSectionTemplateIndices[towerIndex * TowerLayout.SectionsPerTower + sectionIndex];

    private void RegisterTowerSectionTemplates(List<BuildPart> templates)
    {
        _towerSectionTemplateIndices.Clear();
        for (int tower = 0; tower < TowerLayout.TowerCount; tower++)
            for (int section = 0; section < TowerLayout.SectionsPerTower; section++)
            {
                Vector3 bounds = _models.TowerSectionBounds(tower, section);
                string name = TowerPairName + " Section " + tower + "-" + section;
                int index = AddTemplate(templates, "tower-pair", name, Array.Empty<ConstructionRequirement>(), bounds,
                    _models.TowerSectionParts(tower, section), true);
                _towerSectionTemplateIndices.Add(index);
            }
    }

    private int AddTemplate(List<BuildPart> templates, string model, string displayName, ConstructionRequirement[] recipe, Vector3 size,
        IReadOnlyList<AircraftModelPart>? parts = null, bool hidden = false)
    {
        var template = ScriptableObject.CreateInstance<BuildPart>();
        template.name = displayName;
        template.displayName = displayName;
        typeof(BuildPart).GetField("allowXZOffsetPlacement")?.SetValue(template, true);
        typeof(BuildPart).GetField("constructionRequirements")?.SetValue(template, recipe);
        typeof(BuildPart).GetField("constructionMaterial")?.SetValue(template, ConstructionMaterial.LOG);
        typeof(BuildPart).GetField("hideFromBlueprintTool")?.SetValue(template, hidden);
        template.collisionShapes.Add(Matrix4x4.TRS(new Vector3(0, size.y * 0.5f, 0), Quaternion.identity, size));
        template.thumbnailAsset = LoadThumbnail(model);
        template.clearTerrainCells = hidden ? Array.Empty<Unity.Mathematics.int2>() : GroundFootprint(size);
        AddMeshes(template, parts ?? _models.Parts(model));
        templates.Add(template);
        return templates.Count - 1;
    }

    private static Unity.Mathematics.int2[] GroundFootprint(Vector3 size)
    {
        var cells = new List<Unity.Mathematics.int2>();
        for (int x = -Mathf.FloorToInt(size.x * 0.5f); x < Mathf.CeilToInt(size.x * 0.5f); x++)
            for (int z = -Mathf.FloorToInt(size.z * 0.5f); z < Mathf.CeilToInt(size.z * 0.5f); z++)
                cells.Add(new Unity.Mathematics.int2(x, z));
        return cells.ToArray();
    }

    private void AddMeshes(BuildPart template, IReadOnlyList<AircraftModelPart> parts)
    {
        foreach (AircraftModelPart part in parts)
        {
            if (part.Mesh == null) continue;
            Mesh mesh = UnityEngine.Object.Instantiate(part.Mesh);
            Matrix4x4 transform = Matrix4x4.TRS(part.Position, part.Rotation, part.Scale);
            Vector3[] vertices = mesh.vertices;
            for (int index = 0; index < vertices.Length; index++) vertices[index] = transform.MultiplyPoint3x4(vertices[index]);
            mesh.vertices = vertices;
            mesh.RecalculateBounds();
            template.meshMaterials.Add(new MeshMaterialPair { mesh = mesh, material = _models.CreateMaterial(part.Color) });
        }
    }

    private Texture2D LoadThumbnail(string model)
    {
        var texture = new Texture2D(2, 2);
        string path = Path.Combine(_assetDirectory, model + "-thumbnail.png");
        if (!File.Exists(path) || !texture.LoadImage(File.ReadAllBytes(path)))
            throw new InvalidDataException("Missing aircraft blueprint thumbnail: " + path);
        return texture;
    }

    public ConstructionRequirement[] RecipeFor(int templateIndex) => templateIndex switch
    {
        var index when index == AircraftIndex => AircraftRecipe,
        var index when index == RunwayIndex => RunwayRecipe,
        var index when index == TowerPairIndex => TowerPairRecipe,
        _ => throw new ArgumentOutOfRangeException(nameof(templateIndex))
    };
    public static ConstructionRequirement[] AircraftRecipe => Recipe(32, 16, 0);
    public static ConstructionRequirement[] RunwayRecipe => Recipe(16, 32, 0);
    public static ConstructionRequirement[] TowerPairRecipe => Recipe(48, 24, 0);
    public static ConstructionRequirement[] RepairRecipe => Recipe(8, 4, 0);

    public static ConstructionRequirement[] Recipe(int logs, int stones, int grass)
    {
        var requirements = new List<ConstructionRequirement>();
        AddRequirement(requirements, ConstructionMaterial.LOG, logs);
        AddRequirement(requirements, ConstructionMaterial.STONE, stones);
        AddRequirement(requirements, ConstructionMaterial.GRASS, grass);
        return requirements.ToArray();
    }

    private static void AddRequirement(List<ConstructionRequirement> requirements, ConstructionMaterial material, int count)
    {
        if (count > 0) requirements.Add(new ConstructionRequirement { req = material, amount = count });
    }

}

[HarmonyPatch(typeof(StaticRenderer), "Start")]
internal static class RegisterAircraftBlueprintsPatch
{
    private static void Prefix() => AircraftRuntime.Current?.Blueprints.Register();
}

[HarmonyPatch(typeof(BuildPlanManager), nameof(BuildPlanManager.CompleteBuildPlan))]
internal static class CompleteAircraftBlueprintPatch
{
    private static bool Prefix(BuildPlanManager __instance, BuildPlanManager.BuildPlan buildPlan, ref bool __result)
    {
        AircraftRuntime? runtime = AircraftRuntime.Current;
        if (runtime == null || !runtime.Blueprints.IsCustom(buildPlan.templateIndex)) return true;
        __result = runtime.CompleteBuildPlan(__instance, buildPlan);
        return false;
    }
}
