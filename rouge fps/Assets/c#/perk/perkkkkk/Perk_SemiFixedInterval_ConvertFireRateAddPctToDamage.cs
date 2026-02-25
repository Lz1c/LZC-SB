using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Perk 功能：
/// 1）强制武器切换为单发模式（Semi）
/// 2）锁定单发射击间隔为 shotIntervalSeconds（通过 GunStat.SemiFireCooldown 覆盖实现）
/// 3）把“从启用前基准间隔 → 被锁定到目标间隔”的变慢损失转为伤害加成：
///    - 起始 Auto：基准间隔 = 1 /（启用前最终 fireRate），再乘 autoToSemiIntervalPctCorrection 修正
///    - 起始 Semi：基准间隔 =（启用前最终 semiFireCooldown），不做任何修正
///    - 损失百分比 lossPct = (目标间隔 / 基准间隔) - 1，仅当 lossPct > 0 时加伤害
/// 4）可选：把 FireRate.addPct 按比例转为 Damage.addPct（用于“后续拿到攻速 Perk”）
/// 5）清空 FireRate 栈，保证攻速永远不影响射击间隔
/// </summary>
public sealed class Perk_SemiFixedInterval_ConvertFireRateAddPctToDamage : MonoBehaviour, IGunStatModifier
{
    [Header("锁定的单发射击间隔（秒）")]
    [Min(0f)]
    public float shotIntervalSeconds = 0.25f;

    [Header("起始为 Auto 时：等价间隔修正百分比")]
    [Tooltip("Auto 基准间隔 = (1 / 启用前最终fireRate) * (1 + 修正值)。例如 0.1=+10%更慢；-0.1=-10%更快")]
    [Range(-0.95f, 5f)]
    public float autoToSemiIntervalPctCorrection = 0f;

    [Header("将“速度损失百分比”转为伤害加成的比例")]
    [Tooltip("damageAddPct += lossPct * lossPctToDamageRatio，其中 lossPct = (目标间隔/基准间隔)-1")]
    [Min(0f)]
    public float lossPctToDamageRatio = 1f;

    [Header("可选：将攻速 addPct 转为伤害 addPct（用于后续拿攻速 Perk）")]
    [Tooltip("damageAddPct += FireRate.addPct * fireRateToDamageRatio")]
    [Min(0f)]
    public float fireRateToDamageRatio = 1f;

    [Tooltip("是否只转换正向攻速加成")]
    public bool convertPositiveOnly = true;

    [Header("执行优先级（必须较高）")]
    public int priority = 100000;
    public int Priority => priority;

    [HideInInspector] public int targetGunIndex = -1;

    private PerkManager _pm;
    private CameraGunChannel _gun;
    private GunStatContext _ctx;

    private bool _registered;
    private bool _changedFireMode;
    private CameraGunChannel.FireMode _prevFireMode;

    // 启用前缓存的基准间隔（关键：必须是“本 Perk 还没注册”时读到的）
    private CameraGunChannel.FireMode _startedMode;
    private float _baselineIntervalSeconds = -1f;

    private void Awake()
    {
        _pm = FindFirstObjectByType<PerkManager>();
    }

    private void OnEnable()
    {
        Resolve();
        if (_gun == null || _ctx == null) return;

        // 第一步：先强制重算一次（此时还没注册本 Perk），拿到“启用前最终数值”
        _ctx.ForceRebuildNow();

        _startedMode = _gun.fireMode;
        _baselineIntervalSeconds = -1f;

        // 起始 Auto：基准间隔 = 1 /（启用前最终 fireRate），并且允许修正
        if (_startedMode == CameraGunChannel.FireMode.Auto)
        {
            float finalAutoFireRate = Mathf.Max(0.01f, _gun.fireRate);
            float autoInterval = 1f / finalAutoFireRate;

            float scale = 1f + autoToSemiIntervalPctCorrection;
            if (scale < 0.01f) scale = 0.01f;

            _baselineIntervalSeconds = Mathf.Max(0.000001f, autoInterval * scale);
        }
        // 起始 Semi：基准间隔 =（启用前最终 semiFireCooldown），不需要修正
        else
        {
            float semiInterval = Mathf.Max(0f, _gun.semiFireCooldown);
            _baselineIntervalSeconds = Mathf.Max(0.000001f, semiInterval);
        }

        // 第二步：注册本 Perk（从现在开始才会覆盖 SemiFireCooldown / 做转换）
        _ctx.Register(this);
        _registered = true;

        // 第三步：强制切换为 Semi（必须用 SetFireMode 以保证立即生效）
        if (_gun.fireMode != CameraGunChannel.FireMode.Semi)
        {
            _prevFireMode = _gun.fireMode;
            _gun.SetFireMode(CameraGunChannel.FireMode.Semi, forceRebuildNow: true);
            _changedFireMode = true;
        }

        // 第四步：再重算一次，让锁定间隔与伤害转换立即生效
        _ctx.ForceRebuildNow();
    }

    private void OnDisable()
    {
        if (_ctx != null && _registered)
        {
            _ctx.Unregister(this);
            _registered = false;
            _ctx.ForceRebuildNow();
        }

        if (_changedFireMode && _gun != null)
        {
            _gun.SetFireMode(_prevFireMode, forceRebuildNow: true);
        }

        _changedFireMode = false;
        _baselineIntervalSeconds = -1f;

        _gun = null;
        _ctx = null;
    }

    public void ApplyModifiers(CameraGunChannel source, Dictionary<GunStat, StatStack> stacks)
    {
        if (_gun == null || _ctx == null) Resolve();
        if (_gun == null || source != _gun) return;

        if (!stacks.TryGetValue(GunStat.FireRate, out var fr)) return;
        if (!stacks.TryGetValue(GunStat.Damage, out var dmg)) return;
        if (!stacks.TryGetValue(GunStat.SemiFireCooldown, out var cd)) return;

        float targetInterval = Mathf.Max(0f, shotIntervalSeconds);

        // 1）锁定单发射击间隔：强制 SemiFireCooldown 最终值恒为 shotIntervalSeconds
        float baseSemi = Mathf.Max(0f, _ctx.baseSemiFireCooldown);
        cd.flat = targetInterval - baseSemi;
        cd.addPct = 0f;
        cd.mul = 1f;
        stacks[GunStat.SemiFireCooldown] = cd;

        // 2）把“变慢损失”转为伤害：lossPct = (目标间隔/基准间隔)-1
        // 只有当目标更慢（lossPct>0）才加伤害
        if (_baselineIntervalSeconds > 0f && lossPctToDamageRatio > 0f)
        {
            float lossPct = (targetInterval / _baselineIntervalSeconds) - 1f;
            if (lossPct > 0f)
            {
                dmg.addPct += lossPct * lossPctToDamageRatio;
            }
        }

        // 3）可选：将最终 FireRate.addPct 转为 Damage.addPct（用于后续拿到攻速 Perk）
        if (fireRateToDamageRatio > 0f)
        {
            float converted = fr.addPct * fireRateToDamageRatio;

            if (convertPositiveOnly)
            {
                if (converted > 0f) dmg.addPct += converted;
            }
            else
            {
                if (Mathf.Abs(converted) > 0.000001f) dmg.addPct += converted;
            }
        }

        stacks[GunStat.Damage] = dmg;

        // 4）清空 FireRate 栈：确保攻速永远不影响射击间隔
        fr.flat = 0f;
        fr.addPct = 0f;
        fr.mul = 1f;
        stacks[GunStat.FireRate] = fr;
    }

    private void Resolve()
    {
        if (_gun != null && _ctx != null) return;

        _gun = GetComponentInParent<CameraGunChannel>();
        if (_gun != null)
        {
            _ctx = _gun.GetComponent<GunStatContext>();
            if (_ctx == null) _ctx = _gun.GetComponentInParent<GunStatContext>();
            if (_ctx != null) return;
        }

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