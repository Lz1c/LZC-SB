using UnityEngine;

/// <summary>
/// Perk：每消灭一个单位，玩家恢复最大生命值的固定百分比
/// - 监听 CombatEventHub 的击杀事件
/// - 每次击杀：恢复 maxHp * healPercent
/// - 伤害/血量为整数体系：向下取整，若 < 1 则忽略
/// </summary>
public sealed class Perk_HealOnKill : MonoBehaviour
{
    [Header("回复比例")]
    [Range(0f, 1f)]
    [Tooltip("每次击杀回复最大生命值的比例（0.05 = 5%）")]
    public float healPercent = 0.05f;

    [Header("最小回复量")]
    [Min(0)]
    [Tooltip("回复量向下取整后若小于该值，则提升到该值（设为 0 表示允许为 0，即不回复）")]
    public int minHeal = 1;

    [Header("限制（可选）")]
    [Tooltip("如果为 true，会要求 PerkManager.PrerequisitesMet 通过")]
    public bool requirePrerequisites = true;

    private PerkManager _perkManager;
    private CameraGunChannel _boundChannel;
    private PlayerVitals _playerVitals;

    private void OnEnable()
    {
        _perkManager = FindFirstObjectByType<PerkManager>();
        if (_perkManager == null)
        {
            enabled = false;
            return;
        }

        // 1) 判断这个 Perk 属于哪把枪（保证只响应自己的击杀来源）
        int gunIndex = ResolveGunIndexFromManager();
        if (gunIndex < 0)
        {
            enabled = false;
            return;
        }

        // 2)（可选）校验前置条件
        if (requirePrerequisites && !_perkManager.PrerequisitesMet(gameObject, gunIndex))
        {
            enabled = false;
            return;
        }

        // 3) 绑定该枪的 CameraGunChannel
        var gun = _perkManager.GetGun(gunIndex);
        _boundChannel = gun != null ? gun.cameraGunChannel : null;

        // 4) 找玩家血量（优先从枪所在层级往上找）
        if (_boundChannel != null)
            _playerVitals = _boundChannel.GetComponentInParent<PlayerVitals>();

        if (_playerVitals == null)
            _playerVitals = FindFirstObjectByType<PlayerVitals>();

        CombatEventHub.OnKill += HandleKill;
    }

    private void OnDisable()
    {
        CombatEventHub.OnKill -= HandleKill;
    }

    /// <summary>
    /// 判断该 Perk 被 PerkManager 认为属于哪把枪
    /// </summary>
    private int ResolveGunIndexFromManager()
    {
        if (_perkManager == null) return -1;

        if (_perkManager.selectedPerksGunA != null && _perkManager.selectedPerksGunA.Contains(this)) return 0;
        if (_perkManager.selectedPerksGunB != null && _perkManager.selectedPerksGunB.Contains(this)) return 1;

        return -1;
    }

    private void HandleKill(CombatEventHub.KillEvent e)
    {
        if (!isActiveAndEnabled) return;
        if (_playerVitals == null) return;

        // 只响应“本枪来源”的击杀（如果你希望任意来源都回血，把这行删掉即可）
        if (_boundChannel != null && e.source != _boundChannel) return;

        // 死了不回血
        if (_playerVitals.IsDead) return;

        // 计算回复量：向下取整；若 < 1 则按 minHeal（minHeal=0 则允许为 0）
        int heal = Mathf.FloorToInt(_playerVitals.maxHp * Mathf.Clamp01(healPercent));
        if (heal < minHeal) heal = minHeal;
        if (heal < 1) return;

        _playerVitals.Heal(heal);
    }
}