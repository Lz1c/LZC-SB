using UnityEngine;

public class GunSpread : MonoBehaviour
{
    public enum SpreadShape
    {
        Cone,
        Diamond
    }

    [Header("Spread")]
    [Min(0f)] public float baseSpread = 0.2f;
    [Min(0f)] public float spreadIncreasePerShot = 0.12f;
    [Min(0f)] public float spreadRecoverSpeed = 2.5f;
    [Min(0f)] public float maxSpread = 6f;

    [Header("Recover Delay")]
    [Min(0f)]
    [Tooltip("开火键松开后，延迟多久才开始恢复散布（秒）。")]
    public float recoverDelayAfterFireKeyReleased = 0.08f;

    [Header("Growth Curve")]
    public AnimationCurve spreadGrowthCurve01 = AnimationCurve.Linear(0f, 1f, 1f, 1f);
    [Min(0f)] public float minIncreaseMultiplier = 0f;
    [Min(0f)] public float maxIncreaseMultiplier = 3f;

    [Header("Shotgun")]
    [Min(0f)] public float shotgunExtraSpread = 3.5f;

    [Header("Spread Shape")]
    public SpreadShape nonShotgunShape = SpreadShape.Diamond;
    public SpreadShape shotgunShape = SpreadShape.Cone;

    [Header("Shotgun Anisotropic (Perk-driven)")]
    [Min(0f)] public float shotgunHorizontalScale = 1f; // left-right
    [Min(0f)] public float shotgunVerticalScale = 1f;   // up-down

    private float _currentSpread;

    // 原来用“最后一发开火时间”做延迟，现在改成“开火键松开时间”
    private float _lastFireKeyReleaseTime = -999f;

    // 自动绑定当前枪，读取 fireKey
    private CameraGunChannel _gun;
    private bool _prevFireHeld;

    // NEW: cache GunStatContext if present (for SpreadRecoverySpeed)
    private GunStatContext _ctx;

    public float CurrentSpread => _currentSpread;

    private void Awake()
    {
        _currentSpread = baseSpread;

        // 自动找所属枪（GunSpread 通常挂在枪或枪的子物体上）
        _gun = GetComponentInParent<CameraGunChannel>();

        // NEW: try resolve GunStatContext from gun hierarchy
        if (_gun != null)
        {
            _ctx = _gun.GetComponent<GunStatContext>();
            if (_ctx == null) _ctx = _gun.GetComponentInParent<GunStatContext>();
        }
    }

    private void Update()
    {
        // 如果找不到枪，就退回“无输入判定”：直接按 recoverDelayAfterFireKeyReleased 走
        // （你也可以选择 return 直接恢复，但这里保持更安全的兼容）
        if (_gun == null)
        {
            _currentSpread = Mathf.MoveTowards(_currentSpread, baseSpread, spreadRecoverSpeed * Time.deltaTime);
            return;
        }

        // 读取当前枪的开火键按住状态
        bool fireHeld = Input.GetKey(_gun.fireKey);

        // 侦测“松开开火键”的瞬间：上一帧按住，这一帧没按住
        if (_prevFireHeld && !fireHeld)
        {
            _lastFireKeyReleaseTime = Time.time;
        }
        _prevFireHeld = fireHeld;

        // 规则 1：只要还按住开火键，就不恢复（持续压枪期间散布不回正）
        if (fireHeld)
            return;

        // 规则 2：松开后 X 秒内不恢复
        if (Time.time - _lastFireKeyReleaseTime < recoverDelayAfterFireKeyReleased)
            return;

        // 开始恢复
        // NEW: use stat-driven final recovery speed if available
        float finalRecoverSpeed = spreadRecoverSpeed;
        if (_ctx == null)
        {
            // late resolve (in case init order changed)
            _ctx = _gun.GetComponent<GunStatContext>();
            if (_ctx == null) _ctx = _gun.GetComponentInParent<GunStatContext>();
        }
        if (_ctx != null)
        {
            finalRecoverSpeed = _ctx.GetSpreadRecoverySpeedFinal();
        }

        _currentSpread = Mathf.MoveTowards(_currentSpread, baseSpread, Mathf.Max(0.0001f, finalRecoverSpeed) * Time.deltaTime);
    }

    public void OnShotFired()
    {
        AddBloomDegrees(ComputePerShotAddDegrees());
        // 注意：这里不再依赖“最后一发时间”做恢复延迟
        // 延迟完全由“开火键松开时间”控制
    }

    public void ApplyInterferenceDegrees(float degrees)
    {
        AddBloomDegrees(degrees);
    }

    public void ApplyInterferenceScaled(float scale)
    {
        AddBloomDegrees(ComputePerShotAddDegrees() * Mathf.Max(0f, scale));
    }

    private float ComputePerShotAddDegrees()
    {
        float denom = Mathf.Max(0.0001f, maxSpread - baseSpread);
        float t01 = Mathf.Clamp01((_currentSpread - baseSpread) / denom);

        float mult = spreadGrowthCurve01.Evaluate(t01);
        mult = Mathf.Clamp(mult, minIncreaseMultiplier, maxIncreaseMultiplier);

        return spreadIncreasePerShot * mult;
    }

    private void AddBloomDegrees(float add)
    {
        if (add <= 0f) return;

        _currentSpread = Mathf.Min(maxSpread, _currentSpread + add);
    }

    public float GetFinalSpread(bool isShotgun)
    {
        return _currentSpread + (isShotgun ? shotgunExtraSpread : 0f);
    }

    public float GetUiSpreadXDeg(bool isShotgun)
    {
        float spreadDeg = GetFinalSpread(isShotgun);
        if (!isShotgun) return spreadDeg;
        return spreadDeg * Mathf.Max(0f, shotgunHorizontalScale);
    }

    public float GetUiSpreadYDeg(bool isShotgun)
    {
        float spreadDeg = GetFinalSpread(isShotgun);
        if (!isShotgun) return spreadDeg;
        return spreadDeg * Mathf.Max(0f, shotgunVerticalScale);
    }

    public float GetUiSpreadDeg(bool isShotgun)
    {
        float x = GetUiSpreadXDeg(isShotgun);
        float y = GetUiSpreadYDeg(isShotgun);
        return Mathf.Max(x, y);
    }

    public Vector3 GetDirection(Vector3 forward, Vector3 right, Vector3 up, bool isShotgun)
    {
        float spreadDeg = GetFinalSpread(isShotgun);
        if (spreadDeg <= 0.001f) return forward.normalized;

        SpreadShape shape = isShotgun ? shotgunShape : nonShotgunShape;

        float hScale = isShotgun ? shotgunHorizontalScale : 1f;
        float vScale = isShotgun ? shotgunVerticalScale : 1f;

        bool useAniso = isShotgun && (Mathf.Abs(hScale - 1f) > 0.0001f || Mathf.Abs(vScale - 1f) > 0.0001f);

        if (shape == SpreadShape.Diamond)
            return ApplyDiamondSpread(forward, right, up, spreadDeg, hScale, vScale);

        if (useAniso)
            return ApplyEllipticalConeSpread(forward, right, up, spreadDeg, hScale, vScale);

        return ApplyConeSpread(forward, spreadDeg);
    }

    private Vector3 ApplyConeSpread(Vector3 forward, float angleDeg)
    {
        float angleRad = angleDeg * Mathf.Deg2Rad;

        float u = Random.value;
        float v = Random.value;

        float theta = 2f * Mathf.PI * u;
        float phi = Mathf.Acos(1f - v * (1f - Mathf.Cos(angleRad)));

        float x = Mathf.Sin(phi) * Mathf.Cos(theta);
        float y = Mathf.Sin(phi) * Mathf.Sin(theta);
        float z = Mathf.Cos(phi);

        Vector3 w = forward.normalized;
        Vector3 a = Mathf.Abs(Vector3.Dot(w, Vector3.up)) > 0.99f ? Vector3.right : Vector3.up;
        Vector3 uAxis = Vector3.Normalize(Vector3.Cross(a, w));
        Vector3 vAxis = Vector3.Cross(w, uAxis);

        return (uAxis * x + vAxis * y + w * z).normalized;
    }

    private Vector3 ApplyEllipticalConeSpread(Vector3 forward, Vector3 right, Vector3 up, float angleDeg, float hScale, float vScale)
    {
        float u = Random.value;
        float v = Random.value;
        float r = Mathf.Sqrt(u);
        float theta = 2f * Mathf.PI * v;

        float x = r * Mathf.Cos(theta);
        float y = r * Mathf.Sin(theta);

        float t = Mathf.Tan(angleDeg * Mathf.Deg2Rad);

        float xOff = x * t * Mathf.Max(0f, hScale);
        float yOff = y * t * Mathf.Max(0f, vScale);

        Vector3 dir = forward.normalized
                      + right.normalized * xOff
                      + up.normalized * yOff;

        return dir.normalized;
    }

    private Vector3 ApplyDiamondSpread(Vector3 forward, Vector3 right, Vector3 up, float angleDeg, float hScale, float vScale)
    {
        Vector2 p = SampleDiamond();
        float t = Mathf.Tan(angleDeg * Mathf.Deg2Rad);

        float xOff = p.x * t * Mathf.Max(0f, hScale);
        float yOff = p.y * t * Mathf.Max(0f, vScale);

        Vector3 dir = forward.normalized
                      + right.normalized * xOff
                      + up.normalized * yOff;

        return dir.normalized;
    }

    private Vector2 SampleDiamond()
    {
        while (true)
        {
            float x = Random.Range(-1f, 1f);
            float y = Random.Range(-1f, 1f);

            if (Mathf.Abs(x) + Mathf.Abs(y) <= 1f)
                return new Vector2(x, y);
        }
    }

    public float CurrentMaxDiamondSpread(bool isShotgun)
    {
        return GetFinalSpread(isShotgun);
    }
}