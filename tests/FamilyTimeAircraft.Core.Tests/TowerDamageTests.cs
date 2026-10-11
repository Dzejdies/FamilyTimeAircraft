using System;
using System.Linq;
using System.Numerics;
using Xunit;

namespace FamilyTimeAircraft.Core.Tests;

public sealed class TowerDamageTests
{
    [Fact]
    public void BuildTowerPairCreatesTwoStableSectionStacks()
    {
        var world = new AircraftWorld();

        TowerPair pair = world.BuildTowerPair("shared", Vector3.Zero, Quaternion.Identity);

        Assert.Equal(TowerLayout.SectionsPerPair, pair.Sections.Count);
        Assert.Equal(Enumerable.Range(0, TowerLayout.SectionsPerPair), pair.Sections.Select(section => section.Id));
        Assert.Equal(0, pair.Sections[0].TowerIndex);
        Assert.Equal(1, pair.Sections[TowerLayout.SectionsPerTower].TowerIndex);
    }

    [Fact]
    public void TowerPairRejectsInvalidOwnerAndPose()
    {
        var world = new AircraftWorld();

        Assert.Throws<ArgumentException>(() => world.BuildTowerPair(" ", Vector3.Zero, Quaternion.Identity));
        Assert.Throws<ArgumentException>(() => world.BuildTowerPair("owner", new Vector3(float.NaN, 0, 0), Quaternion.Identity));
        Assert.Throws<ArgumentException>(() => world.BuildTowerPair("owner", Vector3.Zero, new Quaternion(0, 0, 0, 2)));
    }

    [Fact]
    public void TowerPairLimitIsEnforced()
    {
        var world = new AircraftWorld();
        for (int index = 0; index < TowerLayout.MaximumTowerPairs; index++)
            world.BuildTowerPair("owner", new Vector3(index * 100, 0, 0), Quaternion.Identity);

        Assert.Throws<InvalidOperationException>(() => world.BuildTowerPair("owner", Vector3.Zero, Quaternion.Identity));
    }

    [Fact]
    public void HeadOnImpactDamagesMoreThanGlancingImpact()
    {
        var headOnWorld = new AircraftWorld();
        TowerPair headOnPair = headOnWorld.BuildTowerPair("owner", Vector3.Zero, Quaternion.Identity);
        headOnWorld.ApplyTowerImpact(headOnPair.Id, 0, 12, Vector3.UnitZ, -Vector3.UnitZ, 1);

        var glancingWorld = new AircraftWorld();
        TowerPair glancingPair = glancingWorld.BuildTowerPair("owner", Vector3.Zero, Quaternion.Identity);
        glancingWorld.ApplyTowerImpact(glancingPair.Id, 0, 12, Vector3.UnitX, -Vector3.UnitZ, 1);

        Assert.True(headOnPair.Sections[0].Integrity < glancingPair.Sections[0].Integrity);
    }

    [Fact]
    public void ImpactDamagesOnlyTheTargetedSection()
    {
        var world = new AircraftWorld();
        TowerPair pair = world.BuildTowerPair("owner", Vector3.Zero, Quaternion.Identity);

        world.ApplyTowerImpact(pair.Id, 3, 12, Vector3.UnitZ, -Vector3.UnitZ, 42);

        Assert.True(pair.Sections[3].Integrity < TowerRules.InitialIntegrity);
        Assert.All(pair.Sections.Where(section => section.Id != 3), section => Assert.Equal(TowerRules.InitialIntegrity, section.Integrity));
    }

    [Fact]
    public void RepeatedImpactSequenceDoesNotDamageASectionTwice()
    {
        var world = new AircraftWorld();
        TowerPair pair = world.BuildTowerPair("shared", Vector3.Zero, Quaternion.Identity);

        world.ApplyTowerImpact(pair.Id, 3, 12, Vector3.UnitZ, -Vector3.UnitZ, 42);
        float integrityAfterFirstHit = pair.Sections[3].Integrity;
        world.ApplyTowerImpact(pair.Id, 3, 12, Vector3.UnitZ, -Vector3.UnitZ, 42);

        Assert.Equal(integrityAfterFirstHit, pair.Sections[3].Integrity);
    }

    [Fact]
    public void FailedSectionCollapsesUnsupportedSectionsTopToBottom()
    {
        var world = new AircraftWorld();
        TowerPair pair = world.BuildTowerPair("owner", Vector3.Zero, Quaternion.Identity);
        for (int hit = 1; hit <= 10; hit++)
            world.ApplyTowerImpact(pair.Id, 2, 36, Vector3.UnitZ, -Vector3.UnitZ, hit);

        TowerEvent[] events = world.DrainTowerEvents().Where(item => item.Kind == TowerEventKind.SectionFailed).ToArray();

        Assert.Equal(new[] { 7, 6, 5, 4, 3, 2 }, events.Select(item => item.SectionId));
        Assert.All(events, item => Assert.Equal(0, item.TowerIndex));
    }

    [Fact]
    public void InvalidImpactVectorsAndSequencesAreRejected()
    {
        var world = new AircraftWorld();
        TowerPair pair = world.BuildTowerPair("owner", Vector3.Zero, Quaternion.Identity);

        Assert.Throws<ArgumentException>(() => world.ApplyTowerImpact(pair.Id, 0, 12, Vector3.Zero, -Vector3.UnitZ, 1));
        Assert.Throws<ArgumentException>(() => world.ApplyTowerImpact(pair.Id, 0, 12, Vector3.UnitZ, Vector3.Zero, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => world.ApplyTowerImpact(pair.Id, 0, 12, Vector3.UnitZ, -Vector3.UnitZ, 0));
    }

    [Fact]
    public void ReplicaAppliesHostFailureOnceWithoutRecomputingDamage()
    {
        var host = new AircraftWorld();
        TowerPair hostPair = host.BuildTowerPair("owner", Vector3.Zero, Quaternion.Identity);
        TowerEvent failure = host.ApplyTowerImpact(hostPair.Id, 2, 36, Vector3.UnitZ, -Vector3.UnitZ, 1).Last();
        var client = new AircraftWorld();
        TowerPair clientPair = client.BuildTowerPair("owner", Vector3.Zero, Quaternion.Identity);

        Assert.True(client.ApplyTowerEvent(failure));
        Assert.False(client.ApplyTowerEvent(failure));
        Assert.Equal(TowerSectionPhase.Collapsing, clientPair.Sections[2].Phase);
        Assert.Equal(0, clientPair.Sections[2].Integrity);
        Assert.Empty(client.DrainTowerEvents());
    }
}
