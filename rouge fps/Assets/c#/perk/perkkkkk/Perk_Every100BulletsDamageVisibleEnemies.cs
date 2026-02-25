using UnityEngine;

public sealed class Perk_Every100BulletsDamageVisibleEnemies : MonoBehaviour
{
    [Header("Trigger")]
    [Tooltip("How many bullets (pellets included) are required to trigger once.")]
    [Min(1)] public int bulletsPerTrigger = 100;

    [Header("Damage")]
    [Tooltip("Damage dealt to each visible enemy when triggered.")]
    [Min(0f)] public float damagePerEnemy = 25f;

    [Tooltip("If true, do not raise OnHit again for the perk damage (avoids recursion).")]
    public bool skipHitEventForPerkDamage = true;

    [Tooltip("If true, do not show hit UI for perk damage.")]
    public bool hideHitUI = true;

    [Header("Targeting")]
    [Tooltip("Max enemies processed per trigger to cap performance.")]
    [Min(1)] public int maxTargetsPerTrigger = 64;

    [Tooltip("If true, requires line-of-sight (raycast) from camera to enemy center.")]
    public bool requireLineOfSight = false;

    [Tooltip("Raycast mask used for LOS test when requireLineOfSight=true.")]
    public LayerMask losMask = ~0;

    [Header("View")]
    [Tooltip("Which camera to use for 'in view'. If null, uses Camera.main.")]
    public Camera viewCameraOverride;

    [Tooltip("If true, targets must be within camera viewport (0..1) and in front (z>0).")]
    public bool requireInViewport = true;

    [Header("Armor First")]
    [Tooltip("Armor damage multiplier applied through BulletArmorPayload (1 = normal).")]
    [Min(0f)] public float armorDamageMultiplier = 1f;

    // Set by PerkManager.InstantiatePerkToGun via reflection (if present).
    [HideInInspector] public int targetGunIndex = -1;

    private CameraGunChannel _gun;
    private PerkManager _pm;

    private int _bulletCounter;

    // Shared armor payload instance (created at runtime, no inspector refs)
    private BulletArmorPayload _armorPayload;

    private void Awake()
    {
        _pm = FindFirstObjectByType<PerkManager>();
    }

    private void OnEnable()
    {
        _gun = ResolveGunChannel();
        EnsureArmorPayload();
        CombatEventHub.OnFire += HandleFire;
    }

    private void OnDisable()
    {
        CombatEventHub.OnFire -= HandleFire;
        _gun = null;

        if (_armorPayload != null)
        {
            Destroy(_armorPayload.gameObject);
            _armorPayload = null;
        }
    }

    private void HandleFire(CombatEventHub.FireEvent e)
    {
        if (e.source == null) return;

        if (_gun == null) _gun = ResolveGunChannel();
        if (_gun == null) return;

        if (e.source != _gun) return;

        int pellets = Mathf.Max(1, e.pellets);
        _bulletCounter += pellets;

        int threshold = Mathf.Max(1, bulletsPerTrigger);
        while (_bulletCounter >= threshold)
        {
            _bulletCounter -= threshold;
            TriggerDamage();
        }
    }

    private void TriggerDamage()
    {
        var cam = viewCameraOverride != null ? viewCameraOverride : Camera.main;
        if (cam == null) return;

        EnsureArmorPayload();

        // Find enemies via MonsterHealth (exists in your project even if not in the zip list)
        var enemies = FindObjectsByType<MonsterHealth>(FindObjectsSortMode.None);
        if (enemies == null || enemies.Length == 0) return;

        int applied = 0;

        for (int i = 0; i < enemies.Length; i++)
        {
            if (applied >= maxTargetsPerTrigger) break;

            var mh = enemies[i];
            if (mh == null || mh.IsDead) continue;

            GameObject enemyGO = mh.gameObject;
            if (enemyGO == null) continue;

            // get a collider to apply hit
            var col = enemyGO.GetComponentInChildren<Collider>();
            if (col == null) continue;

            Vector3 point = col.bounds.center;

            if (requireInViewport && !IsInView(cam, point))
                continue;

            if (requireLineOfSight && !HasLOS(cam, point, col))
                continue;

            var info = new DamageInfo
            {
                source = _gun,
                damage = Mathf.Max(0f, damagePerEnemy),
                isHeadshot = false,
                hitPoint = point,
                hitCollider = col,
                flags = skipHitEventForPerkDamage ? DamageFlags.SkipHitEvent : DamageFlags.None
            };

            DamageResolver.ApplyHit(
                info,
                col,
                point,
                _gun,
                armorPayload: _armorPayload,
                statusPayload: null,
                showHitUI: !hideHitUI
            );

            applied++;
        }
    }

    private static bool IsInView(Camera cam, Vector3 worldPoint)
    {
        Vector3 v = cam.WorldToViewportPoint(worldPoint);
        if (v.z <= 0f) return false;
        return v.x >= 0f && v.x <= 1f && v.y >= 0f && v.y <= 1f;
    }

    private bool HasLOS(Camera cam, Vector3 worldPoint, Collider targetCol)
    {
        Vector3 origin = cam.transform.position;
        Vector3 dir = worldPoint - origin;
        float dist = dir.magnitude;
        if (dist <= 0.001f) return true;

        dir /= dist;

        if (Physics.Raycast(origin, dir, out var hit, dist, losMask, QueryTriggerInteraction.Ignore))
        {
            // LOS is valid only if the first hit is the target (or its child)
            return hit.collider == targetCol || hit.collider.transform.IsChildOf(targetCol.transform);
        }

        return true;
    }

    private void EnsureArmorPayload()
    {
        if (_armorPayload != null) return;

        var go = new GameObject("_PerkArmorPayload_Every100");
        go.hideFlags = HideFlags.HideAndDontSave;

        _armorPayload = go.AddComponent<BulletArmorPayload>();
        _armorPayload.armorDamageMultiplier = Mathf.Max(0f, armorDamageMultiplier);
        _armorPayload.piercePercent = 0f;
        _armorPayload.pierceFlat = 0f;
        _armorPayload.shatterFlat = 0f;
        _armorPayload.shatterPercentOfArmorDamage = 0f;
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