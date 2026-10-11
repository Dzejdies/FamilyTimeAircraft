using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FamilyTimeAircraft.Core;
using UnityEngine;

namespace FamilyTimeAircraft.Rendering;

public sealed class AircraftModelPart
{
    public string Name { get; }
    public Mesh? Mesh { get; }
    public Color Color { get; }
    public Vector3 Position { get; }
    public Quaternion Rotation { get; }
    public Vector3 Scale { get; }

    public AircraftModelPart(string name, Mesh? mesh, Color color, Vector3 position, Quaternion rotation, Vector3 scale)
    {
        Name = name;
        Mesh = mesh;
        Color = color;
        Position = position;
        Rotation = rotation;
        Scale = scale;
    }
}

public sealed class AircraftModels : IDisposable
{
    public static IReadOnlyList<string> ModelNames { get; } = Array.AsReadOnly(new[] { "aircraft", "wreck", "runway", "parachute", "tower-pair", "tower-debris" });
    public const float RunwaySurfaceSeparation = 0.05f;
    public static float RunwayModelElevation => Airfield.MaximumTerrainDeviation + RunwaySurfaceSeparation;
    private const int MaximumParts = 256;
    private const int MaximumVertices = 65535;
    private const int MaximumIndices = 196605;
    private readonly Dictionary<string, AircraftModelPart[]> _models = new();
    private readonly List<Material> _materials = new();
    private readonly Dictionary<AircraftModelPart, Material> _partMaterials = new();
    private readonly Shader _shader;

    public AircraftModels(string assetDirectory)
    {
        _shader = Shader.Find("Universal Render Pipeline/Simple Lit") ?? Shader.Find("Universal Render Pipeline/Lit")
            ?? throw new InvalidOperationException("Aircraft rendering requires the game's URP shader.");
        try
        {
            foreach (string name in ModelNames)
                _models.Add(name, ReadModel(Path.Combine(assetDirectory, name + ".fta"), name == "runway" ? RunwayModelElevation : 0));
            ValidateTowerSections();
        }
        catch { Dispose(); throw; }
    }

    public AircraftModelPart[] Parts(string name) => _models[name];

    public IReadOnlyList<AircraftModelPart> TowerSectionParts(int towerIndex, int sectionIndex)
    {
        RequireTowerSectionIndex(towerIndex, sectionIndex);
        string sectionPrefix = $"TowerSection_{towerIndex}_{sectionIndex}_";
        float towerX = towerIndex == 0 ? -TowerLayout.HorizontalTowerOffset : TowerLayout.HorizontalTowerOffset;
        Vector3 sectionOrigin = new(towerX, sectionIndex * TowerLayout.SectionHeight, 0);
        AircraftModelPart[] parts = Parts("tower-pair")
            .Where(part => part.Name.StartsWith(sectionPrefix, StringComparison.Ordinal))
            .Select(part => new AircraftModelPart(part.Name, part.Mesh, part.Color, part.Position - sectionOrigin, part.Rotation, part.Scale))
            .ToArray();
        if (parts.Length == 0 || parts.Any(part => part.Mesh == null))
            throw new InvalidDataException("Tower section has missing geometry: " + sectionPrefix);
        return Array.AsReadOnly(parts);
    }

    public IReadOnlyList<AircraftModelPart> TowerFoundationParts() => Array.AsReadOnly(Parts("tower-pair")
        .Where(part => part.Name == "TowerPairAnchor").ToArray());

    public Vector3 TowerSectionBounds(int towerIndex, int sectionIndex)
    {
        IReadOnlyList<AircraftModelPart> parts = TowerSectionParts(towerIndex, sectionIndex);
        Vector3 minimum = new(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        Vector3 maximum = new(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
        foreach (AircraftModelPart part in parts) ExtendBounds(part, ref minimum, ref maximum);
        return maximum - minimum;
    }

    public GameObject Instantiate(string name)
    {
        var root = new GameObject("FamilyTimeAircraft." + name);
        foreach (AircraftModelPart part in Parts(name)) AddPart(root.transform, part);
        return root;
    }

    public GameObject InstantiateTowerDebris(int partIndex)
    {
        AircraftModelPart[] parts = Parts("tower-debris");
        if (partIndex < 0 || partIndex >= parts.Length) throw new ArgumentOutOfRangeException(nameof(partIndex));
        AircraftModelPart part = parts[partIndex];
        Mesh mesh = part.Mesh ?? throw new InvalidDataException("Tower debris has no mesh.");
        var root = new GameObject("FamilyTimeAircraft.TowerDebris." + partIndex);
        var meshObject = new GameObject(part.Name);
        meshObject.transform.SetParent(root.transform, false);
        meshObject.transform.localRotation = part.Rotation;
        meshObject.transform.localScale = part.Scale;
        meshObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        meshObject.AddComponent<MeshRenderer>().sharedMaterial = CreateMaterial(part.Color);
        root.AddComponent<BoxCollider>().size = mesh.bounds.size;
        return root;
    }

    public Material CreateMaterial(Color color)
    {
        var material = new Material(_shader) { color = color };
        material.SetColor("_BaseColor", color);
        _materials.Add(material);
        return material;
    }

    private void AddPart(Transform root, AircraftModelPart part)
    {
        var child = new GameObject(part.Name);
        child.transform.SetParent(root, false);
        child.transform.localPosition = part.Position;
        child.transform.localRotation = part.Rotation;
        child.transform.localScale = part.Scale;
        if (part.Mesh == null) return;
        child.AddComponent<MeshFilter>().sharedMesh = part.Mesh;
        child.AddComponent<MeshRenderer>().sharedMaterial = PartMaterial(part);
    }

    private void ValidateTowerSections()
    {
        for (int towerIndex = 0; towerIndex < TowerLayout.TowerCount; towerIndex++)
            for (int sectionIndex = 0; sectionIndex < TowerLayout.SectionsPerTower; sectionIndex++)
                if (TowerSectionBounds(towerIndex, sectionIndex).sqrMagnitude <= 0)
                    throw new InvalidDataException("Tower section has empty bounds.");
    }

    private static void RequireTowerSectionIndex(int towerIndex, int sectionIndex)
    {
        if (towerIndex < 0 || towerIndex >= TowerLayout.TowerCount || sectionIndex < 0 || sectionIndex >= TowerLayout.SectionsPerTower)
            throw new ArgumentOutOfRangeException(nameof(sectionIndex));
    }

    private static void ExtendBounds(AircraftModelPart part, ref Vector3 minimum, ref Vector3 maximum)
    {
        Bounds bounds = part.Mesh!.bounds;
        for (int corner = 0; corner < 8; corner++)
        {
            Vector3 point = new((corner & 1) == 0 ? bounds.min.x : bounds.max.x,
                (corner & 2) == 0 ? bounds.min.y : bounds.max.y,
                (corner & 4) == 0 ? bounds.min.z : bounds.max.z);
            point = part.Position + part.Rotation * Vector3.Scale(point, part.Scale);
            minimum = Vector3.Min(minimum, point);
            maximum = Vector3.Max(maximum, point);
        }
    }

    private Material PartMaterial(AircraftModelPart part)
    {
        if (_partMaterials.TryGetValue(part, out Material material)) return material;
        material = CreateMaterial(part.Color);
        _partMaterials.Add(part, material);
        return material;
    }

    private static AircraftModelPart[] ReadModel(string path, float elevation)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        if (reader.ReadUInt32() != 0x31415446) throw new InvalidDataException("Invalid aircraft model: " + path);
        int count = RequireCount(reader.ReadInt32(), MaximumParts);
        var parts = new AircraftModelPart[count];
        try
        {
            for (int index = 0; index < count; index++) parts[index] = ReadPart(reader, elevation);
            if (stream.Position != stream.Length) throw new InvalidDataException("Trailing aircraft model data.");
            return parts;
        }
        catch { ReleaseMeshes(parts); throw; }
    }

    private static AircraftModelPart ReadPart(BinaryReader reader, float elevation)
    {
        int nameLength = RequireCount(reader.ReadInt32(), 128);
        byte[] nameBytes = reader.ReadBytes(nameLength);
        if (nameBytes.Length != nameLength) throw new EndOfStreamException();
        string name = System.Text.Encoding.UTF8.GetString(nameBytes);
        int vertexCount = RequireCount(reader.ReadInt32(), MaximumVertices);
        int indexCount = RequireCount(reader.ReadInt32(), MaximumIndices);
        Vector3[] vertices = ReadVectors(reader, vertexCount);
        Vector3[] normals = ReadVectors(reader, vertexCount);
        var color = new Color(ReadFloat(reader), ReadFloat(reader), ReadFloat(reader), ReadFloat(reader));
        int[] indices = ReadIndices(reader, indexCount, vertexCount);
        Vector3 position = ReadVector(reader);
        var rotation = new Quaternion(ReadFloat(reader), ReadFloat(reader), ReadFloat(reader), ReadFloat(reader));
        Vector3 scale = ReadVector(reader);
        if (name == "Runway" && elevation > 0) ExtendRunwayFoundation(vertices, position, elevation);
        return new AircraftModelPart(name, CreateMesh(vertices, normals, indices), color, position + Vector3.up * elevation, rotation, scale);
    }

    private static void ExtendRunwayFoundation(Vector3[] vertices, Vector3 position, float elevation)
    {
        float top = float.NegativeInfinity;
        foreach (Vector3 vertex in vertices) top = Math.Max(top, vertex.y);
        float foundation = -Airfield.MaximumTerrainDeviation - RunwaySurfaceSeparation - position.y - elevation;
        for (int index = 0; index < vertices.Length; index++)
            if (vertices[index].y < top) vertices[index].y = Math.Min(vertices[index].y, foundation);
    }

    private static Mesh? CreateMesh(Vector3[] vertices, Vector3[] normals, int[] indices)
    {
        if (vertices.Length == 0) return null;
        var mesh = new Mesh { vertices = vertices, normals = normals, triangles = indices };
        mesh.RecalculateBounds();
        return mesh;
    }

    private static int[] ReadIndices(BinaryReader reader, int count, int vertexCount)
    {
        if (count % 3 != 0) throw new InvalidDataException("Aircraft mesh indices must form triangles.");
        var result = new int[count];
        for (int index = 0; index < count; index++)
        {
            result[index] = reader.ReadInt32();
            if ((uint)result[index] >= (uint)vertexCount) throw new InvalidDataException("Aircraft mesh index is invalid.");
        }
        return result;
    }

    private static Vector3[] ReadVectors(BinaryReader reader, int count)
    {
        var values = new Vector3[count];
        for (int index = 0; index < count; index++) values[index] = ReadVector(reader);
        return values;
    }

    private static Vector3 ReadVector(BinaryReader reader) => new(ReadFloat(reader), ReadFloat(reader), ReadFloat(reader));
    private static float ReadFloat(BinaryReader reader)
    {
        float value = reader.ReadSingle();
        if (float.IsNaN(value) || float.IsInfinity(value)) throw new InvalidDataException("Non-finite aircraft model data.");
        return value;
    }

    private static int RequireCount(int count, int maximum)
    {
        if (count < 0 || count > maximum) throw new InvalidDataException("Aircraft model count exceeds limits.");
        return count;
    }

    public void Dispose()
    {
        foreach (AircraftModelPart[] parts in _models.Values)
            ReleaseMeshes(parts);
        foreach (Material material in _materials) UnityEngine.Object.Destroy(material);
        _models.Clear();
        _materials.Clear();
        _partMaterials.Clear();
    }

    private static void ReleaseMeshes(AircraftModelPart[] parts)
    {
        foreach (AircraftModelPart? part in parts)
            if (part?.Mesh != null) UnityEngine.Object.Destroy(part.Mesh);
    }
}
