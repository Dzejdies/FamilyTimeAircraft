using System;
using System.Numerics;

namespace FamilyTimeAircraft.Core;

public static class TowerRules
{
    public const float InitialIntegrity = 100;
    public const float MaximumImpactSpeed = 36;
    public const float DamagePerImpactStrength = 0.2f;
    public const float MinimumDirectionLength = 0.001f;
    public const float UnitVectorTolerance = 0.05f;

    public static float ImpactStrength(float speed, Vector3 direction, Vector3 surfaceNormal)
    {
        RequireImpact(speed, direction, surfaceNormal);
        float incidence = Math.Max(0, -Vector3.Dot(Vector3.Normalize(direction), Vector3.Normalize(surfaceNormal)));
        return speed * speed * incidence;
    }

    internal static void RequireImpact(float speed, Vector3 direction, Vector3 surfaceNormal)
    {
        if (!AircraftRules.IsFinite(speed) || speed < 0 || speed > MaximumImpactSpeed)
            throw new ArgumentOutOfRangeException(nameof(speed));
        RequireUnitVector(direction, nameof(direction));
        RequireUnitVector(surfaceNormal, nameof(surfaceNormal));
    }

    private static void RequireUnitVector(Vector3 value, string parameterName)
    {
        float length = value.Length();
        if (!AircraftRules.IsFinite(value) || length < MinimumDirectionLength || Math.Abs(length - 1) > UnitVectorTolerance)
            throw new ArgumentException("Tower impact vectors must be finite unit vectors.", parameterName);
    }
}
