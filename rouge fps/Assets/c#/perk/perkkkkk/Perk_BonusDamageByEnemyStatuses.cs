using System.Collections.Generic;
using UnityEngine;

public sealed class Perk_BonusDamageByEnemyStatuses : MonoBehaviour
{
    [Header("Bonus Damage")]
    [Tooltip("Extra damage added per active abnormal status on the enemy.")]
    [Min(0f)] public float bonusDamagePerStatus = 3f;

    [Tooltip("If true, counts Mark as an abnormal status too.")]
    public bool includeMark = false;

    [Tooltip("If true, counts stacks instead of unique status types (e.g., Burn x3 counts as 3).")]
    public bool countStacks = false;

    [Header("Safety")]
    public bool disableIfNotAllowed = true;
    public bool requirePrerequisites = true;

    private PerkManager _perkManager;
    private CameraGunChannel _boundChannel;

    private static readonly Dictionary<CameraGunChannel, Config> _configs = new();

    public struct Config
    {
        public float bonusDamagePerStatus;
        public bool includeMark;
        public bool countStacks;
    }

    public static bool TryGetConfig(CameraGunChannel src, out Config cfg)
    {
        if (src != null && _configs.TryGetValue(src, out cfg))
            return true;

        cfg = default;
        return false;
    }

    private void Awake()
    {
        _perkManager = FindFirstObjectByType<PerkManager>();
    }

    private void OnEnable()
    {
        _perkManager ??= FindFirstObjectByType<PerkManager>();
        if (_perkManager == null)
        {
            if (disableIfNotAllowed) enabled = false;
            return;
        }

        int gunIndex = ResolveGunIndexFromManager();
        if (gunIndex < 0)
        {
            if (disableIfNotAllowed) enabled = false;
            return;
        }

        if (requirePrerequisites && !_perkManager.PrerequisitesMet(gameObject, gunIndex))
        {
            if (disableIfNotAllowed) enabled = false;
            return;
        }

        var gun = _perkManager.GetGun(gunIndex);
        _boundChannel = gun != null ? gun.cameraGunChannel : null;

        if (_boundChannel == null)
        {
            if (disableIfNotAllowed) enabled = false;
            return;
        }

        _configs[_boundChannel] = new Config
        {
            bonusDamagePerStatus = Mathf.Max(0f, bonusDamagePerStatus),
            includeMark = includeMark,
            countStacks = countStacks
        };
    }

    private void OnDisable()
    {
        if (_boundChannel != null)
            _configs.Remove(_boundChannel);
    }

    private int ResolveGunIndexFromManager()
    {
        if (_perkManager == null) return -1;

        if (_perkManager.selectedPerksGunA != null && _perkManager.selectedPerksGunA.Contains(this)) return 0;
        if (_perkManager.selectedPerksGunB != null && _perkManager.selectedPerksGunB.Contains(this)) return 1;

        return -1;
    }
}