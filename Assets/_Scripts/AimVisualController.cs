using UnityEngine;
using UnityEngine.UI;

public class AimVisualController : MonoBehaviour
{
    private RectTransform circle;
    private RectTransform line;
    private RectTransform outerAreaCircle;
    private Canvas canvas;
    private RectTransform lineGraphic;
    private Image lineGraphicImage;
    private float lineGraphicHeight;
    private Color lineGraphicColor;

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

        Image rootImage = line.GetComponent<Image>();
        if (rootImage != null)
        {
            rootImage.enabled = false;
        }

        EnsureLineGraphic();
        return lineGraphic != null && lineGraphicImage != null;
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
        size.x = 0f;
        lineGraphic.sizeDelta = size;
    }

    public void UpdateAimLine(float angle, float length)
    {
        if (line == null || lineGraphic == null || lineGraphicImage == null)
        {
            return;
        }

        line.anchoredPosition = Vector2.zero;
        line.localRotation = Quaternion.Euler(0f, 0f, angle);

        lineGraphic.anchoredPosition = Vector2.zero;
        lineGraphic.localRotation = Quaternion.identity;
        lineGraphicHeight = Mathf.Clamp(lineGraphicHeight, 2f, 10f);
        lineGraphic.sizeDelta = new Vector2(length, lineGraphicHeight);
        lineGraphicImage.color = lineGraphicColor;
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

    private static Sprite CreateWhiteSprite()
    {
        return Sprite.Create(
            Texture2D.whiteTexture,
            new Rect(0f, 0f, 1f, 1f),
            new Vector2(0.5f, 0.5f));
    }
}
