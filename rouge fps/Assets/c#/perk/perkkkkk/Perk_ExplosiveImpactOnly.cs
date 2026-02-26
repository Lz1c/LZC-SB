using System.Collections.Generic;
using UnityEngine;

public sealed class Perk_ExplosiveImpactOnly : MonoBehaviour
{
    [Header("Explosion")]
    [Min(0.01f)] public float radius = 3.5f;
    [Min(0f)] public float explosionDamage = 25f;

    [Tooltip("Layers used to find enemies for AoE damage. Avoid ~0 (Everything) in real scenes.")]
    public LayerMask enemyMask = ~0;

    [Tooltip("If true, explosion damage will set DamageFlags.SkipHitEvent to avoid recursive perk procs.")]
    public bool explosionSkipHitEvent = true;

    [Header("VFX")]
    public GameObject explosionVfxPrefab;
    public Transform vfxParent;
    [Min(0f)] public float vfxAutoDestroySeconds = 6f;
    public bool scaleVfxWithRadius = true;

    private CameraGunChannel _sourceGun;
    private bool _reentryGuard;

    private readonly HashSet<int> _dedupe = new HashSet<int>(128);
    private readonly Collider[] _overlaps = new Collider[128];

    private void Reset()
    {
        int enemy = LayerMask.NameToLayer("Enemy");
        if (enemy >= 0) enemyMask = 1 << enemy;
    }

    private void OnEnable()
    {
        _sourceGun = GetComponentInParent<CameraGunChannel>();
        CombatEventHub.OnHit += HandleHit;
    }

    private void OnDisable()
    {
        CombatEventHub.OnHit -= HandleHit;
        _sourceGun = null;
        _reentryGuard = false;
        _dedupe.Clear();
    }

    private void HandleHit(CombatEventHub.HitEvent e)
    {
        if (_reentryGuard) return;
        if (_sourceGun == null) return;

        if (e.source != _sourceGun) return;
        if ((e.flags & DamageFlags.SkipHitEvent) != 0) return;

        Collider hitCol = e.hitCollider;
        if (hitCol == null) return;

        Vector3 center = e.hitPoint;

        // VFX
        if (explosionVfxPrefab != null)
        {
            Transform parent = vfxParent != null ? vfxParent : null;
            GameObject vfx = Instantiate(explosionVfxPrefab, center, Quaternion.identity, parent);
            if (scaleVfxWithRadius) vfx.transform.localScale *= Mathf.Max(0.001f, radius);
            if (vfxAutoDestroySeconds > 0f) Destroy(vfx, vfxAutoDestroySeconds);
        }

        _dedupe.Clear();

        int count = Physics.OverlapSphereNonAlloc(center, radius, _overlaps, enemyMask, QueryTriggerInteraction.Ignore);
        if (count <= 0) return;

        _reentryGuard = true;
        try
        {
            for (int i = 0; i < count; i++)
            {
                Collider c = _overlaps[i];
                if (c == null) continue;

                int targetId;
                var mh = c.GetComponentInParent<MonsterHealth>();
                if (mh != null)
                {
                    targetId = mh.gameObject.GetInstanceID();
                }
                else
                {
                    var dmg = c.GetComponentInParent<IDamageableEx>();
                    if (dmg == null) continue;

                    var comp = dmg as Component;
                    targetId = comp != null
                        ? comp.gameObject.GetInstanceID()
                        : c.transform.root.gameObject.GetInstanceID();
                }

                if (!_dedupe.Add(targetId)) continue;

                DamageInfo info = default;
                info.source = _sourceGun;
                info.damage = explosionDamage;
                info.hitPoint = center;
                info.hitCollider = c;

                if (explosionSkipHitEvent) info.flags |= DamageFlags.SkipHitEvent;

                DamageResolver.ApplyHit(
                    info,
                    c,
                    center,
                    _sourceGun,
                    armorPayload: null,
                    statusPayload: null,
                    showHitUI: false
                );
            }
        }
        finally
        {
            _reentryGuard = false;
        }
    }
}