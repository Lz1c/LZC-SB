using UnityEngine;

/// <summary>
/// Perk：减少枪械后座
/// 直接修改 GunRecoil 里的 kickPitchPerShot 与 kickYawRandom。
/// </summary>
public sealed class Perk_ReduceGunRecoil : MonoBehaviour
{
    [Header("后座缩减倍率（小于 1 表示减少）")]
    [Min(0.01f)]
    [Tooltip("垂直后座倍率：作用于 kickPitchPerShot")]
    public float pitchMultiplier = 0.7f;

    [Min(0.01f)]
    [Tooltip("水平后座倍率：作用于 kickYawRandom")]
    public float yawMultiplier = 0.7f;

    private CameraGunChannel _gun;
    private GunRecoil _recoil;

    private float _pitch0;
    private float _yaw0;
    private bool _applied;

    private void OnEnable()
    {
        // Perk 挂在枪层级下，直接找
        _gun = GetComponentInParent<CameraGunChannel>();
        if (_gun == null)
        {
            Debug.LogWarning("[Perk_ReduceGunRecoil] 未找到 CameraGunChannel，请确保 Perk 挂在枪层级下。");
            return;
        }

        _recoil = _gun.recoil;
        if (_recoil == null)
        {
            // 兜底：如果没绑引用，尝试从枪物体下面找
            _recoil = _gun.GetComponentInChildren<GunRecoil>(true);
        }

        if (_recoil == null)
        {
            Debug.LogWarning("[Perk_ReduceGunRecoil] 未找到 GunRecoil（CameraGunChannel.recoil 为空且子级也没有）。");
            return;
        }

        Apply();
    }

    private void OnDisable()
    {
        Restore();
        _recoil = null;
        _gun = null;
    }

    private void Apply()
    {
        if (_applied) return;

        _pitch0 = _recoil.kickPitchPerShot;
        _yaw0 = _recoil.kickYawRandom;

        _recoil.kickPitchPerShot = _pitch0 * Mathf.Max(0.01f, pitchMultiplier);
        _recoil.kickYawRandom = _yaw0 * Mathf.Max(0.01f, yawMultiplier);

        _applied = true;
    }

    private void Restore()
    {
        if (!_applied) return;
        if (_recoil == null) return;

        _recoil.kickPitchPerShot = _pitch0;
        _recoil.kickYawRandom = _yaw0;

        _applied = false;
    }
}