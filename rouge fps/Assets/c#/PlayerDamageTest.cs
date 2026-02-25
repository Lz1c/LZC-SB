using UnityEngine;

/// <summary>
/// 测试脚本：按下指定按键时扣除玩家固定血量
/// 用于测试低血触发类 Perk / UI 更新 / 数值刷新
/// </summary>
public sealed class PlayerDamageTest : MonoBehaviour
{
    [Header("按键设置")]
    [Tooltip("按下该按键时扣血")]
    public KeyCode damageKey = KeyCode.K;

    [Header("扣血数值")]
    [Min(1)]
    [Tooltip("每次按键扣除的血量")]
    public int damageAmount = 5;

    [Header("是否无视护甲")]
    [Tooltip("为 true 时使用真实伤害（直接扣血）；为 false 时先扣护甲")]
    public bool ignoreArmor = true;

    private PlayerVitals _playerVitals;

    private void Awake()
    {
        // 自动寻找玩家血量组件
        _playerVitals = FindFirstObjectByType<PlayerVitals>();

        if (_playerVitals == null)
        {
            Debug.LogError("[PlayerDamageTest] 场景中未找到 PlayerVitals 组件。");
        }
    }

    private void Update()
    {
        if (_playerVitals == null) return;

        if (Input.GetKeyDown(damageKey))
        {
            ApplyDamage();
        }
    }

    private void ApplyDamage()
    {
        if (_playerVitals.IsDead) return;

        if (ignoreArmor)
        {
            // 使用真实伤害（无视护甲）
            _playerVitals.ApplyTrueDamage(damageAmount);
        }
        else
        {
            // 使用普通伤害（先扣护甲再扣血）
            _playerVitals.ApplyNormalDamage(damageAmount);
        }

        Debug.Log($"[PlayerDamageTest] 扣除 {damageAmount} 点血量。当前 HP = {_playerVitals.hp}");
    }
}