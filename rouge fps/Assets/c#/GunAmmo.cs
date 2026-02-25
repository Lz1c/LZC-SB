using UnityEngine;

public class GunAmmo : MonoBehaviour
{
    public enum ReloadType { Magazine, PerBullet }

    [Header("Ammo")]
    [Min(1)] public int magazineSize = 30;
    [Min(0)] public int ammoInMag = 30;
    [Min(0)] public int ammoReserve = 120;

    [Header("Reload Type")]
    public ReloadType reloadType = ReloadType.Magazine;

    [Header("Reload Times (Magazine)")]
    [Min(0f)] public float reloadTimeMagazine = 1.8f;

    [Header("Reload Times (PerBullet)")]
    [Min(0f)] public float reloadStartTime = 0.35f;
    [Min(0f)] public float insertOneTime = 0.45f;
    [Min(0f)] public float reloadEndTime = 0.25f;

    [Header("PerBullet 默认插弹数量")]
    [Min(1)]
    [Tooltip("默认 1。若没有 Perk 覆盖，本次 InsertOneNow 会插入该数量。")]
    public int insertCountPerStep = 1;

    public bool IsReloading => _isReloading;
    public bool IsUninterruptible => _isUninterruptible;

    private bool _isReloading;
    private bool _isUninterruptible;

    public System.Action<int, int> OnAmmoChanged;
    public System.Action OnReloadStart;
    public System.Action OnReloadEnd;

    /// <summary>
    /// 插弹数量覆盖钩子（仅用于 PerBullet 插弹）：
    /// - InsertOneNow() 内部会先取 insertCountPerStep
    /// - 然后让订阅者有机会覆盖本次 count（比如交替 1/2）
    /// </summary>
    public delegate void QueryInsertCountHandler(GunAmmo ammo, ref int count);
    public event QueryInsertCountHandler OnQueryInsertCount;

    private void Awake()
    {
        NotifyAmmo();
    }

    public bool HasAmmoInMag() => ammoInMag > 0;

    public bool TryConsumeOne()
    {
        if (ammoInMag <= 0) return false;
        ammoInMag--;
        NotifyAmmo();
        return true;
    }

    public bool NeedsReload()
    {
        if (ammoReserve <= 0) return false;
        return ammoInMag < magazineSize;
    }

    public void BeginExternalReload(bool uninterruptible)
    {
        _isReloading = true;
        _isUninterruptible = uninterruptible;
        OnReloadStart?.Invoke();
    }

    public void EndExternalReload()
    {
        _isUninterruptible = false;
        _isReloading = false;
        OnReloadEnd?.Invoke();
    }

    public void CancelReloadIfPerBullet()
    {
        // CameraGunDual 的协程会处理停止，这里保持语义接口即可
    }

    public void ApplyMagazineReloadNow()
    {
        int needed = magazineSize - ammoInMag;
        if (needed <= 0) return;

        int take = Mathf.Min(needed, ammoReserve);
        ammoInMag += take;
        ammoReserve -= take;
        NotifyAmmo();
    }

    public bool CanInsertOne()
    {
        return ammoReserve > 0 && ammoInMag < magazineSize;
    }

    public void InsertOneNow()
    {
        if (!CanInsertOne()) return;

        // 默认插弹数量
        int count = Mathf.Max(1, insertCountPerStep);

        // 允许 Perk 覆盖本次插弹数量（用于实现 1/2 交替、或其它逻辑）
        OnQueryInsertCount?.Invoke(this, ref count);
        count = Mathf.Max(1, count);

        int space = magazineSize - ammoInMag;
        int take = Mathf.Min(count, Mathf.Min(space, ammoReserve));
        if (take <= 0) return;

        ammoInMag += take;
        ammoReserve -= take;
        NotifyAmmo();
    }

    private void NotifyAmmo()
    {
        OnAmmoChanged?.Invoke(ammoInMag, ammoReserve);
    }
}