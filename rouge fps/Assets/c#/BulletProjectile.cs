using UnityEngine;

public class BulletProjectile : MonoBehaviour
{
    public struct Config
    {
        public CameraGunChannel source;

        public float speed;
        public float gravity;
        public float lifetime;

        public float maxRange;
        public float baseDamage;

        [Tooltip("从多少米开始进行伤害衰减。小于等于0表示从起点开始衰减。")]
        public float falloffStartMeters;

        public LayerMask hitMask;
    }

    private Config _cfg;
    private Vector3 _velocity;
    private float _life;

    // 真实飞行距离（用于衰减和射程判断）
    private float _traveledMeters;

    public void Init(Config cfg)
    {
        _cfg = cfg;
        _velocity = transform.forward * Mathf.Max(0.01f, cfg.speed);
        _life = 0f;
        _traveledMeters = 0f;
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        _life += dt;

        if (_life >= _cfg.lifetime)
        {
            Destroy(gameObject);
            return;
        }

        if (_cfg.gravity > 0f)
            _velocity += Vector3.down * _cfg.gravity * dt;

        Vector3 step = _velocity * dt;
        float stepDist = step.magnitude;

        if (stepDist <= 0.0001f)
            return;

        // 先做射线防穿透
        if (Physics.Raycast(transform.position, step.normalized, out RaycastHit hit, stepDist, _cfg.hitMask, QueryTriggerInteraction.Ignore))
        {
            float traveledAtHit = _traveledMeters + hit.distance;

            float damageMultiplier = ComputeLinearFalloff(traveledAtHit);

            float finalDamage = _cfg.baseDamage * damageMultiplier;

            var armorPayload = GetComponentInChildren<BulletArmorPayload>(true);
            var statusPayload = GetComponentInChildren<BulletStatusPayload>(true);

            var info = new DamageInfo
            {
                source = _cfg.source,
                damage = finalDamage,
                isHeadshot = false,
                hitPoint = hit.point,
                hitCollider = hit.collider,
                flags = DamageFlags.None
            };

            DamageResolver.ApplyHit(
                baseInfo: info,
                hitCol: hit.collider,
                hitPoint: hit.point,
                source: _cfg.source,
                armorPayload: armorPayload,
                statusPayload: statusPayload,
                showHitUI: true
            );

            Destroy(gameObject);
            return;
        }

        transform.position += step;
        _traveledMeters += stepDist;

        if (_cfg.maxRange > 0f && _traveledMeters >= _cfg.maxRange)
        {
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// 线性衰减：
    /// - traveled <= start → 1
    /// - traveled >= maxRange → 0
    /// - 中间线性下降
    /// </summary>
    private float ComputeLinearFalloff(float traveled)
    {
        float start = Mathf.Max(0f, _cfg.falloffStartMeters);
        float max = Mathf.Max(start + 0.0001f, _cfg.maxRange);

        if (traveled <= start)
            return 1f;

        if (traveled >= max)
            return 0f;

        float t = (traveled - start) / (max - start);
        return 1f - Mathf.Clamp01(t);
    }
}