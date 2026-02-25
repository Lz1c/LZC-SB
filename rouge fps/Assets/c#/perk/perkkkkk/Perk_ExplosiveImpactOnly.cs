using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public sealed class Perk_ExplosiveImpactOnly : GunPerkModifierBase
{
    [Header("Explosion")]
    [Min(0.01f)] public float radius = 3.5f;
    [Min(0f)] public float explosionDamage = 25f;
    public LayerMask enemyMask = ~0;
    public bool explosionSkipHitEvent = true;

    [Header("VFX")]
    public GameObject explosionVfxPrefab;
    public Transform vfxParent;
    [Min(0f)] public float vfxAutoDestroySeconds = 6f;
    public bool scaleVfxByRadius = true;
    public bool vfxUseDiameter = true;
    [Min(0f)] public float vfxScalePerUnit = 0.5f;

    [Header("Gun Penalties (GunStatContext)")]
    [Range(0.05f, 1f)] public float fireRateMultiplier = 0.6f;
    [Range(0.05f, 1f)] public float bulletSpeedMultiplier = 0.7f;

    [Header("Direct Hit")]
    public bool cancelDirectDamage = true;

    [Header("Priority")]
    public int priority = 0;
    public override int Priority => priority;

    [Header("Bind")]
    [Tooltip("How many frames to wait while trying to find SourceGun after enabling.")]
    [Min(1)] public int bindRetryFrames = 10;

    private bool _subscribed;
    private Coroutine _bindRoutine;

    private void OnEnable()
    {
        base.OnEnable(); // tries to set SourceGun + register to GunStatContext

        // If SourceGun is not ready yet (e.g. enabled before being parented), retry for a few frames.
        StartBindRoutine();
    }

    private void OnDisable()
    {
        StopBindRoutine();
        Unsubscribe();
        base.OnDisable();
    }

    private void OnDestroy()
    {
        StopBindRoutine();
        Unsubscribe();
    }

    private void OnTransformParentChanged()
    {
        // If parent changes (perk moved between guns), rebind.
        if (isActiveAndEnabled)
        {
            // Re-run base.OnEnable-like discovery safely:
            // (GunPerkModifierBase will update SourceGun on next enable; but here we just retry subscription gate.)
            StartBindRoutine();
        }
    }

    private void StartBindRoutine()
    {
        StopBindRoutine();
        _bindRoutine = StartCoroutine(CoBind());
    }

    private void StopBindRoutine()
    {
        if (_bindRoutine != null)
        {
            StopCoroutine(_bindRoutine);
            _bindRoutine = null;
        }
    }

    private IEnumerator CoBind()
    {
        // Try immediately + a few frames (covers Instantiate->SetParent timing).
        for (int i = 0; i < Mathf.Max(1, bindRetryFrames); i++)
        {
            if (SourceGun != null)
            {
                Subscribe();
                yield break;
            }

            // SourceGun might become available next frame after parenting.
            yield return null;

            // Re-attempt discovery in case the base class cached null during first enable.
            // (We can just search again here without touching GunStatContext registration.)
            if (SourceGun == null)
                TryRefreshSourceGun();
        }

        // If still no gun, do NOT become global; just stay inactive until properly parented.
        Unsubscribe();
    }

    private void TryRefreshSourceGun()
    {
        // Minimal refresh: look up gun in parents.
        // We cannot set GunPerkModifierBase.SourceGun directly (private setter),
        // so we rely on the fact that in correct usage this perk is enabled under a gun.
        // In practice, if you're seeing this fail, it means the perk isn't parented under a gun yet.
        // (No-op here; kept for future extension.)
    }

    private void Subscribe()
    {
        if (_subscribed) return;
        CombatEventHub.OnHit += OnHit;
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_subscribed) return;
        CombatEventHub.OnHit -= OnHit;
        _subscribed = false;
    }

    public override void ApplyModifiers(CameraGunChannel source, Dictionary<GunStat, StatStack> stacks)
    {
        if (source == null || stacks == null) return;

        if (stacks.TryGetValue(GunStat.FireRate, out var fr))
        {
            fr.mul *= Mathf.Max(0.0001f, fireRateMultiplier);
            stacks[GunStat.FireRate] = fr;
        }

        if (stacks.TryGetValue(GunStat.BulletSpeed, out var bs))
        {
            bs.mul *= Mathf.Max(0.0001f, bulletSpeedMultiplier);
            stacks[GunStat.BulletSpeed] = bs;
        }

        if (cancelDirectDamage && stacks.TryGetValue(GunStat.Damage, out var dmg))
        {
            dmg.mul *= 0f;
            stacks[GunStat.Damage] = dmg;
        }
    }

    private void OnHit(CombatEventHub.HitEvent e)
    {
        if (!isActiveAndEnabled) return;
        if (e.source == null) return;

        // Critical: never global.
        if (SourceGun == null) return;
        if (e.source != SourceGun) return;

        if (explosionSkipHitEvent && (e.flags & DamageFlags.SkipHitEvent) != 0) return;

        Vector3 center = e.hitPoint;

        if (explosionVfxPrefab != null)
        {
            Transform parent = vfxParent != null ? vfxParent : null;
            GameObject vfx = parent != null
                ? Instantiate(explosionVfxPrefab, center, Quaternion.identity, parent)
                : Instantiate(explosionVfxPrefab, center, Quaternion.identity);

            if (scaleVfxByRadius)
            {
                float units = vfxUseDiameter ? (radius * 2f) : radius;
                float s = Mathf.Max(0f, units * vfxScalePerUnit);
                vfx.transform.localScale = vfx.transform.localScale * s;
            }

            if (vfxAutoDestroySeconds > 0f)
                Destroy(vfx, vfxAutoDestroySeconds);
        }

        Collider[] cols = Physics.OverlapSphere(center, Mathf.Max(0.01f, radius), enemyMask, QueryTriggerInteraction.Collide);
        if (cols == null || cols.Length == 0) return;

        DamageFlags flags = explosionSkipHitEvent ? DamageFlags.SkipHitEvent : DamageFlags.None;

        for (int i = 0; i < cols.Length; i++)
        {
            Collider col = cols[i];
            if (col == null) continue;

            var info = new DamageInfo
            {
                damage = Mathf.Max(0f, explosionDamage),
                flags = flags
            };

            DamageResolver.ApplyHit(info, col, col.ClosestPoint(center), e.source, null, null, true);
        }
    }
}