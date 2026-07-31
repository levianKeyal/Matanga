using UnityEngine;
using UnityEngine.InputSystem;

public class CoreManager : MonoBehaviour
{
    [SerializeField] private RectTransform circle;
    [SerializeField] private RectTransform line;
    [SerializeField] private RectTransform outerAreaCircle;
    [SerializeField] private Canvas canvas;
    [SerializeField] private Rigidbody blockToLaunch;
    [SerializeField] private float maxLaunchImpulse = 12f;
    [SerializeField, Range(2f, 10f)] private float lineGraphicHeight = 4f;
    [SerializeField] private Color lineGraphicColor = Color.white;

    private RectTransform lineGraphic;
    private UnityEngine.UI.Image lineGraphicImage;
    private bool isAiming;
    private float currentLineLength;

    private void Awake()
    {
        if (circle == null || line == null)
        {
            enabled = false;
            return;
        }

        line.SetParent(circle, false);

        line.anchorMin = new Vector2(0.5f, 0.5f);
        line.anchorMax = new Vector2(0.5f, 0.5f);
        line.pivot = new Vector2(0.5f, 0.5f);
        line.anchoredPosition = Vector2.zero;

        UnityEngine.UI.Image rootImage = line.GetComponent<UnityEngine.UI.Image>();
        if (rootImage != null)
        {
            rootImage.enabled = false;
        }

        EnsureLineGraphic();
    }

    private void Update()
    {
        if (Mouse.current == null)
        {
            return;
        }

        if (Mouse.current.leftButton.wasReleasedThisFrame)
        {
            LaunchBlock();
            isAiming = false;
            if (lineGraphicImage != null)
            {
                lineGraphicImage.enabled = false;
            }
        }

        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            isAiming = IsMouseInsideAimArea();
            if (isAiming && lineGraphicImage != null)
            {
                lineGraphicImage.enabled = true;
                currentLineLength = 0f;
            }
        }

        if (!isAiming)
        {
            return;
        }

        Vector2 mouseScreen = Mouse.current.position.ReadValue();
        Camera uiCamera = GetUiCamera();

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                circle,
                mouseScreen,
                uiCamera,
                out Vector2 mouseLocal))
        {
            return;
        }

        Vector2 center = circle.rect.center;
        Vector2 dir = mouseLocal - center;

        float angle = Mathf.Atan2(-dir.y, -dir.x) * Mathf.Rad2Deg;

        line.anchoredPosition = Vector2.zero;
        line.localRotation = Quaternion.Euler(0f, 0f, angle);

        if (lineGraphic != null)
        {
            lineGraphic.anchoredPosition = Vector2.zero;
            lineGraphic.localRotation = Quaternion.identity;
            lineGraphicHeight = Mathf.Clamp(lineGraphicHeight, 2f, 10f);

            if (IsMouseInsideOuterArea(mouseScreen, uiCamera))
            {
                currentLineLength = dir.magnitude * 2f;
            }

            lineGraphic.sizeDelta = new Vector2(currentLineLength, lineGraphicHeight);

            if (lineGraphicImage != null)
            {
                lineGraphicImage.color = lineGraphicColor;
            }
        }
    }

    private void LaunchBlock()
    {
        if (blockToLaunch == null)
        {
            return;
        }

        if (currentLineLength <= 0.001f)
        {
            return;
        }

        Camera uiCamera = GetUiCamera();
        Vector2 mouseScreen = Mouse.current.position.ReadValue();

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                circle,
                mouseScreen,
                uiCamera,
                out Vector2 mouseLocal))
        {
            return;
        }

        Vector2 center = circle.rect.center;
        Vector2 dir = mouseLocal - center;
        Vector2 launchDirection2D = -dir.normalized;

        float maxLineLength = Mathf.Max(circle.rect.width, circle.rect.height);
        float forceMultiplier = Mathf.Clamp01(currentLineLength / maxLineLength);
        Vector3 launchDirection = new Vector3(launchDirection2D.x, launchDirection2D.y, 0f);

        blockToLaunch.linearVelocity = Vector3.zero;
        blockToLaunch.angularVelocity = Vector3.zero;
        blockToLaunch.isKinematic = false;
        blockToLaunch.AddForce(launchDirection * (forceMultiplier * maxLaunchImpulse), ForceMode.Impulse);
    }

    private bool IsMouseInsideAimArea()
    {
        if (Mouse.current == null)
        {
            return false;
        }

        Vector2 mouseScreen = Mouse.current.position.ReadValue();
        Camera uiCamera = GetUiCamera();

        return IsMouseInsideCircle(mouseScreen, uiCamera) || IsMouseInsideOuterArea(mouseScreen, uiCamera);
    }

    private bool IsMouseInsideCircle(Vector2 mouseScreen, Camera uiCamera)
    {
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                circle,
                mouseScreen,
                uiCamera,
                out Vector2 mouseLocal))
        {
            return false;
        }

        return circle.rect.Contains(mouseLocal);
    }

    private bool IsMouseInsideOuterArea(Vector2 mouseScreen, Camera uiCamera)
    {
        if (outerAreaCircle == null)
        {
            return true;
        }

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                outerAreaCircle,
                mouseScreen,
                uiCamera,
                out Vector2 mouseLocal))
        {
            return false;
        }

        return outerAreaCircle.rect.Contains(mouseLocal);
    }

    private void EnsureLineGraphic()
    {
        Transform existing = line.Find("LineGraphic");
        if (existing != null)
        {
            lineGraphic = existing as RectTransform;
            lineGraphicImage = existing.GetComponent<UnityEngine.UI.Image>();
        }
        else
        {
            GameObject graphicObject = new GameObject("LineGraphic", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            graphicObject.transform.SetParent(line, false);

            lineGraphic = graphicObject.GetComponent<RectTransform>();
            lineGraphicImage = graphicObject.GetComponent<UnityEngine.UI.Image>();
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

        lineGraphicImage.type = UnityEngine.UI.Image.Type.Simple;
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

    private Camera GetUiCamera()
    {
        if (canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay)
        {
            return null;
        }

        return canvas.worldCamera != null ? canvas.worldCamera : Camera.main;
    }
}
