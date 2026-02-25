using UnityEngine;

/// <summary>
/// Perk：自动识别换弹方式并交替增强
///
/// 弹匣模式（Magazine）：
/// - 第 2/4/6... 次换弹：换弹时间 * magazineTimeMultiplierOnEvenReload
///
/// 遂发装填（PerBullet）：
/// - 在一次装填过程中，每次插弹动作交替：+1、+2、+1、+2...
/// - 例如从 0 开始：0-1-3-4-6-7-9...
/// </summary>
public sealed class Perk_AlternateReloadBehavior : MonoBehaviour
{
    [Header("弹匣换弹：偶数次换弹时间倍率")]
    [Min(0.01f)]
    [Tooltip("0.5 表示偶数次换弹耗时减半")]
    public float magazineTimeMultiplierOnEvenReload = 0.5f;

    [Header("遂发装填：交替插弹数量")]
    [Min(1)]
    [Tooltip("奇数步插弹数量（默认 1）")]
    public int perBulletOddStepCount = 1;

    [Min(1)]
    [Tooltip("偶数步插弹数量（默认 2）")]
    public int perBulletEvenStepCount = 2;

    [Header("调试")]
    public bool logDebug = false;

    private CameraGunChannel _gun;
    private GunAmmo _ammo;

    // 弹匣模式：第几次换弹（1,2,3,4...）
    private int _magReloadCount = 0;
    private float _reloadTimeMagazine0;
    private bool _patchedMagazineThisReload;

    // 遂发装填：本次装填内的“插弹步数”（1,2,3,4...）
    private int _perBulletStep = 0;
    private bool _hookedInsert;

    private void OnEnable()
    {
        _gun = GetComponentInParent<CameraGunChannel>();
        if (_gun == null)
        {
            Debug.LogWarning("[Perk_AlternateReloadBehavior] 未找到 CameraGunChannel，请确保 Perk 挂在枪层级下。");
            return;
        }

        _ammo = _gun.ammo;
        if (_ammo == null) _ammo = _gun.GetComponentInChildren<GunAmmo>(true);

        if (_ammo == null)
        {
            Debug.LogWarning("[Perk_AlternateReloadBehavior] 未找到 GunAmmo（CameraGunChannel.ammo 为空且子级也没有）。");
            return;
        }

        _ammo.OnReloadStart += OnReloadStart;
        _ammo.OnReloadEnd += OnReloadEnd;
    }

    private void OnDisable()
    {
        if (_ammo != null)
        {
            _ammo.OnReloadStart -= OnReloadStart;
            _ammo.OnReloadEnd -= OnReloadEnd;

            // 解除遂发插弹钩子
            UnhookPerBullet();
            // 还原弹匣换弹参数（如果正处于被修改状态）
            RestoreMagazineIfNeeded();
        }

        _ammo = null;
        _gun = null;
    }

    private void OnReloadStart()
    {
        if (_ammo == null) return;

        if (_ammo.reloadType == GunAmmo.ReloadType.Magazine)
        {
            UnhookPerBullet();
            HandleMagazineReloadStart();
        }
        else
        {
            RestoreMagazineIfNeeded();
            HandlePerBulletReloadStart();
        }
    }

    private void OnReloadEnd()
    {
        // 换弹结束时：
        // - 弹匣：还原 reloadTimeMagazine
        // - 遂发：解除插弹钩子
        RestoreMagazineIfNeeded();
        UnhookPerBullet();
    }

    // =========================
    // 弹匣换弹：偶数次减半
    // =========================
    private void HandleMagazineReloadStart()
    {
        _magReloadCount++;
        bool isEven = (_magReloadCount % 2) == 0;

        _reloadTimeMagazine0 = _ammo.reloadTimeMagazine;
        _patchedMagazineThisReload = false;

        if (isEven)
        {
            _ammo.reloadTimeMagazine = _reloadTimeMagazine0 * Mathf.Max(0.01f, magazineTimeMultiplierOnEvenReload);
            _patchedMagazineThisReload = true;

            if (logDebug)
                Debug.Log($"[Perk_AlternateReloadBehavior] Magazine 偶数次换弹：{_reloadTimeMagazine0:0.###} -> {_ammo.reloadTimeMagazine:0.###}");
        }
        else
        {
            if (logDebug)
                Debug.Log("[Perk_AlternateReloadBehavior] Magazine 奇数次换弹：不修改时间");
        }
    }

    private void RestoreMagazineIfNeeded()
    {
        if (_ammo == null) return;
        if (!_patchedMagazineThisReload) return;

        _ammo.reloadTimeMagazine = _reloadTimeMagazine0;
        _patchedMagazineThisReload = false;

        if (logDebug)
            Debug.Log("[Perk_AlternateReloadBehavior] Magazine 换弹结束：已还原 reloadTimeMagazine");
    }

    // =========================
    // 遂发装填：每次插弹步交替 1/2
    // =========================
    private void HandlePerBulletReloadStart()
    {
        // 每次进入遂发装填，步数从 0 开始重新计
        _perBulletStep = 0;

        if (!_hookedInsert)
        {
            _ammo.OnQueryInsertCount += OnQueryInsertCount;
            _hookedInsert = true;
        }

        if (logDebug)
            Debug.Log("[Perk_AlternateReloadBehavior] PerBullet 开始：已启用插弹 1/2 交替");
    }

    private void UnhookPerBullet()
    {
        if (_ammo == null) return;
        if (!_hookedInsert) return;

        _ammo.OnQueryInsertCount -= OnQueryInsertCount;
        _hookedInsert = false;
        _perBulletStep = 0;

        if (logDebug)
            Debug.Log("[Perk_AlternateReloadBehavior] PerBullet 结束：已解除插弹交替钩子");
    }

    private void OnQueryInsertCount(GunAmmo ammo, ref int count)
    {
        // 只在遂发装填时生效
        if (ammo == null) return;
        if (ammo.reloadType != GunAmmo.ReloadType.PerBullet) return;
        if (!ammo.IsReloading) return;

        _perBulletStep++;

        bool isEvenStep = (_perBulletStep % 2) == 0;
        count = isEvenStep ? Mathf.Max(1, perBulletEvenStepCount) : Mathf.Max(1, perBulletOddStepCount);

        if (logDebug)
            Debug.Log($"[Perk_AlternateReloadBehavior] PerBullet 第 {_perBulletStep} 步插弹：count={count}");
    }
}