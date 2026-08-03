using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;
using TMPro;

public class CoreManager : MonoBehaviour
{
    [Header("UI Elements")]
    [SerializeField] private RectTransform circle;
    [SerializeField] private RectTransform line;
    [SerializeField] private RectTransform outerAreaCircle;
    [SerializeField] private Canvas canvas;
    [SerializeField, Range(2f, 10f)] private float lineGraphicHeight = 4f;
    [SerializeField] private Color lineGraphicColor = Color.white;
    [SerializeField] private LineRenderer trajectoryLine;
    [SerializeField, Min(8)] private int trajectorySegmentCount = 24;
    [SerializeField, Min(0.01f)] private float trajectoryTimeStep = 0.08f;
    [SerializeField] private bool showTrajectoryDebug = true;

    [SerializeField] private TMP_Text stackedBlocksText;

    [Header("Stacking Elements")]
    [SerializeField] private Rigidbody blockPrefab;
    [SerializeField] private Transform blockSpawnPoint;
    [SerializeField] private Transform baseTargetTransform;
    [SerializeField] private Transform blockBase;
    [SerializeField] private List<TotemBlockDetector> stackedBlocks = new List<TotemBlockDetector>();

    [Header("Stacking Settings")]
    [SerializeField] private float maxLaunchImpulse = 12f;
    [SerializeField, Min(0f)] private float balancedHoldTime = 0.5f;
    [SerializeField, Min(0f)] private float blockBaseMoveDuration = 0.35f;
    [SerializeField] private float blockBaseQuarterTurnDegrees = 90f;
    [SerializeField, Min(0f)] private float spawnDelayAfterBlockBaseMove = 0.5f;
    [SerializeField, Min(0f)] private float unstackedBlockAutoDestroyDelay = 0.35f;

    private RectTransform lineGraphic;
    private UnityEngine.UI.Image lineGraphicImage;
    private bool trajectoryVisible;
    private bool isAiming;
    private float currentLineLength;
    private Rigidbody blockToLaunch;
    private TotemBlockDetector currentBlockDetector;
    private float balancedTimer;
    private bool hasSpawnedNextBlock;
    private bool hasUsedAimForCurrentBlock;
    private bool hasLaunchedCurrentBlock;
    private float unstackedBlockQuietTimer;
    private float respawnTimer;
    private bool hasPendingRespawn;
    private readonly List<TotemBlockDetector> pendingDestroyBlocks = new List<TotemBlockDetector>();
    private int lastPendingDestroyProcessFrame = -1;
    private float blockBaseInitialY;
    private Quaternion blockBaseInitialRotation;
    private bool isBlockBaseAnimating;
    private bool spawnBlockAfterBaseAnimation;
    private float blockBaseAnimationTimer;
    private Vector3 blockBaseAnimationStartPosition;
    private Quaternion blockBaseAnimationStartRotation;
    private Vector3 blockBaseAnimationTargetPosition;
    private Quaternion blockBaseAnimationTargetRotation;

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
        EnsureTrajectoryLine();
        CacheBlockBaseInitialPosition();
        RefreshStackedBlocksText();
        ApplyBlockBaseStateInstant();
        SpawnBlock();
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

        UpdatePendingRespawn();
        UpdateBlockBaseAnimation();
        UpdateBalancedBlockState();
        UpdateUnstackedLaunchedBlockState();
        UpdateTrajectoryDebug();
        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            if (!hasUsedAimForCurrentBlock && !hasLaunchedCurrentBlock && IsMouseInsideAimArea())
            {
                isAiming = true;
                hasUsedAimForCurrentBlock = true;

                if (lineGraphicImage != null)
                {
                    lineGraphicImage.enabled = true;
                    currentLineLength = 0f;
                }
            }
        }

        if (!isAiming)
        {
            HideTrajectoryDebug();
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

        UpdateTrajectoryDebug();
    }

    private void LaunchBlock()
    {
        if (!TryGetLaunchParameters(out Vector3 launchDirection, out float launchImpulse))
        {
            return;
        }

        blockToLaunch.linearVelocity = Vector3.zero;
        blockToLaunch.angularVelocity = Vector3.zero;
        blockToLaunch.isKinematic = false;
        FaceBlockTowardsBase();
        blockToLaunch.AddForce(launchDirection * launchImpulse, ForceMode.Impulse);
        hasLaunchedCurrentBlock = true;
        HideTrajectoryDebug();
    }

    private bool TryGetLaunchParameters(out Vector3 launchDirection, out float launchImpulse)
    {
        launchDirection = Vector3.zero;
        launchImpulse = 0f;

        if (blockToLaunch == null || hasLaunchedCurrentBlock || currentLineLength <= 0.001f)
        {
            return false;
        }

        Camera uiCamera = GetUiCamera();
        Vector2 mouseScreen = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                circle,
                mouseScreen,
                uiCamera,
                out Vector2 mouseLocal))
        {
            return false;
        }

        Vector2 center = circle.rect.center;
        Vector2 dir = mouseLocal - center;
        Vector2 launchDirection2D = -dir.normalized;

        if (launchDirection2D == Vector2.zero)
        {
            return false;
        }

        float maxLineLength = Mathf.Max(circle.rect.width, circle.rect.height);
        float forceMultiplier = Mathf.Clamp01(currentLineLength / maxLineLength);

        launchDirection = new Vector3(launchDirection2D.x, launchDirection2D.y, 0f);
        launchImpulse = forceMultiplier * maxLaunchImpulse;
        return true;
    }

    private void UpdateTrajectoryDebug()
    {
        if (!showTrajectoryDebug)
        {
            HideTrajectoryDebug();
            return;
        }

        if (!isAiming || trajectoryLine == null || blockToLaunch == null)
        {
            HideTrajectoryDebug();
            return;
        }

        if (!TryGetLaunchParameters(out Vector3 launchDirection, out float launchImpulse))
        {
            HideTrajectoryDebug();
            return;
        }

        float mass = Mathf.Max(0.0001f, blockToLaunch.mass);
        Vector3 initialVelocity = launchDirection * (launchImpulse / mass);
        Vector3 gravity = Physics.gravity;
        Vector3 startPosition = blockToLaunch.position;

        int segmentCount = Mathf.Max(2, trajectorySegmentCount);
        float step = Mathf.Max(0.01f, trajectoryTimeStep);

        trajectoryLine.positionCount = segmentCount;
        trajectoryLine.enabled = true;

        for (int i = 0; i < segmentCount; i++)
        {
            float t = i * step;
            Vector3 point = startPosition + initialVelocity * t + 0.5f * gravity * (t * t);
            trajectoryLine.SetPosition(i, point);
        }

        trajectoryVisible = true;
    }

    private void HideTrajectoryDebug()
    {
        if (trajectoryLine != null && trajectoryVisible)
        {
            trajectoryLine.enabled = false;
            trajectoryLine.positionCount = 0;
        }

        trajectoryVisible = false;
    }

    private void SpawnBlock()
    {
        if (blockPrefab == null)
        {
            return;
        }

        if (ProcessOnePendingDestroyBlock())
        {
            return;
        }

        if (pendingDestroyBlocks.Count > 0)
        {
            StartPendingSpawn(spawnDelayAfterBlockBaseMove);
            return;
        }

        CleanupInvalidStackedBlocks();

        if (pendingDestroyBlocks.Count > 0)
        {
            StartPendingSpawn(spawnDelayAfterBlockBaseMove);
            return;
        }

        Vector3 spawnPosition = blockSpawnPoint != null ? blockSpawnPoint.position : Vector3.zero;
        Quaternion spawnRotation = blockSpawnPoint != null ? blockSpawnPoint.rotation : Quaternion.identity;

        blockToLaunch = Instantiate(blockPrefab, spawnPosition, spawnRotation);

        currentBlockDetector = blockToLaunch.GetComponent<TotemBlockDetector>();
        if (currentBlockDetector == null)
        {
            currentBlockDetector = blockToLaunch.gameObject.AddComponent<TotemBlockDetector>();
        }

        balancedTimer = 0f;
        hasSpawnedNextBlock = false;
        hasUsedAimForCurrentBlock = false;
        hasLaunchedCurrentBlock = false;
        unstackedBlockQuietTimer = 0f;
        isAiming = false;
        currentLineLength = 0f;
        hasPendingRespawn = false;
        respawnTimer = 0f;

        if (lineGraphicImage != null)
        {
            lineGraphicImage.enabled = false;
        }
    }

    private void UpdateBalancedBlockState()
    {
        if (hasPendingRespawn)
        {
            return;
        }

        if (isBlockBaseAnimating)
        {
            return;
        }

        if (!hasLaunchedCurrentBlock)
        {
            balancedTimer = 0f;
            hasSpawnedNextBlock = false;
            return;
        }

        if (currentBlockDetector == null)
        {
            balancedTimer = 0f;
            hasSpawnedNextBlock = false;
            return;
        }

        if (!currentBlockDetector.IsBalanced)
        {
            balancedTimer = 0f;
            hasSpawnedNextBlock = false;
            return;
        }

        balancedTimer += Time.deltaTime;
        if (hasSpawnedNextBlock || balancedTimer < balancedHoldTime)
        {
            return;
        }

        RegisterCurrentBlockAsStacked();
        StopCurrentBlockMotion();
        hasSpawnedNextBlock = true;
        RefreshBlockBasePosition(true);
    }

    private void UpdateUnstackedLaunchedBlockState()
    {
        if (!hasLaunchedCurrentBlock || hasPendingRespawn || isBlockBaseAnimating)
        {
            unstackedBlockQuietTimer = 0f;
            return;
        }

        if (currentBlockDetector == null || blockToLaunch == null)
        {
            unstackedBlockQuietTimer = 0f;
            return;
        }

        if (stackedBlocks.Contains(currentBlockDetector) || currentBlockDetector.IsBalanced)
        {
            unstackedBlockQuietTimer = 0f;
            return;
        }

        bool isCompletelyStill =
            blockToLaunch.linearVelocity.sqrMagnitude <= 0.0001f &&
            blockToLaunch.angularVelocity.sqrMagnitude <= 0.0001f;

        if (!isCompletelyStill)
        {
            unstackedBlockQuietTimer = 0f;
            return;
        }

        unstackedBlockQuietTimer += Time.deltaTime;
        if (unstackedBlockQuietTimer < unstackedBlockAutoDestroyDelay)
        {
            return;
        }

        GameObject blockObjectToDestroy = blockToLaunch.gameObject;
        if (currentBlockDetector != null)
        {
            RemoveStackedBlock(currentBlockDetector);
        }

        if (blockObjectToDestroy != null)
        {
            Destroy(blockObjectToDestroy);
        }

        unstackedBlockQuietTimer = 0f;
    }

    private void UpdatePendingRespawn()
    {
        if (!hasPendingRespawn)
        {
            return;
        }

        if (ProcessOnePendingDestroyBlock())
        {
            return;
        }

        if (pendingDestroyBlocks.Count > 0)
        {
            return;
        }

        respawnTimer -= Time.deltaTime;
        if (respawnTimer > 0f)
        {
            return;
        }

        hasPendingRespawn = false;
        respawnTimer = 0f;
        SpawnBlock();
    }

    private void RegisterCurrentBlockAsStacked()
    {
        if (currentBlockDetector == null)
        {
            return;
        }

        if (stackedBlocks.Contains(currentBlockDetector))
        {
            return;
        }

        stackedBlocks.Add(currentBlockDetector);
        RefreshStackedBlocksText();
    }

    private void CleanupInvalidStackedBlocks()
    {
        bool listChanged = false;

        for (int i = stackedBlocks.Count - 1; i >= 0; i--)
        {
            TotemBlockDetector detector = stackedBlocks[i];

            if (detector == null)
            {
                stackedBlocks.RemoveAt(i);
                listChanged = true;
                continue;
            }

            if (detector.IsBalanced)
            {
                continue;
            }

            if (IsBlockCompletelyStill(detector))
            {
                Destroy(detector.gameObject);
            }
            else if (!pendingDestroyBlocks.Contains(detector))
            {
                pendingDestroyBlocks.Add(detector);
            }

            stackedBlocks.RemoveAt(i);
            listChanged = true;
        }

        if (!listChanged)
        {
            return;
        }

        RefreshStackedBlocksText();
        RefreshBlockBasePosition(false);
    }

    private bool ProcessOnePendingDestroyBlock()
    {
        if (pendingDestroyBlocks.Count == 0)
        {
            return false;
        }

        if (lastPendingDestroyProcessFrame == Time.frameCount)
        {
            return false;
        }

        for (int i = 0; i < pendingDestroyBlocks.Count; i++)
        {
            TotemBlockDetector detector = pendingDestroyBlocks[i];

            if (detector == null)
            {
                pendingDestroyBlocks.RemoveAt(i);
                return true;
            }

            if (!IsBlockCompletelyStill(detector))
            {
                continue;
            }

            if (detector == currentBlockDetector ||
                (blockToLaunch != null &&
                 detector.TryGetComponent<Rigidbody>(out Rigidbody detectorRigidbody) &&
                 blockToLaunch == detectorRigidbody))
            {
                ResetCurrentBlockRemovalState();
            }

            Destroy(detector.gameObject);
            pendingDestroyBlocks.RemoveAt(i);
            lastPendingDestroyProcessFrame = Time.frameCount;
            return true;
        }

        return false;
    }

    private bool IsBlockCompletelyStill(TotemBlockDetector detector)
    {
        if (detector == null || detector.TryGetComponent<Rigidbody>(out Rigidbody rigidbody) == false)
        {
            return false;
        }

        return rigidbody.linearVelocity.sqrMagnitude <= 0.0001f &&
               rigidbody.angularVelocity.sqrMagnitude <= 0.0001f;
    }

    private void ResetCurrentBlockRemovalState()
    {
        currentBlockDetector = null;
        blockToLaunch = null;
        isAiming = false;
        currentLineLength = 0f;
        balancedTimer = 0f;
        hasSpawnedNextBlock = false;
        hasPendingRespawn = true;
        respawnTimer = balancedHoldTime;
        spawnBlockAfterBaseAnimation = false;
        hasUsedAimForCurrentBlock = false;
        hasLaunchedCurrentBlock = false;
        unstackedBlockQuietTimer = 0f;

        if (lineGraphicImage != null)
        {
            lineGraphicImage.enabled = false;
        }
    }

    public void RemoveStackedBlock(TotemBlockDetector blockDetector)
    {
        if (blockDetector == null)
        {
            return;
        }

        bool wasCurrentControlledBlock = false;

        if (stackedBlocks.Remove(blockDetector))
        {
            RefreshStackedBlocksText();
            RefreshBlockBasePosition(false);
        }

        if (currentBlockDetector == blockDetector)
        {
            currentBlockDetector = null;
            wasCurrentControlledBlock = true;
        }

        if (blockToLaunch != null && blockDetector.TryGetComponent<Rigidbody>(out Rigidbody detectorRigidbody))
        {
            if (blockToLaunch == detectorRigidbody)
            {
                blockToLaunch = null;
                wasCurrentControlledBlock = true;
            }
        }

        if (wasCurrentControlledBlock)
        {
            isAiming = false;
            currentLineLength = 0f;

            if (lineGraphicImage != null)
            {
                lineGraphicImage.enabled = false;
            }

            balancedTimer = 0f;
            hasSpawnedNextBlock = false;
            hasPendingRespawn = true;
            respawnTimer = balancedHoldTime;
            spawnBlockAfterBaseAnimation = false;
            hasUsedAimForCurrentBlock = false;
            hasLaunchedCurrentBlock = false;
            unstackedBlockQuietTimer = 0f;
        }
    }

    private void CacheBlockBaseInitialPosition()
    {
        if (blockBase == null)
        {
            return;
        }

        blockBaseInitialY = blockBase.position.y;
        blockBaseInitialRotation = blockBase.rotation;
    }

    private void ApplyBlockBaseStateInstant()
    {
        if (blockBase == null)
        {
            return;
        }

        int stackedCount = Mathf.Max(0, stackedBlocks.Count - 1);
        float targetY = blockBaseInitialY + stackedCount;
        Vector3 position = blockBase.position;
        position.y = targetY;
        blockBase.position = position;

        float targetRotation = blockBaseQuarterTurnDegrees * stackedCount;
        blockBase.rotation = blockBaseInitialRotation * Quaternion.Euler(0f, targetRotation, 0f);
    }

    private void RefreshBlockBasePosition(bool spawnAfterAnimation)
    {
        if (blockBase == null)
        {
            if (spawnAfterAnimation)
            {
                SpawnBlock();
            }

            return;
        }

        ProcessOnePendingDestroyBlock();
        CleanupInvalidStackedBlocks();

        int stackedCount = Mathf.Max(0, stackedBlocks.Count - 1);
        Vector3 targetPosition = blockBase.position;
        targetPosition.y = blockBaseInitialY + stackedCount;

        Quaternion targetRotation =
            blockBaseInitialRotation *
            Quaternion.Euler(0f, blockBaseQuarterTurnDegrees * stackedCount, 0f);

        bool hasMeaningfulBaseChange =
            Vector3.Distance(blockBase.position, targetPosition) > 0.001f ||
            Quaternion.Angle(blockBase.rotation, targetRotation) > 0.1f;

        if (!hasMeaningfulBaseChange || blockBaseMoveDuration <= Mathf.Epsilon)
        {
            blockBase.position = targetPosition;
            blockBase.rotation = targetRotation;

            if (spawnAfterAnimation)
            {
                SpawnBlock();
            }

            return;
        }

        blockBaseAnimationStartPosition = blockBase.position;
        blockBaseAnimationStartRotation = blockBase.rotation;
        blockBaseAnimationTargetPosition = targetPosition;
        blockBaseAnimationTargetRotation = targetRotation;
        blockBaseAnimationTimer = 0f;
        isBlockBaseAnimating = true;
        spawnBlockAfterBaseAnimation = spawnAfterAnimation;
    }

    private void UpdateBlockBaseAnimation()
    {
        if (!isBlockBaseAnimating || blockBase == null)
        {
            return;
        }

        blockBaseAnimationTimer += Time.deltaTime;
        float t = Mathf.Clamp01(blockBaseAnimationTimer / blockBaseMoveDuration);
        float smoothT = t * t * (3f - 2f * t);

        blockBase.position = Vector3.Lerp(
            blockBaseAnimationStartPosition,
            blockBaseAnimationTargetPosition,
            smoothT);
        blockBase.rotation = Quaternion.Slerp(
            blockBaseAnimationStartRotation,
            blockBaseAnimationTargetRotation,
            smoothT);

        if (t < 1f)
        {
            return;
        }

        isBlockBaseAnimating = false;

        if (spawnBlockAfterBaseAnimation)
        {
            spawnBlockAfterBaseAnimation = false;
            StartPendingSpawn(spawnDelayAfterBlockBaseMove);
        }
    }

    private void StartPendingSpawn(float delay)
    {
        hasPendingRespawn = true;
        respawnTimer = Mathf.Max(0f, delay);
    }

    private void RefreshStackedBlocksText()
    {
        if (stackedBlocksText == null)
        {
            return;
        }

        stackedBlocksText.text = stackedBlocks.Count.ToString();
    }

    private void StopCurrentBlockMotion()
    {
        if (blockToLaunch == null)
        {
            return;
        }

        blockToLaunch.linearVelocity = Vector3.zero;
        blockToLaunch.angularVelocity = Vector3.zero;
        blockToLaunch.Sleep();
    }

    private void FaceBlockTowardsBase()
    {
        if (baseTargetTransform == null || blockToLaunch == null)
        {
            return;
        }

        Vector3 toBase = baseTargetTransform.position - blockToLaunch.position;
        toBase.y = 0f;

        if (toBase.sqrMagnitude <= Mathf.Epsilon)
        {
            return;
        }

        blockToLaunch.transform.rotation = Quaternion.LookRotation(toBase.normalized, Vector3.up);
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

    private void EnsureTrajectoryLine()
    {
        if (trajectoryLine == null)
        {
            trajectoryLine = GetComponent<LineRenderer>();
        }

        if (trajectoryLine == null)
        {
            trajectoryLine = gameObject.AddComponent<LineRenderer>();
        }

        trajectoryLine.useWorldSpace = true;
        trajectoryLine.alignment = LineAlignment.View;
        trajectoryLine.textureMode = LineTextureMode.Stretch;
        trajectoryLine.widthMultiplier = 0.06f;
        trajectoryLine.positionCount = 0;
        trajectoryLine.enabled = false;

        if (trajectoryLine.material == null)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader != null)
            {
                trajectoryLine.material = new Material(shader);
            }
        }

        Color debugColor = lineGraphicColor;
        debugColor.a = Mathf.Clamp(debugColor.a <= 0f ? 0.85f : debugColor.a, 0.25f, 1f);
        trajectoryLine.startColor = debugColor;
        trajectoryLine.endColor = debugColor;
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
