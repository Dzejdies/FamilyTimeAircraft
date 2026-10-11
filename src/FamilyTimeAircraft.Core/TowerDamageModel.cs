using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace FamilyTimeAircraft.Core;

internal static class TowerDamageModel
{
    internal static TowerDamageResult Apply(TowerPair pair, int sectionId, float impactStrength)
    {
        TowerSectionState target = pair.Sections[sectionId];
        if (target.Phase >= TowerSectionPhase.Collapsing || impactStrength <= 0)
            return new TowerDamageResult(Array.Empty<TowerSectionState>());

        target.Integrity = System.Math.Max(0, target.Integrity - impactStrength * TowerRules.DamagePerImpactStrength);
        if (target.Integrity > 0)
        {
            target.Phase = TowerSectionPhase.Damaged;
            return new TowerDamageResult(new[] { target });
        }

        TowerSectionState[] unsupported = pair.Sections
            .Where(section => section.TowerIndex == target.TowerIndex && section.Level >= target.Level)
            .OrderByDescending(section => section.Level)
            .Where(section => section.Phase < TowerSectionPhase.Collapsing)
            .ToArray();
        foreach (TowerSectionState section in unsupported)
        {
            section.Integrity = 0;
            section.Phase = TowerSectionPhase.Collapsing;
        }
        return new TowerDamageResult(unsupported);
    }
}
