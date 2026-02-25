using System;
using System.Reflection;
using UnityEngine;

public sealed class Perk_HeadshotPercentCurrentHPBonus : MonoBehaviour
{
    [Header("Extra Damage (Headshot Only)")]
    [Tooltip("Extra damage = currentHP * percentOfCurrentHP (e.g., 0.05 = 5%).")]
    [Range(0f, 1f)] public float percentOfCurrentHP = 0.05f;

    [Tooltip("Clamp the extra damage to this maximum value (prevents high-HP enemies from taking too much).")]
    [Min(0f)] public float maxExtraDamage = 120f;

    [Tooltip("Optional minimum extra damage (0 = no minimum).")]
    [Min(0f)] public float minExtraDamage = 0f;

    [Header("Behavior")]
    [Tooltip("If true, ignore hits flagged SkipHitEvent.")]
    public bool ignoreSkipHitEventHits = true;

    [Tooltip("If true, do not re-apply status payloads on the extra damage hit.")]
    public bool doNotReapplyStatuses = true;

    [Tooltip("If true, hide hit UI for the extra damage hit.")]
    public bool hideExtraHitUI = true;

    // Set by PerkManager.InstantiatePerkToGun via reflection (if present).
    [HideInInspector] public int targetGunIndex = -1;

    private CameraGunChannel _gun;
    private PerkManager _pm;

    private void Awake()
    {
        _pm = FindFirstObjectByType<PerkManager>();
    }

    private void OnEnable()
    {
        _gun = ResolveGunChannel();
        CombatEventHub.OnHit += HandleHit;
    }

    private void OnDisable()
    {
        CombatEventHub.OnHit -= HandleHit;
        _gun = null;
    }

    private void HandleHit(CombatEventHub.HitEvent e)
    {
        if (e.source == null || e.target == null || e.hitCollider == null) return;

        if (_gun == null) _gun = ResolveGunChannel();
        if (_gun == null) return;

        if (e.source != _gun) return;

        if (ignoreSkipHitEventHits && (e.flags & DamageFlags.SkipHitEvent) != 0)
            return;

        if (!e.isHeadshot) return;

        var mh = e.target.GetComponent<MonsterHealth>();
        if (mh == null) mh = e.target.GetComponentInParent<MonsterHealth>();
        if (mh == null || mh.IsDead) return;

        float currentHp = GetCurrentHpBestEffort(mh);
        if (currentHp <= 0f) return;

        float extra = currentHp * Mathf.Clamp01(percentOfCurrentHP);
        if (maxExtraDamage > 0f) extra = Mathf.Min(extra, maxExtraDamage);
        if (minExtraDamage > 0f) extra = Mathf.Max(extra, minExtraDamage);

        if (extra <= 0f) return;

        var info = new DamageInfo
        {
            source = _gun,
            damage = extra,
            isHeadshot = true,
            hitPoint = e.hitPoint,
            hitCollider = e.hitCollider,
            flags = DamageFlags.SkipHitEvent
        };

        DamageResolver.ApplyHit(
            info,
            e.hitCollider,
            e.hitPoint,
            _gun,
            armorPayload: null,
            statusPayload: doNotReapplyStatuses ? null : e.statusPayload,
            showHitUI: !hideExtraHitUI
        );
    }

    // Reads current HP from MonsterHealth with reflection fallback so it works even if field/property names differ.
    private static float GetCurrentHpBestEffort(MonsterHealth mh)
    {
        if (mh == null) return 0f;

        // Common patterns: CurrentHealth, currentHealth, health, HP
        const BindingFlags BF = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        // Properties first
        string[] propNames = { "CurrentHealth", "currentHealth", "Health", "health", "HP", "hp" };
        for (int i = 0; i < propNames.Length; i++)
        {
            var p = mh.GetType().GetProperty(propNames[i], BF);
            if (p != null && p.PropertyType == typeof(float))
            {
                try { return Mathf.Max(0f, (float)p.GetValue(mh)); }
                catch { }
            }
        }

        // Fields next
        string[] fieldNames = { "currentHealth", "CurrentHealth", "health", "Health", "hp", "HP" };
        for (int i = 0; i < fieldNames.Length; i++)
        {
            var f = mh.GetType().GetField(fieldNames[i], BF);
            if (f != null && f.FieldType == typeof(float))
            {
                try { return Mathf.Max(0f, (float)f.GetValue(mh)); }
                catch { }
            }
        }

        // If we cannot read it, return 0 to avoid breaking combat.
        return 0f;
    }

    private CameraGunChannel ResolveGunChannel()
    {
        // 1) parent chain
        var inParent = GetComponentInParent<CameraGunChannel>();
        if (inParent != null) return inParent;

        // 2) root children
        Transform root = transform;
        while (root.parent != null) root = root.parent;

        var inChildren = root.GetComponentInChildren<CameraGunChannel>(true);
        if (inChildren != null) return inChildren;

        // 3) via PerkManager gun index
        _pm ??= FindFirstObjectByType<PerkManager>();
        if (_pm != null && targetGunIndex >= 0)
        {
            _pm.RefreshAll(force: true);
            var gunRefs = _pm.GetGun(targetGunIndex);
            if (gunRefs != null && gunRefs.cameraGunChannel != null)
                return gunRefs.cameraGunChannel;
        }

        return null;
    }
}