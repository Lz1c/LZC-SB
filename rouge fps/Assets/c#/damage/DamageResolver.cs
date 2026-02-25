using UnityEngine;

/// <summary>
/// 统一伤害结算：
/// - 读取 hitbox 倍率（通过 HitboxMultiplierManager 计算最终倍率）
/// - 护甲/状态/命中反馈/事件不改动
/// </summary>
public static class DamageResolver
{
    public static bool ApplyHit(
        DamageInfo baseInfo,
        Collider hitCol,
        Vector3 hitPoint,
        CameraGunChannel source,
        BulletArmorPayload armorPayload = null,
        BulletStatusPayload statusPayload = null,
        bool showHitUI = true
    )
    {
        if (hitCol == null) return false;

        var armorEx = hitCol.GetComponentInParent<IDamageableArmorEx>();
        var dmgEx = hitCol.GetComponentInParent<IDamageableEx>();
        var dmg = hitCol.GetComponentInParent<IDamageable>();

        if (armorEx == null && dmgEx == null && dmg == null)
            return false;

        // Hitbox
        float partMult = 1f;
        bool isHeadshot = false;

        var hb = hitCol.GetComponent<Hitbox>();
        if (hb != null)
        {
            isHeadshot = hb.part == Hitbox.Part.Head;

            // 关键：通过 HitboxMultiplierManager 获取“最终倍率”
            var mgr = HitboxMultiplierManager.Instance;
            if (mgr != null)
                partMult = mgr.ResolveMultiplier(source, hb);
            else
                partMult = Mathf.Max(0f, hb.damageMultiplier);
        }

        var info = baseInfo;
        info.source = source;
        info.isHeadshot = isHeadshot;
        info.hitPoint = hitPoint;
        info.hitCollider = hitCol;
        info.damage = Mathf.Max(0f, info.damage) * Mathf.Max(0f, partMult);

        // Apply damage
        if (armorEx != null && armorPayload != null)
        {
            ArmorHitInfo armorInfo = armorPayload.ToArmorHitInfo();
            armorEx.TakeDamage(info, armorInfo);
        }
        else if (dmgEx != null)
        {
            dmgEx.TakeDamage(info);
        }
        else
        {
            dmg.TakeDamage(info.damage);
        }

        // Status payload
        if (statusPayload != null && statusPayload.entries != null && statusPayload.entries.Length > 0)
        {
            var sc = hitCol.GetComponentInParent<StatusContainer>();
            if (sc != null)
            {
                for (int i = 0; i < statusPayload.entries.Length; i++)
                {
                    var e = statusPayload.entries[i];
                    sc.ApplyStatus(new StatusApplyRequest
                    {
                        type = e.type,
                        stacksToAdd = e.stacksToAdd,
                        duration = e.duration,
                        source = source,

                        tickInterval = e.tickInterval,
                        burnDamagePerTickPerStack = e.burnDamagePerTickPerStack,

                        slowPerStack = e.slowPerStack,
                        weakenPerStack = e.weakenPerStack,

                        shockChainDamagePerStack = e.shockChainDamagePerStack,
                        shockChainRadius = e.shockChainRadius,
                        shockMaxChains = e.shockMaxChains
                    });
                }
            }
        }

        // Hit feedback UI
        if (showHitUI)
        {
            var ui = HitFeedbackUI.Instance;
            if (ui == null) ui = Object.FindFirstObjectByType<HitFeedbackUI>();
            if (ui != null) ui.ShowHit(isHeadshot);
        }

        // Raise hit event
        CombatEventHub.RaiseHit(new CombatEventHub.HitEvent
        {
            source = source,
            target = (hitCol.GetComponentInParent<MonsterHealth>() != null)
                ? hitCol.GetComponentInParent<MonsterHealth>().gameObject
                : hitCol.gameObject,
            hitCollider = hitCol,
            hitPoint = hitPoint,
            damage = info.damage,
            isHeadshot = isHeadshot,
            time = Time.time,
            armorPayload = armorPayload,
            statusPayload = statusPayload
        });

        return true;
    }
}