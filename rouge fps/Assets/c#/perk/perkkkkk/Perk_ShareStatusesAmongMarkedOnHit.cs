using UnityEngine;

public sealed class Perk_ShareHitStatusesAmongMarked : MonoBehaviour
{
    [Header("Trigger")]
    [Tooltip("Only trigger when the hit target has an active MarkStatus component.")]
    public bool requireActiveMarkStatus = true;

    [Header("Share")]
    [Tooltip("If false, will not share StatusType.Mark even if present in payload (recommended).")]
    public bool includeMarkInShare = false;

    [Tooltip("Max marked targets to apply per hit (performance cap).")]
    [Min(1)] public int maxTargetsToSync = 64;

    [Tooltip("If true, ignore hits whose DamageInfo has SkipHitEvent flag.")]
    public bool ignoreSkipHitEventHits = true;

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
        if (_gun == null)
        {
            Debug.LogWarning("[Perk_ShareHitStatusesAmongMarked] CameraGunChannel not resolved. Perk will stay enabled and retry on hits.");
        }

        CombatEventHub.OnHit += HandleHit;
    }

    private void OnDisable()
    {
        CombatEventHub.OnHit -= HandleHit;
        _gun = null;
    }

    private void HandleHit(CombatEventHub.HitEvent e)
    {
        if (e.source == null || e.target == null) return;

        if (_gun == null) _gun = ResolveGunChannel();
        if (_gun == null) return;

        if (e.source != _gun) return;

        if (ignoreSkipHitEventHits && (e.flags & DamageFlags.SkipHitEvent) != 0)
            return;

        // Must have status payload on this hit
        if (e.statusPayload == null || e.statusPayload.entries == null || e.statusPayload.entries.Length == 0)
            return;

        // Only when hitting a marked target
        if (!IsTargetMarked(e.target))
            return;

        // Apply payload entries to all other marked targets
        var marks = FindObjectsByType<MarkStatus>(FindObjectsSortMode.None);
        if (marks == null || marks.Length == 0) return;

        int appliedTargets = 0;

        for (int i = 0; i < marks.Length; i++)
        {
            if (appliedTargets >= maxTargetsToSync) break;

            var m = marks[i];
            if (m == null || !m.IsActive) continue;

            GameObject t = m.gameObject;
            if (t == null || t == e.target) continue;

            var dst = GetStatusContainer(t);
            if (dst == null) continue;

            ApplyPayload(dst, e.statusPayload);
            appliedTargets++;
        }
    }

    private void ApplyPayload(StatusContainer dst, BulletStatusPayload payload)
    {
        var entries = payload.entries;
        for (int i = 0; i < entries.Length; i++)
        {
            var se = entries[i];

            if (!includeMarkInShare && se.type == StatusType.Mark)
                continue;

            dst.ApplyStatus(new StatusApplyRequest
            {
                type = se.type,
                stacksToAdd = se.stacksToAdd,
                duration = se.duration,
                source = _gun,

                tickInterval = se.tickInterval,
                burnDamagePerTickPerStack = se.burnDamagePerTickPerStack,

                slowPerStack = se.slowPerStack,
                weakenPerStack = se.weakenPerStack,

                shockChainDamagePerStack = se.shockChainDamagePerStack,
                shockChainRadius = se.shockChainRadius,
                shockMaxChains = se.shockMaxChains
            });
        }
    }

    private bool IsTargetMarked(GameObject target)
    {
        if (target == null) return false;

        if (requireActiveMarkStatus)
        {
            var ms = target.GetComponent<MarkStatus>();
            if (ms == null) ms = target.GetComponentInParent<MarkStatus>();
            return ms != null && ms.IsActive;
        }

        var sc = GetStatusContainer(target);
        return sc != null && sc.GetStacks(StatusType.Mark) > 0;
    }

    private static StatusContainer GetStatusContainer(GameObject go)
    {
        if (go == null) return null;
        var sc = go.GetComponent<StatusContainer>();
        if (sc == null) sc = go.GetComponentInParent<StatusContainer>();
        return sc;
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