using System.Collections.Generic;
using UnityEngine;

public sealed class Perk_DoubleMag_SlowerReload : MonoBehaviour, IGunStatModifier
{
    [Header("Magazine")]
    [Min(0.01f)] public float magazineMultiplier = 2f;

    [Header("Reload")]
    [Min(0.01f)] public float reloadTimeMultiplier = 1.25f;

    public int Priority => 0;

    [HideInInspector] public int targetGunIndex = -1;

    private GunStatContext _ctx;
    private CameraGunChannel _gun;
    private PerkManager _pm;

    private void Awake()
    {
        _pm = FindFirstObjectByType<PerkManager>();
    }

    private void OnEnable()
    {
        ResolveContext();
        if (_ctx != null)
        {
            _ctx.Register(this);
            _ctx.ForceRebuildNow();
        }
    }

    private void OnDisable()
    {
        if (_ctx != null)
        {
            _ctx.Unregister(this);
            _ctx.ForceRebuildNow();
        }

        _ctx = null;
        _gun = null;
    }

    public void ApplyModifiers(CameraGunChannel source, Dictionary<GunStat, StatStack> stacks)
    {
        if (_gun == null) ResolveContext();
        if (_gun == null || source != _gun) return;

        var mag = stacks[GunStat.MagazineSize];
        mag.mul *= Mathf.Max(0.01f, magazineMultiplier);
        stacks[GunStat.MagazineSize] = mag;

        var reload = stacks[GunStat.ReloadTime];
        reload.mul *= Mathf.Max(0.01f, reloadTimeMultiplier);
        stacks[GunStat.ReloadTime] = reload;
    }

    private void ResolveContext()
    {
        if (_ctx != null) return;

        _gun = GetComponentInParent<CameraGunChannel>();
        if (_gun != null)
        {
            _ctx = _gun.GetComponent<GunStatContext>();
            if (_ctx == null) _ctx = _gun.GetComponentInParent<GunStatContext>();
        }

        if (_ctx != null) return;

        _pm ??= FindFirstObjectByType<PerkManager>();
        if (_pm == null || targetGunIndex < 0) return;

        _pm.RefreshAll(force: true);

        var gunRefs = _pm.GetGun(targetGunIndex);
        if (gunRefs == null) return;

        _gun = gunRefs.cameraGunChannel;
        if (_gun == null) return;

        _ctx = _gun.GetComponent<GunStatContext>();
        if (_ctx == null) _ctx = _gun.GetComponentInParent<GunStatContext>();
    }
}