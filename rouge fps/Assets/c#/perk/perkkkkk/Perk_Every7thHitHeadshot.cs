using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

public sealed class Perk_Every7thHitHeadshot : GunPerkModifierBase
{
    [Header("Rule")]
    [Min(1)] public int everyNthHit = 7;

    [Header("Extra Damage Proc")]
    public bool extraDamageSkipHitEvent = true;
    public bool showExtraHitUI = false;
    [Min(0f)] public float maxExtraDamagePerHit = 99999f;

    [Header("Hitbox Multiplier Auto-Read")]
    [Tooltip("If true, head multiplier will be detected as the maximum hitbox multiplier found on the target root.")]
    public bool headIsMaxMultiplierOnTarget = true;

    [Tooltip("If true, the current hit's multiplier will be read from the hit collider (body/limb/etc).")]
    public bool readBodyMultiplierFromHitCollider = true;

    [Tooltip("Fallback multiplier when the hit collider multiplier cannot be read.")]
    [Min(0.01f)] public float fallbackBodyMultiplier = 1f;

    [Tooltip("Fallback head multiplier when target scan fails.")]
    [Min(1f)] public float fallbackHeadMultiplier = 2f;

    private int _hitCount;

    // Cache multipliers per target root instance id.
    private readonly Dictionary<int, float> _cachedHeadMulByRoot = new Dictionary<int, float>(64);
    public override void ApplyModifiers(CameraGunChannel source, Dictionary<GunStat, StatStack> stacks)
    {
        // No stat changes. This perk only adds extra damage on every Nth hit.
    }
    // Reflection cache for HitEvent.
    private static bool _refReady;
    private static FieldInfo _fSource;
    private static FieldInfo _fFlags;
    private static FieldInfo _fCollider;
    private static FieldInfo _fHitPoint;
    private static FieldInfo _fDamageFloat;
    private static FieldInfo _fDamageInfo;

    private void OnEnable()
    {
        base.OnEnable();
        PrepareReflection();
        CombatEventHub.OnHit += OnHit;
        _hitCount = 0;
    }

    private void OnDisable()
    {
        CombatEventHub.OnHit -= OnHit;
        base.OnDisable();
    }

    private void OnDestroy()
    {
        CombatEventHub.OnHit -= OnHit;
    }

    private void OnHit(CombatEventHub.HitEvent e)
    {
        if (!isActiveAndEnabled) return;

        if (SourceGun == null) return;
        if (!TryGetSourceGun(e, out var src) || src != SourceGun) return;

        // Ignore our own extra damage hit.
        if (extraDamageSkipHitEvent && TryGetFlags(e, out var flags) && (flags & DamageFlags.SkipHitEvent) != 0)
            return;

        if (!TryGetHitCollider(e, out var col) || col == null) return;

        _hitCount++;
        if (everyNthHit <= 0) return;
        if ((_hitCount % everyNthHit) != 0) return;

        if (!TryGetEventDamage(e, out float dealtDamage)) return;
        if (dealtDamage <= 0f) return;

        // Read multipliers from the target's hitbox setup.
        float bodyMul = readBodyMultiplierFromHitCollider ? ReadHitboxMultiplier(col) : fallbackBodyMultiplier;
        if (bodyMul <= 0f) bodyMul = fallbackBodyMultiplier;

        float headMul = ResolveHeadMultiplier(col);
        if (headMul < 1f) headMul = fallbackHeadMultiplier;

        // If head multiplier is not larger than current, no need to add damage.
        float ratio = headMul / Mathf.Max(0.0001f, bodyMul);
        if (ratio <= 1f) return;

        float extraDamage = dealtDamage * (ratio - 1f);
        if (maxExtraDamagePerHit > 0f) extraDamage = Mathf.Min(extraDamage, maxExtraDamagePerHit);
        if (extraDamage <= 0f) return;

        Vector3 hitPoint = TryGetHitPoint(e, out var hp) ? hp : col.ClosestPoint(SourceGun.transform.position);

        var info = new DamageInfo
        {
            damage = extraDamage,
            flags = extraDamageSkipHitEvent ? DamageFlags.SkipHitEvent : DamageFlags.None
        };

        DamageResolver.ApplyHit(info, col, hitPoint, src, null, null, showExtraHitUI);
    }

    private float ResolveHeadMultiplier(Collider hitCol)
    {
        if (!headIsMaxMultiplierOnTarget) return fallbackHeadMultiplier;

        var root = FindTargetRoot(hitCol);
        if (root == null) return fallbackHeadMultiplier;

        int key = root.GetInstanceID();
        if (_cachedHeadMulByRoot.TryGetValue(key, out float cached))
            return cached;

        float maxMul = 0f;

        // Scan all colliders under root and read their hitbox multipliers (if any).
        var cols = root.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < cols.Length; i++)
        {
            var c = cols[i];
            if (c == null) continue;

            float m = ReadHitboxMultiplier(c);
            if (m > maxMul) maxMul = m;
        }

        if (maxMul < 1f) maxMul = fallbackHeadMultiplier;

        _cachedHeadMulByRoot[key] = maxMul;
        return maxMul;
    }

    private static Transform FindTargetRoot(Collider hitCol)
    {
        if (hitCol == null) return null;

        // Prefer a root that likely represents the enemy.
        // Try to find common "damageable" markers by interface/component name without hard dependencies.
        Transform t = hitCol.transform;

        // Walk up a bit and pick the highest parent that still looks like part of the same entity.
        Transform last = t;
        for (int i = 0; i < 12 && last.parent != null; i++)
        {
            last = last.parent;
        }

        // Use the top-most transform as a stable root.
        return last;
    }

    private static float ReadHitboxMultiplier(Collider col)
    {
        if (col == null) return 0f;

        // Try common patterns on components attached to this collider or its GameObject:
        // fields: damageMultiplier, multiplier, hitMultiplier, bodyPartMultiplier
        // properties: DamageMultiplier, Multiplier
        var comps = col.GetComponents<Component>();
        for (int i = 0; i < comps.Length; i++)
        {
            var c = comps[i];
            if (c == null) continue;

            var t = c.GetType();

            // Fields
            float v;
            if (TryReadFloatField(c, t, "damageMultiplier", out v)) return v;
            if (TryReadFloatField(c, t, "multiplier", out v)) return v;
            if (TryReadFloatField(c, t, "hitMultiplier", out v)) return v;
            if (TryReadFloatField(c, t, "bodyPartMultiplier", out v)) return v;

            // Properties
            if (TryReadFloatProp(c, t, "DamageMultiplier", out v)) return v;
            if (TryReadFloatProp(c, t, "Multiplier", out v)) return v;
        }

        return 0f;
    }

    private static bool TryReadFloatField(object obj, Type t, string name, out float value)
    {
        value = 0f;
        try
        {
            var f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f == null || f.FieldType != typeof(float)) return false;
            value = (float)f.GetValue(obj);
            return true;
        }
        catch { return false; }
    }

    private static bool TryReadFloatProp(object obj, Type t, string name, out float value)
    {
        value = 0f;
        try
        {
            var p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p == null || p.PropertyType != typeof(float) || !p.CanRead) return false;
            value = (float)p.GetValue(obj);
            return true;
        }
        catch { return false; }
    }

    private static void PrepareReflection()
    {
        if (_refReady) return;
        _refReady = true;

        var t = typeof(CombatEventHub.HitEvent);

        _fSource = t.GetField("source") ?? t.GetField("gun") ?? t.GetField("origin");
        _fFlags = t.GetField("flags") ?? t.GetField("damageFlags");
        _fCollider = t.GetField("hitCollider") ?? t.GetField("collider") ?? t.GetField("targetCollider");
        _fHitPoint = t.GetField("hitPoint") ?? t.GetField("point") ?? t.GetField("worldPoint");

        _fDamageFloat = t.GetField("damage") ?? t.GetField("finalDamage") ?? t.GetField("damageDealt");
        _fDamageInfo = t.GetField("damageInfo") ?? t.GetField("info") ?? t.GetField("dmgInfo");
    }

    private static bool TryGetSourceGun(CombatEventHub.HitEvent e, out CameraGunChannel gun)
    {
        gun = null;
        if (_fSource == null) return false;
        try { gun = _fSource.GetValue(e) as CameraGunChannel; return gun != null; }
        catch { return false; }
    }

    private static bool TryGetFlags(CombatEventHub.HitEvent e, out DamageFlags flags)
    {
        flags = DamageFlags.None;
        if (_fFlags == null) return false;
        try
        {
            object v = _fFlags.GetValue(e);
            if (v is DamageFlags df) { flags = df; return true; }
        }
        catch { }
        return false;
    }

    private static bool TryGetHitCollider(CombatEventHub.HitEvent e, out Collider col)
    {
        col = null;
        if (_fCollider == null) return false;
        try { col = _fCollider.GetValue(e) as Collider; return col != null; }
        catch { return false; }
    }

    private static bool TryGetHitPoint(CombatEventHub.HitEvent e, out Vector3 p)
    {
        p = default;
        if (_fHitPoint == null) return false;
        try
        {
            object v = _fHitPoint.GetValue(e);
            if (v is Vector3 vv) { p = vv; return true; }
        }
        catch { }
        return false;
    }

    private static bool TryGetEventDamage(CombatEventHub.HitEvent e, out float dmg)
    {
        dmg = 0f;

        if (_fDamageFloat != null)
        {
            try
            {
                object v = _fDamageFloat.GetValue(e);
                if (v is float f) { dmg = f; return true; }
            }
            catch { }
        }

        if (_fDamageInfo != null)
        {
            try
            {
                object infoObj = _fDamageInfo.GetValue(e);
                if (infoObj == null) return false;

                var it = infoObj.GetType();
                var f = it.GetField("damage");
                if (f != null && f.FieldType == typeof(float))
                {
                    dmg = (float)f.GetValue(infoObj);
                    return true;
                }
            }
            catch { }
        }

        return false;
    }
}