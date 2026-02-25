using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Central manager for ADS zoom percents.
/// - Each AimScopeController has a set of active percent entries registered by perks.
/// - Manager exposes min/max/current percent and scroll adjustment.
/// </summary>
public static class AdsZoomPercentManager
{
    private sealed class Entry
    {
        public object key;
        public float percent;
    }

    private sealed class ScopeState
    {
        public readonly List<Entry> entries = new List<Entry>();
        public float currentPercent = 1f;
        public bool currentInitialized;
    }

    private static readonly Dictionary<AimScopeController, ScopeState> _states = new Dictionary<AimScopeController, ScopeState>();

    public static void Register(AimScopeController scope, object key, float percent)
    {
        if (scope == null || key == null) return;

        var st = GetOrCreate(scope);

        int idx = st.entries.FindIndex(e => ReferenceEquals(e.key, key));
        if (idx >= 0) st.entries[idx].percent = Sanitize(percent);
        else st.entries.Add(new Entry { key = key, percent = Sanitize(percent) });

        if (!st.currentInitialized)
        {
            st.currentPercent = Sanitize(percent);
            st.currentInitialized = true;
        }

        ClampCurrentToRange(st);
    }

    public static void Unregister(AimScopeController scope, object key)
    {
        if (scope == null || key == null) return;
        if (!_states.TryGetValue(scope, out var st)) return;

        st.entries.RemoveAll(e => ReferenceEquals(e.key, key));

        if (st.entries.Count == 0)
        {
            _states.Remove(scope);
            return;
        }

        ClampCurrentToRange(st);
    }

    public static void UpdateValue(AimScopeController scope, object key, float percent)
    {
        if (scope == null || key == null) return;
        if (!_states.TryGetValue(scope, out var st)) return;

        int idx = st.entries.FindIndex(e => ReferenceEquals(e.key, key));
        if (idx < 0) return;

        st.entries[idx].percent = Sanitize(percent);
        ClampCurrentToRange(st);
    }

    public static bool TryGetRange(AimScopeController scope, out float minPercent, out float maxPercent, out float currentPercent)
    {
        minPercent = maxPercent = currentPercent = 1f;
        if (scope == null) return false;
        if (!_states.TryGetValue(scope, out var st)) return false;
        if (st.entries.Count == 0) return false;

        GetRange(st, out minPercent, out maxPercent);
        currentPercent = st.currentPercent;
        return true;
    }

    public static bool HasAny(AimScopeController scope)
    {
        return scope != null && _states.TryGetValue(scope, out var st) && st.entries.Count > 0;
    }

    public static float GetCurrentOrDefault(AimScopeController scope, float fallbackPercent)
    {
        if (scope == null) return fallbackPercent;
        if (!_states.TryGetValue(scope, out var st)) return fallbackPercent;
        if (st.entries.Count == 0) return fallbackPercent;

        return st.currentPercent;
    }

    public static float AdjustByScroll(AimScopeController scope, float wheelDeltaY, float step)
    {
        if (scope == null) return 1f;
        if (!_states.TryGetValue(scope, out var st)) return 1f;
        if (st.entries.Count == 0) return 1f;

        GetRange(st, out float minP, out float maxP);

        float cur = st.currentPercent;
        // wheel up (positive) => zoom in => smaller percent
        float next = cur - wheelDeltaY * Mathf.Max(0.0001f, step);
        next = Mathf.Clamp(next, minP, maxP);

        st.currentPercent = next;
        st.currentInitialized = true;
        return next;
    }

    private static ScopeState GetOrCreate(AimScopeController scope)
    {
        if (!_states.TryGetValue(scope, out var st))
        {
            st = new ScopeState();
            _states[scope] = st;
        }
        return st;
    }

    private static void ClampCurrentToRange(ScopeState st)
    {
        GetRange(st, out float minP, out float maxP);
        st.currentPercent = Mathf.Clamp(st.currentPercent, minP, maxP);
    }

    private static void GetRange(ScopeState st, out float minP, out float maxP)
    {
        minP = 1f;
        maxP = 0f;

        for (int i = 0; i < st.entries.Count; i++)
        {
            float p = st.entries[i].percent;
            if (p < minP) minP = p;
            if (p > maxP) maxP = p;
        }

        minP = Mathf.Clamp(minP, 0.05f, 1f);
        maxP = Mathf.Clamp(maxP, 0.05f, 1f);

        if (minP > maxP)
        {
            float t = minP;
            minP = maxP;
            maxP = t;
        }
    }

    private static float Sanitize(float p) => Mathf.Clamp(p, 0.05f, 1f);
}