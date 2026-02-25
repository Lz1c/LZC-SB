using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public sealed class TimedGunStatModifier : MonoBehaviour, IGunStatModifier
{
    [Header("Target")]
    [HideInInspector] public int targetGunIndex = -1;

    [Header("Duration (seconds)")]
    [Min(0.01f)] public float duration = 2f;

    [Header("Bonuses (addPct form)")]
    public float damageAddPct = 0f;
    public float fireRateAddPct = 0f;
    public float bulletSpeedAddPct = 0f;
    public float maxRangeAddPct = 0f;

    [Header("Flat")]
    public float damageFlat = 0f;
    public float fireRateFlat = 0f;
    public float bulletSpeedFlat = 0f;
    public float maxRangeFlat = 0f;

    [Header("Order")]
    public int priority = 0;
    public int Priority => priority;

    private PerkManager _pm;
    private CameraGunChannel _gun;
    private GunStatContext _ctx;
    private bool _registered;
    private Coroutine _life;

    private void Awake()
    {
        _pm = FindFirstObjectByType<PerkManager>();
    }

    private void OnEnable()
    {
        TryBindAndRegister();
        if (!_registered) StartCoroutine(BindNextFrames());

        _life = StartCoroutine(Life());
    }

    private void OnDisable()
    {
        if (_life != null)
        {
            StopCoroutine(_life);
            _life = null;
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

    private IEnumerator Life()
    {
        yield return new WaitForSeconds(duration);
        Destroy(gameObject);
    }

    public void ApplyModifiers(CameraGunChannel source, Dictionary<GunStat, StatStack> stacks)
    {
        if (_gun == null || source != _gun) return;

        ApplyOne(GunStat.Damage, damageFlat, damageAddPct, stacks);
        ApplyOne(GunStat.FireRate, fireRateFlat, fireRateAddPct, stacks);
        ApplyOne(GunStat.BulletSpeed, bulletSpeedFlat, bulletSpeedAddPct, stacks);
        ApplyOne(GunStat.MaxRange, maxRangeFlat, maxRangeAddPct, stacks);
    }

    private static void ApplyOne(GunStat stat, float flat, float addPct, Dictionary<GunStat, StatStack> stacks)
    {
        var st = stacks[stat];
        st.flat += flat;
        st.addPct += addPct;
        st.mul = 1f; // keep unified
        stacks[stat] = st;
    }

    private IEnumerator BindNextFrames()
    {
        for (int i = 0; i < 10 && !_registered; i++)
        {
            yield return null;
            TryBindAndRegister();
        }
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
        _gun = GetComponentInParent<CameraGunChannel>();
        if (_gun != null)
        {
            _ctx = _gun.GetComponent<GunStatContext>() ?? _gun.GetComponentInParent<GunStatContext>();
            if (_ctx != null) return;
        }

        _pm ??= FindFirstObjectByType<PerkManager>();
        if (_pm == null || targetGunIndex < 0) return;

        _pm.RefreshAll(force: true);
        var gunRefs = _pm.GetGun(targetGunIndex);
        if (gunRefs == null) return;

        _gun = gunRefs.cameraGunChannel;
        if (_gun == null) return;

        _ctx = _gun.GetComponent<GunStatContext>() ?? _gun.GetComponentInParent<GunStatContext>();
    }
}