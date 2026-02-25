using System.Collections.Generic;
using UnityEngine;

public class MarkManager : MonoBehaviour
{
    [Header("Config")]
    public MarkConfig config;

    [Header("Activation")]
    [Tooltip("If false, MarkManager will ignore all events (no apply, no detonate). Enable it from a perk.")]
    public bool systemEnabled = false;

    [Header("Enemy Mask (for optional jump)")]
    public LayerMask enemyMask = ~0;

    private void OnEnable()
    {
        CombatEventHub.OnHit += HandleHit;
        CombatEventHub.OnKill += HandleKill;
    }

    private void OnDisable()
    {
        CombatEventHub.OnHit -= HandleHit;
        CombatEventHub.OnKill -= HandleKill;
    }

    private void HandleHit(CombatEventHub.HitEvent e)
    {
        if (!systemEnabled) return;
        if (config == null) return;
        if (e.source == null || e.target == null) return;

        // Applier gun -> apply/refresh mark
        if (e.source.role == config.applierRole)
        {
            ApplyOrRefreshMark(e.target, e.source);
            return;
        }

        // Detonator gun -> detonate mark
        if (e.source.role == config.detonatorRole)
        {
            var mark = GetMark(e.target);
            if (mark == null || !mark.IsActive) return;

            if (config.shareStatusesOnDetonateHit)
                ShareStatusesFromMarkedTarget(e.target);

            float detonateDmg = mark.ComputeDetonateDamage();
            if (detonateDmg > 0f)
            {
                var mh = e.target.GetComponent<MonsterHealth>();
                if (mh == null) mh = e.target.GetComponentInParent<MonsterHealth>();

                if (mh != null && !mh.IsDead)
                {
                    mh.TakeDamage(new DamageInfo
                    {
                        source = e.source,
                        damage = detonateDmg,
                        isHeadshot = false,
                        hitPoint = e.hitPoint,
                        hitCollider = e.hitCollider,
                        flags = config.detonateSkipHitEvent ? DamageFlags.SkipHitEvent : DamageFlags.None
                    });
                }
            }

            if (config.consumeOnDetonate)
            {
                // Consume the runtime MarkStatus component
                mark.Consume();

                // Also remove the StatusContainer Mark entry immediately (for debug & perk queries)
                var sc = e.target.GetComponent<StatusContainer>();
                if (sc == null) sc = e.target.GetComponentInParent<StatusContainer>();
                if (sc != null) sc.RemoveStatus(StatusType.Mark);
            }

            return;
        }
    }

    private void HandleKill(CombatEventHub.KillEvent e)
    {
        if (!systemEnabled) return;
        if (config == null) return;
        if (!config.jumpMarkOnKill) return;
        if (e.target == null) return;

        var mark = GetMark(e.target);
        if (mark == null || !mark.IsActive) return;

        GameObject next = FindRandomEnemy(exclude: e.target, origin: e.target.transform.position);
        if (next == null) return;

        if (e.source != null)
            ApplyOrRefreshMark(next, e.source);
    }

    private void ApplyOrRefreshMark(GameObject target, CameraGunChannel applierGun)
    {
        if (!systemEnabled) return;
        if (target == null || applierGun == null || config == null) return;

        var mark = target.GetComponent<MarkStatus>();
        if (mark == null) mark = target.AddComponent<MarkStatus>();

        mark.ApplyOrRefresh(config, applierGun);

        // Bridge mark into StatusContainer for perk queries
        var sc = target.GetComponent<StatusContainer>();
        if (sc == null) sc = target.GetComponentInParent<StatusContainer>();
        if (sc != null)
        {
            sc.ApplyStatus(new StatusApplyRequest
            {
                type = StatusType.Mark,
                stacksToAdd = 1,
                duration = Mathf.Max(0.01f, config.duration),
                source = applierGun
            });
        }
    }

    private MarkStatus GetMark(GameObject target)
    {
        if (target == null) return null;
        var m = target.GetComponent<MarkStatus>();
        if (m == null) m = target.GetComponentInParent<MarkStatus>();
        return m;
    }

    private void ShareStatusesFromMarkedTarget(GameObject markedTarget)
    {
        if (!systemEnabled) return;
        if (markedTarget == null) return;

        var src = markedTarget.GetComponent<StatusContainer>();
        if (src == null) src = markedTarget.GetComponentInParent<StatusContainer>();
        if (src == null) return;

        List<StatusSnapshot> snaps = StatusSnapshotPool.Get();
        snaps.Clear();
        src.ExportSnapshots(snaps);

        if (snaps.Count == 0)
        {
            StatusSnapshotPool.Release(snaps);
            return;
        }

        var allMarks = FindObjectsByType<MarkStatus>(FindObjectsSortMode.None);
        for (int i = 0; i < allMarks.Length; i++)
        {
            var m = allMarks[i];
            if (m == null || !m.IsActive) continue;

            GameObject t = m.gameObject;
            if (t == markedTarget) continue;

            var dst = t.GetComponent<StatusContainer>();
            if (dst == null) dst = t.GetComponentInParent<StatusContainer>();
            if (dst == null) continue;

            dst.ApplySnapshots(snaps);
        }

        StatusSnapshotPool.Release(snaps);
    }

    private GameObject FindRandomEnemy(GameObject exclude, Vector3 origin)
    {
        int limit = Mathf.Max(1, config != null ? config.jumpSearchLimit : 1);

        // Prefer physics overlap by mask (cheaper & respects enemyMask)
        var hits = Physics.OverlapSphere(origin, 50f, enemyMask, QueryTriggerInteraction.Ignore);
        if (hits == null || hits.Length == 0) return null;

        List<GameObject> candidates = new List<GameObject>(Mathf.Min(16, Mathf.Min(limit, hits.Length)));

        for (int i = 0; i < hits.Length && candidates.Count < limit; i++)
        {
            var col = hits[i];
            if (col == null) continue;

            var mh = col.GetComponentInParent<MonsterHealth>();
            if (mh == null || mh.IsDead) continue;

            var go = mh.gameObject;
            if (exclude != null && go == exclude) continue;

            if (!candidates.Contains(go))
                candidates.Add(go);
        }

        if (candidates.Count == 0) return null;
        return candidates[Random.Range(0, candidates.Count)];
    }
}