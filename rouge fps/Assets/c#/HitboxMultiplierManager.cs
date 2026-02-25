using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 命中部位倍率管理器：
/// - 支持每把枪设置“绝对倍率覆盖”（兼容你原来的 SetGunOverride）
/// - 支持每把枪叠加“add pct”（例如爆头 +30%）
/// - 由 DamageResolver 在结算时直接查询，确保当次命中立刻生效
/// </summary>
public sealed class HitboxMultiplierManager : MonoBehaviour
{
    public static HitboxMultiplierManager Instance { get; private set; }

    private enum OverrideMode
    {
        无 = 0,
        绝对覆盖 = 1
    }

    private struct AbsoluteOverride
    {
        public float body;
        public float head;
        public OverrideMode mode;
    }

    private struct AddPctEntry
    {
        public Object key;
        public int priority;
        public float bodyAddPct;
        public float headAddPct;
    }

    // 兼容旧逻辑：每把枪的“绝对倍率覆盖”
    private readonly Dictionary<CameraGunChannel, AbsoluteOverride> _absoluteOverrides = new();

    // 新增：每把枪的 add pct 列表（可叠加、可按优先级排序）
    private readonly Dictionary<CameraGunChannel, List<AddPctEntry>> _addPct = new();

    // 缓存每个 Hitbox 的初始倍率（防止其它脚本临时改动造成漂移）
    private readonly Dictionary<Hitbox, float> _original = new();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    /// <summary>
    /// 兼容你原来的 API：设置“绝对倍率覆盖”
    /// </summary>
    public void SetGunOverride(CameraGunChannel gun, float bodyMultiplier, float headMultiplier)
    {
        if (gun == null) return;

        _absoluteOverrides[gun] = new AbsoluteOverride
        {
            body = Mathf.Max(0f, bodyMultiplier),
            head = Mathf.Max(0f, headMultiplier),
            mode = OverrideMode.绝对覆盖
        };
    }

    public void ClearGunOverride(CameraGunChannel gun)
    {
        if (gun == null) return;
        _absoluteOverrides.Remove(gun);
    }

    /// <summary>
    /// 新增 API：为某把枪注册“add pct”倍率（可叠加）
    /// 例：爆头 addPct=0.3 => 最终爆头倍率 = 原倍率 * (1 + 0.3)
    /// </summary>
    public void RegisterAddPct(CameraGunChannel gun, Object key, float bodyAddPct, float headAddPct, int priority)
    {
        if (gun == null || key == null) return;

        if (!_addPct.TryGetValue(gun, out var list))
        {
            list = new List<AddPctEntry>(4);
            _addPct.Add(gun, list);
        }

        // 如果同 key 已存在则覆盖
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].key == key)
            {
                list[i] = new AddPctEntry
                {
                    key = key,
                    priority = priority,
                    bodyAddPct = Mathf.Max(0f, bodyAddPct),
                    headAddPct = Mathf.Max(0f, headAddPct)
                };
                return;
            }
        }

        list.Add(new AddPctEntry
        {
            key = key,
            priority = priority,
            bodyAddPct = Mathf.Max(0f, bodyAddPct),
            headAddPct = Mathf.Max(0f, headAddPct)
        });
    }

    public void UnregisterAddPct(CameraGunChannel gun, Object key)
    {
        if (gun == null || key == null) return;
        if (!_addPct.TryGetValue(gun, out var list)) return;

        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (list[i].key == key)
                list.RemoveAt(i);
        }

        if (list.Count == 0)
            _addPct.Remove(gun);
    }

    /// <summary>
    /// 供 DamageResolver 调用：给出该枪对该 hitbox 的“最终倍率”
    /// 规则：
    /// 1) 若存在绝对覆盖，则直接返回覆盖倍率
    /// 2) 否则返回：原倍率 * (1 + 总addPct)
    /// </summary>
    public float ResolveMultiplier(CameraGunChannel gun, Hitbox hb)
    {
        if (hb == null) return 1f;

        CacheOriginal(hb);

        bool isHead = hb.part == Hitbox.Part.Head;

        // 1) 绝对覆盖优先
        if (gun != null && _absoluteOverrides.TryGetValue(gun, out var abs) && abs.mode == OverrideMode.绝对覆盖)
        {
            return Mathf.Max(0f, isHead ? abs.head : abs.body);
        }

        // 2) add pct 叠加
        float baseMult = _original.TryGetValue(hb, out var v) ? v : Mathf.Max(0f, hb.damageMultiplier);
        float addPct = GetTotalAddPct(gun, isHead);
        return Mathf.Max(0f, baseMult * (1f + addPct));
    }

    private float GetTotalAddPct(CameraGunChannel gun, bool isHead)
    {
        if (gun == null) return 0f;
        if (!_addPct.TryGetValue(gun, out var list) || list == null || list.Count == 0) return 0f;

        // priority 越小越“更早”，这里是求和，排序主要是为了未来你要改成“最高优先级覆盖”时方便
        list.Sort((a, b) => a.priority.CompareTo(b.priority));

        float sum = 0f;
        for (int i = 0; i < list.Count; i++)
            sum += Mathf.Max(0f, isHead ? list[i].headAddPct : list[i].bodyAddPct);

        return sum;
    }

    private void CacheOriginal(Hitbox hb)
    {
        if (hb == null) return;
        if (_original.ContainsKey(hb)) return;
        _original.Add(hb, Mathf.Max(0f, hb.damageMultiplier));
    }

    /// <summary>
    /// 可选：场景重置时恢复所有 hitbox 的初始倍率
    /// </summary>
    public void RestoreAll()
    {
        foreach (var kv in _original)
        {
            if (kv.Key != null)
                kv.Key.damageMultiplier = kv.Value;
        }
        _original.Clear();
    }
}