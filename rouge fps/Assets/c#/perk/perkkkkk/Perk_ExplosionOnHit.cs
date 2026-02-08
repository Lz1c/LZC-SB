using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Perk：子弹命中敌人后触发小范围爆炸
/// - 直击伤害：由原有 DamageResolver/子弹系统结算（本 Perk 不改直击）
/// - 爆炸伤害：对半径内敌人造成较小伤害
/// - 爆炸伤害带 SkipHitEvent：避免“爆炸再次触发命中类 Perk”造成递归/无限触发
/// - VFX：触发爆炸时在爆炸中心生成一个特效，可按半径缩放
/// </summary>
public sealed class Perk_ExplosionOnHit : MonoBehaviour
{
    [Header("爆炸参数")]
    [Min(0.01f)]
    [Tooltip("爆炸半径（x）")]
    public float radius = 2.5f;

    [Min(0f)]
    [Tooltip("爆炸伤害 = 命中事件伤害 * 倍率 + 平伤加成")]
    public float damageMultiplier = 0.35f;

    [Min(0f)]
    [Tooltip("在倍率基础上额外增加的固定伤害（可选）")]
    public float flatBonusDamage = 0f;

    [Tooltip("是否排除直接命中的那个目标（推荐开启：直击一次 + 溅射伤害给周围）")]
    public bool excludeDirectTarget = true;

    [Header("目标筛选")]
    [Tooltip("用于筛选敌人的层（建议只勾 Enemy 层）")]
    public LayerMask enemyMask = ~0;

    [Header("VFX（特效）")]
    [Tooltip("爆炸触发时生成的特效 Prefab（你拖进去即可）")]
    public GameObject explosionVfxPrefab;

    [Tooltip("特效的父物体（可不填，不填就生成在场景根下）")]
    public Transform vfxParent;

    [Min(0f)]
    [Tooltip("特效自动销毁时间（如果你的特效 Prefab 本身会自毁，这里可以设为 0）")]
    public float vfxAutoDestroySeconds = 6f;

    [Tooltip("特效是否随爆炸半径缩放")]
    public bool scaleVfxByRadius = true;

    [Tooltip("缩放时用直径(2r)还是半径(r)。大多数“圈/冲击波”类特效更适合用直径")]
    public bool vfxUseDiameter = true;

    [Min(0f)]
    [Tooltip("最终缩放 = (半径或直径) * 该系数。用于适配不同特效资源的尺寸基准")]
    public float vfxScalePerUnit = 0.5f;

    [Header("允许条件（不满足则不激活）")]
    [Tooltip("若为 true：当未被 PerkManager 正确注册/或前置不满足时，自动禁用本组件")]
    public bool disableIfNotAllowed = true;

    [Tooltip("若为 true：额外要求 PerkManager.PrerequisitesMet(gameObject, gunIndex) 为真（防止被外部脚本绕开系统强行挂载）")]
    public bool requirePrerequisites = true;

    private PerkManager _perkManager;
    private CameraGunChannel _boundChannel;

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

        // 1) 按你现有 Perk 模板：通过“我是否在 PerkManager 的列表里”判断属于哪把枪
        int gunIndex = ResolveGunIndexFromManager();
        if (gunIndex < 0)
        {
            // 典型原因：未通过 PerkManager API 添加、或添加顺序错误导致 OnEnable 先跑（你已修过 PerkManager 的激活顺序后一般不会出现）
            if (disableIfNotAllowed) enabled = false;
            return;
        }

        // 2) 可选：额外校验前置条件（与你现有 PerkManager.PrerequisitesMet 保持一致）
        if (requirePrerequisites && !_perkManager.PrerequisitesMet(gameObject, gunIndex))
        {
            if (disableIfNotAllowed) enabled = false;
            return;
        }

        // 3) 绑定这把枪的 source，用于只响应“这把枪打出来的命中事件”
        var gun = _perkManager.GetGun(gunIndex);
        _boundChannel = gun != null ? gun.cameraGunChannel : null;

        CombatEventHub.OnHit += HandleHit;
    }

    private void OnDisable()
    {
        CombatEventHub.OnHit -= HandleHit;
    }

    /// <summary>
    /// 按你现有 Perk 的模板：通过“我是否在 PerkManager 的列表里”判断属于哪把枪
    /// </summary>
    private int ResolveGunIndexFromManager()
    {
        if (_perkManager == null) return -1;

        if (_perkManager.selectedPerksGunA != null && _perkManager.selectedPerksGunA.Contains(this)) return 0;
        if (_perkManager.selectedPerksGunB != null && _perkManager.selectedPerksGunB.Contains(this)) return 1;

        return -1;
    }

    private void HandleHit(CombatEventHub.HitEvent e)
    {
        if (!isActiveAndEnabled) return;
        if (e.target == null) return;

        // 只处理属于这把枪的命中事件
        if (_boundChannel != null && e.source != _boundChannel) return;

        if (radius <= 0.01f) return;

        // 爆炸中心：优先用命中点，没有就退化为目标位置
        Vector3 center = e.hitPoint;
        if (center == Vector3.zero)
            center = e.target.transform.position;

        // OverlapSphere 找半径内敌人
        Collider[] cols = Physics.OverlapSphere(center, radius, enemyMask, QueryTriggerInteraction.Collide);
        if (cols == null || cols.Length == 0) return;

        // 爆炸伤害（较小）
        float explosionDamage = (e.damage * damageMultiplier) + flatBonusDamage;
        if (explosionDamage <= 0f) return;

        // 同一个敌人可能有多个 collider：去重，避免重复伤害
        var uniqueTargets = new HashSet<MonsterHealth>();
        bool didDamageAny = false;

        for (int i = 0; i < cols.Length; i++)
        {
            var mh = cols[i].GetComponentInParent<MonsterHealth>();
            if (mh == null) continue;

            // 可选：排除直击目标，避免“直击 + 爆炸”对同一个人重复伤害
            if (excludeDirectTarget && mh.gameObject == e.target) continue;

            if (!uniqueTargets.Add(mh)) continue;

            didDamageAny = true;

            // 走你项目现有的统一受伤入口，并标记 SkipHitEvent，避免递归触发命中类 perk
            mh.TakeDamage(new DamageInfo
            {
                source = e.source,
                damage = explosionDamage,
                isHeadshot = false,
                hitPoint = center,
                hitCollider = null,
                flags = DamageFlags.SkipHitEvent
            });
        }

        // 命中即发生爆炸：不管是否炸到其它敌人，都播放特效
        SpawnVfx(center);

    }

    private void SpawnVfx(Vector3 position)
    {
        if (explosionVfxPrefab == null) return;

        var go = Instantiate(explosionVfxPrefab, position, Quaternion.identity, vfxParent);

        // 按爆炸半径缩放特效
        if (scaleVfxByRadius)
        {
            float baseValue = vfxUseDiameter ? (radius * 2f) : radius;
            float s = Mathf.Max(0.0001f, baseValue * vfxScalePerUnit);
            go.transform.localScale = Vector3.one * s;
        }

        // 如果你的特效不会自毁，就用这个兜底
        if (vfxAutoDestroySeconds > 0f)
        {
            Destroy(go, vfxAutoDestroySeconds);
        }
    }
}
