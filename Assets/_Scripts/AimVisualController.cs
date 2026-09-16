using UnityEngine;
using UnityEngine.UI;

public class AimVisualController : MonoBehaviour
{
    [Header("Horizontal Reference Aim Guide Block")]
    [SerializeField] private RectTransform aimGuideBlock;
    [SerializeField] private bool useAimGuideBlock = true;
    [SerializeField] private float aimGuideReturnSmoothTime = 0.12f;
    [SerializeField] private bool hideAimGuideBlockWhenNoBlockReady = true;

    private RectTransform circle;
    private RectTransform line;
    private RectTransform outerAreaCircle;
    private Canvas canvas;
    private RectTransform lineGraphic;
    private Image lineGraphicImage;
    private float lineGraphicHeight;
    private Color lineGraphicColor;
    private Transform originalAimLineParent;
    private int originalAimLineSiblingIndex;
    private bool useAimGuideBlockAsLineOrigin;
    private bool isReturningAimGuideBlock;
    private Quaternion aimGuideBlockBaseLocalRotation = Quaternion.identity;
    private bool hasAimGuideBlockBaseRotation;
    private Vector2 aimGuideReturnVelocity;
    private Vector2 aimGuideTargetPosition;

    public bool HasAimGuideBlock => useAimGuideBlock && aimGuideBlock != null;

    public Vector2 AimGuideBlockPosition =>
        aimGuideBlock != null ? aimGuideBlock.anchoredPosition : Vector2.zero;

    public bool Initialize(
        RectTransform circleReference,
        RectTransform lineReference,
        RectTransform outerAreaReference,
        Canvas canvasReference,
        float graphicHeight,
        Color graphicColor)
    {
        circle = circleReference;
        line = lineReference;
        outerAreaCircle = outerAreaReference;
        canvas = canvasReference;
        lineGraphicHeight = graphicHeight;
        lineGraphicColor = graphicColor;

        if (circle == null || line == null)
        {
            return false;
        }

        line.SetParent(circle, false);
        line.anchorMin = new Vector2(0.5f, 0.5f);
        line.anchorMax = new Vector2(0.5f, 0.5f);
        line.pivot = new Vector2(0.5f, 0.5f);
        line.anchoredPosition = Vector2.zero;
        originalAimLineParent = line.parent;
        originalAimLineSiblingIndex = line.GetSiblingIndex();

        Image rootImage = line.GetComponent<Image>();
        if (rootImage != null)
        {
            rootImage.enabled = false;
        }

        EnsureLineGraphic();
        ConfigureAimGuideBlock();
        return lineGraphic != null && lineGraphicImage != null;
    }

    private void Update()
    {
        UpdateAimGuideBlockReturn();
    }

    public Camera GetUiCamera()
    {
        if (canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay)
        {
            return null;
        }

        return canvas.worldCamera != null ? canvas.worldCamera : Camera.main;
    }

    public bool IsPointerInsideAimArea(Vector2 pointerScreen)
    {
        Camera uiCamera = GetUiCamera();
        return IsPointerInsideCircle(pointerScreen, uiCamera) ||
               IsPointerInsideOuterArea(pointerScreen, uiCamera);
    }

    public bool IsPointerInsideOuterArea(Vector2 pointerScreen)
    {
        return IsPointerInsideOuterArea(pointerScreen, GetUiCamera());
    }

    public bool TryGetPointerLocalPosition(Vector2 pointerScreen, out Vector2 pointerLocal)
    {
        pointerLocal = Vector2.zero;

        return RectTransformUtility.ScreenPointToLocalPointInRectangle(
            circle,
            pointerScreen,
            GetUiCamera(),
            out pointerLocal);
    }

    public Vector2 GetAimCenter()
    {
        return circle.rect.center;
    }

    public float GetMaxAimLineLength()
    {
        return Mathf.Max(circle.rect.width, circle.rect.height);
    }

    public void ShowAimLine()
    {
        if (lineGraphicImage != null)
        {
            lineGraphicImage.enabled = true;
        }
    }

    public void HideAimLine()
    {
        if (lineGraphicImage != null)
        {
            lineGraphicImage.enabled = false;
        }
    }

    public void ResetAimLineLength()
    {
        if (lineGraphic == null)
        {
            return;
        }

        Vector2 size = lineGraphic.sizeDelta;
        if (useAimGuideBlockAsLineOrigin && HasAimGuideBlock)
        {
            line.sizeDelta = new Vector2(lineGraphicHeight, 0f);
        }
        else
        {
            size.x = 0f;
        }

        lineGraphic.sizeDelta = size;
    }

    public void SetUseAimGuideBlockAsLineOrigin(bool useGuideBlockOrigin)
    {
        useAimGuideBlockAsLineOrigin = useGuideBlockOrigin;
    }

    public void UpdateAimLine(float angle, float length)
    {
        if (line == null || lineGraphic == null || lineGraphicImage == null)
        {
            return;
        }

        if (useAimGuideBlockAsLineOrigin && HasAimGuideBlock)
        {
            UpdateAimLineFromGuideBlock(length);
        }
        else
        {
            UpdateAimLineFromCircle(angle, length);
        }

        lineGraphicImage.color = lineGraphicColor;
    }

    public void ShowAimGuideBlock()
    {
        if (!HasAimGuideBlock)
        {
            return;
        }

        aimGuideBlock.gameObject.SetActive(true);
    }

    public void HideAimGuideBlock()
    {
        if (aimGuideBlock == null)
        {
            return;
        }

        aimGuideBlock.gameObject.SetActive(false);
        isReturningAimGuideBlock = false;
        aimGuideReturnVelocity = Vector2.zero;
    }

    public void SetAimGuideBlockPosition(Vector2 localPosition)
    {
        if (!HasAimGuideBlock)
        {
            return;
        }

        isReturningAimGuideBlock = false;
        aimGuideReturnVelocity = Vector2.zero;
        aimGuideBlock.anchoredPosition = localPosition;
        aimGuideBlock.gameObject.SetActive(true);
    }

    public void SetAimGuideBlockRotation(float aimAngle)
    {
        if (!HasAimGuideBlock)
        {
            return;
        }

        CacheAimGuideBlockBaseRotation();
        float visualAimAngle = aimAngle - 90f;
        Quaternion aimOffsetRotation = Quaternion.Euler(0f, 0f, visualAimAngle);
        aimGuideBlock.localRotation = aimGuideBlockBaseLocalRotation * aimOffsetRotation;
    }

    public void ResetAimGuideBlockImmediate(Vector2 center)
    {
        if (!HasAimGuideBlock)
        {
            return;
        }

        isReturningAimGuideBlock = false;
        aimGuideReturnVelocity = Vector2.zero;
        aimGuideBlock.anchoredPosition = center;
        CacheAimGuideBlockBaseRotation();
        aimGuideBlock.localRotation = aimGuideBlockBaseLocalRotation;
        aimGuideBlock.gameObject.SetActive(true);
    }

    public void ReturnAimGuideBlockToCenter(Vector2 center)
    {
        if (!HasAimGuideBlock)
        {
            return;
        }

        aimGuideTargetPosition = center;
        aimGuideReturnVelocity = Vector2.zero;
        isReturningAimGuideBlock = true;
        CacheAimGuideBlockBaseRotation();
        aimGuideBlock.localRotation = aimGuideBlockBaseLocalRotation;
        aimGuideBlock.gameObject.SetActive(true);
    }

    public void StopAimGuideBlockReturn()
    {
        isReturningAimGuideBlock = false;
        aimGuideReturnVelocity = Vector2.zero;
    }

    public bool IsPointerInsideAimGuideBlock(Vector2 screenPosition)
    {
        if (!HasAimGuideBlock || !aimGuideBlock.gameObject.activeInHierarchy)
        {
            return false;
        }

        return RectTransformUtility.RectangleContainsScreenPoint(
            aimGuideBlock,
            screenPosition,
            GetUiCamera());
    }

    public void HideAimGuideBlockIfNoBlockReady()
    {
        if (hideAimGuideBlockWhenNoBlockReady)
        {
            HideAimGuideBlock();
        }
    }

    public Vector2 GetAimGuideBlockIdlePosition(RectTransform referenceRect)
    {
        if (referenceRect == null)
        {
            return GetAimCenter();
        }

        return new Vector2(0f, referenceRect.rect.height * 0.5f);
    }

    private bool IsPointerInsideCircle(Vector2 pointerScreen, Camera uiCamera)
    {
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                circle,
                pointerScreen,
                uiCamera,
                out Vector2 pointerLocal))
        {
            return false;
        }

        return circle.rect.Contains(pointerLocal);
    }

    private bool IsPointerInsideOuterArea(Vector2 pointerScreen, Camera uiCamera)
    {
        if (outerAreaCircle == null)
        {
            return true;
        }

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                outerAreaCircle,
                pointerScreen,
                uiCamera,
                out Vector2 pointerLocal))
        {
            return false;
        }

        return outerAreaCircle.rect.Contains(pointerLocal);
    }

    private void EnsureLineGraphic()
    {
        Transform existing = line.Find("LineGraphic");
        if (existing != null)
        {
            lineGraphic = existing as RectTransform;
            lineGraphicImage = existing.GetComponent<Image>();
        }
        else
        {
            GameObject graphicObject = new GameObject(
                "LineGraphic",
                typeof(RectTransform),
                typeof(Image));
            graphicObject.transform.SetParent(line, false);

            lineGraphic = graphicObject.GetComponent<RectTransform>();
            lineGraphicImage = graphicObject.GetComponent<Image>();
        }

        if (lineGraphic == null || lineGraphicImage == null)
        {
            return;
        }

        lineGraphic.anchorMin = new Vector2(0.5f, 0.5f);
        lineGraphic.anchorMax = new Vector2(0.5f, 0.5f);
        lineGraphic.pivot = new Vector2(0f, 0.5f);
        lineGraphic.anchoredPosition = Vector2.zero;
        lineGraphic.localScale = Vector3.one;
        lineGraphicHeight = Mathf.Clamp(lineGraphicHeight, 2f, 10f);
        lineGraphic.sizeDelta = new Vector2(lineGraphic.sizeDelta.x, lineGraphicHeight);

        lineGraphicImage.type = Image.Type.Simple;
        lineGraphicImage.raycastTarget = false;
        lineGraphicImage.sprite = CreateWhiteSprite();
        lineGraphicImage.color = lineGraphicColor;
        lineGraphicImage.enabled = false;
    }

    private void ConfigureAimGuideBlock()
    {
        if (aimGuideBlock == null)
        {
            return;
        }

        CacheAimGuideBlockBaseRotation();
        aimGuideBlock.anchorMin = new Vector2(0.5f, 0.5f);
        aimGuideBlock.anchorMax = new Vector2(0.5f, 0.5f);
        aimGuideBlock.anchoredPosition = Vector2.zero;
        aimGuideBlock.localScale = Vector3.one;

        Image guideImage = aimGuideBlock.GetComponent<Image>();
        if (guideImage != null)
        {
            guideImage.raycastTarget = false;
        }

        if (hideAimGuideBlockWhenNoBlockReady)
        {
            aimGuideBlock.gameObject.SetActive(false);
        }
    }

    private void UpdateAimGuideBlockReturn()
    {
        if (!isReturningAimGuideBlock || aimGuideBlock == null)
        {
            return;
        }

        aimGuideBlock.anchoredPosition = Vector2.SmoothDamp(
            aimGuideBlock.anchoredPosition,
            aimGuideTargetPosition,
            ref aimGuideReturnVelocity,
            aimGuideReturnSmoothTime);

        if ((aimGuideBlock.anchoredPosition - aimGuideTargetPosition).sqrMagnitude <= 0.01f)
        {
            aimGuideBlock.anchoredPosition = aimGuideTargetPosition;
            isReturningAimGuideBlock = false;
            aimGuideReturnVelocity = Vector2.zero;
        }
    }

    private void UpdateAimLineFromCircle(float angle, float length)
    {
        RestoreAimLineParent();

        line.anchorMin = new Vector2(0.5f, 0.5f);
        line.anchorMax = new Vector2(0.5f, 0.5f);
        line.pivot = new Vector2(0.5f, 0.5f);
        line.anchoredPosition = Vector2.zero;
        line.localRotation = Quaternion.Euler(0f, 0f, angle);
        line.localScale = Vector3.one;

        lineGraphic.anchorMin = new Vector2(0.5f, 0.5f);
        lineGraphic.anchorMax = new Vector2(0.5f, 0.5f);
        lineGraphic.pivot = new Vector2(0f, 0.5f);
        lineGraphic.anchoredPosition = Vector2.zero;
        lineGraphic.localRotation = Quaternion.identity;
        lineGraphic.localScale = Vector3.one;
        lineGraphicHeight = Mathf.Clamp(lineGraphicHeight, 2f, 10f);
        lineGraphic.sizeDelta = new Vector2(length, lineGraphicHeight);
    }

    private void UpdateAimLineFromGuideBlock(float length)
    {
        if (aimGuideBlock == null)
        {
            return;
        }

        if (line.parent != aimGuideBlock)
        {
            line.SetParent(aimGuideBlock, false);
        }

        line.anchorMin = new Vector2(0.5f, 1f);
        line.anchorMax = new Vector2(0.5f, 1f);
        line.pivot = new Vector2(0.5f, 0f);
        line.anchoredPosition = Vector2.zero;
        line.localRotation = Quaternion.identity;
        line.localScale = Vector3.one;
        lineGraphicHeight = Mathf.Clamp(lineGraphicHeight, 2f, 10f);
        line.sizeDelta = new Vector2(lineGraphicHeight, length);

        lineGraphic.anchorMin = Vector2.zero;
        lineGraphic.anchorMax = Vector2.one;
        lineGraphic.pivot = new Vector2(0.5f, 0f);
        lineGraphic.anchoredPosition = Vector2.zero;
        lineGraphic.localRotation = Quaternion.identity;
        lineGraphic.localScale = Vector3.one;
        lineGraphic.sizeDelta = Vector2.zero;
    }

    private void RestoreAimLineParent()
    {
        Transform targetParent = originalAimLineParent != null
            ? originalAimLineParent
            : circle;

        if (line.parent != targetParent)
        {
            line.SetParent(targetParent, false);
            line.SetSiblingIndex(originalAimLineSiblingIndex);
        }
    }

    private void CacheAimGuideBlockBaseRotation()
    {
        if (aimGuideBlock == null || hasAimGuideBlockBaseRotation)
        {
            return;
        }

        aimGuideBlockBaseLocalRotation = aimGuideBlock.localRotation;
        hasAimGuideBlockBaseRotation = true;
    }

    private static Sprite CreateWhiteSprite()
    {
        return Sprite.Create(
            Texture2D.whiteTexture,
            new Rect(0f, 0f, 1f, 1f),
            new Vector2(0.5f, 0.5f));
    }
}
