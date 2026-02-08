using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class PerkManager : MonoBehaviour
{
    [Header("Guns (drag these)")]
    [SerializeField] private GameObject gun1;
    [SerializeField] private GameObject gun2;

    [Header("Gun Control Root (optional)")]
    [SerializeField] private GameObject gunControlRoot;

    [Header("Selected Perks (GunA / GunB)")]
    public List<MonoBehaviour> selectedPerksGunA = new();
    public List<MonoBehaviour> selectedPerksGunB = new();

    [Header("Auto Refresh")]
    [SerializeField] private bool autoRefreshInPlayMode = true;

    [Header("Optional: Filter")]
    [Tooltip("If set, the manager will prefer components under this child name (per gun). Leave empty to disable.")]
    [SerializeField] private string preferChildNameForGunSearch = "";

    [Serializable]
    public sealed class GunRefs
    {
        public GameObject root;

        public CameraGunChannel cameraGunChannel;
        public GunAmmo gunAmmo;
        public GunRecoil gunRecoil;
        public GunSpread gunSpread;
        public SpreadDiamondUI spreadDiamondUI;
        public AutoAimLockOn autoAimLock;
        public ShockChainProc shockChainProc;

        public bool IsComplete()
        {
            return root != null && cameraGunChannel != null;
        }

        public void Clear()
        {
            root = null;
            cameraGunChannel = null;
            gunAmmo = null;
            gunRecoil = null;
            gunSpread = null;
            spreadDiamondUI = null;
            autoAimLock = null;
            shockChainProc = null;
        }
    }

    [Serializable]
    public sealed class ControlRefs
    {
        public GameObject root;

        public AbilityKeyEmitter abilityKeyEmitter;
        public AmmoDualUI ammoDualUI;
        public CameraGunDual cameraGunDual;
        public AimScopeController aimScopeController;
        public MarkManager markManager;

        public bool IsComplete()
        {
            return root != null && cameraGunDual != null;
        }

        public void Clear()
        {
            root = null;
            abilityKeyEmitter = null;
            ammoDualUI = null;
            cameraGunDual = null;
            aimScopeController = null;
            markManager = null;
        }
    }

    [Header("Read-only cached refs")]
    public GunRefs gunARefs = new();
    public GunRefs gunBRefs = new();
    public ControlRefs controlRefs = new();

    public event Action RefsRefreshed;

    // New: fires whenever perk list content changes (count/order/instances)
    public event Action<int> PerksChangedForGun; // 0 = GunA, 1 = GunB
    public event Action PerksChangedAny;

    public GameObject GunAObject => gun1;
    public GameObject GunBObject => gun2;
    public GameObject GunControlRoot => gunControlRoot;

    public GunRefs GunA => gunARefs;
    public GunRefs GunB => gunBRefs;
    public ControlRefs Control => controlRefs;

    private int _lastSignature;
    private int _lastPerkSigA;
    private int _lastPerkSigB;

    private void Awake()
    {
        RefreshAll(force: true);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (!Application.isPlaying)
            RefreshAll(force: false);
    }
#endif

    private void Update()
    {
        if (!Application.isPlaying) return;
        if (!autoRefreshInPlayMode) return;

        int sig = ComputeSignature();
        if (sig != _lastSignature)
            RefreshAll(force: false);
    }

    private int ComputeSignature()
    {
        unchecked
        {
            int h = 17;
            h = h * 31 + (gun1 != null ? gun1.GetInstanceID() : 0);
            h = h * 31 + (gun2 != null ? gun2.GetInstanceID() : 0);
            h = h * 31 + (gunControlRoot != null ? gunControlRoot.GetInstanceID() : 0);
            h = h * 31 + (autoRefreshInPlayMode ? 1 : 0);
            h = h * 31 + (preferChildNameForGunSearch != null ? preferChildNameForGunSearch.GetHashCode() : 0);

            // New: include perk list signatures so inspector/runtime list edits trigger refresh
            h = h * 31 + ComputePerkListSignature(selectedPerksGunA);
            h = h * 31 + ComputePerkListSignature(selectedPerksGunB);

            return h;
        }
    }

    private static int ComputePerkListSignature(List<MonoBehaviour> list)
    {
        unchecked
        {
            int h = 23;
            if (list == null) return h;

            h = h * 31 + list.Count;
            for (int i = 0; i < list.Count; i++)
            {
                var mb = list[i];
                h = h * 31 + (mb != null ? mb.GetInstanceID() : 0);

                // Optional: also include type hash for extra safety if references get swapped
                h = h * 31 + (mb != null ? mb.GetType().GetHashCode() : 0);
            }

            return h;
        }
    }

    public void RefreshAll(bool force)
    {
        EnsureGunControlRoot(force);

        RefreshGunRefs(gun1, gunARefs, force, preferChildNameForGunSearch);
        RefreshGunRefs(gun2, gunBRefs, force, preferChildNameForGunSearch);

        RefreshControlRefs(gunControlRoot, controlRefs, force);

        // New: detect perk changes and fire events / update state
        RefreshPerkState(force);

        _lastSignature = ComputeSignature();
        RefsRefreshed?.Invoke();
    }

    private void RefreshPerkState(bool force)
    {
        int sigA = ComputePerkListSignature(selectedPerksGunA);
        int sigB = ComputePerkListSignature(selectedPerksGunB);

        bool changedA = force || sigA != _lastPerkSigA;
        bool changedB = force || sigB != _lastPerkSigB;

        if (!changedA && !changedB) return;

        _lastPerkSigA = sigA;
        _lastPerkSigB = sigB;

        if (changedA) PerksChangedForGun?.Invoke(0);
        if (changedB) PerksChangedForGun?.Invoke(1);
        PerksChangedAny?.Invoke();
    }

    private static void RefreshGunRefs(GameObject gunObj, GunRefs refs, bool force, string preferChildName)
    {
        bool needsRefresh =
            force ||
            refs.root != gunObj ||
            (gunObj != null && !refs.IsComplete());

        if (!needsRefresh) return;

        refs.Clear();
        refs.root = gunObj;
        if (gunObj == null) return;

        Transform searchRoot = gunObj.transform;

        if (!string.IsNullOrWhiteSpace(preferChildName))
        {
            var child = FindChildRecursive(searchRoot, preferChildName);
            if (child != null) searchRoot = child;
        }

        refs.cameraGunChannel = searchRoot.GetComponentInChildren<CameraGunChannel>(true);
        refs.gunAmmo = searchRoot.GetComponentInChildren<GunAmmo>(true);
        refs.gunRecoil = searchRoot.GetComponentInChildren<GunRecoil>(true);
        refs.gunSpread = searchRoot.GetComponentInChildren<GunSpread>(true);
        refs.spreadDiamondUI = searchRoot.GetComponentInChildren<SpreadDiamondUI>(true);
        refs.autoAimLock = searchRoot.GetComponentInChildren<AutoAimLockOn>(true);
        refs.shockChainProc = searchRoot.GetComponentInChildren<ShockChainProc>(true);
    }

    private void EnsureGunControlRoot(bool force)
    {
        if (gunControlRoot != null) return;

        var dual = FindFirstObjectByType<CameraGunDual>();
        if (dual != null)
        {
            gunControlRoot = dual.gameObject;
            return;
        }

        var mark = FindFirstObjectByType<MarkManager>();
        if (mark != null)
        {
            gunControlRoot = mark.gameObject;
            return;
        }

        var existing = transform.Find("GunControlRoot");
        if (existing == null)
        {
            var go = new GameObject("GunControlRoot");
            go.transform.SetParent(transform, worldPositionStays: false);
            gunControlRoot = go;
        }
        else
        {
            gunControlRoot = existing.gameObject;
        }

        if (force)
        {
            controlRefs.Clear();
            controlRefs.root = gunControlRoot;
        }
    }

    private static void RefreshControlRefs(GameObject rootObj, ControlRefs refs, bool force)
    {
        bool needsRefresh =
            force ||
            refs.root != rootObj ||
            (rootObj != null && !refs.IsComplete());

        if (!needsRefresh) return;

        refs.Clear();
        refs.root = rootObj;
        if (rootObj == null) return;

        refs.abilityKeyEmitter = rootObj.GetComponentInChildren<AbilityKeyEmitter>(true);
        refs.ammoDualUI = rootObj.GetComponentInChildren<AmmoDualUI>(true);
        refs.cameraGunDual = rootObj.GetComponentInChildren<CameraGunDual>(true);
        refs.aimScopeController = rootObj.GetComponentInChildren<AimScopeController>(true);
        refs.markManager = rootObj.GetComponentInChildren<MarkManager>(true);
    }

    private static Transform FindChildRecursive(Transform root, string name)
    {
        if (root == null) return null;
        if (root.name == name) return root;

        for (int i = 0; i < root.childCount; i++)
        {
            var found = FindChildRecursive(root.GetChild(i), name);
            if (found != null) return found;
        }
        return null;
    }

    // -----------------------------
    // Perk lists (GunA / GunB)
    // -----------------------------

    public List<MonoBehaviour> GetPerkList(int gunIndex)
    {
        return gunIndex == 0 ? selectedPerksGunA : selectedPerksGunB;
    }

    public GunRefs GetGun(int gunIndex)
    {
        return gunIndex == 0 ? gunARefs : gunBRefs;
    }

    public bool HasPerk(string perkId, int gunIndex)
    {
        if (string.IsNullOrWhiteSpace(perkId)) return false;

        var list = GetPerkList(gunIndex);
        for (int i = 0; i < list.Count; i++)
        {
            var mb = list[i];
            if (mb == null) continue;

            var meta = mb.GetComponent<PerkMeta>();
            if (meta != null)
            {
                if (meta.EffectiveId == perkId) return true;
            }
            else
            {
                if (mb.GetType().Name == perkId) return true;
            }
        }
        return false;
    }

    public bool PrerequisitesMet(GameObject perkObject, int gunIndex)
    {
        if (perkObject == null) return false;

        var meta = perkObject.GetComponent<PerkMeta>();
        if (meta == null) return true;

        var req = meta.requiredPerkIds;
        if (req == null || req.Count == 0) return true;

        for (int i = 0; i < req.Count; i++)
        {
            var id = req[i];
            if (string.IsNullOrWhiteSpace(id)) continue;

            if (!HasPerk(id, gunIndex)) return false;
        }

        return true;
    }

    public bool TryAddPerkInstanceToGun(MonoBehaviour perkInstance, int gunIndex)
    {
        if (perkInstance == null) return false;

        var list = GetPerkList(gunIndex);

        string id = "";
        var meta = perkInstance.GetComponent<PerkMeta>();
        if (meta != null) id = meta.EffectiveId;
        if (string.IsNullOrWhiteSpace(id)) id = perkInstance.GetType().Name;

        if (HasPerk(id, gunIndex)) return false;
        if (!PrerequisitesMet(perkInstance.gameObject, gunIndex)) return false;

        list.Add(perkInstance);

        // New: immediate perk state refresh when API-based changes happen
        ForcePerkStateRefresh();

        return true;
    }

    public bool TryRemovePerkFromGun(MonoBehaviour perkInstance, int gunIndex)
    {
        if (perkInstance == null) return false;

        var list = GetPerkList(gunIndex);
        bool removed = list.Remove(perkInstance);
        if (removed) ForcePerkStateRefresh();
        return removed;
    }

    public void ClearPerksForGun(int gunIndex)
    {
        var list = GetPerkList(gunIndex);
        if (list.Count == 0) return;

        list.Clear();
        ForcePerkStateRefresh();
    }

    public void ForcePerkStateRefresh()
    {
        // Recompute perk sigs and fire events immediately
        RefreshPerkState(force: true);

        // Also update main signature so Update() doesn't double-trigger in the same frame
        _lastSignature = ComputeSignature();

        // Keep existing contract: listeners using RefsRefreshed will also react
        RefsRefreshed?.Invoke();
    }

    public MonoBehaviour InstantiatePerkToGun(GameObject perkPrefab, int gunIndex, Transform parent)
    {
        if (perkPrefab == null) return null;

        var finalParent = parent != null ? parent : transform;

        // Staging container stays inactive so perk OnEnable won't run yet.
        var staging = new GameObject($"__PerkStaging_{perkPrefab.name}");
        staging.hideFlags = HideFlags.HideInHierarchy;
        staging.transform.SetParent(finalParent, false);
        staging.SetActive(false);

        var inst = Instantiate(perkPrefab, staging.transform);

        var behaviours = inst.GetComponents<MonoBehaviour>();
        MonoBehaviour perkLogic = null;

        for (int i = 0; i < behaviours.Length; i++)
        {
            var b = behaviours[i];
            if (b == null) continue;
            if (b is PerkMeta) continue;
            perkLogic = b;
            break;
        }

        if (perkLogic == null)
        {
            Destroy(inst);
            Destroy(staging);
            return null;
        }

        // Optional: set targetGunIndex if the perk supports it.
        var t = perkLogic.GetType();
        var field = t.GetField("targetGunIndex");
        if (field != null && field.FieldType == typeof(int))
            field.SetValue(perkLogic, gunIndex);

        // Register first (while inactive)
        if (!TryAddPerkInstanceToGun(perkLogic, gunIndex))
        {
            Destroy(inst);
            Destroy(staging);
            return null;
        }

        // Move to final parent, then activate so OnEnable sees list membership.
        inst.transform.SetParent(finalParent, false);
        inst.SetActive(true);
        perkLogic.enabled = true;

        Destroy(staging);
        return perkLogic;
    }


    public int GetPerkTier(GameObject perkObject)
    {
        if (perkObject == null) return 1;
        var meta = perkObject.GetComponent<PerkMeta>();
        if (meta == null) return 1;
        return Mathf.Clamp(meta.perkTier, 1, 2);
    }

    public bool HasValidRefs()
    {
        if (gun1 == null || gun2 == null) return false;
        if (!gunARefs.IsComplete() || !gunBRefs.IsComplete()) return false;
        return true;
    }
}
