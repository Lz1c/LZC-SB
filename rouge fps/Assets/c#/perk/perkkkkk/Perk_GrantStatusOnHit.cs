using UnityEngine;

/// <summary>
/// Perk：赋予异常属性（可选异常类型 + 自定义数值）
///
/// 作用：当该枪造成命中时，给命中的敌人施加你选择的异常状态。
/// 使用方式：做成 Perk prefab，PerkGiveTest 实例化到枪下面即可。
/// </summary>
public sealed class Perk_GrantStatusOnHit : MonoBehaviour
{
    /// <summary>
    /// 选择要赋予的异常类型
    /// </summary>
    public enum GrantType
    {
        燃烧 = 0,
        中毒 = 1,
        减速 = 2,
        电击 = 3
    }

    [Header("异常类型")]
    public GrantType grantType = GrantType.燃烧;

    [Header("触发设置")]
    [Range(0f, 1f)]
    [Tooltip("命中触发概率。1 = 100%，0.25 = 25%")]
    public float procChance = 1f;

    [Min(1)]
    [Tooltip("每次触发增加的层数")]
    public int stacksToAdd = 1;

    [Min(0.01f)]
    [Tooltip("状态持续时间（秒）")]
    public float duration = 4f;

    [Header("燃烧(Burn)参数")]
    [Min(0.01f)]
    [Tooltip("燃烧 tick 间隔（秒）。你的 StatusContainer 会按这个参数 tick")]
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
        // 找到该 Perk 所属的枪
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
        // 只处理本枪造成的命中
        if (_sourceGun == null) return;
        if (e.source != _sourceGun) return;

        // 跳过 SkipHitEvent（比如燃烧 tick / 链电二次伤害等），防止递归触发
        if ((e.flags & DamageFlags.SkipHitEvent) != 0) return;

        // 概率判定
        if (procChance < 1f && Random.value > procChance) return;

        // 获取目标的状态容器
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

        // 把 Inspector 配置转换成 StatusType
        StatusType type = ConvertToStatusType(grantType);

        // 组装请求（真正逻辑由你原本的 StatusContainer / ShockChainProc 执行）
        var req = new StatusApplyRequest
        {
            type = type,
            stacksToAdd = Mathf.Max(1, stacksToAdd),
            duration = Mathf.Max(0.01f, duration),
            source = _sourceGun,

            // 以下字段按类型使用，其余会被你的系统忽略
            tickInterval = burnTickInterval,
            burnDamagePerTickPerStack = burnDamagePerTickPerStack,

            slowPerStack = slowPerStack,
            weakenPerStack = weakenPerStack,

            shockChainDamagePerStack = shockChainDamagePerStack,
            shockChainRadius = shockChainRadius,
            shockMaxChains = Mathf.Max(1, shockMaxChains)
        };

        // 施加状态
        sc.ApplyStatus(req);
    }

    private static StatusType ConvertToStatusType(GrantType gt)
    {
        switch (gt)
        {
            case GrantType.燃烧: return StatusType.Burn;
            case GrantType.中毒: return StatusType.Poison;
            case GrantType.减速: return StatusType.Slow;
            default: return StatusType.Shock;
        }
    }
}