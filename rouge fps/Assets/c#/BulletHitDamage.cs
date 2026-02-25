using System.Collections.Generic;
using UnityEngine;

public class BulletHitDamage : MonoBehaviour
{
    [Header("Damage (set when spawned)")]
    public float baseDamage = 10f;

    [Tooltip("The gun channel that fired this bullet.")]
    public CameraGunChannel source;

    [Header("Destroy")]
    public bool destroyOnHit = true;

    private bool _didHit;

    // Penetration (kept as-is)
    private bool _penetrationEnabled;
    private float _secondHitMultiplier = 1f;
    private int _enemyHitCount = 0;
    private MonsterHealth _firstEnemyHit;

    // Damage falloff (projectile bullets)
    private Vector3 _spawnPos;
    private bool _spawnPosSet;

    private float _falloffStartMeters;
    private float _maxRangeMeters;

    private GunStatContext _ctx;

    private void Awake()
    {
        TryBindContext();

        CacheRuntimeFalloffValues();
        CacheSpawnPos();

        if (Perk_PenetrateOneTarget.TryGetConfig(source, out var cfg))
        {
            _penetrationEnabled = true;
            _secondHitMultiplier = Mathf.Clamp01(cfg.secondHitDamageMultiplier);
        }
        else
        {
            _penetrationEnabled = false;
            _secondHitMultiplier = 1f;
        }
    }

    public void Init(float damage, CameraGunChannel src)
    {
        baseDamage = damage;
        source = src;

        TryBindContext();
        CacheRuntimeFalloffValues();
        CacheSpawnPos();

        if (Perk_PenetrateOneTarget.TryGetConfig(src, out var cfg))
        {
            _penetrationEnabled = true;
            _secondHitMultiplier = Mathf.Clamp01(cfg.secondHitDamageMultiplier);
        }
        else
        {
            _penetrationEnabled = false;
            _secondHitMultiplier = 1f;
        }

        _enemyHitCount = 0;
        _firstEnemyHit = null;
        _didHit = false;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other == null) return;
        if (_didHit) return;

        Vector3 hitPoint = other.ClosestPoint(transform.position);
        MonsterHealth mh = other.GetComponentInParent<MonsterHealth>();
        bool isEnemy = mh != null;

        if (_penetrationEnabled && !isEnemy)
        {
            _didHit = true;
            Destroy(gameObject);
            return;
        }

        if (_penetrationEnabled && isEnemy)
        {
            if (_enemyHitCount >= 1 && mh == _firstEnemyHit)
                return;
        }

        var armorPayload = GetComponentInChildren<BulletArmorPayload>(true);
        var statusPayload = GetComponentInChildren<BulletStatusPayload>(true);

        if (!_spawnPosSet) CacheSpawnPos();

        // Re-read once on hit so perks that changed stats are reflected.
        CacheRuntimeFalloffValues();

        float damageToApply = baseDamage * ComputeLinearFalloff(GetTraveledMeters());

        if (_penetrationEnabled && isEnemy && _enemyHitCount == 1 && mh != _firstEnemyHit)
            damageToApply *= _secondHitMultiplier;

        if (isEnemy && Perk_BonusDamageByEnemyStatuses.TryGetConfig(source, out var cfg) && cfg.bonusDamagePerStatus > 0f)
        {
            var sc = mh.GetComponent<StatusContainer>();
            if (sc == null) sc = mh.GetComponentInParent<StatusContainer>();

            if (sc != null)
            {
                int count = 0;

                CountStatus(sc, StatusType.Burn, cfg.countStacks, ref count);
                CountStatus(sc, StatusType.Poison, cfg.countStacks, ref count);
                CountStatus(sc, StatusType.Slow, cfg.countStacks, ref count);
                CountStatus(sc, StatusType.Shock, cfg.countStacks, ref count);
                if (cfg.includeMark) CountStatus(sc, StatusType.Mark, cfg.countStacks, ref count);

                if (count > 0)
                    damageToApply += cfg.bonusDamagePerStatus * count;
            }
        }

        var info = new DamageInfo
        {
            source = source,
            damage = damageToApply,
            isHeadshot = false,
            hitPoint = hitPoint,
            hitCollider = other,
            flags = DamageFlags.None
        };

        bool applied = DamageResolver.ApplyHit(
            baseInfo: info,
            hitCol: other,
            hitPoint: hitPoint,
            source: source,
            armorPayload: armorPayload,
            statusPayload: statusPayload,
            showHitUI: true
        );

        if (!applied) return;

        if (!_penetrationEnabled)
        {
            _didHit = true;
            if (destroyOnHit) Destroy(gameObject);
            return;
        }

        if (isEnemy)
        {
            if (_enemyHitCount == 0)
            {
                _enemyHitCount = 1;
                _firstEnemyHit = mh;

                IgnoreAllEnemyColliders(mh);
                return;
            }

            _enemyHitCount = 2;
            _didHit = true;
            Destroy(gameObject);
            return;
        }

        _didHit = true;
        Destroy(gameObject);
    }

    private void TryBindContext()
    {
        if (source == null) return;
        _ctx = source.GetComponent<GunStatContext>();
        if (_ctx == null) _ctx = source.GetComponentInParent<GunStatContext>();
    }

    private void CacheSpawnPos()
    {
        _spawnPos = transform.position;
        _spawnPosSet = true;
    }

    private void CacheRuntimeFalloffValues()
    {
        if (source == null)
        {
            _falloffStartMeters = 0f;
            _maxRangeMeters = 0f;
            return;
        }

        if (_ctx != null)
        {
            _maxRangeMeters = Mathf.Max(0f, _ctx.GetMaxRange());
            _falloffStartMeters = Mathf.Max(0f, _ctx.GetProjectileFalloffStart());
            return;
        }

        _maxRangeMeters = Mathf.Max(0f, source.maxRange);
        _falloffStartMeters = Mathf.Max(0f, source.projectileFalloffStartMeters);
    }

    private float GetTraveledMeters()
    {
        if (!_spawnPosSet) return 0f;
        return Vector3.Distance(_spawnPos, transform.position);
    }

    private float ComputeLinearFalloff(float traveled)
    {
        if (_maxRangeMeters <= 0.0001f) return 1f;

        float start = Mathf.Max(0f, _falloffStartMeters);
        float max = Mathf.Max(start + 0.0001f, _maxRangeMeters);

        if (traveled <= start) return 1f;
        if (traveled >= max) return 0f;

        float t = (traveled - start) / (max - start);
        return 1f - Mathf.Clamp01(t);
    }

    private void IgnoreAllEnemyColliders(MonsterHealth mh)
    {
        if (mh == null) return;

        var bulletCols = GetComponentsInChildren<Collider>(true);
        if (bulletCols == null || bulletCols.Length == 0) return;

        var enemyCols = mh.GetComponentsInChildren<Collider>(true);
        if (enemyCols == null || enemyCols.Length == 0) return;

        for (int i = 0; i < bulletCols.Length; i++)
        {
            var bc = bulletCols[i];
            if (bc == null) continue;

            for (int j = 0; j < enemyCols.Length; j++)
            {
                var ec = enemyCols[j];
                if (ec == null) continue;

                Physics.IgnoreCollision(bc, ec, true);
            }
        }
    }

    private static void CountStatus(StatusContainer sc, StatusType type, bool countStacks, ref int count)
    {
        int stacks = sc.GetStacks(type);
        if (stacks <= 0) return;
        count += countStacks ? stacks : 1;
    }
}