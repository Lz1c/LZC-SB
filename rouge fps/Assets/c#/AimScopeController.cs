using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class AimScopeController : MonoBehaviour
{
    [Header("Input")]
    public KeyCode adsKey = KeyCode.Mouse3;
    public bool holdToADS = true;

    [Header("滚轮缩放（由Perk控制）")]

    /// <summary>
    /// 是否启用滚轮缩放功能。
    /// 勾选后：当存在开镜放大倍率 Perk 时，开镜状态下可以用鼠标滚轮在不同倍率之间切换。
    /// 取消勾选：即使拥有多个倍率 Perk，也不会允许滚轮调节。
    /// </summary>
    public bool enableWheelZoom = true;

    /// <summary>
    /// 每次滚轮滚动时，放大倍率变化的幅度。
    /// 例如：
    /// 0.02 = 每滚动一次，倍率变化 2%
    /// 数值越小：缩放越细腻、过渡更平滑
    /// 数值越大：缩放变化更明显、切换更快
    /// 推荐范围：0.01 ~ 0.03
    /// </summary>
    [Range(0.001f, 0.2f)]
    public float wheelZoomStep = 0.02f;

    /// <summary>
    /// 判定“正在开镜”的容差值。
    /// 当摄像机当前FOV 小于 (hipFov - aimingEpsilon) 时，
    /// 系统会认为玩家处于开镜状态，从而允许滚轮缩放。
    /// 如果发现刚开镜时滚轮偶尔无效，可以适当调大此值。
    /// 推荐范围：0.1 ~ 0.2
    /// </summary>
    [Min(0.01f)]
    public float aimingEpsilon = 0.1f;

    [Header("Camera")]
    public Camera targetCamera;
    public float hipFov = 60f;
    public float adsFov = 35f;
    [Min(0.01f)] public float fovSmooth = 14f;

    [Header("Volume")]
    public Volume adsVolume;
    [Range(0f, 1f)] public float adsWeightOn = 1f;
    [Min(0.01f)] public float weightSmooth = 14f;

    [Header("Drive DOF Start/End (Raycast)")]
    public bool driveDofStartEnd = true;
    public LayerMask rayMask = ~0;
    public float maxDistanceWhenNoHit = 100f;

    [Tooltip("FinalDistance = RaycastDistance + distanceOffset")]
    public float distanceOffset = 0f;

    [Tooltip("How fast Start/End follow the target (bigger = faster)")]
    public float distanceSmooth = 20f;

    [Header("Sharp Zone Around Hit Distance")]
    public float nearSharpRange = 2f;
    public float farSharpRange = 8f;
    public float minStart = 0.05f;

    [Header("Disable DOF When Hipfire")]
    public bool disableDofWhenNotADS = true;

    [Header("Block ADS While Reloading")]
    [Tooltip("Drag BOTH channels' GunAmmo here. If any is reloading, ADS is blocked.")]
    public GunAmmo[] reloadBlocksADS;

    private bool _adsActive;
    private bool _adsToggle;

    private float _targetFov;
    private float _targetWeight;

    private float _currentStart;
    private float _currentEnd;

    private DepthOfField _dof;

    private void Awake()
    {
        if (targetCamera == null) targetCamera = Camera.main;
        if (targetCamera != null) hipFov = targetCamera.fieldOfView;

        _adsActive = false;
        _targetFov = hipFov;
        _targetWeight = 0f;

        if (adsVolume != null) adsVolume.weight = 0f;

        CacheDof();

        if (disableDofWhenNotADS) SetDofEnabled(false);
    }

    private void OnEnable()
    {
        CacheDof();
        if (disableDofWhenNotADS && !_adsActive) SetDofEnabled(false);
    }

    private void Update()
    {
        bool isReloading = IsAnyReloading();

        // If reloading, force ADS off + ignore input
        if (isReloading)
        {
            _adsToggle = false;
            _adsActive = false;
        }
        else
        {
            // ADS input only when NOT reloading
            if (holdToADS)
            {
                _adsActive = Input.GetKey(adsKey);
            }
            else
            {
                if (Input.GetKeyDown(adsKey)) _adsToggle = !_adsToggle;
                _adsActive = _adsToggle;
            }
        }

        float target = _adsActive ? adsFov : hipFov;

        // If perks registered zoom percents, override ADS target FOV using current percent
        if (_adsActive && enableWheelZoom && AdsZoomPercentManager.HasAny(this))
        {
            float wheel = Input.mouseScrollDelta.y;
            if (Mathf.Abs(wheel) > 0.0001f)
            {
                AdsZoomPercentManager.AdjustByScroll(this, wheel, wheelZoomStep);
            }

            float curPercent = AdsZoomPercentManager.GetCurrentOrDefault(this, adsFov / Mathf.Max(0.01f, hipFov));
            target = hipFov * Mathf.Clamp(curPercent, 0.05f, 1f);
        }

        // Optional fallback aiming detection (if you support toggle ADS and want wheel to work during transition)
        // If you want this, replace `_adsActive` in the if(...) above with: 
        // bool aiming = targetCamera.fieldOfView < (hipFov - aimingEpsilon);

        _targetFov = target;
        _targetWeight = _adsActive ? adsWeightOn : 0f;

        // Smooth FOV
        if (targetCamera != null)
        {
            float t = 1f - Mathf.Exp(-fovSmooth * Time.deltaTime);
            targetCamera.fieldOfView = Mathf.Lerp(targetCamera.fieldOfView, _targetFov, t);
        }

        // Smooth Volume Weight
        if (adsVolume != null)
        {
            float t = 1f - Mathf.Exp(-weightSmooth * Time.deltaTime);
            adsVolume.weight = Mathf.Lerp(adsVolume.weight, _targetWeight, t);
        }

        // Toggle DOF component on/off based on ADS
        if (disableDofWhenNotADS)
        {
            SetDofEnabled(_adsActive);
        }

        // Drive DOF while ADS
        if (driveDofStartEnd && _adsActive)
        {
            if (adsVolume == null || adsVolume.profile == null || targetCamera == null) return;

            if (_dof == null) CacheDof();
            if (_dof == null) return;

            float rayDist = GetCenterHitDistance();
            float d = Mathf.Max(0.01f, rayDist + distanceOffset);

            float targetStart = Mathf.Max(minStart, d - Mathf.Max(0f, nearSharpRange));
            float targetEnd = Mathf.Max(targetStart + 0.01f, d + Mathf.Max(0f, farSharpRange));

            float tt = 1f - Mathf.Exp(-distanceSmooth * Time.deltaTime);
            _currentStart = Mathf.Lerp(_currentStart, targetStart, tt);
            _currentEnd = Mathf.Lerp(_currentEnd, targetEnd, tt);

            _dof.gaussianStart.overrideState = true;
            _dof.gaussianEnd.overrideState = true;
            _dof.gaussianStart.value = _currentStart;
            _dof.gaussianEnd.value = _currentEnd;
        }

        // Hipfire: clear overrides to avoid residual influence
        if (!_adsActive && disableDofWhenNotADS)
        {
            ClearDofOverrides();
        }
    }

    private bool IsAnyReloading()
    {
        if (reloadBlocksADS == null || reloadBlocksADS.Length == 0) return false;

        for (int i = 0; i < reloadBlocksADS.Length; i++)
        {
            var a = reloadBlocksADS[i];
            if (a != null && a.IsReloading) return true;
        }

        return false;
    }

    private float GetCenterHitDistance()
    {
        Ray ray = targetCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        if (Physics.Raycast(ray, out RaycastHit hit, 9999f, rayMask, QueryTriggerInteraction.Ignore))
            return Mathf.Max(0.01f, hit.distance);

        return Mathf.Max(0.01f, maxDistanceWhenNoHit);
    }

    private void CacheDof()
    {
        _dof = null;
        if (adsVolume == null || adsVolume.profile == null) return;
        adsVolume.profile.TryGet(out _dof);
    }

    private void SetDofEnabled(bool enabled)
    {
        if (_dof == null) CacheDof();
        if (_dof == null) return;
        _dof.active = enabled;
    }

    private void ClearDofOverrides()
    {
        if (_dof == null) return;
        _dof.gaussianStart.overrideState = false;
        _dof.gaussianEnd.overrideState = false;
    }
}
