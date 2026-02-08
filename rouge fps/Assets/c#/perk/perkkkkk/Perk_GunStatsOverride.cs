using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 枪械基础数值设定 Perk（直接设定，不做倍率）
/// 可设置：
/// - 子弹伤害（baseDamage）
/// - 最大有效距离（maxRange）
/// - 开火速率（fireRate，单位：每秒发射次数）
/// - 子弹初速度（bulletSpeed）
/// - 后坐力曲线（接管 recoil：用曲线在时间上分配 recoil）
/// 
/// 归属哪把枪：由 PerkManager 的 Perk 列表决定（GunA / GunB）
/// 等级只在 PerkMeta 上填写，本脚本不使用等级
/// </summary>
public sealed class Perk_GunStatsOverride : MonoBehaviour
{
    [Header("前置条件")]
    [Tooltip("前置条件不满足时是否自动禁用该 Perk")]
    public bool disableIfPrereqMissing = true;

    [Header("基础数值（直接设定）")]
    [Tooltip("是否直接设定子弹伤害（CameraGunChannel.baseDamage）")]
    public bool setDamage = true;
    [Min(0f)] public float damage = 10f;

    [Tooltip("是否直接设定最大有效距离（CameraGunChannel.maxRange）")]
    public bool setMaxRange = true;
    [Min(0f)] public float maxRange = 200f;

    [Tooltip("是否直接设定开火速率（CameraGunChannel.fireRate，单位：每秒发射次数）")]
    public bool setFireRate = true;
    [Min(0.01f)] public float fireRate = 10f;

    [Tooltip("是否直接设定子弹初速度（CameraGunChannel.bulletSpeed）")]
    public bool setBulletSpeed = true;
    [Min(0.01f)] public float bulletSpeed = 80f;

    [Header("后坐力曲线（接管 GunRecoil）")]
    [Tooltip("是否启用“后坐力曲线接管”。启用后会把 GunRecoil 的原始 kick 置零，然后按曲线施加 recoil。")]
    public bool enableRecoilCurve = false;

    [Tooltip("后坐力持续时间（秒）。曲线的横轴会按 0~1 映射到这个时长。")]
    [Min(0.01f)] public float recoilDuration = 0.12f;

    [Tooltip("后坐力俯仰总量（度）。曲线的纵轴是“分配比例”，最终会累计到这个总量。")]
    [Min(0f)] public float recoilPitchTotal = 1.2f;

    [Tooltip("左右随机总量（度）。每次开枪会随机一个 yaw 方向，并用曲线分配到 recoilDuration 内。")]
    [Min(0f)] public float recoilYawRandomTotal = 0.6f;

    [Tooltip("后坐力曲线：输入 0~1（时间进度），输出 0~1（该时刻的分配权重）。建议用从 0 到 1 的平滑曲线。")]
    public AnimationCurve recoilCurve01 = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    private PerkManager _pm;

    private struct GunState
    {
        public float baseDamage;
        public float maxRange;
        public float fireRate;
        public float bulletSpeed;

        // 用于恢复 GunRecoil 原始参数
        public float kickPitchPerShot;
        public float kickYawRandom;
    }

    private readonly Dictionary<CameraGunChannel, GunState> _saved = new();
    private bool _applied;

    // 用于解除事件绑定
    private CameraGunChannel _boundGun;
    private GunRecoil _boundRecoil;

    private Coroutine _recoilRoutine;

    private void Awake()
    {
        _pm = FindFirstObjectByType<PerkManager>();
    }

    private void OnEnable()
    {
        _pm ??= FindFirstObjectByType<PerkManager>();
        if (_pm == null) return;

        int gunIndex = ResolveGunIndexFromManager();
        if (gunIndex < 0)
        {
            // 说明该 Perk 尚未被注册进任何枪的 Perk 列表
            enabled = false;
            return;
        }

        if (disableIfPrereqMissing && !_pm.PrerequisitesMet(gameObject, gunIndex))
        {
            enabled = false;
            return;
        }

        Apply(gunIndex);
    }

    private void OnDisable()
    {
        Revert();
    }

    private void OnDestroy()
    {
        Revert();
    }

    /// <summary>
    /// 通过 PerkManager 的 Perk 列表判断该 Perk 属于哪把枪
    /// </summary>
    private int ResolveGunIndexFromManager()
    {
        if (_pm.selectedPerksGunA.Contains(this)) return 0;
        if (_pm.selectedPerksGunB.Contains(this)) return 1;
        return -1;
    }

    private void Apply(int gunIndex)
    {
        if (_applied) return;

        var gunRefs = _pm.GetGun(gunIndex);
        var gun = gunRefs != null ? gunRefs.cameraGunChannel : null;
        if (gun == null) return;

        _saved.Clear();
        SaveIfNeeded(gun, gunRefs.gunRecoil);

        // 直接设定基础数据
        if (setDamage) gun.baseDamage = Mathf.Max(0f, damage);
        if (setMaxRange) gun.maxRange = Mathf.Max(0f, maxRange);
        if (setFireRate) gun.fireRate = Mathf.Max(0.01f, fireRate);
        if (setBulletSpeed) gun.bulletSpeed = Mathf.Max(0.01f, bulletSpeed);

        // 后坐力曲线接管
        if (enableRecoilCurve)
        {
            BindRecoilCurve(gun, gunRefs.gunRecoil);
        }

        _applied = true;
    }

    private void Revert()
    {
        if (!_applied) return;

        // 解除事件绑定与协程
        UnbindRecoilCurve();

        // 恢复原始枪数值与 recoil 参数
        foreach (var kv in _saved)
        {
            var gun = kv.Key;
            if (gun == null) continue;

            var s = kv.Value;
            gun.baseDamage = s.baseDamage;
            gun.maxRange = s.maxRange;
            gun.fireRate = s.fireRate;
            gun.bulletSpeed = s.bulletSpeed;

            // 恢复 GunRecoil 参数（如果当前枪上还有 recoil）
            var recoil = gun.recoil;
            if (recoil != null)
            {
                recoil.kickPitchPerShot = s.kickPitchPerShot;
                recoil.kickYawRandom = s.kickYawRandom;
            }
        }

        _saved.Clear();
        _applied = false;
    }

    private void SaveIfNeeded(CameraGunChannel gun, GunRecoil recoil)
    {
        if (gun == null) return;
        if (_saved.ContainsKey(gun)) return;

        float pitch = 0f, yaw = 0f;
        if (recoil != null)
        {
            pitch = recoil.kickPitchPerShot;
            yaw = recoil.kickYawRandom;
        }

        _saved.Add(gun, new GunState
        {
            baseDamage = gun.baseDamage,
            maxRange = gun.maxRange,
            fireRate = gun.fireRate,
            bulletSpeed = gun.bulletSpeed,
            kickPitchPerShot = pitch,
            kickYawRandom = yaw
        });
    }

    /// <summary>
    /// 接管后坐力：监听枪的 OnShot，并在每次射击时按曲线施加 recoil
    /// </summary>
    private void BindRecoilCurve(CameraGunChannel gun, GunRecoil recoil)
    {
        if (gun == null) return;

        _boundGun = gun;
        _boundRecoil = recoil;

        // 为避免“双重后坐力”，把原本 GunRecoil 的瞬时 kick 置零
        if (_boundRecoil != null)
        {
            _boundRecoil.kickPitchPerShot = 0f;
            _boundRecoil.kickYawRandom = 0f;
        }

        // 防止重复绑定
        _boundGun.OnShot -= HandleShot;
        _boundGun.OnShot += HandleShot;
    }

    private void UnbindRecoilCurve()
    {
        if (_boundGun != null)
        {
            _boundGun.OnShot -= HandleShot;
        }

        if (_recoilRoutine != null)
        {
            StopCoroutine(_recoilRoutine);
            _recoilRoutine = null;
        }

        _boundGun = null;
        _boundRecoil = null;
    }

    private void HandleShot(CameraGunChannel gun)
    {
        // 每次开枪都触发一次曲线后坐力
        if (!enableRecoilCurve) return;
        if (gun == null) return;

        // 找到 “look” 对象（GunRecoil.look），并通过反射调用 AddRecoil(pitch, yaw)
        object lookObj = null;
        if (gun.recoil != null) lookObj = gun.recoil.look;

        if (lookObj == null) return;

        // 停掉上一次的曲线（避免叠太多导致异常手感）
        if (_recoilRoutine != null)
        {
            StopCoroutine(_recoilRoutine);
            _recoilRoutine = null;
        }

        float yaw = (recoilYawRandomTotal <= 0f) ? 0f : Random.Range(-recoilYawRandomTotal, recoilYawRandomTotal);
        _recoilRoutine = StartCoroutine(ApplyRecoilCurveRoutine(lookObj, recoilPitchTotal, yaw));
    }

    private IEnumerator ApplyRecoilCurveRoutine(object lookObj, float pitchTotal, float yawTotal)
    {
        float dur = Mathf.Max(0.01f, recoilDuration);

        // 预采样：把曲线的“面积”归一化，保证总量稳定
        // 这里用固定采样点近似积分
        const int samples = 24;
        float area = 0f;
        float prevT = 0f;
        float prevV = EvalCurve01(0f);

        for (int i = 1; i <= samples; i++)
        {
            float t = i / (float)samples;
            float v = EvalCurve01(t);
            float dt = t - prevT;
            area += (prevV + v) * 0.5f * dt;
            prevT = t;
            prevV = v;
        }

        // 如果曲线面积太小，就当成均匀分配
        if (area <= 0.0001f) area = 1f;

        float elapsed = 0f;
        float appliedPitch = 0f;
        float appliedYaw = 0f;

        while (elapsed < dur)
        {
            float dtSec = Time.deltaTime;
            elapsed += dtSec;

            float t01 = Mathf.Clamp01(elapsed / dur);
            float w = EvalCurve01(t01);

            // 这帧应当分配的比例（按面积归一）
            // 用 w/area 再乘以 dt(按 0~1 时间)，近似让总量等于 pitchTotal/yawTotal
            float dt01 = dtSec / dur;
            float portion = (w / area) * dt01;

            float targetPitchThis = pitchTotal * portion;
            float targetYawThis = yawTotal * portion;

            appliedPitch += targetPitchThis;
            appliedYaw += targetYawThis;

            CallAddRecoil(lookObj, targetPitchThis, targetYawThis);

            yield return null;
        }

        // 末尾做一次“差值补偿”，确保累计量严格等于总量
        float pitchDiff = pitchTotal - appliedPitch;
        float yawDiff = yawTotal - appliedYaw;

        if (Mathf.Abs(pitchDiff) > 0.0001f || Mathf.Abs(yawDiff) > 0.0001f)
        {
            CallAddRecoil(lookObj, pitchDiff, yawDiff);
        }

        _recoilRoutine = null;
    }

    private float EvalCurve01(float t01)
    {
        if (recoilCurve01 == null) return 1f;
        return Mathf.Clamp01(recoilCurve01.Evaluate(t01));
    }

    /// <summary>
    /// 通过反射调用 look.AddRecoil(float pitch, float yaw)
    /// 这样不需要在此脚本里引用 PrototypeFPC 的类型，避免程序集差异导致编译问题
    /// </summary>
    private static void CallAddRecoil(object lookObj, float pitch, float yaw)
    {
        if (lookObj == null) return;

        var type = lookObj.GetType();
        var method = type.GetMethod("AddRecoil", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        if (method == null) return;

        try
        {
            method.Invoke(lookObj, new object[] { pitch, yaw });
        }
        catch
        {
            // 这里不抛异常，避免影响游戏运行
        }
    }
}
