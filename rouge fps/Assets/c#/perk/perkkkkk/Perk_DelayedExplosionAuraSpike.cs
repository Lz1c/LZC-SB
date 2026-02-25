using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

public sealed class Perk_DelayedExplosionAuraSpike : GunPerkModifierBase
{
    [Header("Gun Damage (GunStatContext)")]
    [Tooltip("Multiply bullet base damage (direct hit damage).")]
    [Min(0f)] public float damageMultiplier = 1.35f;

    [Header("Delayed Explosion")]
    [Min(0.01f)] public float delaySeconds = 0.45f;
    [Min(0.01f)] public float radius = 3.5f;

    [Tooltip("If true, explosion damage is based on the direct hit damage dealt (post-calculation).")]
    public bool explosionDamageFromHit = true;

    [Tooltip("Explosion damage = (hitDealtDamage * explosionDamageMultiplier) when explosionDamageFromHit is true.")]
    [Min(0f)] public float explosionDamageMultiplier = 1.0f;

    [Tooltip("Explosion damage (flat) when explosionDamageFromHit is false.")]
    [Min(0f)] public float explosionDamageFlat = 25f;

    [Tooltip("Layers used to find enemies for AoE damage.")]
    public LayerMask enemyMask = ~0;

    [Tooltip("Explosion damage uses SkipHitEvent to avoid recursive procs.")]
    public bool explosionSkipHitEvent = true;

    [Header("Aura Spike VFX (optional)")]
    [Tooltip("Spawned immediately on hit and destroyed on explosion.")]
    public GameObject auraSpikeVfxPrefab;

    [Tooltip("Spawned at explosion time.")]
    public GameObject explosionVfxPrefab;

    public Transform vfxParent;
    [Min(0f)] public float vfxAutoDestroySeconds = 6f;

    public bool scaleVfxByRadius = true;
    public bool vfxUseDiameter = true;
    [Min(0f)] public float vfxScalePerUnit = 0.5f;

    [Header("Priority")]
    public int priority = 0;
    public override int Priority => priority;

    // Cached reflection for CombatEventHub.HitEvent (keeps this perk compatible if field names differ slightly).
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

    public override void ApplyModifiers(CameraGunChannel source, Dictionary<GunStat, StatStack> stacks)
    {
        if (source == null || stacks == null) return;

        if (stacks.TryGetValue(GunStat.Damage, out var dmg))
        {
            dmg.mul *= Mathf.Max(0f, damageMultiplier);
            stacks[GunStat.Damage] = dmg;
        }
    }

    private void OnHit(CombatEventHub.HitEvent e)
    {
        if (!isActiveAndEnabled) return;

        // Only this gun.
        if (SourceGun == null) return;
        if (!TryGetSourceGun(e, out var src) || src != SourceGun) return;

        // Avoid recursive procs.
        if (explosionSkipHitEvent && TryGetFlags(e, out var flags) && (flags & DamageFlags.SkipHitEvent) != 0)
            return;

        if (!TryGetHitCollider(e, out var hitCol) || hitCol == null) return;
        if (!TryGetHitPoint(e, out var hitPoint)) hitPoint = hitCol.ClosestPoint(SourceGun.transform.position);

        float dealtDamage = 0f;
        if (explosionDamageFromHit)
        {
            if (!TryGetEventDamage(e, out dealtDamage)) dealtDamage = 0f;
        }

        // Create an anchor that follows the hit target (or stays in world if desired).
        var anchorGO = new GameObject("DelayedExplosion_AuraSpike");
        var anchor = anchorGO.AddComponent<DelayedExplosionAnchor>();
        anchor.Init(
            owner: this,
            source: src,
            attachTo: hitCol.transform,
            localPos: hitCol.transform.InverseTransformPoint(hitPoint),
            worldPos: hitPoint,
            delay: Mathf.Max(0.01f, delaySeconds),
            radius: Mathf.Max(0.01f, radius),
            enemyMask: enemyMask,
            skipHitEvent: explosionSkipHitEvent,
            explosionFromHit: explosionDamageFromHit,
            hitDealtDamage: dealtDamage,
            explosionMul: Mathf.Max(0f, explosionDamageMultiplier),
            explosionFlat: Mathf.Max(0f, explosionDamageFlat),
            auraVfxPrefab: auraSpikeVfxPrefab,
            explosionVfxPrefab: explosionVfxPrefab,
            vfxParent: vfxParent,
            vfxAutoDestroySeconds: vfxAutoDestroySeconds,
            scaleVfxByRadius: scaleVfxByRadius,
            vfxUseDiameter: vfxUseDiameter,
            vfxScalePerUnit: vfxScalePerUnit
        );
    }

    // -------------------------
    // Helper component
    // -------------------------
    private sealed class DelayedExplosionAnchor : MonoBehaviour
    {
        private Perk_DelayedExplosionAuraSpike _owner;
        private CameraGunChannel _source;
        private Transform _attachTo;
        private Vector3 _localPos;
        private Vector3 _worldPos;

        private float _delay;
        private float _radius;
        private LayerMask _enemyMask;
        private bool _skipHitEvent;

        private bool _explosionFromHit;
        private float _hitDealtDamage;
        private float _explosionMul;
        private float _explosionFlat;

        private GameObject _auraVfx;
        private GameObject _explosionVfxPrefab;
        private Transform _vfxParent;
        private float _vfxAutoDestroySeconds;

        private bool _scaleVfxByRadius;
        private bool _vfxUseDiameter;
        private float _vfxScalePerUnit;

        public void Init(
            Perk_DelayedExplosionAuraSpike owner,
            CameraGunChannel source,
            Transform attachTo,
            Vector3 localPos,
            Vector3 worldPos,
            float delay,
            float radius,
            LayerMask enemyMask,
            bool skipHitEvent,
            bool explosionFromHit,
            float hitDealtDamage,
            float explosionMul,
            float explosionFlat,
            GameObject auraVfxPrefab,
            GameObject explosionVfxPrefab,
            Transform vfxParent,
            float vfxAutoDestroySeconds,
            bool scaleVfxByRadius,
            bool vfxUseDiameter,
            float vfxScalePerUnit
        )
        {
            _owner = owner;
            _source = source;

            _attachTo = attachTo;
            _localPos = localPos;
            _worldPos = worldPos;

            _delay = delay;
            _radius = radius;
            _enemyMask = enemyMask;
            _skipHitEvent = skipHitEvent;

            _explosionFromHit = explosionFromHit;
            _hitDealtDamage = hitDealtDamage;
            _explosionMul = explosionMul;
            _explosionFlat = explosionFlat;

            _explosionVfxPrefab = explosionVfxPrefab;
            _vfxParent = vfxParent;
            _vfxAutoDestroySeconds = vfxAutoDestroySeconds;

            _scaleVfxByRadius = scaleVfxByRadius;
            _vfxUseDiameter = vfxUseDiameter;
            _vfxScalePerUnit = vfxScalePerUnit;

            // Place anchor.
            if (_attachTo != null)
            {
                transform.SetParent(_attachTo, worldPositionStays: false);
                transform.localPosition = _localPos;
            }
            else
            {
                transform.position = _worldPos;
            }

            // Spawn aura spike vfx now.
            if (auraVfxPrefab != null)
            {
                Transform parent = _vfxParent != null ? _vfxParent : transform;
                _auraVfx = Instantiate(auraVfxPrefab, transform.position, Quaternion.identity, parent);

                if (_scaleVfxByRadius)
                {
                    float units = _vfxUseDiameter ? (_radius * 2f) : _radius;
                    float s = Mathf.Max(0f, units * _vfxScalePerUnit);
                    _auraVfx.transform.localScale = _auraVfx.transform.localScale * s;
                }
            }

            Invoke(nameof(Explode), _delay);
        }

        private void Explode()
        {
            if (_auraVfx != null) Destroy(_auraVfx);

            Vector3 center = (_attachTo != null) ? transform.position : _worldPos;

            // Explosion vfx
            if (_explosionVfxPrefab != null)
            {
                Transform parent = _vfxParent != null ? _vfxParent : null;
                GameObject vfx = parent != null
                    ? Instantiate(_explosionVfxPrefab, center, Quaternion.identity, parent)
                    : Instantiate(_explosionVfxPrefab, center, Quaternion.identity);

                if (_scaleVfxByRadius)
                {
                    float units = _vfxUseDiameter ? (_radius * 2f) : _radius;
                    float s = Mathf.Max(0f, units * _vfxScalePerUnit);
                    vfx.transform.localScale = vfx.transform.localScale * s;
                }

                if (_vfxAutoDestroySeconds > 0f)
                    Destroy(vfx, _vfxAutoDestroySeconds);
            }

            float explosionDamage = _explosionFromHit
                ? Mathf.Max(0f, _hitDealtDamage * _explosionMul)
                : Mathf.Max(0f, _explosionFlat);

            if (explosionDamage > 0f)
            {
                Collider[] cols = Physics.OverlapSphere(center, Mathf.Max(0.01f, _radius), _enemyMask, QueryTriggerInteraction.Collide);
                if (cols != null)
                {
                    DamageFlags flags = _skipHitEvent ? DamageFlags.SkipHitEvent : DamageFlags.None;

                    for (int i = 0; i < cols.Length; i++)
                    {
                        Collider col = cols[i];
                        if (col == null) continue;

                        var info = new DamageInfo
                        {
                            damage = explosionDamage,
                            flags = flags
                        };

                        DamageResolver.ApplyHit(info, col, col.ClosestPoint(center), _source, null, null, true);
                    }
                }
            }

            Destroy(gameObject);
        }
    }

    // -------------------------
    // HitEvent reflection
    // -------------------------
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