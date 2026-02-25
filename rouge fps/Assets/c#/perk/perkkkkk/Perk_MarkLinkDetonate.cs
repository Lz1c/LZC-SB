using UnityEngine;

/// <summary>
/// 印记联动引爆 Perk
/// - 通过 PerkManager.InstantiatePerkToGun 实例化到某把枪的 root 下
/// - 当前这把枪成为印记施加者
/// - 另一把枪成为引爆者
/// </summary>
public sealed class Perk_MarkLinkDetonate : MonoBehaviour
{
    [Header("印记持续时间")]
    [Tooltip("印记在敌人身上持续的时间（秒）。超过该时间未被引爆则自动消失。")]
    [Min(0.01f)]
    public float duration = 6f;

    [Header("施加逻辑")]
    [Tooltip("当施加者命中已带印记的目标时，是否叠加命中计数。若关闭，则只刷新持续时间，不增加层数。")]
    public bool addCountOnApplyHit = true;

    [Header("引爆伤害")]

    [Tooltip("引爆时的基础伤害值。")]
    [Min(0f)]
    public float detonateBaseDamage = 10f;

    [Tooltip("每次施加命中所增加的额外引爆伤害。最终伤害 = 基础伤害 + (命中次数 × 此值)。")]
    [Min(0f)]
    public float detonateDamagePerApplyHit = 5f;

    [Tooltip("引爆伤害是否跳过 CombatEventHub 的 OnHit 事件。开启可避免某些 Perk 连锁触发。")]
    public bool detonateSkipHitEvent = true;

    [Tooltip("引爆后是否消耗印记。关闭则印记不会消失，可被多次引爆。")]
    public bool consumeOnDetonate = true;

    [Header("可选扩展")]

    [Tooltip("引爆命中时是否共享施加者子弹上的状态效果（如 DOT、减速等）。")]
    public bool shareStatusesOnDetonateHit = false;

    [Tooltip("当目标因引爆死亡时，是否将印记跳转到附近的另一个敌人。")]
    public bool jumpMarkOnKill = false;

    [Tooltip("跳转印记时最多搜索的敌人数量上限，用于控制性能消耗。")]
    [Min(1)]
    public int jumpSearchLimit = 64;

    // 由 PerkManager.InstantiatePerkToGun 通过反射自动写入
    [HideInInspector]
    public int targetGunIndex = -1;

    private CameraGunChannel _gun;
    private MarkManager _mgr;

    // 防止多个实例同时修改同一个 MarkManager 配置
    private static Perk_MarkLinkDetonate _activeOwner;

    private void OnEnable()
    {
        _gun = ResolveGunChannel();
        if (_gun == null)
        {
            Debug.LogError("[Perk_MarkLinkDetonate] 未找到 CameraGunChannel。请确保该 Perk 被实例化在枪的 root 结构下。");
            return;
        }

        _mgr = ResolveMarkManager();
        if (_mgr == null)
        {
            Debug.LogError("[Perk_MarkLinkDetonate] 未找到或创建 MarkManager。");
            return;
        }

        if (_mgr.config == null)
            _mgr.config = ScriptableObject.CreateInstance<MarkConfig>();

        _activeOwner = this;
        _mgr.systemEnabled = true;
        ApplyConfig();
    }

    private void OnDisable()
    {
        if (_activeOwner == this && _mgr != null)
            _mgr.systemEnabled = false;
        if (_activeOwner == this)
            _activeOwner = null;
    }

    /// <summary>
    /// 解析当前 Perk 所属的 CameraGunChannel
    /// </summary>
    private CameraGunChannel ResolveGunChannel()
    {
        var inParent = GetComponentInParent<CameraGunChannel>();
        if (inParent != null)
            return inParent;

        Transform root = transform;
        while (root.parent != null)
            root = root.parent;

        var inChildren = root.GetComponentInChildren<CameraGunChannel>(true);
        if (inChildren != null)
            return inChildren;

        var pm = FindPerkManagerIncludingInactive();
        if (pm != null && targetGunIndex >= 0)
        {
            pm.RefreshAll(force: true);
            var gunRefs = pm.GetGun(targetGunIndex);
            if (gunRefs != null && gunRefs.cameraGunChannel != null)
                return gunRefs.cameraGunChannel;
        }

        return null;
    }

    /// <summary>
    /// 解析或创建 MarkManager
    /// </summary>
    private MarkManager ResolveMarkManager()
    {
        var pm = FindPerkManagerIncludingInactive();
        if (pm != null)
        {
            pm.RefreshAll(force: true);

            if (pm.Control != null && pm.Control.markManager != null)
            {
                var mm = pm.Control.markManager;

                if (!mm.gameObject.activeInHierarchy)
                    mm.gameObject.SetActive(true);

                if (!mm.enabled)
                    mm.enabled = true;

                return mm;
            }
        }

        var found = FindMarkManagerIncludingInactive();
        if (found != null)
        {
            if (!found.gameObject.activeInHierarchy)
                found.gameObject.SetActive(true);

            if (!found.enabled)
                found.enabled = true;

            return found;
        }

        var go = new GameObject("_MarkManager");
        var created = go.AddComponent<MarkManager>();
        DontDestroyOnLoad(go);
        return created;
    }

    /// <summary>
    /// 将当前 Perk 的参数写入 MarkConfig
    /// </summary>
    private void ApplyConfig()
    {
        if (_activeOwner != this)
            return;

        if (_mgr == null || _mgr.config == null || _gun == null)
            return;

        var cfg = _mgr.config;

        cfg.applierRole = _gun.role;

        cfg.detonatorRole =
            (_gun.role == CameraGunChannel.Role.Primary)
            ? CameraGunChannel.Role.Secondary
            : CameraGunChannel.Role.Primary;

        cfg.duration = Mathf.Max(0.01f, duration);
        cfg.addCountOnApplyHit = addCountOnApplyHit;

        cfg.detonateBaseDamage = Mathf.Max(0f, detonateBaseDamage);
        cfg.detonateDamagePerApplyHit = Mathf.Max(0f, detonateDamagePerApplyHit);

        cfg.detonateSkipHitEvent = detonateSkipHitEvent;
        cfg.consumeOnDetonate = consumeOnDetonate;

        cfg.shareStatusesOnDetonateHit = shareStatusesOnDetonateHit;
        cfg.jumpMarkOnKill = jumpMarkOnKill;
        cfg.jumpSearchLimit = Mathf.Max(1, jumpSearchLimit);
    }

    private static PerkManager FindPerkManagerIncludingInactive()
    {
#if UNITY_2023_1_OR_NEWER
        return Object.FindFirstObjectByType<PerkManager>(FindObjectsInactive.Include);
#else
        return Object.FindFirstObjectByType<PerkManager>();
#endif
    }

    private static MarkManager FindMarkManagerIncludingInactive()
    {
#if UNITY_2023_1_OR_NEWER
        return Object.FindFirstObjectByType<MarkManager>(FindObjectsInactive.Include);
#else
        return Object.FindFirstObjectByType<MarkManager>();
#endif
    }
}