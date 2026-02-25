using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using PrototypeFPC;

/// <summary>
/// Perk:
/// 1) Shotgun pellets * 2 (same approach as Perk_HalfPelle: ShotgunPelletsPerShot.postMul)
/// 2) Highest priority (smaller Priority = earlier)
/// 3) After each fire: for X seconds, set player's walk speed to original * N%, then restore
///
/// Notes:
/// - Uses Movement.GetWalkSpeed() / SetWalkSpeed() (same pattern as Perk_Ability_MoveSpeedAndHealOnKill_FPC)
/// - Re-firing refreshes duration without stacking multipliers.
/// </summary>
public sealed class Perk_DoublePellets_SlowAfterFire_FPC : GunPerkModifierBase
{
    [Header("Shotgun only")]
    public bool disableComponentIfNotShotgun = true;

    [Header("Pellets (postMul like Perk_HalfPelle)")]
    [Min(0.01f)] public float pelletsPostMul = 2f;

    [Header("Slow after fire")]
    [Min(0.01f)] public float slowDuration = 0.6f;
    [Range(0.01f, 1f)] public float slowPercent = 0.6f;

    [Header("Optional: assign Movement manually")]
    public Movement playerMovement;

    // Injected by PerkManager.InstantiatePerkToGun (if your pipeline sets it)
    [HideInInspector] public int targetGunIndex = -1;

    [Header("Priority")]
    public int statPriority = -20000;
    public override int Priority => -20000;

    private PerkManager _pm;
    private CameraGunChannel _gun;
    private bool _resolved;

    private bool _active;
    private float _endTime;
    private Coroutine _tickCo;

    private bool _savedMove;
    private float _savedWalkSpeed;

    private void Awake()
    {
        _pm = FindFirstObjectByType<PerkManager>();
    }

    protected override void OnEnable()
    {
        base.OnEnable();

        CombatEventHub.OnFire -= HandleFire;
        CombatEventHub.OnFire += HandleFire;

        TryResolveOnce();

        // If we can resolve immediately, gate non-shotgun now.
        if (_resolved && _gun != null && _gun.shotType != CameraGunChannel.ShotType.Shotgun)
        {
            if (disableComponentIfNotShotgun)
                enabled = false;
        }
    }

    protected override void OnDisable()
    {
        CombatEventHub.OnFire -= HandleFire;

        StopSlowAndRestore();

        _resolved = false;
        _gun = null;

        base.OnDisable();
    }

    private void Update()
    {
        if (!_resolved)
            TryResolveOnce();

        if (_active && Time.time >= _endTime)
            StopSlowAndRestore();
    }

    /// <summary>
    /// Pellets * 2 using ShotgunPelletsPerShot.postMul (same style as Perk_HalfPelle).
    /// </summary>
    public override void ApplyModifiers(CameraGunChannel source, Dictionary<GunStat, StatStack> stacks)
    {
        if (SourceGun == null || source != SourceGun) return;

        if (source.shotType != CameraGunChannel.ShotType.Shotgun)
            return;

        var st = stacks[GunStat.ShotgunPelletsPerShot];
        st.postMul *= Mathf.Max(0.01f, pelletsPostMul);
        stacks[GunStat.ShotgunPelletsPerShot] = st;
    }

    private void HandleFire(CombatEventHub.FireEvent e)
    {
        if (!_resolved)
            TryResolveOnce();

        if (!_resolved || _gun == null) return;

        // Only respond to this gun
        if (e.source != _gun) return;

        // Shotgun-only safety
        if (_gun.shotType != CameraGunChannel.ShotType.Shotgun)
            return;

        ResolveMovement();
        if (playerMovement == null) return;

        // Refresh duration
        _endTime = Time.time + Mathf.Max(0.01f, slowDuration);

        if (!_active)
        {
            _active = true;

            // Cache original speed once per activation window
            _savedWalkSpeed = playerMovement.GetWalkSpeed();
            _savedMove = true;

            float target = Mathf.Max(0.01f, _savedWalkSpeed * Mathf.Clamp(slowPercent, 0.01f, 1f));
            playerMovement.SetWalkSpeed(target);

            // Optional: keep a tiny coroutine alive if you prefer not to rely on Update
            if (_tickCo != null) StopCoroutine(_tickCo);
            _tickCo = StartCoroutine(SlowTick());
        }
        // If already active, we do NOT re-multiply; just extend time.
    }

    private IEnumerator SlowTick()
    {
        while (_active)
        {
            if (Time.time >= _endTime)
            {
                StopSlowAndRestore();
                yield break;
            }
            yield return null;
        }
    }

    private void StopSlowAndRestore()
    {
        if (_tickCo != null)
        {
            StopCoroutine(_tickCo);
            _tickCo = null;
        }

        if (!_active && !_savedMove) return;

        _active = false;
        _endTime = 0f;

        if (playerMovement != null && _savedMove)
        {
            playerMovement.SetWalkSpeed(_savedWalkSpeed);
        }

        _savedMove = false;
    }

    private void TryResolveOnce()
    {
        if (_resolved) return;

        // A) Most common: perk is under the gun
        _gun = GetComponentInParent<CameraGunChannel>();

        // B) Fallback: injected targetGunIndex
        if (_gun == null && targetGunIndex >= 0)
        {
            _pm ??= FindFirstObjectByType<PerkManager>();
            if (_pm != null)
            {
                _pm.RefreshAll(force: true);
                var gunRefs = _pm.GetGun(targetGunIndex);
                _gun = gunRefs != null ? gunRefs.cameraGunChannel : null;
            }
        }

        if (_gun == null) return;

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