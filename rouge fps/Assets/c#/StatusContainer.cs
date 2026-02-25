using System.Collections.Generic;
using UnityEngine;

public class StatusContainer : MonoBehaviour
{
    [Header("Default Tick Interval")]
    public float defaultTickInterval = 1f;

    [Header("Clamp")]
    public float minMoveSpeedMultiplier = 0.05f;
    public float minOutgoingDamageMultiplier = 0.10f;

    // =========================
    // 异常施加修饰器（全局）
    // 说明：
    // - 所有异常施加最终都会走 ApplyStatus(StatusApplyRequest)
    // - 这里提供一个全局的修改入口，让 Perk 能统一修改 stacksToAdd 等参数
    // - Priority 越大越先执行
    // =========================
    private static readonly List<IStatusApplyModifier> _applyMods = new List<IStatusApplyModifier>();
    private static bool _modsDirty = false;

    /// <summary>
    /// 注册异常施加修饰器（全局）
    /// </summary>
    public static void RegisterApplyModifier(IStatusApplyModifier mod)
    {
        if (mod == null) return;
        if (_applyMods.Contains(mod)) return;
        _applyMods.Add(mod);
        _modsDirty = true;
    }

    /// <summary>
    /// 反注册异常施加修饰器（全局）
    /// </summary>
    public static void UnregisterApplyModifier(IStatusApplyModifier mod)
    {
        if (mod == null) return;
        if (_applyMods.Remove(mod))
            _modsDirty = true;
    }

    /// <summary>
    /// 如果修饰器列表有变更，则按优先级排序（从大到小）
    /// </summary>
    private static void SortModsIfDirty()
    {
        if (!_modsDirty) return;
        _modsDirty = false;
        _applyMods.Sort((a, b) => b.Priority.CompareTo(a.Priority));
    }

    [System.Serializable]
    public class StatusDebugEntry
    {
        public StatusType type;
        public int stacks;
        public float remainingTime;

        public float tickInterval;
        public float burnDamagePerTickPerStack;

        public float slowPerStack;
        public float weakenPerStack;

        public float shockChainDamagePerTickPerStack; // 注意：保持兼容（如果你原来没有这个字段可删掉）
        public float shockChainDamagePerStack;
        public float shockChainRadius;
        public int shockMaxChains;

        public CameraGunChannel source;
    }

    [Header("Debug (Play Mode)")]
    [SerializeField] private List<StatusDebugEntry> debugStatuses = new List<StatusDebugEntry>();
    [SerializeField] private string debugSummary;
    [SerializeField] private string debugMarkSummary;

    public bool debugRefreshEveryFrame = true;

    private class StatusState
    {
        public StatusType type;
        public int stacks;
        public float expireTime;

        public float tickInterval;
        public float nextTickTime;
        public float burnDamagePerTickPerStack;

        public float slowPerStack;
        public float weakenPerStack;

        public float shockChainDamagePerStack;
        public float shockChainRadius;
        public int shockMaxChains;

        public CameraGunChannel source;
    }

    private readonly Dictionary<StatusType, StatusState> _states = new Dictionary<StatusType, StatusState>();
    private MonsterHealth _health;

    private void Awake()
    {
        _health = GetComponent<MonsterHealth>();
        if (_health == null) _health = GetComponentInParent<MonsterHealth>();

        RefreshDebugView();
    }

    private void Update()
    {
        float now = Time.time;

        List<StatusType> toRemove = null;

        foreach (var kv in _states)
        {
            StatusState s = kv.Value;

            if (now >= s.expireTime)
            {
                toRemove ??= new List<StatusType>();
                toRemove.Add(s.type);
                continue;
            }

            if (s.type == StatusType.Burn && s.burnDamagePerTickPerStack > 0f && s.tickInterval > 0f && now >= s.nextTickTime)
            {
                s.nextTickTime = now + s.tickInterval;

                float dmg = Mathf.Max(0f, s.burnDamagePerTickPerStack) * Mathf.Max(0, s.stacks);
                if (dmg > 0f && _health != null && !_health.IsDead)
                {
                    _health.TakeDamage(new DamageInfo
                    {
                        source = s.source,
                        damage = dmg,
                        isHeadshot = false,
                        hitPoint = transform.position,
                        hitCollider = null,
                        flags = DamageFlags.SkipHitEvent
                    });
                }
            }
        }

        if (toRemove != null)
        {
            for (int i = 0; i < toRemove.Count; i++)
                _states.Remove(toRemove[i]);
        }

        if (debugRefreshEveryFrame)
            RefreshDebugView();
    }

    public void ApplyStatus(StatusApplyRequest req)
    {
        // =========================
        // 新增：异常施加修饰器入口
        // 任何来源只要走 ApplyStatus，都可以在这里统一修改 stacksToAdd 等字段
        // =========================
        SortModsIfDirty();
        for (int i = 0; i < _applyMods.Count; i++)
        {
            // 允许修饰器直接改 req（例如 stacksToAdd +1）
            _applyMods[i].Modify(this, ref req);
        }

        float now = Time.time;

        int add = Mathf.Max(1, req.stacksToAdd);
        float duration = Mathf.Max(0.01f, req.duration);

        if (_states.TryGetValue(req.type, out StatusState s))
        {
            s.stacks += add;
            s.expireTime = now + duration;
            s.source = req.source;

            ApplyParamsToState(s, req, now);
        }
        else
        {
            s = new StatusState
            {
                type = req.type,
                stacks = add,
                expireTime = now + duration,
                source = req.source
            };
            ApplyParamsToState(s, req, now);
            _states.Add(req.type, s);
        }

        RefreshDebugView();
    }

    public void RemoveStatus(StatusType type)
    {
        if (_states.Remove(type))
            RefreshDebugView();
    }

    private void ApplyParamsToState(StatusState s, StatusApplyRequest req, float now)
    {
        switch (req.type)
        {
            case StatusType.Burn:
                {
                    float tick = (req.tickInterval > 0f) ? req.tickInterval : defaultTickInterval;
                    if (tick <= 0f) tick = 1f;

                    s.tickInterval = tick;
                    s.nextTickTime = now + tick;
                    s.burnDamagePerTickPerStack = req.burnDamagePerTickPerStack;
                    break;
                }
            case StatusType.Slow:
                {
                    s.slowPerStack = req.slowPerStack;
                    break;
                }
            case StatusType.Poison:
                {
                    s.weakenPerStack = req.weakenPerStack;
                    break;
                }
            case StatusType.Shock:
                {
                    s.shockChainDamagePerStack = req.shockChainDamagePerStack;
                    s.shockChainRadius = req.shockChainRadius;
                    s.shockMaxChains = Mathf.Max(1, req.shockMaxChains);
                    break;
                }
            case StatusType.Mark:
                {
                    break;
                }
        }
    }

    public bool HasStatus(StatusType type) => _states.ContainsKey(type);

    public int GetStacks(StatusType type)
        => _states.TryGetValue(type, out var s) ? Mathf.Max(0, s.stacks) : 0;

    public int GetTotalStacks()
    {
        int sum = 0;
        foreach (var kv in _states)
            sum += Mathf.Max(0, kv.Value.stacks);
        return sum;
    }

    public float GetMoveSpeedMultiplier()
    {
        int stacks = GetStacks(StatusType.Slow);
        if (stacks <= 0) return 1f;

        float per = _states[StatusType.Slow].slowPerStack;
        float mult = 1f - per * stacks;
        return Mathf.Clamp(mult, minMoveSpeedMultiplier, 1f);
    }

    public float GetOutgoingDamageMultiplier()
    {
        int stacks = GetStacks(StatusType.Poison);
        if (stacks <= 0) return 1f;

        float per = _states[StatusType.Poison].weakenPerStack;
        float mult = 1f - per * stacks;
        return Mathf.Clamp(mult, minOutgoingDamageMultiplier, 1f);
    }

    public bool TryGetShockChainParams(out float chainDamagePerTarget, out float radius, out int maxChains, out CameraGunChannel source)
    {
        if (!_states.TryGetValue(StatusType.Shock, out var s) || s.stacks <= 0)
        {
            chainDamagePerTarget = 0f;
            radius = 0f;
            maxChains = 0;
            source = null;
            return false;
        }

        chainDamagePerTarget = Mathf.Max(0f, s.shockChainDamagePerStack) * Mathf.Max(0, s.stacks);
        radius = Mathf.Max(0.1f, s.shockChainRadius);
        maxChains = Mathf.Max(1, s.shockMaxChains);
        source = s.source;

        return chainDamagePerTarget > 0f;
    }

    public void ExportSnapshots(List<StatusSnapshot> outList)
    {
        if (outList == null) return;

        float now = Time.time;

        foreach (var kv in _states)
        {
            var s = kv.Value;
            float remaining = Mathf.Max(0f, s.expireTime - now);
            if (remaining <= 0f) continue;

            outList.Add(new StatusSnapshot
            {
                type = s.type,
                stacks = s.stacks,
                remaining = remaining,

                tickInterval = s.tickInterval,
                burnDamagePerTickPerStack = s.burnDamagePerTickPerStack,

                slowPerStack = s.slowPerStack,
                weakenPerStack = s.weakenPerStack,

                shockChainDamagePerStack = s.shockChainDamagePerStack,
                shockChainRadius = s.shockChainRadius,
                shockMaxChains = s.shockMaxChains,

                source = s.source
            });
        }
    }

    public void ApplySnapshots(List<StatusSnapshot> snapshots)
    {
        if (snapshots == null) return;

        for (int i = 0; i < snapshots.Count; i++)
        {
            var snap = snapshots[i];

            ApplyStatus(new StatusApplyRequest
            {
                type = snap.type,
                stacksToAdd = Mathf.Max(1, snap.stacks),
                duration = Mathf.Max(0.01f, snap.remaining),
                source = snap.source,

                tickInterval = snap.tickInterval,
                burnDamagePerTickPerStack = snap.burnDamagePerTickPerStack,

                slowPerStack = snap.slowPerStack,
                weakenPerStack = snap.weakenPerStack,

                shockChainDamagePerStack = snap.shockChainDamagePerStack,
                shockChainRadius = snap.shockChainRadius,
                shockMaxChains = snap.shockMaxChains
            });
        }
    }

    private void RefreshDebugView()
    {
        if (!Application.isPlaying) return;

        debugStatuses.Clear();

        float now = Time.time;

        foreach (var kv in _states)
        {
            var s = kv.Value;
            float remaining = Mathf.Max(0f, s.expireTime - now);

            debugStatuses.Add(new StatusDebugEntry
            {
                type = s.type,
                stacks = s.stacks,
                remainingTime = remaining,

                tickInterval = s.tickInterval,
                burnDamagePerTickPerStack = s.burnDamagePerTickPerStack,

                slowPerStack = s.slowPerStack,
                weakenPerStack = s.weakenPerStack,

                shockChainDamagePerStack = s.shockChainDamagePerStack,
                shockChainRadius = s.shockChainRadius,
                shockMaxChains = s.shockMaxChains,

                source = s.source
            });
        }

        debugSummary = $"Count={debugStatuses.Count}, TotalStacks={GetTotalStacks()}";
        debugMarkSummary = $"HasMark={HasStatus(StatusType.Mark)}, MarkStacks={GetStacks(StatusType.Mark)}";
    }
}