using UnityEngine;

public sealed class Perk_GunStatsOverride : MonoBehaviour
{
    [Header("Override Base Values (GunStatContext)")]

    public bool setDamage = true;
    [Min(0f)] public float damage = 10f;

    public bool setMaxRange = true;
    [Min(0f)] public float maxRange = 200f;

    [Header("Auto 模式：覆盖 baseFireRate")]
    public bool setFireRate = true;
    [Min(0.01f)] public float fireRate = 10f;

    [Header("Semi 模式：覆盖 baseSemiFireCooldown（单发间隔）")]
    public bool setSemiFireCooldown = true;
    [Min(0f)] public float semiFireCooldown = 0.15f;

    public bool setBulletSpeed = true;
    [Min(0.01f)] public float bulletSpeed = 80f;

    [Header("Recoil (optional, direct fields)")]
    public bool setRecoil = false;
    [Min(0f)] public float kickPitchPerShot = 1.2f;
    [Min(0f)] public float kickYawRandom = 0.6f;

    private GunStatContext _ctx;
    private GunRecoil _recoil;
    private CameraGunChannel _gun;

    private struct Saved
    {
        public float baseDamage;
        public float baseFireRate;
        public float baseSemiFireCooldown;
        public float baseBulletSpeed;
        public float baseMaxRange;

        public float kickPitchPerShot;
        public float kickYawRandom;
    }

    private Saved _saved;
    private bool _applied;

    private void OnEnable()
    {
        _gun = GetComponentInParent<CameraGunChannel>();
        if (_gun == null) { enabled = false; return; }

        _ctx = _gun.GetComponent<GunStatContext>();
        if (_ctx == null) _ctx = _gun.GetComponentInParent<GunStatContext>();
        if (_ctx == null) { enabled = false; return; }

        _recoil = _gun.GetComponent<GunRecoil>();
        if (_recoil == null) _recoil = _gun.GetComponentInParent<GunRecoil>();

        // 订阅：运行时 Auto/Semi 切换时立即更新
        _gun.OnFireModeChanged -= HandleFireModeChanged;
        _gun.OnFireModeChanged += HandleFireModeChanged;

        if (!_applied)
        {
            _saved.baseDamage = _ctx.baseDamage;
            _saved.baseFireRate = _ctx.baseFireRate;
            _saved.baseSemiFireCooldown = _ctx.baseSemiFireCooldown;
            _saved.baseBulletSpeed = _ctx.baseBulletSpeed;
            _saved.baseMaxRange = _ctx.baseMaxRange;

            if (_recoil != null)
            {
                _saved.kickPitchPerShot = _recoil.kickPitchPerShot;
                _saved.kickYawRandom = _recoil.kickYawRandom;
            }

            _applied = true;
        }

        ApplyForCurrentMode(forceRebuildNow: true);
    }

    private void OnDisable()
    {
        Revert();
    }

    private void OnDestroy()
    {
        Revert();
    }

    private void HandleFireModeChanged(CameraGunChannel gun, CameraGunChannel.FireMode prev, CameraGunChannel.FireMode next)
    {
        // 只要模式变了，就按“新模式”重新应用覆盖值，确保立刻生效
        ApplyForCurrentMode(forceRebuildNow: true);
    }

    private void ApplyForCurrentMode(bool forceRebuildNow)
    {
        if (_ctx == null || _gun == null) return;

        // 通用覆盖
        if (setDamage) _ctx.baseDamage = Mathf.Max(0f, damage);
        if (setBulletSpeed) _ctx.baseBulletSpeed = Mathf.Max(0.0001f, bulletSpeed);
        if (setMaxRange) _ctx.baseMaxRange = Mathf.Max(0f, maxRange);

        // 根据当前模式覆盖不同“射速”基值
        if (_gun.fireMode == CameraGunChannel.FireMode.Semi)
        {
            if (setSemiFireCooldown)
                _ctx.baseSemiFireCooldown = Mathf.Max(0f, semiFireCooldown);
        }
        else
        {
            if (setFireRate)
                _ctx.baseFireRate = Mathf.Max(0.0001f, fireRate);
        }

        // Recoil 直接写字段
        if (setRecoil && _recoil != null)
        {
            _recoil.kickPitchPerShot = Mathf.Max(0f, kickPitchPerShot);
            _recoil.kickYawRandom = Mathf.Max(0f, kickYawRandom);
        }

        // 立即重算，保证“切模式后立刻生效”
        if (forceRebuildNow) _ctx.ForceRebuildNow();
        else _ctx.MarkDirty();
    }

    private void Revert()
    {
        if (!_applied) return;

        if (_gun != null)
        {
            _gun.OnFireModeChanged -= HandleFireModeChanged;
        }

        if (_ctx != null)
        {
            _ctx.baseDamage = _saved.baseDamage;
            _ctx.baseFireRate = _saved.baseFireRate;
            _ctx.baseSemiFireCooldown = _saved.baseSemiFireCooldown;
            _ctx.baseBulletSpeed = _saved.baseBulletSpeed;
            _ctx.baseMaxRange = _saved.baseMaxRange;

            _ctx.ForceRebuildNow();
        }

        if (_recoil != null)
        {
            _recoil.kickPitchPerShot = _saved.kickPitchPerShot;
            _recoil.kickYawRandom = _saved.kickYawRandom;
        }

        _applied = false;
        _ctx = null;
        _recoil = null;
        _gun = null;
    }
}