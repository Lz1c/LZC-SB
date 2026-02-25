using System.Collections.Generic;
using UnityEngine;

public sealed class Perk_AutoFire_DoubleFireRateBonuses : MonoBehaviour, IGunStatModifier
{
    [Header("连发强制")]
    [Tooltip("为 true 时：启用时把 Semi 切成 Auto；若本来就是 Auto 则不做任何修改；禁用时仅在本脚本改动过的情况下还原。")]
    public bool forceAuto = true;

    [Header("射速加成翻倍")]
    [Tooltip("为 true 时：自动识别当前模式；Auto 翻倍 FireRate.addPct；Semi 翻倍 SemiFireCooldown.addPct。")]
    public bool doubleFireRateAddPctOnly = true;

    // 需要在其它修饰器之后执行，才能对最终 stack 做变换
    public int Priority => 100000;

    // 由 PerkManager.InstantiatePerkToGun 注入（如存在）
    [HideInInspector] public int targetGunIndex = -1;

    private PerkManager _pm;
    private CameraGunChannel _gun;
    private GunStatContext _ctx;

    private bool _changedFireMode;
    private CameraGunChannel.FireMode _prevFireMode;

    private void Awake()
    {
        _pm = FindFirstObjectByType<PerkManager>();
    }

    private void OnEnable()
    {
        Resolve();
        if (_ctx != null) _ctx.Register(this);

        // 枪械变为连发：若已为连发则不变
        // 注意：必须使用 SetFireMode，确保立即 ForceRebuildNow 并触发 OnFireModeChanged
        if (forceAuto && _gun != null)
        {
            if (_gun.fireMode == CameraGunChannel.FireMode.Semi)
            {
                _prevFireMode = _gun.fireMode;
                _gun.SetFireMode(CameraGunChannel.FireMode.Auto, forceRebuildNow: true);
                _changedFireMode = true;
            }
        }

        if (_ctx != null) _ctx.ForceRebuildNow();
    }

    private void OnDisable()
    {
        if (_ctx != null)
        {
            _ctx.Unregister(this);
            _ctx.ForceRebuildNow();
        }

        // 仅当本脚本改动过开火模式时才还原
        // 同样用 SetFireMode 保证立即生效
        if (_changedFireMode && _gun != null)
        {
            _gun.SetFireMode(_prevFireMode, forceRebuildNow: true);
        }

        _changedFireMode = false;
        _gun = null;
        _ctx = null;
    }

    public void ApplyModifiers(CameraGunChannel source, Dictionary<GunStat, StatStack> stacks)
    {
        if (!doubleFireRateAddPctOnly) return;

        if (_gun == null || _ctx == null) Resolve();
        if (_gun == null || source != _gun) return;

        // 自动识别当前模式
        if (source.fireMode == CameraGunChannel.FireMode.Semi)
        {
            if (!stacks.TryGetValue(GunStat.SemiFireCooldown, out var cd))
                return;

            // Semi：翻倍“单发间隔 Stat”的 addPct
            // 单发加速通常表现为 cd.addPct 为负数（例如 -0.2 表示缩短 20%）
            // 翻倍后：-0.2 -> -0.4，缩短幅度翻倍
            cd.addPct *= 2f;
            stacks[GunStat.SemiFireCooldown] = cd;
        }
        else
        {
            if (!stacks.TryGetValue(GunStat.FireRate, out var fr))
                return;

            // Auto：翻倍 FireRate 的 addPct
            fr.addPct *= 2f;
            stacks[GunStat.FireRate] = fr;
        }
    }

    private void Resolve()
    {
        if (_gun != null && _ctx != null) return;

        // 1) 优先走父级链：当 perk 作为子物体挂在枪下时
        _gun = GetComponentInParent<CameraGunChannel>();
        if (_gun != null)
        {
            _ctx = _gun.GetComponent<GunStatContext>();
            if (_ctx == null) _ctx = _gun.GetComponentInParent<GunStatContext>();
            if (_ctx != null) return;
        }

        // 2) PerkGiveTest 路径：通过 PerkManager + targetGunIndex 找到对应枪
        _pm ??= FindFirstObjectByType<PerkManager>();
        if (_pm == null || targetGunIndex < 0) return;

        _pm.RefreshAll(force: true);
        var gunRefs = _pm.GetGun(targetGunIndex);
        if (gunRefs == null) return;

        _gun = gunRefs.cameraGunChannel;
        if (_gun == null) return;

        _ctx = _gun.GetComponent<GunStatContext>();
        if (_ctx == null) _ctx = _gun.GetComponentInParent<GunStatContext>();
    }
}