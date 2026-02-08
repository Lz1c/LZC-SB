using UnityEngine;

public class PerkGiveTest : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private PerkManager perkManager;
    [SerializeField] private GameObject perkPrefab;

    [Header("Target")]
    [Tooltip("0 = gun1, 1 = gun2")]
    [SerializeField] private int gunIndex = 0;

    [Tooltip("Optional. Leave empty to let PerkManager choose a default parent.")]
    [SerializeField] private Transform parentOverride;

    [Header("Input")]
    [SerializeField] private KeyCode key = KeyCode.P;

    private void Reset()
    {
        perkManager = FindFirstObjectByType<PerkManager>();
    }

    private void Update()
    {
        if (!Input.GetKeyDown(key)) return;

        if (perkManager == null)
        {
            Debug.LogError("[PerkGiveTest] PerkManager is null.");
            return;
        }

        if (perkPrefab == null)
        {
            Debug.LogError("[PerkGiveTest] Perk prefab is null.");
            return;
        }

        if (gunIndex != 0 && gunIndex != 1)
        {
            Debug.LogError($"[PerkGiveTest] gunIndex must be 0 or 1. Current: {gunIndex}");
            return;
        }

        var perk = perkManager.InstantiatePerkToGun(perkPrefab, gunIndex, parentOverride);
        if (perk == null)
        {
            Debug.LogWarning("[PerkGiveTest] Perk apply failed (InstantiatePerkToGun returned null). Check prereqs / duplicate id / prefab components.");
            return;
        }

        Debug.Log($"[PerkGiveTest] Perk applied to gunIndex {gunIndex}: {perk.GetType().Name} ({perk.name})");
    }
}
