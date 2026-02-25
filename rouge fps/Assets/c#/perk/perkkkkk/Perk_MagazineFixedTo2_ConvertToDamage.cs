using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Perk：弹匣容量强制固定为 2；
/// 玩家“本应拥有的弹匣容量”每高出 2 的 1 点，转化为 +1.5% 伤害 addPct。
///
/// 说明：
/// - “本应拥有的弹匣容量” = 其它所有修改 MagazineSize 的 Perk 作用完后的结果（在本 Perk 覆盖之前读取）
/// - 最终弹匣容量永远为 2（通过覆盖 MagazineSize 的 StatStack 实现）
/// - 后续再拿“加弹匣容量”的 Perk：仍然只会变成更多伤害 addPct，而不会让弹匣超过 2
/// </summary>
public sealed class Perk_MagFixed2_ConvertMagToDamageAddPct : MonoBehaviour, IGunStatModifier
{
    [Header("固定弹匣容量")]
    [Min(1)]
    public int fixedMagazineSize = 2;

    [Header("转换倍率")]
    [Min(0f)]
    [Tooltip("每多出 1 点“本应弹匣容量”，转化为多少伤害 addPct（0.015 = 1.5%）")]
    public float damageAddPctPerExtraMag = 0.015f;

    [Header("优先级")]
    [Tooltip("必须大于其它修改弹匣容量/伤害的 Perk，才能读取到最终“本应容量”并在最后强制覆盖为 2")]
    public int priority = 100000;

    public int Priority => priority;

    // 由 PerkManager.InstantiatePerkToGun 注入（如存在）
    [HideInInspector] public int targetGunIndex = -1;

    private PerkManager _pm;
    private CameraGunChannel _gun;
    private GunStatContext _ctx;

    private void Awake()
    {
        _pm = FindFirstObjectByType<PerkManager>();
    }

    private void OnEnable()
    {
        Resolve();
        if (_ctx != null)
        {
            _ctx.Register(this);
            _ctx.MarkDirty();
        }
    }

    private void OnDisable()
    {
        if (_ctx != null)
        {
            _ctx.Unregister(this);
            _ctx.MarkDirty();
        }

        _gun = null;
        _ctx = null;
    }

    public void ApplyModifiers(CameraGunChannel source, Dictionary<GunStat, StatStack> stacks)
    {
        if (_gun == null || _ctx == null) Resolve();
        if (_gun == null || source != _gun) return;

        if (fixedMagazineSize < 1) fixedMagazineSize = 1;
        if (damageAddPctPerExtraMag < 0f) damageAddPctPerExtraMag = 0f;

        // 需要 MagazineSize 和 Damage 两个栈
        if (!stacks.TryGetValue(GunStat.MagazineSize, out var magStack)) return;
        if (!stacks.TryGetValue(GunStat.Damage, out var dmgStack)) return;

        // 读取基础弹匣容量（真实来源：GunStatContext.baseMagazineSize）
        int baseMag = Mathf.Max(1, _ctx.baseMagazineSize);

        // 先计算“本应拥有的弹匣容量”（其它 Perk 都加完之后的结果）
        // 注意：StatStack.Evaluate 返回 float，这里我们四舍五入到 int（更符合“+1弹匣”的直觉）
        int shouldMag = Mathf.RoundToInt(magStack.Evaluate(baseMag));
        if (shouldMag < 0) shouldMag = 0;

        // 计算高出固定容量的部分，并转换成伤害 addPct
        int extra = Mathf.Max(0, shouldMag - fixedMagazineSize);
        float bonusAddPct = extra * damageAddPctPerExtraMag;

        if (bonusAddPct > 0f)
        {
            dmgStack.addPct += bonusAddPct;
            stacks[GunStat.Damage] = dmgStack;
        }

        // 最后强制覆盖弹匣容量为 fixedMagazineSize：
        // 根据 Evaluate： (base + flat) * (1 + (addPct + (mul-1)))
        // 我们让倍率部分为 1：addPct=0, mul=1
        // 然后用 flat 把 base 推到 fixed：flat = fixed - base
        magStack.flat = fixedMagazineSize - baseMag;
        magStack.addPct = 0f;
        magStack.mul = 1f;
        stacks[GunStat.MagazineSize] = magStack;
    }

    private void Resolve()
    {
        if (_gun != null && _ctx != null) return;

        // 1) perk 挂在枪下面时，直接从父级找到
        _gun = GetComponentInParent<CameraGunChannel>();
        if (_gun != null)
        {
            _ctx = _gun.GetComponent<GunStatContext>();
            if (_ctx == null) _ctx = _gun.GetComponentInParent<GunStatContext>();
            if (_ctx != null) return;
        }

        // 2) PerkGiveTest 路径：通过 PerkManager + targetGunIndex
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