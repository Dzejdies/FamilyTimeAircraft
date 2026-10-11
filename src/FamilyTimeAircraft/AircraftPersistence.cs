using System.Collections;
using System.IO;
using FamilyTimeAircraft.Core;
using GAT;
using HarmonyLib;
using UnityEngine;

namespace FamilyTimeAircraft;

public sealed class AircraftPersistence : MonoBehaviour
{
    private const string SectionId = "fta.aircraft";
    private const int SectionVersion = 2;
    private const int SectionOrder = 100100;
    private AircraftRuntime _runtime = null!;
    private WorldSerializer? _registered;

    public void Initialize(AircraftRuntime runtime) { _runtime = runtime; Register(); }
    private void Update() => Register();

    public void Register()
    {
        if (World.worldSerializer == null || World.worldSerializer == _registered) return;
        World.worldSerializer.Register(SectionId, this, SectionOrder, SaveSection, LoadSection);
        _registered = World.worldSerializer;
    }

    private IEnumerator SaveSection(MemoryStream stream)
    {
        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true);
        writer.Write(SectionVersion);
        using var worldStream = new MemoryStream();
        WorldRecord.Write(worldStream, _runtime.World);
        WriteBlob(writer, worldStream.ToArray());
        WriteBlob(writer, _runtime.CaptureBuildState());
        yield break;
    }

    private IEnumerator LoadSection(MemoryStream stream)
    {
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true);
        if (reader.ReadInt32() != SectionVersion) throw new InvalidDataException("Unsupported aircraft save section.");
        using var worldStream = new MemoryStream(ReadBlob(reader), false);
        _runtime.ApplySavedSnapshot(WorldRecord.Read(worldStream));
        _runtime.RestoreBuildState(ReadBlob(reader));
        if (stream.Position != stream.Length) throw new InvalidDataException("Trailing aircraft save section data.");
        yield break;
    }

    private static void WriteBlob(BinaryWriter writer, byte[] bytes) { writer.Write(bytes.Length); writer.Write(bytes); }
    private static byte[] ReadBlob(BinaryReader reader)
    {
        int length = reader.ReadInt32();
        if (length < 0 || length > WorldRecord.MaximumBytes) throw new InvalidDataException("Aircraft save payload exceeds limits.");
        byte[] bytes = reader.ReadBytes(length);
        if (bytes.Length != length) throw new EndOfStreamException();
        return bytes;
    }
}

[HarmonyPatch(typeof(WorldSerializer), nameof(WorldSerializer.Load))]
internal static class RegisterAircraftBeforeLoadPatch
{
    private static void Prefix() => AircraftRuntime.Current?.GetComponent<AircraftPersistence>().Register();
}

[HarmonyPatch(typeof(CommandService), nameof(CommandService.ClearForWorldLoad))]
internal static class ClearAircraftOnAcceptedWorldLoadPatch
{
    private static void Prefix() => AircraftRuntime.Current?.ClearWorld();
}

[HarmonyPatch(typeof(World), "OnDestroy")]
internal static class ClearAircraftWithNativeWorldPatch
{
    private static void Prefix() => AircraftRuntime.Current?.ClearWorld();
}
