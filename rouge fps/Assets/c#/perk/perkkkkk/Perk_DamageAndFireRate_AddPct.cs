using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public sealed class Perk_DamageAndFireRate_AddPct : MonoBehaviour, IGunStatModifier
{
    [Header("Damage")]
    [Tooltip("Damage bonus in addPct form. 0.2 = +20% damage.")]
    [Range(-0.99f, 10f)] public float damageAddPct = 0.2f;

    [Header("Fire Rate")]
    [Tooltip("Fire rate bonus in addPct form. 0.15 = +15% fire rate.")]
    [Range(-0.99f, 10f)] public float fireRateAddPct = 0.15f;

    [Header("Order")]
    [Tooltip("Lower runs first, higher runs later.")]
    public int priority = 0;

    public int Priority => priority;

    // Injected by PerkManager.InstantiatePerkToGun (may be set after OnEnable)
    [HideInInspector] public int targetGunIndex = -1;

    private PerkManager _pm;
    private CameraGunChannel _gun;
    private GunStatContext _ctx;

    private bool _registered;
    private Coroutine _bindRoutine;

    private void Awake()
    {
        _pm = FindFirstObjectByType<PerkManager>();
    }

    private void OnEnable()
    {
        TryBindAndRegister();

        if (!_registered)
            _bindRoutine = StartCoroutine(BindNextFrames());
    }

    private void OnDisable()
    {
        if (_bindRoutine != null)
        {
            StopCoroutine(_bindRoutine);
            _bindRoutine = null;
        }

        if (_registered && _ctx != null)
        {
            _ctx.Unregister(this);
            _ctx.ForceRebuildNow();
        }

        _registered = false;
        _ctx = null;
        _gun = null;
    }

    public void ApplyModifiers(CameraGunChannel source, Dictionary<GunStat, StatStack> stacks)
    {
        if (_gun == null || source != _gun) return;

        // Damage: add to Damage.addPct
        var dmg = stacks[GunStat.Damage];
        dmg.addPct += damageAddPct;
        dmg.mul = 1f; // rule: avoid mul stacking
        stacks[GunStat.Damage] = dmg;

        // Fire speed:
        // - Auto: modify FireRate (gates _nextFireTime)
        // - Semi: modify SemiFireCooldown (gates _nextSemiAllowedTime)
        // Note: a positive fireRateAddPct should make Semi cooldown shorter.
        // Our StatStack evaluates (base + flat) * (1 + combined), so we apply a negative addPct.
        if (source.fireMode == CameraGunChannel.FireMode.Semi)
        {
            var cd = stacks[GunStat.SemiFireCooldown];
            cd.addPct -= fireRateAddPct;
            cd.mul = 1f;
            stacks[GunStat.SemiFireCooldown] = cd;
        }
        else
        {
            var fr = stacks[GunStat.FireRate];
            fr.addPct += fireRateAddPct;
            fr.mul = 1f;
            stacks[GunStat.FireRate] = fr;
        }
    }

    private IEnumerator BindNextFrames()
    {
        for (int i = 0; i < 10 && !_registered; i++)
        {
            yield return null;
            TryBindAndRegister();
        }

        _bindRoutine = null;
    }

    private void TryBindAndRegister()
    {
        if (_registered) return;

        Resolve();
        if (_ctx == null) return;

        _ctx.Register(this);
        _ctx.ForceRebuildNow();
        _registered = true;
    }

    private void Resolve()
    {
        if (_ctx != null && _gun != null) return;

        // 1) If instantiated under gun
        _gun = GetComponentInParent<CameraGunChannel>();
        if (_gun != null)
        {
            _ctx = _gun.GetComponent<GunStatContext>() ?? _gun.GetComponentInParent<GunStatContext>();
            if (_ctx != null) return;
        }

        // 2) PerkGiveTest path: resolve via PerkManager + targetGunIndex
        _pm ??= FindFirstObjectByType<PerkManager>();
        if (_pm == null) return;
        if (targetGunIndex < 0) return;

        _pm.RefreshAll(force: true);
        var gunRefs = _pm.GetGun(targetGunIndex);
        if (gunRefs == null) return;

        _gun = gunRefs.cameraGunChannel;
        if (_gun == null) return;

        _ctx = _gun.GetComponent<GunStatContext>() ?? _gun.GetComponentInParent<GunStatContext>();
    }
}