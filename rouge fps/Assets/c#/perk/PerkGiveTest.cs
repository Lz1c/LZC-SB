using UnityEngine;

public sealed class PerkGiveTest : MonoBehaviour
{
    public enum TargetGun
    {
        GunA = 0,
        GunB = 1
    }

    [Header("Refs")]
    public PerkManager perkManager;

    [Tooltip("Prefab from Project window, not a scene instance.")]
    public GameObject perkPrefab;

    [Header("Give To")]
    public TargetGun targetGun = TargetGun.GunA;

    [Header("Test")]
    public bool giveOnStart = false;
    public KeyCode giveKey = KeyCode.P;

    private void Start()
    {
        if (giveOnStart) Give();
    }

    private void Update()
    {
        if (Input.GetKeyDown(giveKey)) Give();
    }

    public void Give()
    {
        if (perkManager == null)
        {
            Debug.LogError("[PerkGiveTest] perkManager is null.");
            return;
        }

        if (perkPrefab == null)
        {
            Debug.LogError("[PerkGiveTest] perkPrefab is null.");
            return;
        }

        // Ensure cached refs are up to date.
        perkManager.RefreshAll(force: true);

        int gunIndex = (int)targetGun;
        var gunRefs = perkManager.GetGun(gunIndex);

        if (gunRefs == null || gunRefs.root == null)
        {
            Debug.LogError("[PerkGiveTest] GunRefs.root is null. Check PerkManager gun assignments in Inspector.");
            return;
        }

        // Parent perk under gun root so GunPerkModifierBase can find CameraGunChannel in parents.
        Transform parent = gunRefs.root.transform;

        var inst = perkManager.InstantiatePerkToGun(perkPrefab, gunIndex, parent);
        if (inst == null)
        {
            Debug.LogError($"[PerkGiveTest] InstantiatePerkToGun failed for '{perkPrefab.name}' on {targetGun}.");
            return;
        }

        Debug.Log($"[PerkGiveTest] Granted '{perkPrefab.name}' to {targetGun}.");
    }
}