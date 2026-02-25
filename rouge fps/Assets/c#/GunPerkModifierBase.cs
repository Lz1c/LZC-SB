using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Base class for perks that modify gun stats through GunStatContext.
/// Robust against Instantiate/SetParent timing: it will auto-rebind when parent changes.
/// </summary>
public abstract class GunPerkModifierBase : MonoBehaviour, IGunStatModifier
{
    public virtual int Priority => 0;

    private GunStatContext _ctx;
    protected CameraGunChannel SourceGun { get; private set; }

    protected virtual void OnEnable()
    {
        TryBind();
    }

    protected virtual void OnDisable()
    {
        Unbind();
    }

    protected virtual void OnDestroy()
    {
        Unbind();
    }

    private void OnTransformParentChanged()
    {
        // Parent changed (moved under a gun, or moved away). Rebind.
        if (!isActiveAndEnabled) return;
        TryBind();
    }

    private void LateUpdate()
    {
        // Covers the common pattern: enabled this frame, parent assigned later this frame.
        if (SourceGun == null) TryBind();
    }

    private void TryBind()
    {
        var newGun = GetComponentInParent<CameraGunChannel>();

        // No change & already bound.
        if (newGun == SourceGun && _ctx != null) return;

        // If gun changed or we were previously bound, unbind first.
        if (_ctx != null || SourceGun != null) Unbind();

        SourceGun = newGun;

        if (SourceGun == null) return;

        _ctx = SourceGun.GetComponent<GunStatContext>();
        if (_ctx == null) _ctx = SourceGun.GetComponentInParent<GunStatContext>();

        if (_ctx != null) _ctx.Register(this);
    }

    private void Unbind()
    {
        if (_ctx != null) _ctx.Unregister(this);
        _ctx = null;
        SourceGun = null;
    }

    public abstract void ApplyModifiers(CameraGunChannel source, Dictionary<GunStat, StatStack> stacks);
}