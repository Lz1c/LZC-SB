using System.Collections.Generic;
using UnityEngine;

public sealed class Perk_FastFireLowDamage : GunPerkModifierBase
{
    [Header("Multipliers")]
    [Tooltip("Multiply fire rate. > 1 means faster.")]
    [Min(0.01f)] public float fireRateMultiplier = 2.5f;

    [Tooltip("Multiply damage. < 1 means lower.")]
    [Min(0f)] public float damageMultiplier = 0.6f;

    [Tooltip("Lower runs first, higher runs later.")]
    public int priority = 0;

    public override int Priority => priority;

    public override void ApplyModifiers(CameraGunChannel source, Dictionary<GunStat, StatStack> stacks)
    {
        if (source == null || stacks == null) return;

        if (stacks.TryGetValue(GunStat.FireRate, out var fr))
        {
            fr.mul *= Mathf.Max(0.0001f, fireRateMultiplier);
            stacks[GunStat.FireRate] = fr;
        }

        if (stacks.TryGetValue(GunStat.Damage, out var dmg))
        {
            dmg.mul *= Mathf.Max(0f, damageMultiplier);
            stacks[GunStat.Damage] = dmg;
        }
    }
}