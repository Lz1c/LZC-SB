using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Perk：射击时有概率自残并对周围敌人造成一次范围伤害
/// - 每次触发射击事件时：有 chance 概率触发
/// - 触发后：对自己造成“当前生命值 * selfHpPercent”的伤害（无视护甲）
/// - 然后：以自身为圆心，在 radius 范围内，对敌人造成一次 aoeDamage 的伤害
/// </summary>
public sealed class Perk_SelfBloodNovaOnFire : MonoBehaviour
{
    [Header("触发概率")]
    [Range(0f, 1f)]
    [Tooltip("每次射击触发的概率（0~1）")]
    public float chance = 0.20f;

    [Header("自残")]
    [Range(0f, 1f)]
    [Tooltip("自残伤害 = 当前生命值 * 该比例（例如 0.1 = 10%）")]
    public float selfHpPercent = 0.10f;

    [Min(0)]
    [Tooltip("自残最小伤害（避免当前血量很低时算出来为 0，导致白嫖）。设为 0 则允许为 0。")]
    public int minSelfDamage = 1;

    [Header("范围伤害")]
    [Min(0.01f)]
    [Tooltip("范围半径 x")]
    public float radius = 5f;

    [Min(0f)]
    [Tooltip("范围伤害 y")]
    public float aoeDamage = 30f;

    [Tooltip("用于范围检索敌人的 Layer")]
    public LayerMask enemyMask = ~0;

    [Header("事件/递归保护（可选）")]
    [Tooltip("为 true 时，范围伤害会带 SkipHitEvent 标记，避免触发依赖 OnHit 的 Perk 连锁")]
    public bool aoeSkipHitEvent = false;

    [Header("限制（可选）")]
    [Tooltip("如果为 true，但该 Perk 不是通过 PerkManager 正确实例化/挂载，会自动禁用")]
    public bool disableIfNotAllowed = true;

    [Tooltip("如果为 true，会要求 PerkManager.PrerequisitesMet 通过")]
    public bool requirePrerequisites = true;

    private PerkManager _perkManager;
    private CameraGunChannel _boundChannel;
    private PlayerVitals _playerVitals;
    private Transform _playerRoot;

    private void Awake()
    {
        _perkManager = FindFirstObjectByType<PerkManager>();
    }

    private void OnEnable()
    {
        _perkManager ??= FindFirstObjectByType<PerkManager>();
        if (_perkManager == null)
        {
            if (disableIfNotAllowed) enabled = false;
            return;
        }

        // 1) 确认该 Perk 属于哪把枪（GunA=0 / GunB=1），确保只响应自己的那把枪
        int gunIndex = ResolveGunIndexFromManager();
        if (gunIndex < 0)
        {
            if (disableIfNotAllowed) enabled = false;
            return;
        }

        // 2)（可选）校验前置条件
        if (requirePrerequisites && !_perkManager.PrerequisitesMet(gameObject, gunIndex))
        {
            if (disableIfNotAllowed) enabled = false;
            return;
        }

        // 3) 绑定该枪的 CameraGunChannel
        var gun = _perkManager.GetGun(gunIndex);
        _boundChannel = gun != null ? gun.cameraGunChannel : null;

        // 4) 获取玩家血量脚本（优先从枪所在层级往上找）
        if (_boundChannel != null)
        {
            _playerVitals = _boundChannel.GetComponentInParent<PlayerVitals>();
            _playerRoot = _boundChannel.transform.root;
        }

        if (_playerVitals == null)
        {
            // 兜底：场景里找一个（不建议长期依赖，但能避免你测试时空引用）
            _playerVitals = FindFirstObjectByType<PlayerVitals>();
            _playerRoot = _playerVitals != null ? _playerVitals.transform : null;
        }

        CombatEventHub.OnFire += HandleFire;
    }

    private void OnDisable()
    {
        CombatEventHub.OnFire -= HandleFire;
    }

    /// <summary>
    /// 判断这个 Perk 被 PerkManager 认为属于哪把枪
    /// </summary>
    private int ResolveGunIndexFromManager()
    {
        if (_perkManager == null) return -1;

        if (_perkManager.selectedPerksGunA != null && _perkManager.selectedPerksGunA.Contains(this)) return 0;
        if (_perkManager.selectedPerksGunB != null && _perkManager.selectedPerksGunB.Contains(this)) return 1;

        return -1;
    }

    private void HandleFire(CombatEventHub.FireEvent e)
    {
        if (!isActiveAndEnabled) return;

        // 只响应绑定的那把枪
        if (_boundChannel != null && e.source != _boundChannel) return;

        // 参数保护
        if (chance <= 0f) return;
        if (radius <= 0.01f) return;
        if (aoeDamage <= 0f) return;
        if (_playerVitals == null || _playerRoot == null) return;

        // 概率判定
        if (Random.value > chance) return;

        // ========== 1) 先自残（无视护甲，按当前生命值百分比） ==========
        // 注意：这里按“当前 hp”计算，并向下取整；若 < 1 则按 minSelfDamage（可为 0）
        int currentHp = _playerVitals.hp;
        if (currentHp > 0)
        {
            int selfDmg = Mathf.FloorToInt(currentHp * Mathf.Clamp01(selfHpPercent));
            if (selfDmg < minSelfDamage) selfDmg = minSelfDamage;

            if (selfDmg >= 1)
            {
                _playerVitals.ApplyTrueDamage(selfDmg);
            }
        }

        // ========== 2) 再对周围敌人造成一次 AoE 伤害 ==========
        Vector3 center = _playerVitals.transform.position;

        Collider[] cols = Physics.OverlapSphere(center, radius, enemyMask, QueryTriggerInteraction.Collide);
        if (cols == null || cols.Length == 0) return;

        var uniqueTargets = new HashSet<MonsterHealth>();

        for (int i = 0; i < cols.Length; i++)
        {
            var hitCol = cols[i];
            if (hitCol == null) continue;

            var mh = hitCol.GetComponentInParent<MonsterHealth>();
            if (mh == null) continue;
            if (!uniqueTargets.Add(mh)) continue;

            var info = new DamageInfo
            {
                source = e.source,
                damage = aoeDamage,
                isHeadshot = false,
                hitPoint = center,
                hitCollider = hitCol,
                flags = aoeSkipHitEvent ? DamageFlags.SkipHitEvent : 0
            };

            DamageResolver.ApplyHit(
                baseInfo: info,
                hitCol: hitCol,
                hitPoint: center,
                source: info.source,
                armorPayload: null,
                statusPayload: null,
                showHitUI: false
            );
        }
    }
}