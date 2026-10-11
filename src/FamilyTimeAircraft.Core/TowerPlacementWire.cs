using System;

namespace FamilyTimeAircraft.Core;

public sealed class TowerPlacementRequest
{
    public const int MaximumGridCoordinate = 32767;

    public TowerPlacementRequest(int gridX, int gridZ, byte quarterTurns)
    {
        if (gridX < -MaximumGridCoordinate || gridX > MaximumGridCoordinate || gridZ < -MaximumGridCoordinate || gridZ > MaximumGridCoordinate)
            throw new ArgumentOutOfRangeException(nameof(gridX));
        if (quarterTurns > 3) throw new ArgumentOutOfRangeException(nameof(quarterTurns));
        GridX = gridX;
        GridZ = gridZ;
        QuarterTurns = quarterTurns;
    }

    public int GridX { get; }
    public int GridZ { get; }
    public byte QuarterTurns { get; }
}
