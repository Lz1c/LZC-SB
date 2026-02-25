using UnityEngine;

/// <summary>
/// Perk: registers a single ADS zoom percent (ADS FOV = hipFov * percent) into AdsZoomPercentManager.
/// Wheel zoom and range selection are handled by AimScopeController.
/// </summary>
public sealed class Perk_ADSFovPercentOfHip_FPC : MonoBehaviour
{
    [Range(0.05f, 1f)]
    public float adsPercentOfHip = 0.6f;

    public AimScopeController aimScope;

    [HideInInspector] public int targetGunIndex = -1;

    private PerkManager _pm;
    private CameraGunChannel _gun;
    private bool _resolved;

    private void Awake()
    {
        _pm = FindFirstObjectByType<PerkManager>();
    }

    private void OnEnable()
    {
        TryResolveOnce();
        if (!_resolved || aimScope == null) return;

        AdsZoomPercentManager.Register(aimScope, this, adsPercentOfHip);
    }

    private void OnDisable()
    {
        if (aimScope != null)
            AdsZoomPercentManager.Unregister(aimScope, this);

        _resolved = false;
        _gun = null;
    }

    private void Update()
    {
        // Handle init order and runtime tuning
        if (!_resolved)
        {
            TryResolveOnce();
            if (_resolved && aimScope != null)
                AdsZoomPercentManager.Register(aimScope, this, adsPercentOfHip);
        }
        else
        {
            if (aimScope != null)
                AdsZoomPercentManager.UpdateValue(aimScope, this, adsPercentOfHip);
        }
    }

    private void TryResolveOnce()
    {
        if (_resolved) return;

        _gun = GetComponentInParent<CameraGunChannel>();

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

        if (aimScope == null)
        {
            aimScope = _gun.GetComponentInParent<AimScopeController>();
            if (aimScope == null)
                aimScope = FindFirstObjectByType<AimScopeController>();
        }

        if (aimScope == null) return;

        _resolved = true;
    }
}