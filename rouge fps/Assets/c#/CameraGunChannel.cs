using UnityEngine;

public class CameraGunChannel : MonoBehaviour
{
    public enum Role { Primary, Secondary }
    public enum FireMode { Semi, Auto }
    public enum ShotType { Single, Shotgun }
    public enum BallisticsMode { Hitscan, Projectile }

    [Header("Role")]
    public Role role = Role.Primary;

    [Header("Input")]
    public KeyCode fireKey = KeyCode.Mouse0;

    [Header("Refs")]
    public Transform firePoint;
    public GunAmmo ammo;
    public GunRecoil recoil;
    public GunSpread spread;

    [Header("Modes")]
    public FireMode fireMode = FireMode.Auto;
    public ShotType shotType = ShotType.Single;
    public BallisticsMode ballisticsMode = BallisticsMode.Hitscan;

    [Header("Fire")]
    [Min(0.01f)] public float fireRate = 10f;
    [Min(0f)] public float maxRange = 200f;
    [Min(1)] public int pelletsPerShot = 8;

    [Header("Semi Extra Cooldown")]
    [Min(0f)] public float semiFireCooldown = 0.15f;

    [Header("Projectile")]
    public BulletProjectile projectilePrefab;
    [Min(0.01f)] public float bulletSpeed = 80f;
    [Min(0f)] public float bulletGravity = 0f;
    [Min(0.01f)] public float bulletLifetime = 3f;

    [Header("Projectile Damage Falloff（实体子弹专用）")]
    [Min(0f)]
    [Tooltip("实体子弹从多少米开始线性衰减伤害：\n- <=该距离：满伤害\n- 到 maxRange：衰减到 0\n注意：只对 Projectile 生效，Hitscan 仍使用下方曲线。")]
    public float projectileFalloffStartMeters = 0f;

    [Header("Damage")]
    [Min(0f)] public float baseDamage = 10f;

    [Tooltip("仅对 Hitscan 生效（FireHitscan 使用该曲线）。Projectile 已改为使用 projectileFalloffStartMeters 线性衰减。")]
    public AnimationCurve damageFalloffByDistance01 = AnimationCurve.Linear(0f, 1f, 1f, 1f);

    [Header("Hit")]
    public LayerMask hitMask = ~0;

    public System.Action<CameraGunChannel> OnShot;

    private float _nextFireTime;
    private float _nextSemiAllowedTime;

    private CameraGunDual _dual;

    private void Awake()
    {
        if (firePoint == null) firePoint = transform;

        AutoAssignIfSafe();

        _dual = GetComponent<CameraGunDual>();
        if (_dual == null) _dual = FindFirstObjectByType<CameraGunDual>();

        HookAmmoEvents();
    }

    private void OnEnable()
    {
        HookAmmoEvents();
    }

    private void OnDisable()
    {
        UnhookAmmoEvents();
    }

    private void OnDestroy()
    {
        UnhookAmmoEvents();
    }

    private void Update()
    {
        HandleFireInput();
    }

    public bool HasValidSetup()
    {
        return firePoint != null && ammo != null && recoil != null && spread != null;
    }

    public void ApplyInterferenceScaled(float scale)
    {
        if (spread == null) return;
        spread.ApplyInterferenceScaled(scale);
    }

    private void HandleFireInput()
    {
        if (!HasValidSetup()) return;

        // 弹匣换弹不可中断阶段：完全禁止开火
        if (_dual != null && _dual.ShouldBlockFiring())
            return;

        // 是否想开火：交由 Dual 决定，保证半自动能抢占自动
        bool wantsFire = (_dual != null)
            ? _dual.GetWantsFire(this)
            : (fireMode == FireMode.Auto ? Input.GetKey(fireKey) : Input.GetKeyDown(fireKey));

        if (!wantsFire) return;

        // 半自动：只用 semiFireCooldown 限制，不受 fireRate/_nextFireTime 影响
        if (fireMode == FireMode.Semi && semiFireCooldown > 0f)
        {
            if (Time.time < _nextSemiAllowedTime) return;
        }

        // 遂发填弹进行中：开火会中断遂发填弹（弹匣阶段不可中断）
        if (_dual != null)
            _dual.InterruptPerBulletReloadForFire();

        if (ammo.IsReloading && ammo.IsUninterruptible) return;

        // 自动：受 fireRate 限制
        if (fireMode == FireMode.Auto)
        {
            if (Time.time < _nextFireTime) return;
        }

        if (!ammo.HasAmmoInMag())
        {
            // 自动换弹由 Dual 处理
            return;
        }

        if (!ammo.TryConsumeOne())
            return;

        OnShot?.Invoke(this);

        recoil.Kick();
        spread.OnShotFired();

        // ===================== 关键改动 1：霰弹枪弹丸数量走“最终倍率” =====================
        // 这样 Perk 的“弹丸数量 /2”可以作为开火瞬间最后一步生效（等价最高优先级）
        int shots = (shotType == ShotType.Shotgun) ? GetFinalPelletsPerShot() : 1;
        bool isShotgun = (shotType == ShotType.Shotgun);

        for (int i = 0; i < shots; i++)
        {
            Vector3 dir = spread.GetDirection(
                firePoint.forward,
                firePoint.right,
                firePoint.up,
                isShotgun
            );

            if (ballisticsMode == BallisticsMode.Hitscan)
                FireHitscan(dir);
            else
                FireProjectile(dir);
        }

        // 自动：冷却按最终 fireRate 计算（GunStatContext 会回写）
        if (fireMode == FireMode.Auto)
            _nextFireTime = Time.time + (1f / Mathf.Max(0.01f, fireRate));

        // Fire 事件：pellets 是本次真实发射数量
        CombatEventHub.RaiseFire(new CombatEventHub.FireEvent
        {
            source = this,
            pellets = shots,
            isProjectile = (ballisticsMode == BallisticsMode.Projectile),
            time = Time.time
        });

        // 半自动：只受 semiFireCooldown 限制
        if (fireMode == FireMode.Semi && semiFireCooldown > 0f)
            _nextSemiAllowedTime = Time.time + semiFireCooldown;
    }

    /// <summary>
    /// 获取本次开火的“最终弹丸数”
    /// - 在开火瞬间读取倍率，保证效果永远在所有其它修改之后生效
    /// - 例如 Perk：弹丸数量/2 => mul=0.5
    /// </summary>
    public int GetFinalPelletsPerShot()
    {
        int basePellets = Mathf.Max(1, pelletsPerShot);

        // 注意：你需要在项目里有 PelletCountMultiplierRegistry（我之前给你的注册表脚本）
        float mul = PelletCountMultiplierRegistry.GetFinalMultiplier(this);

        // 使用 Floor：更符合“/2”的直觉（7 -> 3），并且永远保底 1
        int finalPellets = Mathf.FloorToInt(basePellets * mul + 0.0001f);
        return Mathf.Max(1, finalPellets);
    }

    private void FireHitscan(Vector3 dir)
    {
        Ray ray = new Ray(firePoint.position, dir);

        if (Physics.Raycast(ray, out RaycastHit hit, maxRange, hitMask, QueryTriggerInteraction.Ignore))
        {
            // Hitscan：用曲线做衰减（保持你原来的算法）
            float mr = maxRange;
            float t01 = mr <= 0.0001f ? 1f : Mathf.Clamp01(hit.distance / mr);
            float mult = Mathf.Max(0f, damageFalloffByDistance01.Evaluate(t01));
            float finalDamage = baseDamage * mult;

            // ===================== 关键改动 2：命中结算走 DamageResolver =====================
            // 目的：让 HitboxMultiplierManager 可以在当次命中就影响倍率（包括你要的“爆头倍率 add pct”）
            // 同时保留护甲、状态、事件、UI 等统一链路
            var armorPayload = GetComponentInChildren<BulletArmorPayload>(true);
            var statusPayload = GetComponentInChildren<BulletStatusPayload>(true);

            DamageResolver.ApplyHit(
                baseInfo: new DamageInfo
                {
                    source = this,
                    damage = finalDamage
                },
                hitCol: hit.collider,
                hitPoint: hit.point,
                source: this,
                armorPayload: armorPayload,
                statusPayload: statusPayload,
                showHitUI: true
            );
        }
    }

    private void FireProjectile(Vector3 dir)
    {
        if (projectilePrefab == null) return;

        // 1) 生成实体子弹
        BulletProjectile p = Instantiate(projectilePrefab, firePoint.position, Quaternion.LookRotation(dir));
        p.gameObject.SetActive(true);

        // 2) 如果子弹上挂了 BulletHitDamage（兼容你现有逻辑）
        var hitDamage = p.GetComponentInChildren<BulletHitDamage>(true);
        if (hitDamage != null)
        {
            hitDamage.source = this;
            hitDamage.baseDamage = baseDamage;

            // 保持你现有 API 行为
            hitDamage.Init(baseDamage, this);
        }

        // 3) 确保可见
        var r = p.GetComponentInChildren<Renderer>();
        if (r != null) r.enabled = true;

        // 4) 初始化子弹配置
        // 注意：Projectile 的伤害衰减已经改为“从 projectileFalloffStartMeters 开始线性衰减到 maxRange”
        // 不再使用 damageFalloffByDistance01 曲线
        p.Init(new BulletProjectile.Config
        {
            source = this,

            speed = Mathf.Max(0.01f, bulletSpeed),
            gravity = bulletGravity,
            lifetime = bulletLifetime,

            maxRange = maxRange,
            baseDamage = baseDamage,

            falloffStartMeters = projectileFalloffStartMeters,

            hitMask = hitMask
        });
    }

    public void FireBonusPellets(int pellets)
    {
        // 额外子弹不消耗冷却，也不再次触发 OnShot
        if (!HasValidSetup()) return;
        if (pellets <= 0) return;

        bool isShotgun = (shotType == ShotType.Shotgun);

        for (int i = 0; i < pellets; i++)
        {
            Vector3 dir = spread.GetDirection(
                firePoint.forward,
                firePoint.right,
                firePoint.up,
                isShotgun
            );

            if (ballisticsMode == BallisticsMode.Hitscan)
                FireHitscan(dir);
            else
                FireProjectile(dir);
        }
    }

    private void AutoAssignIfSafe()
    {
        if (ammo == null)
        {
            var arr = GetComponents<GunAmmo>();
            if (arr.Length == 1) ammo = arr[0];
        }

        if (recoil == null)
        {
            var arr = GetComponents<GunRecoil>();
            if (arr.Length == 1) recoil = arr[0];
        }

        if (spread == null)
        {
            var arr = GetComponents<GunSpread>();
            if (arr.Length == 1) spread = arr[0];
        }
    }

    private void HookAmmoEvents()
    {
        if (ammo == null) return;

        // 防止重复订阅
        ammo.OnReloadStart -= HandleReloadStart;
        ammo.OnReloadEnd -= HandleReloadEnd;

        ammo.OnReloadStart += HandleReloadStart;
        ammo.OnReloadEnd += HandleReloadEnd;
    }

    private void UnhookAmmoEvents()
    {
        if (ammo == null) return;
        ammo.OnReloadStart -= HandleReloadStart;
        ammo.OnReloadEnd -= HandleReloadEnd;
    }

    private void HandleReloadStart()
    {
        CombatEventHub.RaiseReload(new CombatEventHub.ReloadEvent
        {
            source = this,
            isStart = true,
            time = Time.time
        });
    }

    private void HandleReloadEnd()
    {
        CombatEventHub.RaiseReload(new CombatEventHub.ReloadEvent
        {
            source = this,
            isStart = false,
            time = Time.time
        });
    }

    // ========== FireMode 变化通知 + 立即刷新（新增） ==========

    /// <summary>
    /// 当 fireMode 被切换时触发（使用 SetFireMode 才会触发）
    /// </summary>
    public System.Action<CameraGunChannel, FireMode, FireMode> OnFireModeChanged;

    /// <summary>
    /// 推荐：所有地方切换开火模式都用这个方法
    /// - 会触发 OnFireModeChanged
    /// - 会让 GunStatContext 立刻刷新（可选）
    /// </summary>
    public void SetFireMode(FireMode newMode, bool forceRebuildNow = true)
    {
        if (fireMode == newMode) return;

        FireMode prev = fireMode;
        fireMode = newMode;

        OnFireModeChanged?.Invoke(this, prev, newMode);

        var ctx = GetComponent<GunStatContext>();
        if (ctx == null) ctx = GetComponentInParent<GunStatContext>();
        if (ctx != null)
        {
            if (forceRebuildNow) ctx.ForceRebuildNow();
            else ctx.MarkDirty();
        }
    }
}

// Keep ONLY ONE copy of this interface in your whole project.
public interface IDamageable
{
    void TakeDamage(float amount);
}