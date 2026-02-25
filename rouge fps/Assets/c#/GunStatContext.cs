using System;
using System.Collections.Generic;
using UnityEngine;

public enum GunStat
{
    Damage,
    FireRate,
    SemiFireCooldown,
    BulletSpeed,
    MaxRange,
    ProjectileFalloffStartMeters,
    MagazineSize,
    ReloadTime,

    // Higher = faster spread recovery.
    // This stat is treated as a "speed value" (final value), based on baseSpreadRecoverySpeed.
    SpreadRecoverySpeed,

    // Shotgun pellets per shot (int-like stat)
    ShotgunPelletsPerShot
}

[Serializable]
public struct StatStack
{
    public float flat;
    public float addPct;
    public float mul;

    // Applied after addPct (and after mul) ONLY when using EvaluateWithPostMul()
    public float postMul;

    public void Reset()
    {
        flat = 0f;
        addPct = 0f;
        mul = 1f;
        postMul = 1f;
    }

    // Existing behavior: (base + flat) * (1 + (addPct + (mul-1)))
    public float Evaluate(float baseValue)
    {
        float combined = addPct + (mul - 1f);
        return (baseValue + flat) * (1f + combined);
    }

    // Ordered evaluation with postMul:
    // (base + flat) * (1 + addPct) * mul * postMul
    public float EvaluateWithPostMul(float baseValue)
    {
        float v = baseValue + flat;
        v *= (1f + addPct);
        v *= mul;
        v *= postMul;
        return v;
    }
}

public interface IGunStatModifier
{
    int Priority { get; }
    void ApplyModifiers(CameraGunChannel source, Dictionary<GunStat, StatStack> stacks);
}

public sealed class GunStatContext : MonoBehaviour
{
    [Header("Auto capture base stats from gun components on Awake")]
    public bool captureBaseFromGunOnAwake = true;

    [Header("Apply computed stats back to runtime components")]
    public bool applyToGunChannel = true;
    public bool applyToGunAmmo = true;

    [Header("Base values (captured or manual)")]
    public float baseDamage = 10f;
    public float baseFireRate = 5f;
    public float baseSemiFireCooldown = 0.15f;
    public float baseBulletSpeed = 50f;
    public float baseMaxRange = 40f;

    [Tooltip("Projectile-only: distance (meters) at which damage falloff starts.")]
    public float baseProjectileFalloffStartMeters = 0f;

    [Header("Base shotgun")]
    public int baseShotgunPelletsPerShot = 8;

    [Header("Ammo base values (captured or manual)")]
    public int baseMagazineSize = 12;
    public float baseReloadTime = 1.5f;

    [Header("Base spread")]
    [Tooltip("Captured from GunSpread.spreadRecoverSpeed if a GunSpread is found. Otherwise uses this value.")]
    public float baseSpreadRecoverySpeed = 1f;

    private CameraGunChannel _gun;
    private GunAmmo _ammo;
    private GunSpread _spread;

    private readonly List<IGunStatModifier> _mods = new();
    private readonly Dictionary<GunStat, StatStack> _stacks = new();
    private bool _dirty = true;

    private void Awake()
    {
        _gun = GetComponent<CameraGunChannel>();
        if (_gun == null) _gun = GetComponentInParent<CameraGunChannel>();

        _ammo = ResolveAmmo(_gun);

        // Resolve spread reference early so we can capture its base recovery speed.
        _spread = ResolveSpread(_gun);

        if (captureBaseFromGunOnAwake)
        {
            if (_gun != null)
            {
                baseDamage = _gun.baseDamage;
                baseFireRate = _gun.fireRate;
                baseSemiFireCooldown = Mathf.Max(0f, _gun.semiFireCooldown);
                baseBulletSpeed = _gun.bulletSpeed;
                baseMaxRange = _gun.maxRange;
                baseProjectileFalloffStartMeters = Mathf.Max(0f, _gun.projectileFalloffStartMeters);

                baseShotgunPelletsPerShot = Mathf.Max(1, _gun.pelletsPerShot);
            }

            if (_ammo != null)
            {
                baseMagazineSize = Mathf.Max(1, _ammo.magazineSize);
                baseReloadTime = Mathf.Max(0.01f, _ammo.reloadTimeMagazine);
            }

            // IMPORTANT: capture base spread recovery speed from GunSpread if present
            if (_spread != null)
            {
                baseSpreadRecoverySpeed = Mathf.Max(0.01f, _spread.spreadRecoverSpeed);
            }
            else
            {
                baseSpreadRecoverySpeed = Mathf.Max(0.01f, baseSpreadRecoverySpeed);
            }
        }
        else
        {
            baseSpreadRecoverySpeed = Mathf.Max(0.01f, baseSpreadRecoverySpeed);
        }

        ForceRebuildNow();
    }

    private void Update()
    {
        if (_dirty)
            RebuildIfDirty();
    }

    // KEEP: method name
    public void Register(IGunStatModifier mod)
    {
        if (mod == null) return;
        if (_mods.Contains(mod)) return;
        _mods.Add(mod);
        _dirty = true;
    }

    // KEEP: method name
    public void Unregister(IGunStatModifier mod)
    {
        if (mod == null) return;
        if (_mods.Remove(mod)) _dirty = true;
    }

    // KEEP: method name
    public void MarkDirty() => _dirty = true;

    // KEEP: method name
    public void ForceRebuildNow()
    {
        _dirty = true;
        RebuildIfDirty();
    }

    // Public access to current stack (useful for GunSpread/UI/debug)
    public StatStack GetStack(GunStat stat)
    {
        RebuildIfDirty();
        return _stacks.TryGetValue(stat, out var st) ? st : default;
    }

    // Common helper: get final value using Evaluate(baseValue)
    public float GetFinalValue(GunStat stat, float baseValue)
    {
        RebuildIfDirty();
        if (!_stacks.TryGetValue(stat, out var st)) return baseValue;
        return st.Evaluate(baseValue);
    }

    // Common helper: get final multiplier-like number where base is 1
    public float GetFinalMultiplier(GunStat stat)
    {
        RebuildIfDirty();
        if (!_stacks.TryGetValue(stat, out var st)) return 1f;
        return Mathf.Max(0.0001f, st.Evaluate(1f));
    }

    // Returns FINAL spread recovery speed value (already includes baseSpreadRecoverySpeed)
    public float GetSpreadRecoverySpeedFinal()
    {
        RebuildIfDirty();
        float baseV = Mathf.Max(0.01f, baseSpreadRecoverySpeed);
        float finalV = _stacks[GunStat.SpreadRecoverySpeed].Evaluate(baseV);
        return Mathf.Max(0.0001f, finalV);
    }

    // Returns multiplier relative to baseSpreadRecoverySpeed (safe for multiplying a base speed)
    public float GetSpreadRecoverySpeedMul()
    {
        RebuildIfDirty();
        float baseV = Mathf.Max(0.01f, baseSpreadRecoverySpeed);
        float finalV = Mathf.Max(0.0001f, _stacks[GunStat.SpreadRecoverySpeed].Evaluate(baseV));
        return Mathf.Max(0.0001f, finalV / baseV);
    }

    // Backward-compatible name: "Multiplier" returns RELATIVE multiplier (not the final value)
    public float GetSpreadRecoverySpeedMultiplier() => GetSpreadRecoverySpeedMul();

    private void RebuildIfDirty()
    {
        if (!_dirty) return;

        _stacks.Clear();
        foreach (GunStat s in Enum.GetValues(typeof(GunStat)))
        {
            var st = new StatStack();
            st.Reset();
            _stacks[s] = st;
        }

        _mods.Sort((a, b) => a.Priority.CompareTo(b.Priority));

        for (int i = 0; i < _mods.Count; i++)
            _mods[i].ApplyModifiers(_gun, _stacks);

        _dirty = false;

        ApplyRuntimeIfEnabled();
    }

    private void ApplyRuntimeIfEnabled()
    {
        if (applyToGunChannel)
            ApplyGunStatsToRuntime();

        if (applyToGunAmmo)
            ApplyAmmoStatsToRuntime();

        // NEW: push final spread recovery speed back to GunSpread so inspector/runtime matches
        if (_spread != null)
            _spread.spreadRecoverSpeed = GetSpreadRecoverySpeedFinal();
    }

    private void ApplyGunStatsToRuntime()
    {
        if (_gun == null) return;

        _gun.baseDamage = GetDamage_Internal();
        _gun.fireRate = Mathf.Max(0.01f, GetFireRate_Internal());
        _gun.semiFireCooldown = Mathf.Max(0f, GetSemiFireCooldown_Internal());
        _gun.bulletSpeed = Mathf.Max(0.01f, GetBulletSpeed_Internal());
        _gun.maxRange = Mathf.Max(0.01f, GetMaxRange_Internal());
        _gun.projectileFalloffStartMeters = Mathf.Max(0f, GetProjectileFalloffStart_Internal());

        _gun.pelletsPerShot = Mathf.Max(1, GetShotgunPelletsPerShot_Internal());
    }

    private void ApplyAmmoStatsToRuntime()
    {
        _ammo ??= ResolveAmmo(_gun);
        if (_ammo == null) return;

        int mag = GetMagazineSize_Internal();
        float reload = GetReloadTime_Internal();

        _ammo.magazineSize = Mathf.Max(1, mag);
        _ammo.reloadTimeMagazine = Mathf.Max(0.01f, reload);

        _ammo.ammoInMag = Mathf.Min(_ammo.ammoInMag, _ammo.magazineSize);
        _ammo.OnAmmoChanged?.Invoke(_ammo.ammoInMag, _ammo.ammoReserve);
    }

    // KEEP: method names (public getters)
    public float GetDamage() { RebuildIfDirty(); return GetDamage_Internal(); }
    public float GetFireRate() { RebuildIfDirty(); return GetFireRate_Internal(); }
    public float GetSemiFireCooldown() { RebuildIfDirty(); return GetSemiFireCooldown_Internal(); }
    public float GetBulletSpeed() { RebuildIfDirty(); return GetBulletSpeed_Internal(); }
    public float GetMaxRange() { RebuildIfDirty(); return GetMaxRange_Internal(); }
    public float GetProjectileFalloffStart() { RebuildIfDirty(); return GetProjectileFalloffStart_Internal(); }
    public int GetMagazineSize() { RebuildIfDirty(); return GetMagazineSize_Internal(); }
    public float GetReloadTime() { RebuildIfDirty(); return GetReloadTime_Internal(); }
    public int GetShotgunPelletsPerShot() { RebuildIfDirty(); return GetShotgunPelletsPerShot_Internal(); }

    private float GetDamage_Internal() => _stacks[GunStat.Damage].Evaluate(baseDamage);
    private float GetFireRate_Internal() => _stacks[GunStat.FireRate].Evaluate(baseFireRate);

    private float GetSemiFireCooldown_Internal()
    {
        float v = _stacks[GunStat.SemiFireCooldown].Evaluate(Mathf.Max(0f, baseSemiFireCooldown));
        return Mathf.Max(0f, v);
    }

    private float GetBulletSpeed_Internal() => _stacks[GunStat.BulletSpeed].Evaluate(baseBulletSpeed);
    private float GetMaxRange_Internal() => _stacks[GunStat.MaxRange].Evaluate(baseMaxRange);

    private float GetProjectileFalloffStart_Internal()
    {
        return Mathf.Max(0f, _stacks[GunStat.ProjectileFalloffStartMeters].Evaluate(baseProjectileFalloffStartMeters));
    }

    private int GetMagazineSize_Internal()
    {
        float v = _stacks[GunStat.MagazineSize].Evaluate(Mathf.Max(1f, baseMagazineSize));
        return Mathf.Max(1, Mathf.RoundToInt(v));
    }

    private float GetReloadTime_Internal()
    {
        float v = _stacks[GunStat.ReloadTime].Evaluate(Mathf.Max(0.01f, baseReloadTime));
        return Mathf.Max(0.01f, v);
    }

    // pellets uses ordered evaluation so postMul is guaranteed after addPct
    private int GetShotgunPelletsPerShot_Internal()
    {
        float baseVal = Mathf.Max(1f, baseShotgunPelletsPerShot);

        float v = _stacks[GunStat.ShotgunPelletsPerShot].EvaluateWithPostMul(baseVal);

        int pellets = Mathf.FloorToInt(v + 0.0001f);
        return Mathf.Max(1, pellets);
    }

    private static GunAmmo ResolveAmmo(CameraGunChannel gun)
    {
        if (gun == null) return null;
        if (gun.ammo != null) return gun.ammo;

        var a = gun.GetComponent<GunAmmo>();
        if (a != null) return a;

        return gun.GetComponentInParent<GunAmmo>();
    }

    private static GunSpread ResolveSpread(CameraGunChannel gun)
    {
        if (gun == null) return null;

        // Prefer the direct reference first
        if (gun.spread != null) return gun.spread;

        // Fallback search
        var s = gun.GetComponent<GunSpread>();
        if (s != null) return s;

        return gun.GetComponentInParent<GunSpread>();
    }
}