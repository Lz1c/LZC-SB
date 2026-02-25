using System.Collections.Generic;
using UnityEngine;

public sealed class Perk_MarkJumpOnKillRandom : MonoBehaviour
{
    [Header("Mark")]
    [Tooltip("Duration for the mark applied by this perk.")]
    [Min(0.01f)] public float markDuration = 6f;

    [Tooltip("If true, when applying mark to an already-marked target, the mark counter is allowed to increase (depends on MarkStatus logic).")]
    public bool addCountOnApplyHit = true;

    [Header("Targeting")]
    [Tooltip("Max enemies scanned to pick a random target (performance cap).")]
    [Min(1)] public int searchLimit = 64;

    [Tooltip("If true, will prefer targets that are NOT currently marked.")]
    public bool preferUnmarkedTarget = true;

    [Tooltip("If true, the killed target's own object will be excluded (always recommended).")]
    public bool excludeKilledTarget = true;

    [Header("Detection")]
    [Tooltip("If true, requires an active MarkStatus component on the killed target. If false, StatusContainer Mark stacks > 0 also counts.")]
    public bool requireActiveMarkStatus = true;

    // Set by PerkManager.InstantiatePerkToGun via reflection (if present).
    [HideInInspector] public int targetGunIndex = -1;

    private CameraGunChannel _gun;

    private void OnEnable()
    {
        _gun = ResolveGunChannel();
        CombatEventHub.OnKill += HandleKill;
    }

    private void OnDisable()
    {
        CombatEventHub.OnKill -= HandleKill;
        _gun = null;
    }

    private void HandleKill(CombatEventHub.KillEvent e)
    {
        if (e.target == null) return;
        if (e.source == null) return;

        if (_gun == null) _gun = ResolveGunChannel();
        if (_gun == null) return;

        if (e.source != _gun) return;

        // Only when the killed target is marked
        if (!IsMarked(e.target))
            return;

        var next = PickRandomEnemy(exclude: excludeKilledTarget ? e.target : null);
        if (next == null) return;

        ApplyMark(next, _gun);
    }

    private bool IsMarked(GameObject target)
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

    private void ApplyMark(GameObject target, CameraGunChannel applierGun)
    {
        if (target == null || applierGun == null) return;

        // Ensure MarkStatus exists
        var mark = target.GetComponent<MarkStatus>();
        if (mark == null) mark = target.AddComponent<MarkStatus>();

        // Use a lightweight config instance (no scene dependency).
        // Roles here don't matter for "mark exists" itself; they matter only for MarkManager behavior (detonation etc).
        // MarkStatus uses config for duration/damage rules, so we provide it.
        var cfg = ScriptableObject.CreateInstance<MarkConfig>();
        cfg.duration = Mathf.Max(0.01f, markDuration);
        cfg.addCountOnApplyHit = addCountOnApplyHit;

        // Keep defaults for detonate fields; your existing MarkManager config can override at runtime if you use it elsewhere.
        mark.ApplyOrRefresh(cfg, applierGun);

        // Bridge into StatusContainer for debug / other perk queries
        var sc = GetStatusContainer(target);
        if (sc != null)
        {
            sc.ApplyStatus(new StatusApplyRequest
            {
                type = StatusType.Mark,
                stacksToAdd = 1,
                duration = Mathf.Max(0.01f, markDuration),
                source = applierGun
            });
        }
    }

    private GameObject PickRandomEnemy(GameObject exclude)
    {
        var enemies = FindObjectsByType<MonsterHealth>(FindObjectsSortMode.None);
        if (enemies == null || enemies.Length == 0) return null;

        int limit = Mathf.Max(1, searchLimit);

        // Two-pass: prefer unmarked first if enabled
        List<GameObject> unmarked = null;
        List<GameObject> marked = null;

        for (int i = 0; i < enemies.Length; i++)
        {
            if (limit <= 0) break;

            var mh = enemies[i];
            if (mh == null || mh.IsDead) continue;

            var go = mh.gameObject;
            if (go == null) continue;
            if (exclude != null && go == exclude) continue;

            bool isMarked = IsMarked(go);

            if (preferUnmarkedTarget && !isMarked)
            {
                unmarked ??= new List<GameObject>(16);
                unmarked.Add(go);
                limit--;
                continue;
            }

            marked ??= new List<GameObject>(16);
            marked.Add(go);
            limit--;
        }

        if (preferUnmarkedTarget && unmarked != null && unmarked.Count > 0)
            return unmarked[Random.Range(0, unmarked.Count)];

        if (marked != null && marked.Count > 0)
            return marked[Random.Range(0, marked.Count)];

        return null;
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

        // 3) PerkManager index (if injected)
        var pm = FindFirstObjectByType<PerkManager>();
        if (pm != null && targetGunIndex >= 0)
        {
            pm.RefreshAll(force: true);
            var gunRefs = pm.GetGun(targetGunIndex);
            if (gunRefs != null && gunRefs.cameraGunChannel != null)
                return gunRefs.cameraGunChannel;
        }

        return null;
    }
}