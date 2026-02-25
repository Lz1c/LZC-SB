using UnityEngine;

public class SpreadDiamondUI : MonoBehaviour
{
    public enum Channel { Primary, Secondary }

    [Header("Refs")]
    public CameraGunDual dual;
    public CameraGunChannel primary;
    public CameraGunChannel secondary;
    public Channel channel = Channel.Primary;

    [Header("UI Points")]
    public RectTransform top;
    public RectTransform bottom;
    public RectTransform left;
    public RectTransform right;

    [Header("Scale")]
    public float pixelsPerDegreeX = 6f;
    public float pixelsPerDegreeY = 6f;
    public float maxPixelsX = 250f;
    public float maxPixelsY = 250f;
    public float smooth = 20f;

    private float _uiRadiusX;
    private float _uiRadiusY;

    private void Awake()
    {
        TryAutoWire();
    }

    private void Update()
    {
        TryAutoWireIfMissing();

        CameraGunChannel ch = (channel == Channel.Primary) ? primary : secondary;
        if (ch == null || ch.spread == null) return;

        bool isShotgun = ch.shotType == CameraGunChannel.ShotType.Shotgun;

        // NEW: read anisotropic UI spread values
        float spreadXDeg = ch.spread.GetUiSpreadXDeg(isShotgun);
        float spreadYDeg = ch.spread.GetUiSpreadYDeg(isShotgun);

        float targetX = Mathf.Min(maxPixelsX, spreadXDeg * pixelsPerDegreeX);
        float targetY = Mathf.Min(maxPixelsY, spreadYDeg * pixelsPerDegreeY);

        float t = 1f - Mathf.Exp(-smooth * Time.deltaTime);
        _uiRadiusX = Mathf.Lerp(_uiRadiusX, targetX, t);
        _uiRadiusY = Mathf.Lerp(_uiRadiusY, targetY, t);

        if (top != null) top.anchoredPosition = new Vector2(0f, _uiRadiusY);
        if (bottom != null) bottom.anchoredPosition = new Vector2(0f, -_uiRadiusY);
        if (left != null) left.anchoredPosition = new Vector2(-_uiRadiusX, 0f);
        if (right != null) right.anchoredPosition = new Vector2(_uiRadiusX, 0f);
    }

    private void TryAutoWire()
    {
        DualGunResolver.TryResolve(ref dual, ref primary, ref secondary);
    }

    private void TryAutoWireIfMissing()
    {
        if (primary == null || secondary == null)
            TryAutoWire();
    }
}