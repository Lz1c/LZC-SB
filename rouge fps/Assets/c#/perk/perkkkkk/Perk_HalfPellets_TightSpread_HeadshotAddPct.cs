using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 三合一霰弹 Perk：
/// 1) 弹丸数量 /2（postMul，保证在 addPct 之后执行）
/// 2) 霰弹散布减半（横/竖都乘 0.5，影响准星）
/// 3) 爆头倍率 add pct n%（通过 HitboxMultiplierManager，只加爆头）
///
/// 注意：只对 ShotType.Shotgun 生效；非霰弹枪默认不应用（可选禁用组件）。
/// </summary>
public sealed class Perk_HalfPelle : GunPerkModifierBase
{
    [Header("仅霰弹枪生效")]
    public bool disableComponentIfNotShotgun = true;

    [Header("弹丸数量（postMul：在 addPct 之后执行）")]
    [Tooltip("0.5 = 在所有 addPct 计算完之后再 /2")]
    [Min(0.01f)] public float pelletsPostMul = 0.5f;

    [Header("散布缩放（霰弹专用，影响准星）")]
    [Tooltip("0.5 = 左右散布减半")]
    [Min(0f)] public float shotgunHorizontalScaleMul = 0.5f;

    [Tooltip("0.5 = 上下散布减半")]
    [Min(0f)] public float shotgunVerticalScaleMul = 0.5f;

    [Header("爆头倍率 add pct")]
    [Tooltip("0.3 = 爆头伤害额外 +30%（以当前倍率为基准相乘）")]
    [Min(0f)] public float headshotAddPct = 0.3f;

    [Header("优先级")]
    [Tooltip("越小越早。弹丸 /2 使用 postMul，因此天然保证在 addPct 之后执行。")]
    public int statPriority = -9999;

    public override int Priority => -9999;

    private GunSpread _spread;
    private float _prevH = 1f;
    private float _prevV = 1f;
    private bool _spreadApplied;
    private bool _hitboxApplied;

    protected override void OnEnable()
    {
        base.OnEnable();

        // 找枪与 spread
        var gun = SourceGun;
        if (gun == null)
        {
            enabled = false;
            return;
        }

        // 非霰弹枪：不应用
        if (gun.shotType != CameraGunChannel.ShotType.Shotgun)
        {
            if (disableComponentIfNotShotgun) enabled = false;
            return;
        }

        // 1) 散布减半（即时生效，影响实际散布与准星）
        _spread = gun.spread != null ? gun.spread : gun.GetComponentInParent<GunSpread>();
        if (_spread != null)
        {
            _prevH = _spread.shotgunHorizontalScale;
            _prevV = _spread.shotgunVerticalScale;

            _spread.shotgunHorizontalScale = Mathf.Max(0f, _spread.shotgunHorizontalScale * shotgunHorizontalScaleMul);
            _spread.shotgunVerticalScale = Mathf.Max(0f, _spread.shotgunVerticalScale * shotgunVerticalScaleMul);

            _spreadApplied = true;
        }

        // 2) 爆头倍率 add pct（通过 HitboxMultiplierManager）
        var mgr = HitboxMultiplierManager.Instance;
        if (mgr != null && headshotAddPct > 0f)
        {
            // 只加爆头，不改身体
            mgr.RegisterAddPct(gun, this, bodyAddPct: 0f, headAddPct: headshotAddPct, priority: statPriority);
            _hitboxApplied = true;
        }
    }

    protected override void OnDisable()
    {
        // 还原散布
        if (_spreadApplied && _spread != null)
        {
            _spread.shotgunHorizontalScale = _prevH;
            _spread.shotgunVerticalScale = _prevV;
        }
        _spreadApplied = false;
        _spread = null;

        // 取消爆头 add pct
        if (_hitboxApplied && SourceGun != null)
        {
            var mgr = HitboxMultiplierManager.Instance;
            if (mgr != null)
                mgr.UnregisterAddPct(SourceGun, this);
        }
        _hitboxApplied = false;

        base.OnDisable();
    }

    /// <summary>
    /// 3) 弹丸数量 /2：通过 GunStatContext 栈改 ShotgunPelletsPerShot
    /// 使用 postMul，保证在 addPct 之后再除 2。
    /// </summary>
    public override void ApplyModifiers(CameraGunChannel source, Dictionary<GunStat, StatStack> stacks)
    {
        if (SourceGun == null || source != SourceGun) return;

        // 再次保护：只对霰弹枪生效
        if (source.shotType != CameraGunChannel.ShotType.Shotgun)
            return;

        var st = stacks[GunStat.ShotgunPelletsPerShot];
        st.postMul *= Mathf.Max(0.01f, pelletsPostMul);
        stacks[GunStat.ShotgunPelletsPerShot] = st;
    }
}