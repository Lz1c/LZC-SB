using UnityEngine;

/// <summary>
/// Shotgun-only perk: reduces horizontal (left-right) and vertical (up-down) projectile spread.
/// Works by scaling GunSpread.shotgunHorizontalScale / shotgunVerticalScale.
/// If the gun is not ShotType.Shotgun, this perk auto-disables and does nothing.
/// </summary>
public sealed class Perk_ShotgunTightenSpreadXY : MonoBehaviour
{
    [Header("Multipliers (<1 reduces spread)")]
    [Min(0f)] public float horizontalScale = 0.35f; // left-right
    [Min(0f)] public float verticalScale = 0.35f;   // up-down

    [Header("Behavior")]
    public bool disableSelfIfNotShotgun = true;

    private CameraGunChannel _gun;
    private GunSpread _spread;

    private float _prevH = 1f;
    private float _prevV = 1f;
    private bool _applied;

    private void OnEnable()
    {
        _gun = GetComponentInParent<CameraGunChannel>();
        _spread = _gun != null ? _gun.spread : null;
        if (_spread == null) _spread = GetComponentInParent<GunSpread>();

        if (_gun == null || _spread == null)
        {
            enabled = false;
            return;
        }

        if (_gun.shotType != CameraGunChannel.ShotType.Shotgun)
        {
            if (disableSelfIfNotShotgun) enabled = false;
            return;
        }

        _prevH = _spread.shotgunHorizontalScale;
        _prevV = _spread.shotgunVerticalScale;

        // Multiply so multiple perks can stack predictably if you ever allow that.
        _spread.shotgunHorizontalScale = Mathf.Max(0f, _spread.shotgunHorizontalScale * horizontalScale);
        _spread.shotgunVerticalScale = Mathf.Max(0f, _spread.shotgunVerticalScale * verticalScale);

        _applied = true;
    }

    private void OnDisable()
    {
        if (!_applied) return;
        if (_spread == null) return;

        // Restore previous values (safe even if perk removed)
        _spread.shotgunHorizontalScale = _prevH;
        _spread.shotgunVerticalScale = _prevV;

        _applied = false;
    }
}