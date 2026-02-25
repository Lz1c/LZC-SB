using System.Collections.Generic;
using UnityEngine;

public sealed class Perk_ShotgunMode : GunPerkModifierBase
{
    [Header("Shotgun")]
    [Min(1)] public int pelletsPerShot = 6;

    [Tooltip("If true, keep total damage roughly constant by dividing by pellets.")]
    public bool keepTotalDamageConstant = true;

    [Tooltip("Multiplier applied to total damage (or per-pellet if keepTotalDamageConstant is false).")]
    [Min(0f)] public float totalDamageMultiplier = 1f;

    [Header("Priority")]
    public int priority = 0;
    public override int Priority => priority;

    private CameraGunChannel.ShotType _prevShotType;
    private int _prevPellets;
    private bool _applied;

    private void OnEnable()
    {
        base.OnEnable();

        if (SourceGun == null) return;

        if (!_applied)
        {
            _prevShotType = SourceGun.shotType;
            _prevPellets = SourceGun.pelletsPerShot;

            SourceGun.shotType = CameraGunChannel.ShotType.Shotgun;
            SourceGun.pelletsPerShot = Mathf.Max(1, pelletsPerShot);

            _applied = true;
        }
    }

    private void OnDisable()
    {
        if (_applied && SourceGun != null)
        {
            SourceGun.shotType = _prevShotType;
            SourceGun.pelletsPerShot = _prevPellets;
            _applied = false;
        }

        base.OnDisable();
    }

    public override void ApplyModifiers(CameraGunChannel source, Dictionary<GunStat, StatStack> stacks)
    {
        if (source == null || stacks == null) return;

        if (!stacks.TryGetValue(GunStat.Damage, out var dmg)) return;

        int pellets = Mathf.Max(1, pelletsPerShot);
        float mult;

        if (keepTotalDamageConstant)
            mult = (pellets > 0) ? (totalDamageMultiplier / pellets) : 0f;
        else
            mult = totalDamageMultiplier;

        dmg.mul *= Mathf.Max(0f, mult);
        stacks[GunStat.Damage] = dmg;
    }
}