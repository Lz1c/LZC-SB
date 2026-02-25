using UnityEngine;
using PrototypeFPC;

public sealed class Perk_ReloadSpeedAndWalkSpeed : MonoBehaviour
{
    [Header("换弹倍率（越小越快）")]
    [Tooltip("遂发装填：每一发装填时间 *= 该倍率")]
    [Min(0.01f)] public float perBulletInsertTimeMultiplier = 0.8f;

    [Tooltip("弹匣换弹：整次换弹时间 *= 该倍率")]
    [Min(0.01f)] public float magazineReloadTimeMultiplier = 0.75f;

    [Header("移动速度倍率（直接改 walkSpeed）")]
    [Tooltip("walkSpeed *= 该倍率")]
    [Min(0.01f)] public float walkSpeedMultiplier = 1.2f;

    [Header("可选：手动指定玩家 Movement（不指定就找 Tag=Player）")]
    public Movement playerMovement;

    // 由 PerkManager.InstantiatePerkToGun 注入（如存在）
    [HideInInspector] public int targetGunIndex = -1;

    private PerkManager _pm;
    private CameraGunChannel _gun;
    private GunAmmo _ammo;

    // 是否已经真正“生效过”（用于避免未赋予时就改数值）
    private bool _applied;

    // 换弹原值缓存
    private bool _savedAmmo;
    private float _savedReloadTimeMagazine;
    private float _savedInsertOneTime;

    // walkSpeed 原值缓存
    private bool _savedMove;
    private float _savedWalkSpeed;

    private void Awake()
    {
        _pm = FindFirstObjectByType<PerkManager>();
    }

    private void OnEnable()
    {
        // 关键：只有在“确认已赋予到枪上”时才执行 Apply
        TryApplyOnce();
    }

    private void Update()
    {
        // 保险：如果启用时还没解析到枪（比如某些初始化顺序），后续每帧再尝试一次
        // 一旦成功会 _applied=true，后面不会重复执行
        if (!_applied)
            TryApplyOnce();
    }

    private void OnDisable()
    {
        // 只有真正生效过，才需要还原
        if (!_applied) return;

        // 还原换弹
        if (_ammo != null && _savedAmmo)
        {
            _ammo.reloadTimeMagazine = _savedReloadTimeMagazine;
            _ammo.insertOneTime = _savedInsertOneTime;
        }

        // 还原 walkSpeed
        if (playerMovement != null && _savedMove)
        {
            playerMovement.SetWalkSpeed(_savedWalkSpeed);
        }

        // 清状态
        _applied = false;
        _savedAmmo = false;
        _savedMove = false;

        _gun = null;
        _ammo = null;
    }

    private void TryApplyOnce()
    {
        if (_applied) return;

        // 1) 先判断：是否真的“已经赋予到枪上”
        //    - 挂在枪下面：能找到父级 CameraGunChannel
        //    - PerkGiveTest 注入：targetGunIndex >= 0 且能从 PerkManager 找到对应枪
        if (!ResolveGunAndAmmo_OnlyIfGranted())
            return;

        // 2) 现在确认已赋予，才开始修改数值
        // ---- 换弹缓存 ----
        if (_ammo != null && !_savedAmmo)
        {
            _savedReloadTimeMagazine = _ammo.reloadTimeMagazine;
            _savedInsertOneTime = _ammo.insertOneTime;
            _savedAmmo = true;
        }

        // ---- 应用换弹倍率（基于原值，不叠乘）----
        ApplyReload();

        // ---- 修改 walkSpeed ----
        ResolveMovement();
        if (playerMovement != null && !_savedMove)
        {
            _savedWalkSpeed = playerMovement.GetWalkSpeed();
            playerMovement.SetWalkSpeed(_savedWalkSpeed * walkSpeedMultiplier);
            _savedMove = true;
        }

        _applied = true;
    }

    private void ApplyReload()
    {
        if (_ammo == null || !_savedAmmo) return;

        // 先还原到原值，避免重复叠乘
        _ammo.reloadTimeMagazine = _savedReloadTimeMagazine;
        _ammo.insertOneTime = _savedInsertOneTime;

        // 自动识别遂发/弹匣
        if (_ammo.reloadType == GunAmmo.ReloadType.PerBullet)
        {
            _ammo.insertOneTime = Mathf.Max(0f, _ammo.insertOneTime * perBulletInsertTimeMultiplier);
        }
        else
        {
            _ammo.reloadTimeMagazine = Mathf.Max(0f, _ammo.reloadTimeMagazine * magazineReloadTimeMultiplier);
        }
    }

    private void ResolveMovement()
    {
        if (playerMovement != null) return;

        var player = GameObject.FindGameObjectWithTag("Player");
        if (player == null) return;

        playerMovement = player.GetComponentInChildren<Movement>(true);
    }

    /// <summary>
    /// 只在“确定已赋予”时才解析枪与弹药。
    /// 如果只是把 Perk prefab 放在场景里（没挂到枪下、也没 targetGunIndex），这里会返回 false。
    /// </summary>
    private bool ResolveGunAndAmmo_OnlyIfGranted()
    {
        // 路径 1：Perk 作为枪的子物体
        _gun = GetComponentInParent<CameraGunChannel>();
        if (_gun != null)
        {
            _ammo = _gun.ammo != null ? _gun.ammo : _gun.GetComponentInParent<GunAmmo>();
            return true;
        }

        // 路径 2：PerkGiveTest 注入 targetGunIndex
        if (targetGunIndex < 0) return false;

        _pm ??= FindFirstObjectByType<PerkManager>();
        if (_pm == null) return false;

        _pm.RefreshAll(force: true);
        var gunRefs = _pm.GetGun(targetGunIndex);
        if (gunRefs == null) return false;

        _gun = gunRefs.cameraGunChannel;
        if (_gun == null) return false;

        _ammo = _gun.ammo != null ? _gun.ammo : _gun.GetComponentInParent<GunAmmo>();
        return true;
    }
}