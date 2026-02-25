using System;
using UnityEngine;

/// <summary>
/// Player vitals (HP + Armor), integer-only.
/// - HP/Armor are stored as ints (what you see is what you have).
/// - Incoming damage is floored to an int; if < 1 it is ignored.
/// - Normal damage consumes Armor first, then HP.
/// - Provides an explicit API to bypass Armor (true damage).
///
/// Compatible with the project's damage pipeline:
/// - IDamageable / IDamageableEx for generic hits
/// - IDamageableArmorEx for ArmorHitInfo-aware hits
/// </summary>
public sealed class PlayerVitals : MonoBehaviour, IDamageable, IDamageableEx, IDamageableArmorEx
{
    [Header("Health (Int)")]
    [Min(1)] public int maxHp = 100;
    [Min(0)] public int hp = 100;

    [Header("Armor (Int)")]
    [Min(0)] public int maxArmor = 50;
    [Min(0)] public int armor = 50;

    [Header("State")]
    [SerializeField] private bool isDead;

    public bool IsDead => isDead || hp <= 0;

    public event Action<int, int> OnHpChanged;       // (current, max)
    public event Action<int, int> OnArmorChanged;    // (current, max)
    public event Action OnDied;

    private void Awake()
    {
        if (maxHp < 1) maxHp = 1;
        hp = Mathf.Clamp(hp, 0, maxHp);

        if (maxArmor < 0) maxArmor = 0;
        armor = Mathf.Clamp(armor, 0, maxArmor);
    }

    // -----------------------------
    // Public convenience APIs
    // -----------------------------

    /// <summary>Normal damage: Armor first, then HP.</summary>
    public void ApplyNormalDamage(float amount)
    {
        int dmg = FloorToIntDamage(amount);
        if (dmg < 1 || IsDead) return;
        ApplyDamageInternal(dmg, ignoreArmor: false, armorInfo: ArmorHitInfo.Default);
    }

    /// <summary>True damage: bypass Armor, directly reduces HP.</summary>
    public void ApplyTrueDamage(float amount)
    {
        int dmg = FloorToIntDamage(amount);
        if (dmg < 1 || IsDead) return;
        ApplyDamageInternal(dmg, ignoreArmor: true, armorInfo: ArmorHitInfo.Default);
    }

    public void Heal(float amount)
    {
        int add = FloorToIntDamage(amount);
        if (add < 1 || IsDead) return;

        int before = hp;
        hp = Mathf.Clamp(hp + add, 0, maxHp);

        if (before != hp)
            OnHpChanged?.Invoke(hp, maxHp);
    }

    public void RestoreArmor(float amount)
    {
        int add = FloorToIntDamage(amount);
        if (add < 1 || IsDead) return;

        int before = armor;
        armor = Mathf.Clamp(armor + add, 0, maxArmor);

        if (before != armor)
            OnArmorChanged?.Invoke(armor, maxArmor);
    }

    // -----------------------------
    // Interface: IDamageable (legacy)
    // -----------------------------

    /// <summary>Legacy float damage path. Treated as NORMAL damage (Armor first).</summary>
    public void TakeDamage(float damage)
    {
        ApplyNormalDamage(damage);
    }

    // -----------------------------
    // Interface: IDamageableEx
    // -----------------------------

    /// <summary>Extended damage path (DamageInfo). Treated as NORMAL damage by default.</summary>
    public void TakeDamage(DamageInfo info)
    {
        int dmg = FloorToIntDamage(info.damage);
        if (dmg < 1 || IsDead) return;
        ApplyDamageInternal(dmg, ignoreArmor: false, armorInfo: ArmorHitInfo.Default);
    }

    /// <summary>Explicit bypass-armor hook for special attacks.</summary>
    public void TakeDamageIgnoreArmor(DamageInfo info)
    {
        int dmg = FloorToIntDamage(info.damage);
        if (dmg < 1 || IsDead) return;
        ApplyDamageInternal(dmg, ignoreArmor: true, armorInfo: ArmorHitInfo.Default);
    }

    // -----------------------------
    // Interface: IDamageableArmorEx
    // -----------------------------

    /// <summary>
    /// Armor-aware hit path with ArmorHitInfo:
    /// - piercePercent/pierceFlat: direct HP damage (bypasses Armor)
    /// - armorDamageMultiplier: scales damage applied to Armor
    /// - shatterFlat/shatterPercentOfArmorDamage: additional Armor break
    /// Remaining damage overflows to HP.
    /// </summary>
    public void TakeDamage(DamageInfo info, ArmorHitInfo armorInfo)
    {
        int dmg = FloorToIntDamage(info.damage);
        if (dmg < 1 || IsDead) return;
        ApplyDamageInternal(dmg, ignoreArmor: false, armorInfo: armorInfo);
    }

    // -----------------------------
    // Core logic (integer-only)
    // -----------------------------

    private static int FloorToIntDamage(float value)
    {
        // "Ignore decimals" + "< 1 means no damage"
        if (value < 1f) return 0;
        return Mathf.FloorToInt(value);
    }

    private void ApplyDamageInternal(int rawDamage, bool ignoreArmor, ArmorHitInfo armorInfo)
    {
        if (rawDamage < 1 || IsDead) return;

        int hpBefore = hp;
        int armorBefore = armor;

        int directHp = 0;

        if (ignoreArmor)
        {
            directHp = rawDamage;
        }
        else
        {
            float piercePct = Mathf.Clamp01(armorInfo.piercePercent);
            int pierceFromPct = Mathf.FloorToInt(rawDamage * piercePct);

            int pierceFlat = FloorToIntDamage(armorInfo.pierceFlat);

            directHp = pierceFromPct + pierceFlat;
            if (directHp > rawDamage) directHp = rawDamage;
            if (directHp < 0) directHp = 0;
        }

        if (directHp > 0)
        {
            hp = Mathf.Max(0, hp - directHp);
        }

        int remaining = rawDamage - directHp;
        if (remaining < 0) remaining = 0;

        if (!ignoreArmor && remaining > 0 && armor > 0)
        {
            float armorMult = Mathf.Max(0f, armorInfo.armorDamageMultiplier);
            int armorDamage = Mathf.FloorToInt(remaining * armorMult);
            if (armorDamage < 0) armorDamage = 0;

            int armorTaken = Mathf.Min(armor, armorDamage);
            armor = Mathf.Max(0, armor - armorTaken);

            if (armorTaken > 0)
            {
                float shatterRaw =
                    Mathf.Max(0f, armorInfo.shatterFlat) +
                    Mathf.Max(0f, armorInfo.shatterPercentOfArmorDamage) * armorTaken;

                int shatter = FloorToIntDamage(shatterRaw);
                if (shatter > 0)
                    armor = Mathf.Max(0, armor - shatter);
            }

            // Overflow uses remaining (not armorDamage), consistent with "damage budget"
            int overflowToHp = remaining - armorTaken;
            if (overflowToHp > 0)
                hp = Mathf.Max(0, hp - overflowToHp);
        }
        else if (remaining > 0)
        {
            // No armor (or bypass), remaining goes to HP
            hp = Mathf.Max(0, hp - remaining);
        }

        if (armorBefore != armor)
            OnArmorChanged?.Invoke(armor, maxArmor);

        if (hpBefore != hp)
            OnHpChanged?.Invoke(hp, maxHp);

        if (!isDead && hp <= 0)
        {
            isDead = true;
            OnDied?.Invoke();
        }
    }
}