using UnityEngine;

/// <summary>
/// Perk：减少子弹散布
/// 
/// 作用：降低 GunSpread 的散布相关参数，让枪更精准。
/// 用法：做成 Perk prefab，PerkGiveTest 实例化到枪下面即可。
/// </summary>
public sealed class Perk_ReduceSpread : MonoBehaviour
{
    [Header("缩减倍率（小于 1 表示减少）")]
    [Min(0.01f)]
    [Tooltip("基础散布倍率，例如 0.8 表示基础散布减少 20%")]
    public float baseSpreadMultiplier = 0.8f;

    [Min(0.01f)]
    [Tooltip("每发增加散布倍率，例如 0.7 表示每发增长减少 30%")]
    public float perShotIncreaseMultiplier = 0.7f;

    [Min(0.01f)]
    [Tooltip("最大散布倍率，例如 0.85 表示最大散布减少 15%")]
    public float maxSpreadMultiplier = 0.85f;

    [Header("可选：霰弹枪额外散布是否也缩减")]
    public bool affectShotgunExtraSpread = true;

    [Min(0.01f)]
    [Tooltip("霰弹枪额外散布倍率（仅 affectShotgunExtraSpread 为 true 时生效）")]
    public float shotgunExtraSpreadMultiplier = 0.9f;

    private GunSpread _spread;

    // 缓存原值，便于移除时还原
    private float _baseSpread0;
    private float _increase0;
    private float _maxSpread0;
    private float _shotgunExtra0;

    private bool _applied;

    private void OnEnable()
    {
        // Perk 通常挂在枪层级下，直接从父级找 GunSpread
        _spread = GetComponentInParent<GunSpread>();

        if (_spread == null)
        {
            Debug.LogWarning("[Perk_ReduceSpread] 未在父级找到 GunSpread，该 Perk 不会生效。");
            return;
        }

        Apply();
    }

    private void OnDisable()
    {
        Restore();
        _spread = null;
    }

    /// <summary>
    /// 应用散布缩减（乘法）
    /// </summary>
    private void Apply()
    {
        if (_spread == null) return;
        if (_applied) return;

        // 缓存原值
        _baseSpread0 = _spread.baseSpread;
        _increase0 = _spread.spreadIncreasePerShot;
        _maxSpread0 = _spread.maxSpread;
        _shotgunExtra0 = _spread.shotgunExtraSpread;

        // 应用倍率（确保不为 0）
        _spread.baseSpread = _baseSpread0 * Mathf.Max(0.01f, baseSpreadMultiplier);
        _spread.spreadIncreasePerShot = _increase0 * Mathf.Max(0.01f, perShotIncreaseMultiplier);
        _spread.maxSpread = _maxSpread0 * Mathf.Max(0.01f, maxSpreadMultiplier);

        if (affectShotgunExtraSpread)
        {
            _spread.shotgunExtraSpread = _shotgunExtra0 * Mathf.Max(0.01f, shotgunExtraSpreadMultiplier);
        }

        _applied = true;
    }

    /// <summary>
    /// 还原散布参数
    /// </summary>
    private void Restore()
    {
        if (_spread == null) return;
        if (!_applied) return;

        _spread.baseSpread = _baseSpread0;
        _spread.spreadIncreasePerShot = _increase0;
        _spread.maxSpread = _maxSpread0;
        _spread.shotgunExtraSpread = _shotgunExtra0;

        _applied = false;
    }
}