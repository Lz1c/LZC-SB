using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Increases projectile max range (MaxRange) and projectile falloff start distance (ProjectileFalloffStartMeters)
/// via GunStatContext.
/// Attach under the gun object so it can find CameraGunChannel in parents.
/// </summary>
public sealed class Perk_ProjectileRangeAndFalloffBoost : GunPerkModifierBase
{
    [Header("Flat meters")]
    [Min(0f)] public float addMaxRangeMeters = 25f;
    [Min(0f)] public float addFalloffStartMeters = 10f;

    [Header("Optional percent bonuses (addPct form, e.g. 0.2 = +20%)")]
    public float maxRangeAddPct = 0f;
    public float falloffStartAddPct = 0f;

    public override int Priority => 0;

    public override void ApplyModifiers(CameraGunChannel source, Dictionary<GunStat, StatStack> stacks)
    {
        if (SourceGun == null || source != SourceGun) return;

        ApplyOne(GunStat.MaxRange, addMaxRangeMeters, maxRangeAddPct, stacks);
        ApplyOne(GunStat.ProjectileFalloffStartMeters, addFalloffStartMeters, falloffStartAddPct, stacks);
    }

    private static void ApplyOne(GunStat stat, float flat, float addPct, Dictionary<GunStat, StatStack> stacks)
    {
        var st = stacks[stat];
        st.flat += flat;
        st.addPct += addPct;
        st.mul = 1f; // keep unified
        stacks[stat] = st;
    }
}