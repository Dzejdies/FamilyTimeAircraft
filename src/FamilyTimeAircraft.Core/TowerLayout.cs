namespace FamilyTimeAircraft.Core;

public static class TowerLayout
{
    public const int SectionsPerTower = 8;
    public const int TowerCount = 2;
    public const int SectionsPerPair = SectionsPerTower * TowerCount;
    public const int HorizontalTowerOffset = 5;
    public const int SectionHeight = 3;
    public const int MaximumTowerHeight = SectionsPerTower * SectionHeight;
    public const int TowerFootprintHalfWidth = 8;
    public const int TowerFootprintHalfDepth = 5;
    public const int MaximumTowerPairs = 4;
    public const int MaximumActiveDebrisPerPair = 32;
    public const int DebrisVariants = 6;
    public const float DebrisLifetimeSeconds = 15;

    internal static TowerSectionState[] CreateSections()
    {
        var sections = new TowerSectionState[SectionsPerPair];
        for (int towerIndex = 0; towerIndex < TowerCount; towerIndex++)
            for (int level = 0; level < SectionsPerTower; level++)
            {
                int sectionId = towerIndex * SectionsPerTower + level;
                sections[sectionId] = new TowerSectionState(sectionId, towerIndex, level);
            }
        return sections;
    }
}
