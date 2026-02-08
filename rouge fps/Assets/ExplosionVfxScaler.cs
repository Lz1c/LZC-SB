using UnityEngine;
#if UNITY_VISUAL_EFFECT_GRAPH
using UnityEngine.VFX;
#endif

/// <summary>
/// 爆炸特效缩放接收器：
/// 让 VFX prefab 自己根据“爆炸半径”调整可生效的参数（比改 Transform Scale 稳定）
/// </summary>
public sealed class ExplosionVfxScaler : MonoBehaviour
{
    [Header("输入参数名（VFX Graph 用）")]
    [Tooltip("如果你的特效是 VFX Graph，请在 Graph 里暴露一个 float，并用这个名字接收半径。")]
    public string vfxGraphRadiusParam = "ExplosionRadius";

    [Header("ParticleSystem 缩放策略")]
    [Tooltip("ParticleSystem：startSizeMultiplier = radius * 该系数")]
    public float particleStartSizePerRadius = 0.5f;

    [Tooltip("ParticleSystem：shape.radius = radius * 该系数（如果你的爆炸形状用到了 Shape）")]
    public float particleShapeRadiusPerRadius = 1.0f;

    [Header("兜底（Transform）")]
    [Tooltip("如果上述方式都没命中，最后用 Transform 缩放兜底：localScale = 原始scale * (radius * 该系数)")]
    public float fallbackTransformScalePerRadius = 0.5f;

    private Vector3 _originalScale;

    private void Awake()
    {
        _originalScale = transform.localScale;
    }

    /// <summary>
    /// 由 Perk 调用：把爆炸半径传进来
    /// </summary>
    public void ApplyRadius(float radius)
    {
        if (radius <= 0f) return;

        bool applied = false;

        // 1) VFX Graph：通过暴露参数驱动（最推荐）
#if UNITY_VISUAL_EFFECT_GRAPH
        var vfx = GetComponentInChildren<VisualEffect>(true);
        if (vfx != null && !string.IsNullOrEmpty(vfxGraphRadiusParam))
        {
            vfx.SetFloat(vfxGraphRadiusParam, radius);
            applied = true;
        }
#endif

        // 2) ParticleSystem：改真正影响粒子的参数
        var psArray = GetComponentsInChildren<ParticleSystem>(true);
        if (psArray != null && psArray.Length > 0)
        {
            for (int i = 0; i < psArray.Length; i++)
            {
                var ps = psArray[i];
                if (ps == null) continue;

                // 2.1 start size（很常用）
                var main = ps.main;
                main.startSizeMultiplier = radius * particleStartSizePerRadius;

                // 2.2 shape 半径（如果你的爆炸依赖 shape）
                var shape = ps.shape;
                if (shape.enabled)
                {
                    shape.radius = radius * particleShapeRadiusPerRadius;
                }
            }

            applied = true;
        }

        // 3) 兜底：Transform（有些特效确实不吃这个，但不影响）
        if (!applied)
        {
            float s = Mathf.Max(0.0001f, radius * fallbackTransformScalePerRadius);
            transform.localScale = _originalScale * s;
        }
    }
}
