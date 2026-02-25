using System.Collections.Generic;
using UnityEngine;

public sealed class Perk_RecoilRecoveryFast : GunPerkModifierBase
{
    [Header("Effect (AddPct)")]
    [Tooltip("0.5 = +50% spread recovery speed (faster reset).")]
    [Min(0f)]
    public float spreadRecoverySpeedAddPct = 0.5f;

    [Header("Priority")]
    public int statPriority = -9999;

    public override int Priority => statPriority;

    public override void ApplyModifiers(CameraGunChannel source, Dictionary<GunStat, StatStack> stacks)
    {
        if (SourceGun == null || source != SourceGun) return;

        if (!stacks.TryGetValue(GunStat.SpreadRecoverySpeed, out var st))
            return;

        st.addPct += Mathf.Max(0f, spreadRecoverySpeedAddPct);
        stacks[GunStat.SpreadRecoverySpeed] = st;
    }
}