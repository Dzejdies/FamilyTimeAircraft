using System.IO;
using System.Numerics;
using System.Text;
using Xunit;

namespace FamilyTimeAircraft.Core.Tests;

public sealed class TowerPersistenceTests
{
    [Fact]
    public void VersionThreeSaveRestoresDamagedTowerState()
    {
        var world = new AircraftWorld();
        TowerPair pair = world.BuildTowerPair("family", new Vector3(12, 0, 25), Quaternion.Identity);
        world.ApplyTowerImpact(pair.Id, 3, 12, Vector3.UnitZ, -Vector3.UnitZ, 8);
        using var stream = new MemoryStream();

        WorldRecord.Write(stream, world);
        stream.Position = 0;
        AircraftWorld restored = WorldRecord.Read(stream);

        TowerPair restoredPair = Assert.Single(restored.TowerPairs);
        Assert.Equal(pair.Id, restoredPair.Id);
        Assert.Equal(pair.Position, restoredPair.Position);
        Assert.Equal(pair.Sections[3].Integrity, restoredPair.Sections[3].Integrity);
        Assert.Equal(TowerSectionPhase.Damaged, restoredPair.Sections[3].Phase);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void LegacySaveLoadsWithNoTowerPairs(int version)
    {
        using MemoryStream stream = CreateLegacyWorld(version);

        AircraftWorld world = WorldRecord.Read(stream);

        Assert.Empty(world.TowerPairs);
    }

    [Fact]
    public void SaveRejectsMoreThanTheMaximumTowerPairs()
    {
        var world = new AircraftWorld();
        using var stream = new MemoryStream();
        WorldRecord.Write(stream, world);
        byte[] bytes = stream.ToArray();
        using (var writer = new BinaryWriter(new MemoryStream(bytes, true)))
        {
            writer.BaseStream.Position = 52;
            writer.Write(TowerLayout.MaximumTowerPairs + 1);
        }

        Assert.Throws<InvalidDataException>(() => WorldRecord.Read(new MemoryStream(bytes)));
    }

    [Fact]
    public void SaveRejectsMismatchedTowerSectionIdentity()
    {
        var world = new AircraftWorld();
        world.BuildTowerPair("family", Vector3.Zero, Quaternion.Identity);
        using var stream = new MemoryStream();
        WorldRecord.Write(stream, world);
        byte[] bytes = stream.ToArray();
        using (var writer = new BinaryWriter(new MemoryStream(bytes, true)))
        {
            writer.BaseStream.Position = 102;
            writer.Write(1);
        }

        Assert.Throws<InvalidDataException>(() => WorldRecord.Read(new MemoryStream(bytes)));
    }

    private static MemoryStream CreateLegacyWorld(int version)
    {
        var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
        {
            writer.Write(0x31544146);
            writer.Write(version);
            writer.Write(0L);
            writer.Write(1);
            writer.Write(1);
            writer.Write(0);
            writer.Write(0);
            writer.Write(0);
            if (version >= 2) writer.Write(0);
        }
        stream.Position = 0;
        return stream;
    }
}
