using UnityEngine;

/// <summary>
/// Perk：子弹命中敌人时，有概率随机施加一层状态（燃烧/中毒/减速/电击）
/// 挂载位置：放在枪的层级下（和其他 Perk 一样）
/// </summary>
public sealed class Perk_RandomStatusOnHit : MonoBehaviour
{
    [Header("触发设置")]
    [Range(0f, 1f)]
    [Tooltip("每次命中触发概率，例如 0.25 = 25%")]
    public float procChance = 0.25f;

    [Min(1)]
    [Tooltip("每次触发增加的层数")]
    public int stacksToAdd = 1;

    [Min(0.01f)]
    [Tooltip("状态持续时间（秒）")]
    public float duration = 4f;

    [Header("燃烧(Burn)参数")]
    [Min(0.01f)]
    [Tooltip("燃烧的 tick 间隔（秒）。<=0 会使用 StatusContainer 默认值")]
    public float burnTickInterval = 1f;

    [Min(0f)]
    [Tooltip("每层燃烧每次 tick 造成的伤害")]
    public float burnDamagePerTickPerStack = 2f;

    [Header("中毒(Poison)参数")]
    [Range(0f, 1f)]
    [Tooltip("每层中毒降低的“敌人输出伤害倍率”，例如 0.06 = 每层 -6%")]
    public float weakenPerStack = 0.06f;

    [Header("减速(Slow)参数")]
    [Range(0f, 1f)]
    [Tooltip("每层减速降低的“敌人移速倍率”，例如 0.05 = 每层 -5%")]
    public float slowPerStack = 0.05f;

    [Header("电击(Shock)参数（用于 ShockChainProc 链电）")]
    [Min(0f)]
    [Tooltip("每层电击提供的“链电对每个被链接目标的额外伤害”")]
    public float shockChainDamagePerStack = 6f;

    [Min(0.1f)]
    [Tooltip("链电半径（米）")]
    public float shockChainRadius = 3f;

    [Min(1)]
    [Tooltip("最多链接目标数量")]
    public int shockMaxChains = 3;

    private CameraGunChannel _sourceGun;

    private void OnEnable()
    {
        // 只对挂在该 Perk 所在层级的那把枪生效
        _sourceGun = GetComponentInParent<CameraGunChannel>();
        CombatEventHub.OnHit += HandleHit;
    }

    private void OnDisable()
    {
        CombatEventHub.OnHit -= HandleHit;
        _sourceGun = null;
    }

    private void HandleHit(CombatEventHub.HitEvent e)
    {
        // 过滤：只处理本枪命中
        if (_sourceGun == null) return;
        if (e.source != _sourceGun) return;

        // 过滤：跳过 SkipHitEvent（比如燃烧 tick / 链电伤害等），避免无限触发
        if ((e.flags & DamageFlags.SkipHitEvent) != 0) return;

        // 概率判定
        if (Random.value > procChance) return;

        // 目标必须有 StatusContainer（一般在怪物根节点或父节点上）
        StatusContainer sc = null;
        if (e.hitCollider != null)
        {
            sc = e.hitCollider.GetComponentInParent<StatusContainer>();
        }
        if (sc == null && e.target != null)
        {
            sc = e.target.GetComponentInParent<StatusContainer>();
        }
        if (sc == null) return;

        // 四选一随机
        StatusType type = RollRandomType();

        // 组装请求并施加
        var req = new StatusApplyRequest
        {
            type = type,
            stacksToAdd = Mathf.Max(1, stacksToAdd),
            duration = Mathf.Max(0.01f, duration),
            source = _sourceGun,

            // 下面这些字段只对对应状态类型有意义，其余类型会被 StatusContainer 忽略
            tickInterval = burnTickInterval,
            burnDamagePerTickPerStack = burnDamagePerTickPerStack,

            slowPerStack = slowPerStack,
            weakenPerStack = weakenPerStack,

            shockChainDamagePerStack = shockChainDamagePerStack,
            shockChainRadius = shockChainRadius,
            shockMaxChains = Mathf.Max(1, shockMaxChains)
        };

        sc.ApplyStatus(req);
    }

    private static StatusType RollRandomType()
    {
        // 0~3 对应 Burn/Poison/Slow/Shock
        int r = Random.Range(0, 4);
        switch (r)
        {
            case 0: return StatusType.Burn;
            case 1: return StatusType.Poison;
            case 2: return StatusType.Slow;
            default: return StatusType.Shock;
        }
    }
}