using System.Collections;
using UnityEngine;

public sealed class Perk_HeadshotHighBodyLow_HitboxOverride : MonoBehaviour
{
    [Header("Hitbox Multipliers")]
    [Range(0.05f, 1f)] public float bodyDamageMultiplier = 0.6f;
    [Min(1f)] public float headshotDamageMultiplier = 2.5f;

    [HideInInspector] public int targetGunIndex = -1;

    private PerkManager _pm;
    private CameraGunChannel _gun;
    private bool _bound;

    private void Awake()
    {
        _pm = FindFirstObjectByType<PerkManager>();
    }

    private void OnEnable()
    {
        TryBind();
        if (!_bound) StartCoroutine(BindNextFrames());
    }

    private IEnumerator BindNextFrames()
    {
        for (int i = 0; i < 10 && !_bound; i++)
        {
            yield return null;
            TryBind();
        }
    }

    private void OnDisable()
    {
        if (_gun != null && HitboxMultiplierManager.Instance != null)
            HitboxMultiplierManager.Instance.ClearGunOverride(_gun);

        _gun = null;
        _bound = false;
    }

    private void TryBind()
    {
        if (_bound) return;

        // Ensure manager exists
        if (HitboxMultiplierManager.Instance == null)
        {
            var go = new GameObject("_HitboxMultiplierManager");
            go.AddComponent<HitboxMultiplierManager>();
        }

        // 1) parent chain
        _gun = GetComponentInParent<CameraGunChannel>();
        if (_gun == null)
        {
            // 2) PerkGiveTest path
            _pm ??= FindFirstObjectByType<PerkManager>();
            if (_pm == null || targetGunIndex < 0) return;

            _pm.RefreshAll(force: true);
            var gunRefs = _pm.GetGun(targetGunIndex);
            if (gunRefs == null) return;
            _gun = gunRefs.cameraGunChannel;
        }

        if (_gun == null) return;

        HitboxMultiplierManager.Instance.SetGunOverride(_gun, bodyDamageMultiplier, headshotDamageMultiplier);
        _bound = true;
    }
}