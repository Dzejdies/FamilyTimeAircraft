using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GAT;
using HarmonyLib;
using UnityEngine;

namespace FamilyTimeAircraft.Diagnostics;

public sealed class AircraftBuildingFixture
{
    private const int NeighborClearance = 32;
    public int TemplateIndex { get; }
    public Vector3Int TargetPosition { get; }
    public Vector3Int NeighborPosition { get; }
    public Vector3 TargetPoint { get; }
    private readonly int[] _targetRenderIds;
    private readonly int[] _neighborRenderIds;

    private AircraftBuildingFixture(int template, Vector3Int target, Vector3Int neighbor, int[] targetRenderIds, int[] neighborRenderIds)
    {
        TemplateIndex = template; TargetPosition = target; NeighborPosition = neighbor;
        _targetRenderIds = targetRenderIds; _neighborRenderIds = neighborRenderIds;
        BuildPart part = World.buildPartManager.BuildPartTemplates[template];
        TargetPoint = part.GetPlacementMatrix(target, BuildPart.Rotation.R_0).MultiplyPoint3x4(part.collisionShapes[0].GetPosition());
    }

    public static int WallTemplateIndex => FindWallTemplate();

    public static Vector3Int GridPosition(Vector3 position) => Vector3Int.RoundToInt(position);

    public static Vector3Int NeighborGridPosition(Vector3 position)
    {
        Vector3 neighbor = position + Vector3.right * NeighborClearance;
        neighbor.y = World.heightfield.GetWorldHeight(neighbor.x, neighbor.z);
        return GridPosition(neighbor);
    }

    public static AircraftBuildingFixture Create(Vector3 origin) => CreateAt(origin + Vector3.forward * 80);

    public static AircraftBuildingFixture CreateAt(Vector3 position)
    {
        int template = WallTemplateIndex;
        Vector3Int target = GridPosition(position);
        Vector3Int neighbor = NeighborGridPosition(position);
        return new AircraftBuildingFixture(template, target, neighbor, CreatePart(template, target), CreatePart(template, neighbor));
    }

    public static AircraftBuildingFixture InspectExisting(int template, Vector3Int target, Vector3Int neighbor) =>
        new(template, target, neighbor, PartRenderIds(template, target), PartRenderIds(template, neighbor));

    public static AircraftBuildingFixture InspectCollisionOnly(int template, Vector3Int target, Vector3Int neighbor) =>
        new(template, target, neighbor, Array.Empty<int>(), Array.Empty<int>());

    public static bool PartPresent(int template, Vector3Int position)
    {
        BuildPart part = World.buildPartManager.BuildPartTemplates[template];
        Vector3 center = part.GetPlacementMatrix(position, BuildPart.Rotation.R_0).MultiplyPoint3x4(part.collisionShapes[0].GetPosition());
        if (!World.Raycast(center + Vector3.up * 6, Vector3.down, 12, out World.HitInfo hit, CPUCollisionMask.DEFAULT)) return false;
        return World.buildPartManager.TryGetBuildPart(hit, out int foundTemplate, out Vector3Int foundPosition, out BuildPart.Rotation rotation)
            && foundTemplate == template && foundPosition == position && rotation == BuildPart.Rotation.R_0;
    }

    public static bool PartRenderersPresent(int template, Vector3Int position)
    {
        try { return PartRenderIds(template, position).Length > 0; }
        catch (InvalidOperationException) { return false; }
    }

    public bool TargetCollisionPresent => HitPart(TargetPosition);
    public bool NeighborCollisionPresent => HitPart(NeighborPosition);
    public bool TargetRenderersPresent => _targetRenderIds.Length > 0 && _targetRenderIds.All(RendererPresent);
    public bool TargetRenderersRemoved => _targetRenderIds.Length > 0 && _targetRenderIds.All(id => !RendererPresent(id));
    public bool NeighborRenderersPresent => _neighborRenderIds.Length > 0 && _neighborRenderIds.All(RendererPresent);

    private static int FindWallTemplate() => Enumerable.Range(0, World.buildPartManager.BuildPartTemplates.Count).First(index =>
    {
        BuildPart candidate = World.buildPartManager.BuildPartTemplates[index];
        return candidate != null && candidate.name.IndexOf("wall", StringComparison.OrdinalIgnoreCase) >= 0
            && candidate.collisionShapes.Count > 0 && candidate.MeshMaterialCount > 0;
    });

    private static int[] CreatePart(int template, Vector3Int position)
    {
        var before = RendererDictionary().Keys.Cast<int>().ToHashSet();
        if (!TryCreatePart(template, position)) throw new InvalidOperationException("Native aircraft collision fixture could not create its wall.");
        return RendererDictionary().Keys.Cast<int>().Where(id => !before.Contains(id)).ToArray();
    }

    private static bool TryCreatePart(int template, Vector3Int position)
    {
        Type[] leadingParameters = { typeof(int), typeof(Vector3Int), typeof(BuildPart.Rotation), typeof(bool) };
        MethodInfo method = AccessTools.GetDeclaredMethods(typeof(BuildPartManager)).FirstOrDefault(candidate =>
        {
            ParameterInfo[] parameters = candidate.GetParameters();
            return candidate.Name == "TryCreateBuildPart" && parameters.Length >= leadingParameters.Length
                && leadingParameters.Select((type, index) => parameters[index].ParameterType == type).All(matches => matches)
                && parameters.Skip(leadingParameters.Length).All(parameter => parameter.IsOptional);
        }) ?? throw new MissingMethodException(nameof(BuildPartManager), "TryCreateBuildPart");
        object[] arguments = new object[method.GetParameters().Length];
        arguments[0] = template;
        arguments[1] = position;
        arguments[2] = BuildPart.Rotation.R_0;
        arguments[3] = false;
        for (int index = leadingParameters.Length; index < arguments.Length; index++) arguments[index] = Type.Missing;
        return (bool)method.Invoke(World.buildPartManager, arguments);
    }

    private static int[] PartRenderIds(int template, Vector3Int position)
    {
        var keys = (IDictionary)AccessTools.Field(typeof(BuildPartManager), "m_runtimeIdByKey").GetValue(World.buildPartManager);
        foreach (DictionaryEntry entry in keys)
        {
            Type keyType = entry.Key.GetType();
            if ((int)AccessTools.Property(keyType, "TemplateIndex").GetValue(entry.Key) != template
                || (Vector3Int)AccessTools.Property(keyType, "Position").GetValue(entry.Key) != position
                || (BuildPart.Rotation)AccessTools.Property(keyType, "Rotation").GetValue(entry.Key) != BuildPart.Rotation.R_0) continue;
            object[] arguments = { entry.Value, null! };
            if (!(bool)AccessTools.Method(typeof(BuildPartManager), "TryGetRuntime").Invoke(World.buildPartManager, arguments)) break;
            object runtime = arguments[1];
            int offset = Convert.ToInt32(AccessTools.Field(runtime.GetType(), "renderOffset").GetValue(runtime));
            int count = Convert.ToInt32(AccessTools.Field(runtime.GetType(), "renderCount").GetValue(runtime));
            var pool = (IList)AccessTools.Field(typeof(BuildPartManager), "m_renderIdPool").GetValue(World.buildPartManager);
            return Enumerable.Range(offset, count).Select(index => (int)pool[index]).ToArray();
        }
        throw new InvalidOperationException("Native aircraft collision fixture building is absent.");
    }

    private bool HitPart(Vector3Int position)
    {
        BuildPart part = World.buildPartManager.BuildPartTemplates[TemplateIndex];
        Vector3 center = part.GetPlacementMatrix(position, BuildPart.Rotation.R_0).MultiplyPoint3x4(part.collisionShapes[0].GetPosition());
        if (!World.Raycast(center + Vector3.up * 6, Vector3.down, 12, out World.HitInfo hit, CPUCollisionMask.DEFAULT)) return false;
        return World.buildPartManager.TryGetBuildPart(hit, out int template, out Vector3Int grid, out BuildPart.Rotation rotation)
            && template == TemplateIndex && grid == position && rotation == BuildPart.Rotation.R_0;
    }

    private static IDictionary RendererDictionary() => (IDictionary)AccessTools.Field(typeof(StaticRenderer), "idToChunk").GetValue(World.staticRenderer);
    private static bool RendererPresent(int id) => RendererDictionary().Contains(id);
}
