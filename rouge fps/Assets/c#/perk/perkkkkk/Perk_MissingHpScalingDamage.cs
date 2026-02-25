using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Perk：玩家血量每下降 n%，伤害增加 x * n%（使用 addPct）
///
/// 需求：必须实时随玩家血量变化更新
/// 实现：订阅 PlayerVitals.OnHpChanged，并对相关 GunStatContext 调用 MarkDirty()
/// </summary>
public sealed class Perk_MissingHpScalingDamage : GunPerkModifierBase
{
    [Header("增伤系数 x")]
    [Min(0f)]
    [Tooltip("增伤比例 = x * 已损失生命比例。例如 x=2，掉血10% -> +20%")]
    public float x = 2f;

    [Header("增伤上限（可选）")]
    [Min(0f)]
    [Tooltip("最大增伤比例上限（0=不限制）。例如 2 表示最多 +200%")]
    public float maxBonusPercentCap = 0f;

    [Header("优先级")]
    public int priority = 0;
    public override int Priority => priority;

    private PlayerVitals _playerVitals;

    // 需要被刷新数值的上下文（本枪 + 可选：另一把枪）
    private GunStatContext _ctxSelf;
    private GunStatContext _ctxOther;

    protected override void OnEnable()
    {
        base.OnEnable();

        // 1) 找玩家血量
        if (SourceGun != null)
            _playerVitals = SourceGun.GetComponentInParent<PlayerVitals>();

        if (_playerVitals == null)
            _playerVitals = FindFirstObjectByType<PlayerVitals>();

        // 2) 获取本枪 GunStatContext（一定要刷新它）
        if (SourceGun != null)
        {
            _ctxSelf = SourceGun.GetComponent<GunStatContext>();
            if (_ctxSelf == null) _ctxSelf = SourceGun.GetComponentInParent<GunStatContext>();
        }

        // 3) 可选：如果你希望“血量变化时两把枪都立刻刷新”，就把另一把也缓存
        //    这不会改变“只影响本枪”的效果，只是让 UI/数值刷新更及时
        CacheOtherGunContext();

        // 4) 订阅血量变化 -> 刷新
        if (_playerVitals != null)
            _playerVitals.OnHpChanged += HandleHpChanged;

        // 启用时立刻刷新一次
        MarkAllDirty();
    }

    protected override void OnDisable()
    {
        if (_playerVitals != null)
            _playerVitals.OnHpChanged -= HandleHpChanged;

        _playerVitals = null;
        _ctxSelf = null;
        _ctxOther = null;

        base.OnDisable();
    }

    private void HandleHpChanged(int current, int max)
    {
        // 血量每变化一次，就立刻标记脏，让增伤实时更新
        MarkAllDirty();
    }

    private void MarkAllDirty()
    {
        if (_ctxSelf != null) _ctxSelf.MarkDirty();
        if (_ctxOther != null) _ctxOther.MarkDirty();
    }

    private void CacheOtherGunContext()
    {
        // 找到 PerkManager，拿到另一把枪的 Channel，再拿它的 GunStatContext
        var pm = FindFirstObjectByType<PerkManager>();
        if (pm == null) return;

        // 判断本 perk 属于哪把枪
        int selfIndex = -1;
        if (pm.selectedPerksGunA != null && pm.selectedPerksGunA.Contains(this)) selfIndex = 0;
        else if (pm.selectedPerksGunB != null && pm.selectedPerksGunB.Contains(this)) selfIndex = 1;
        if (selfIndex < 0) return;

        int otherIndex = selfIndex == 0 ? 1 : 0;
        var otherGun = pm.GetGun(otherIndex);
        if (otherGun == null || otherGun.cameraGunChannel == null) return;

        var otherCh = otherGun.cameraGunChannel;
        _ctxOther = otherCh.GetComponent<GunStatContext>();
        if (_ctxOther == null) _ctxOther = otherCh.GetComponentInParent<GunStatContext>();
    }

    public override void ApplyModifiers(CameraGunChannel source, Dictionary<GunStat, StatStack> stacks)
    {
        if (_playerVitals == null) return;
        if (!stacks.ContainsKey(GunStat.Damage)) return;

        int maxHp = Mathf.Max(1, _playerVitals.maxHp);
        int curHp = Mathf.Clamp(_playerVitals.hp, 0, maxHp);

        // 已损失生命比例（0~1）
        float lost01 = 1f - ((float)curHp / maxHp);
        lost01 = Mathf.Clamp01(lost01);

        // 增伤比例 = x * 已损失比例
        float bonus01 = Mathf.Max(0f, x) * lost01;

        // 可选：限制上限
        if (maxBonusPercentCap > 0f)
            bonus01 = Mathf.Min(bonus01, maxBonusPercentCap);

        if (bonus01 <= 0f) return;

        // StatStack 是 struct，必须取出-修改-写回
        StatStack s = stacks[GunStat.Damage];
        s.addPct += bonus01;              // ← 这里赋予百分比倍率
        stacks[GunStat.Damage] = s;
    }
}