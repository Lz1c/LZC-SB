#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Perk_GrantStatusOnHit 的自定义 Inspector：
/// 选择异常类型后，只显示该异常相关的数值配置。
/// </summary>
[CustomEditor(typeof(Perk_GrantStatusOnHit))]
public sealed class Perk_GrantStatusOnHitEditor : Editor
{
    // 需要显示/隐藏的序列化字段
    private SerializedProperty _grantType;

    private SerializedProperty _procChance;
    private SerializedProperty _stacksToAdd;
    private SerializedProperty _duration;

    private SerializedProperty _burnTickInterval;
    private SerializedProperty _burnDamagePerTickPerStack;

    private SerializedProperty _weakenPerStack;

    private SerializedProperty _slowPerStack;

    private SerializedProperty _shockChainDamagePerStack;
    private SerializedProperty _shockChainRadius;
    private SerializedProperty _shockMaxChains;

    private void OnEnable()
    {
        // 基础配置
        _grantType = serializedObject.FindProperty(nameof(Perk_GrantStatusOnHit.grantType));

        _procChance = serializedObject.FindProperty(nameof(Perk_GrantStatusOnHit.procChance));
        _stacksToAdd = serializedObject.FindProperty(nameof(Perk_GrantStatusOnHit.stacksToAdd));
        _duration = serializedObject.FindProperty(nameof(Perk_GrantStatusOnHit.duration));

        // 燃烧
        _burnTickInterval = serializedObject.FindProperty(nameof(Perk_GrantStatusOnHit.burnTickInterval));
        _burnDamagePerTickPerStack = serializedObject.FindProperty(nameof(Perk_GrantStatusOnHit.burnDamagePerTickPerStack));

        // 中毒
        _weakenPerStack = serializedObject.FindProperty(nameof(Perk_GrantStatusOnHit.weakenPerStack));

        // 减速
        _slowPerStack = serializedObject.FindProperty(nameof(Perk_GrantStatusOnHit.slowPerStack));

        // 电击
        _shockChainDamagePerStack = serializedObject.FindProperty(nameof(Perk_GrantStatusOnHit.shockChainDamagePerStack));
        _shockChainRadius = serializedObject.FindProperty(nameof(Perk_GrantStatusOnHit.shockChainRadius));
        _shockMaxChains = serializedObject.FindProperty(nameof(Perk_GrantStatusOnHit.shockMaxChains));
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        // === 异常类型 ===
        EditorGUILayout.PropertyField(_grantType);

        EditorGUILayout.Space(6);

        // === 通用设置（所有异常都需要）===
        EditorGUILayout.LabelField("触发设置", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(_procChance);
        EditorGUILayout.PropertyField(_stacksToAdd);
        EditorGUILayout.PropertyField(_duration);

        EditorGUILayout.Space(10);

        // 根据当前选择的异常类型，只显示对应参数
        var gt = (Perk_GrantStatusOnHit.GrantType)_grantType.enumValueIndex;

        switch (gt)
        {
            case Perk_GrantStatusOnHit.GrantType.燃烧:
                DrawBurn();
                break;

            case Perk_GrantStatusOnHit.GrantType.中毒:
                DrawPoison();
                break;

            case Perk_GrantStatusOnHit.GrantType.减速:
                DrawSlow();
                break;

            case Perk_GrantStatusOnHit.GrantType.电击:
                DrawShock();
                break;
        }

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawBurn()
    {
        EditorGUILayout.LabelField("燃烧(Burn)参数", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(_burnTickInterval);
        EditorGUILayout.PropertyField(_burnDamagePerTickPerStack);
    }

    private void DrawPoison()
    {
        EditorGUILayout.LabelField("中毒(Poison)参数", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(_weakenPerStack);
    }

    private void DrawSlow()
    {
        EditorGUILayout.LabelField("减速(Slow)参数", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(_slowPerStack);
    }

    private void DrawShock()
    {
        EditorGUILayout.LabelField("电击(Shock)参数", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(_shockChainDamagePerStack);
        EditorGUILayout.PropertyField(_shockChainRadius);
        EditorGUILayout.PropertyField(_shockMaxChains);
    }
}
#endif