using UnityEngine;

public sealed class Perk_UnarmoredBodyBonusDamage : MonoBehaviour
{
    [Header("Bonus Damage")]
    [Tooltip("Extra damage added when hitting Body (non-head) and the target is not protected by armor.")]
    [Min(0f)] public float bonusDamageFlat = 10f;

    [Tooltip("If > 0, extra damage will be baseHitDamage * bonusDamageMultiplier (in addition to flat).")]
    [Min(0f)] public float bonusDamageMultiplier = 0f;

    [Header("Conditions")]
    [Tooltip("If true, requires Hitbox component and Hitbox.Part must be Body. If false, uses isHeadshot=false as Body.")]
    public bool requireHitboxBody = true;

    [Tooltip("If true, target must have EnemyArmor AND armor <= 0 to count as unarmored. If false, missing EnemyArmor also counts as unarmored.")]
    public bool requireArmorComponent = false;

    [Tooltip("If true, only triggers when EnemyArmor.armor <= 0. If target has armor > 0, no bonus.")]
    public bool requireArmorBroken = true;

    [Header("Safety")]
    [Tooltip("If true, ignore hits flagged SkipHitEvent (prevents recursion / perk chains).")]
    public bool ignoreSkipHitEventHits = true;

    [Tooltip("If true, extra damage will not re-apply status payloads.")]
    public bool doNotReapplyStatuses = true;

    [Tooltip("If true, extra damage will not show hit UI.")]
    public bool hideExtraHitUI = true;

    // Set by PerkManager.InstantiatePerkToGun via reflection (if present)
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

        if (!IsBodyHit(e))
            return;

        if (!IsUnarmoredTarget(e.target))
            return;

        float extra = Mathf.Max(0f, bonusDamageFlat) + Mathf.Max(0f, e.damage) * Mathf.Max(0f, bonusDamageMultiplier);
        if (extra <= 0f) return;

        // Apply extra damage as a separate hit to avoid changing the core damage pipeline.
        var info = new DamageInfo
        {
            source = _gun,
            damage = extra,
            isHeadshot = false,
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

    private bool IsBodyHit(CombatEventHub.HitEvent e)
    {
        if (!requireHitboxBody)
            return !e.isHeadshot;

        var hb = e.hitCollider.GetComponent<Hitbox>();
        if (hb == null) hb = e.hitCollider.GetComponentInParent<Hitbox>();
        if (hb == null) return false;

        return hb.part == Hitbox.Part.Body;
    }

    private bool IsUnarmoredTarget(GameObject target)
    {
        var armor = target.GetComponent<EnemyArmor>();
        if (armor == null) armor = target.GetComponentInParent<EnemyArmor>();

        if (armor == null)
            return !requireArmorComponent; // missing armor counts as unarmored if allowed

        if (!requireArmorBroken)
            return true;

        return armor.armor <= 0f;
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