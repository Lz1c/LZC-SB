using UnityEngine;

public sealed class Perk_HeadshotMarkedChanceRestoreAmmo : MonoBehaviour
{
    [Header("Trigger")]
    [Tooltip("Only triggers on headshot hits.")]
    public bool requireHeadshot = true;

    [Tooltip("If true, requires active MarkStatus on the target. If false, StatusContainer Mark stacks > 0 also counts.")]
    public bool requireActiveMarkStatus = true;

    [Header("Chance & Amount")]
    [Tooltip("Chance to restore ammo on a valid hit. 0.25 = 25%.")]
    [Range(0f, 1f)] public float restoreChance = 0.25f;

    [Tooltip("How many bullets to restore into the current magazine per proc.")]
    [Min(1)] public int restoreAmount = 1;

    [Header("Limits")]
    [Tooltip("If true, cannot exceed magazineSize.")]
    public bool clampToMagazineSize = true;

    [Tooltip("If true, will not trigger while reloading.")]
    public bool blockWhileReloading = true;

    [Header("Debug")]
    public bool logProc = false;

    // Set by PerkManager.InstantiatePerkToGun via reflection (if present).
    [HideInInspector] public int targetGunIndex = -1;

    private CameraGunChannel _gun;
    private GunAmmo _ammo;
    private PerkManager _pm;

    private void Awake()
    {
        _pm = FindFirstObjectByType<PerkManager>();
    }

    private void OnEnable()
    {
        _gun = ResolveGunChannel();
        _ammo = ResolveAmmo(_gun);
        CombatEventHub.OnHit += HandleHit;
    }

    private void OnDisable()
    {
        CombatEventHub.OnHit -= HandleHit;
        _gun = null;
        _ammo = null;
    }

    private void HandleHit(CombatEventHub.HitEvent e)
    {
        if (e.source == null || e.target == null) return;

        if (_gun == null) _gun = ResolveGunChannel();
        if (_gun == null) return;

        if (e.source != _gun) return;

        if (requireHeadshot && !e.isHeadshot) return;

        if (!IsTargetMarked(e.target)) return;

        _ammo ??= ResolveAmmo(_gun);
        if (_ammo == null) return;

        if (blockWhileReloading && _ammo.IsReloading) return;

        if (restoreChance <= 0f) return;
        if (Random.value > restoreChance) return;

        int before = _ammo.ammoInMag;
        int add = Mathf.Max(1, restoreAmount);

        int after = before + add;
        if (clampToMagazineSize)
            after = Mathf.Min(after, _ammo.magazineSize);

        if (after == before) return;

        _ammo.ammoInMag = after;

        // Notify UI via public event (NotifyAmmo is private)
        _ammo.OnAmmoChanged?.Invoke(_ammo.ammoInMag, _ammo.ammoReserve);

        if (logProc)
            Debug.Log($"[Perk_HeadshotMarkedChanceRestoreAmmo] Proc: +{after - before} ammo (now {_ammo.ammoInMag}/{_ammo.magazineSize})");
    }

    private bool IsTargetMarked(GameObject target)
    {
        if (target == null) return false;

        if (requireActiveMarkStatus)
        {
            var ms = target.GetComponent<MarkStatus>();
            if (ms == null) ms = target.GetComponentInParent<MarkStatus>();
            return ms != null && ms.IsActive;
        }

        var sc = GetStatusContainer(target);
        return sc != null && sc.GetStacks(StatusType.Mark) > 0;
    }

    private static StatusContainer GetStatusContainer(GameObject go)
    {
        if (go == null) return null;
        var sc = go.GetComponent<StatusContainer>();
        if (sc == null) sc = go.GetComponentInParent<StatusContainer>();
        return sc;
    }

    private static GunAmmo ResolveAmmo(CameraGunChannel gun)
    {
        if (gun == null) return null;
        if (gun.ammo != null) return gun.ammo;

        var a = gun.GetComponent<GunAmmo>();
        if (a != null) return a;

        return gun.GetComponentInParent<GunAmmo>();
    }

    private CameraGunChannel ResolveGunChannel()
    {
        // 1) parent chain
        var inParent = GetComponentInParent<CameraGunChannel>();
        if (inParent != null) return inParent;

        // 2) root children
        Transform root = transform;
        while (root.parent != null) root = root.parent;

        var inChildren = root.GetComponentInChildren<CameraGunChannel>(true);
        if (inChildren != null) return inChildren;

        // 3) via PerkManager gun index
        _pm ??= FindFirstObjectByType<PerkManager>();
        if (_pm != null && targetGunIndex >= 0)
        {
            _pm.RefreshAll(force: true);
            var gunRefs = _pm.GetGun(targetGunIndex);
            if (gunRefs != null && gunRefs.cameraGunChannel != null)
                return gunRefs.cameraGunChannel;
        }

        return null;
    }
}