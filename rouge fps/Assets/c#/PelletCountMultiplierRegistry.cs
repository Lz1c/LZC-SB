using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 弹丸数量倍率注册表：
/// - 允许 Perk 以“乘法倍率”的方式影响霰弹枪弹丸数
/// - 在开火时读取，确保能作为“最终一步”生效（达到你要的优先级最高）
/// </summary>
public static class PelletCountMultiplierRegistry
{
    private class Entry
    {
        public Object key;
        public float mul;
        public int priority;
    }

    private static readonly Dictionary<CameraGunChannel, List<Entry>> _map = new();

    public static void Add(CameraGunChannel gun, Object key, float mul, int priority)
    {
        if (gun == null || key == null) return;
        mul = Mathf.Max(0.01f, mul);

        if (!_map.TryGetValue(gun, out var list))
        {
            list = new List<Entry>(4);
            _map.Add(gun, list);
        }

        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].key == key)
            {
                list[i].mul = mul;
                list[i].priority = priority;
                return;
            }
        }

        list.Add(new Entry { key = key, mul = mul, priority = priority });
    }

    public static void Remove(CameraGunChannel gun, Object key)
    {
        if (gun == null || key == null) return;
        if (!_map.TryGetValue(gun, out var list)) return;

        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (list[i].key == key)
                list.RemoveAt(i);
        }

        if (list.Count == 0) _map.Remove(gun);
    }

    public static float GetFinalMultiplier(CameraGunChannel gun)
    {
        if (gun == null) return 1f;
        if (!_map.TryGetValue(gun, out var list) || list.Count == 0) return 1f;

        list.Sort((a, b) => a.priority.CompareTo(b.priority));

        float m = 1f;
        for (int i = 0; i < list.Count; i++)
            m *= Mathf.Max(0.01f, list[i].mul);

        return m;
    }
}