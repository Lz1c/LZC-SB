using UnityEngine;
using PrototypeFPC;

/// <summary>
/// 主动技能 Perk（FPC版本）：
/// - 由 AbilityKeyEmitter 触发
/// - 激活后提升玩家 walkSpeed（持续一段时间，结束后还原）
/// - 持续期间击杀敌人回血
/// </summary>
public sealed class Perk_Ability_MoveSpeedAndHealOnKill_FPC : MonoBehaviour
{
    [Header("主动技能按键（必须与 AbilityKeyEmitter 的 abilityKey 一致）")]
    public KeyCode abilityKey = KeyCode.F;

    [Header("持续时间")]
    [Min(0.1f)]
    [Tooltip("技能持续时间（秒）")]
    public float duration = 8f;

    [Header("移动速度")]
    [Min(0.01f)]
    [Tooltip("walkSpeed *= 该倍率（例如 1.25 = +25%）")]
    public float walkSpeedMultiplier = 1.25f;

    [Header("击杀回血")]
    [Min(0)]
    [Tooltip("技能持续期间，每次击杀回复多少生命（整数）")]
    public int healOnKill = 10;

    [Header("可选：手动指定玩家 Movement（不指定就找 Tag=Player）")]
    public Movement playerMovement;

    // 由 PerkManager.InstantiatePerkToGun 注入（如果存在）
    [HideInInspector] public int targetGunIndex = -1;

    private PerkManager _pm;
    private CameraGunChannel _gun;
    private PlayerVitals _playerVitals;

    private bool _resolved;          // 是否已解析到 gun / player / vitals
    private bool _active;            // 技能是否激活中
    private float _endTime;          // 技能结束时间

    private bool _savedMove;
    private float _savedWalkSpeed;

    private void Awake()
    {
        _pm = FindFirstObjectByType<PerkManager>();
    }

    private void OnEnable()
    {
        CombatEventHub.OnAbility += HandleAbility;
        CombatEventHub.OnKill += HandleKill;

        // 尽量早解析（但不强求，后续会在 Update 里补）
        TryResolveOnce();
    }

    private void OnDisable()
    {
        CombatEventHub.OnAbility -= HandleAbility;
        CombatEventHub.OnKill -= HandleKill;

        // 关闭时如果技能还在，记得还原移速
        EndAndRestore();

        _resolved = false;
        _gun = null;
        _playerVitals = null;
    }

    private void Update()
    {
        // 防止某些初始化顺序导致 OnEnable 时还没解析到引用
        if (!_resolved)
            TryResolveOnce();

        // 到时间就结束并还原
        if (_active && Time.time >= _endTime)
        {
            EndAndRestore();
        }
    }

    private void HandleAbility(CombatEventHub.AbilityEvent e)
    {
        if (e.key != abilityKey) return;

        // 触发时再尝试一次解析，确保稳定
        if (!_resolved)
            TryResolveOnce();

        if (!_resolved) return;

        ActivateOrRefresh();
    }

    private void HandleKill(CombatEventHub.KillEvent e)
    {
        // 仅技能激活期间回血
        if (!_active) return;
        if (!_resolved) return;
        if (_playerVitals == null) return;
        if (_playerVitals.IsDead) return;

        // 只统计“本枪造成”的击杀（和你现有 Perk_HealOnKill 的默认行为一致）
        if (_gun != null && e.source != _gun) return;

        if (healOnKill <= 0) return;
        _playerVitals.Heal(healOnKill);
    }

    /// <summary>
    /// 激活技能：如果未激活则应用加速；如果已激活则刷新持续时间
    /// </summary>
    private void ActivateOrRefresh()
    {
        _endTime = Time.time + Mathf.Max(0.1f, duration);

        // 已激活：只刷新时间，不重复乘倍率（避免越按越快）
        if (_active) return;

        _active = true;

        // 确保拿到 Movement
        ResolveMovement();
        if (playerMovement == null) return;

        // 缓存并应用
        _savedWalkSpeed = playerMovement.GetWalkSpeed();
        playerMovement.SetWalkSpeed(_savedWalkSpeed * walkSpeedMultiplier);
        _savedMove = true;
    }

    /// <summary>
    /// 结束技能并还原 walkSpeed
    /// </summary>
    private void EndAndRestore()
    {
        if (!_active && !_savedMove) return;

        _active = false;
        _endTime = 0f;

        if (playerMovement != null && _savedMove)
        {
            playerMovement.SetWalkSpeed(_savedWalkSpeed);
        }

        _savedMove = false;
    }

    /// <summary>
    /// 解析 gun + playerVitals（参考你 Perk_ReloadSpeedAndWalkSpeed 的“只在确实被赋予时解析”思路）
    /// </summary>
    private void TryResolveOnce()
    {
        if (_resolved) return;

        // 1) 情况 A：Perk 是枪的子物体（最常见、最稳）
        _gun = GetComponentInParent<CameraGunChannel>();
        if (_gun == null)
        {
            // 2) 情况 B：PerkGiveTest 通过 targetGunIndex 注入
            if (targetGunIndex >= 0)
            {
                _pm ??= FindFirstObjectByType<PerkManager>();
                if (_pm != null)
                {
                    _pm.RefreshAll(force: true);
                    var gunRefs = _pm.GetGun(targetGunIndex);
                    _gun = gunRefs != null ? gunRefs.cameraGunChannel : null;
                }
            }
        }

        // 找不到 gun 就视为“尚未真正赋予”
        if (_gun == null) return;

        // 解析玩家血量
        _playerVitals = _gun.GetComponentInParent<PlayerVitals>();
        if (_playerVitals == null)
            _playerVitals = FindFirstObjectByType<PlayerVitals>();

        // Movement 可以等激活时再找，但这里先尝试一次也没坏处
        ResolveMovement();

        _resolved = true;
    }

    private void ResolveMovement()
    {
        if (playerMovement != null) return;

        var player = GameObject.FindGameObjectWithTag("Player");
        if (player == null) return;

        playerMovement = player.GetComponentInChildren<Movement>(true);
    }
}