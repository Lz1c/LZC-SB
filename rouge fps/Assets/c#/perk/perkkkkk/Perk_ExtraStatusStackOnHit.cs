using UnityEngine;

/// <summary>
/// Perk：异常层数+1（最高优先级）
///
/// 作用：任何来源（子弹载荷/异常Perk/未来新增异常Perk）只要调用 StatusContainer.ApplyStatus(req)，
/// 这个 Perk 都会把 req.stacksToAdd 额外 +1。
/// </summary>
public sealed class Perk_StatusExtraStackOnHit : MonoBehaviour, IStatusApplyModifier
{
    [Header("额外层数")]
    [Min(1)]
    [Tooltip("对每次施加的异常额外增加的层数")]
    public int extraStacks = 1;

    [Header("是否只作用于本枪来源")]
    [Tooltip("开启后：仅当 req.source == 本Perk所在枪 时才 +1（避免影响其它枪/敌人系统）")]
    public bool onlyForThisGun = true;

    private CameraGunChannel _sourceGun;

    /// <summary>
    /// 最高优先级：确保它先于其它异常修饰器执行
    /// </summary>
    public int Priority => 100000;

    private void OnEnable()
    {
        _sourceGun = GetComponentInParent<CameraGunChannel>();
        StatusContainer.RegisterApplyModifier(this);
    }

    private void OnDisable()
    {
        StatusContainer.UnregisterApplyModifier(this);
        _sourceGun = null;
    }

    public void Modify(StatusContainer target, ref StatusApplyRequest req)
    {
        int add = Mathf.Max(1, extraStacks);

        if (onlyForThisGun)
        {
            // 如果你希望它只增强“自己这把枪施加的异常”，就做来源过滤
            if (_sourceGun == null) return;
            if (req.source != _sourceGun) return;
        }

        // stacksToAdd 至少为 1，再额外 +add
        int baseStacks = req.stacksToAdd <= 0 ? 1 : req.stacksToAdd;
        req.stacksToAdd = baseStacks + add;
    }
}