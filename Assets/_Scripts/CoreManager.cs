using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using System.Collections.Generic;
using TMPro;

public class CoreManager : MonoBehaviour
{
    private enum PointerSource
    {
        None,
        Mouse,
        Touch
    }

    private struct PointerFrameState
    {
        public PointerSource source;
        public Vector2 screenPosition;
        public bool pressedThisFrame;
        public bool releasedThisFrame;
    }

    [System.Serializable]
    private class BlockSnapshotEntry
    {
        public TotemBlockDetector block;
        public int stackIndex;
        public Vector3 localPosition;
        public Quaternion localRotation;
        public float localY;
        public bool wasBalanced;
    }

    [System.Serializable]
    private class TowerSnapshot
    {
        public List<BlockSnapshotEntry> blocks = new List<BlockSnapshotEntry>();
        public float capturedAtTime;
    }

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
    [SerializeField, Range(0.1f, 1f)] private float aimDragSensitivity = 0.6f;
    [SerializeField, Range(0.01f, 1f)] private float aimDirectionResponse = 0.08f;

    [SerializeField] private TMP_Text stackedBlocksText;

    [Header("Stacking Elements")]
    [SerializeField] private Rigidbody blockPrefab;
    [SerializeField] private Transform blockSpawnPoint;
    [SerializeField] private Transform baseTargetTransform;
    [SerializeField] private Transform totemBase;
    [SerializeField] private Transform blockBase;
    [SerializeField] private List<TotemBlockDetector> stackedBlocks = new List<TotemBlockDetector>();

    [Header("Stacking Settings")]
    [SerializeField] private float maxLaunchImpulse = 12f;
    [SerializeField, Min(0f)] private float balancedHoldTime = 0.5f;
    [SerializeField, Min(1f)] private float apexFallGravityMultiplier = 3.5f;
    [SerializeField, Min(0f)] private float apexFallBlendTime = 0.12f;
    [SerializeField] private bool enableBlockBaseMovement = true;
    [SerializeField, Min(1)] private int blockBaseMovementStartStackCount = 2;
    [SerializeField, Min(0f)] private float blockBaseMoveDuration = 0.35f;
    [SerializeField] private float blockBaseQuarterTurnDegrees = 90f;
    [SerializeField, Min(0f)] private float spawnDelayAfterBlockBaseMove = 0.5f;
    [SerializeField, Min(0f)] private float unstackedBlockAutoDestroyDelay = 0.35f;
    [SerializeField, Min(0f)] private float snapshotYDropTolerance = 0.1f;
    [SerializeField] private bool enableSnapshotDebugLogs = true;

    private RectTransform lineGraphic;
    private UnityEngine.UI.Image lineGraphicImage;
    private bool trajectoryVisible;
    private bool isAiming;
    private float currentLineLength;
    private float currentAimAngle;
    private bool hasAimAngle;
    private Rigidbody blockToLaunch;
    private TotemBlockDetector currentBlockDetector;
    private float balancedTimer;
    private bool hasSpawnedNextBlock;
    private bool hasUsedAimForCurrentBlock;
    private bool hasLaunchedCurrentBlock;
    private bool hasEnteredFallPhase;
    private float fallPhaseTimer;
    private float launchApexY;
    private float launchApexTime;
    private float launchStartFixedTime;
    private bool hasLaunchApexData;
    private float unstackedBlockQuietTimer;
    private float respawnTimer;
    private bool hasPendingRespawn;
    private readonly List<TotemBlockDetector> pendingDestroyBlocks = new List<TotemBlockDetector>();
    private int lastPendingDestroyProcessFrame = -1;
    private readonly TowerSnapshot towerSnapshot = new TowerSnapshot();
    private bool hasTowerSnapshot;
    private float blockBaseInitialY;
    private Quaternion blockBaseInitialRotation;
    private bool isBlockBaseAnimating;
    private bool spawnBlockAfterBaseAnimation;
    private float blockBaseAnimationTimer;
    private Vector3 blockBaseAnimationStartPosition;
    private Quaternion blockBaseAnimationStartRotation;
    private Vector3 blockBaseAnimationTargetPosition;
    private Quaternion blockBaseAnimationTargetRotation;
    private PointerSource activePointerSource = PointerSource.None;

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
        bool hasPointerState = TryGetPointerFrameState(out PointerFrameState pointerState);

        if (hasPointerState && pointerState.releasedThisFrame)
        {
            LaunchBlock();
            isAiming = false;
            activePointerSource = PointerSource.None;
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
        if (hasPointerState && pointerState.pressedThisFrame)
        {
            if (!hasUsedAimForCurrentBlock && !hasLaunchedCurrentBlock && IsPointerInsideAimArea(pointerState.screenPosition))
            {
                isAiming = true;
                hasUsedAimForCurrentBlock = true;
                hasAimAngle = false;
                activePointerSource = pointerState.source;

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

        if (!hasPointerState)
        {
            return;
        }

        Vector2 pointerScreen = pointerState.screenPosition;
        Camera uiCamera = GetUiCamera();

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                circle,
                pointerScreen,
                uiCamera,
                out Vector2 pointerLocal))
        {
            return;
        }

        Vector2 center = circle.rect.center;
        Vector2 dir = pointerLocal - center;
        float targetAngle = Mathf.Atan2(-dir.y, -dir.x) * Mathf.Rad2Deg;

        if (!hasAimAngle)
        {
            currentAimAngle = targetAngle;
            hasAimAngle = true;
        }
        else
        {
            currentAimAngle = Mathf.LerpAngle(
                currentAimAngle,
                targetAngle,
                aimDirectionResponse);
        }

        line.anchoredPosition = Vector2.zero;
        line.localRotation = Quaternion.Euler(0f, 0f, currentAimAngle);

        if (lineGraphic != null)
        {
            lineGraphic.anchoredPosition = Vector2.zero;
            lineGraphic.localRotation = Quaternion.identity;
            lineGraphicHeight = Mathf.Clamp(lineGraphicHeight, 2f, 10f);

            if (IsPointerInsideOuterArea(pointerScreen, uiCamera))
            {
                currentLineLength = dir.magnitude * 2f * aimDragSensitivity;
            }

            lineGraphic.sizeDelta = new Vector2(currentLineLength, lineGraphicHeight);

            if (lineGraphicImage != null)
            {
                lineGraphicImage.color = lineGraphicColor;
            }
        }

        UpdateTrajectoryDebug();
    }

    private void FixedUpdate()
    {
        UpdateLaunchFallBehavior();
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
        CacheLaunchApexData(launchDirection, launchImpulse);
        blockToLaunch.AddForce(launchDirection * launchImpulse, ForceMode.Impulse);
        hasLaunchedCurrentBlock = true;
        hasEnteredFallPhase = false;
        fallPhaseTimer = 0f;
        HideTrajectoryDebug();
        hasAimAngle = false;
    }

    private bool TryGetLaunchParameters(out Vector3 launchDirection, out float launchImpulse)
    {
        launchDirection = Vector3.zero;
        launchImpulse = 0f;

        if (blockToLaunch == null || hasLaunchedCurrentBlock || currentLineLength <= 0.001f)
        {
            return false;
        }

        if (!TryGetPointerScreenPosition(out Vector2 pointerScreen))
        {
            return false;
        }

        Camera uiCamera = GetUiCamera();

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                circle,
                pointerScreen,
                uiCamera,
                out Vector2 pointerLocal))
        {
            return false;
        }

        Vector2 launchDirection2D = new Vector2(
            Mathf.Cos(currentAimAngle * Mathf.Deg2Rad),
            Mathf.Sin(currentAimAngle * Mathf.Deg2Rad));

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
        Vector3 startPosition = blockToLaunch.position;
        float predictedApexTime = GetPredictedLaunchApexTime(initialVelocity.y);

        int segmentCount = Mathf.Max(2, trajectorySegmentCount);
        float step = Mathf.Max(0.01f, trajectoryTimeStep);
        float physicsStep = Mathf.Max(Time.fixedDeltaTime, 0.0001f);
        float minimumSimulationTime = step * (segmentCount - 1);
        float totalSimulationTime = Mathf.Max(minimumSimulationTime, predictedApexTime * 2.25f);
        segmentCount = Mathf.Max(segmentCount, Mathf.CeilToInt(totalSimulationTime / step) + 1);

        trajectoryLine.positionCount = segmentCount;
        trajectoryLine.enabled = true;

        Vector3 simulatedPosition = startPosition;
        Vector3 simulatedVelocity = initialVelocity;
        float simulatedFallTimer = 0f;
        bool simulatedFallPhase = false;
        float simulatedTime = 0f;

        for (int i = 0; i < segmentCount; i++)
        {
            float targetTime = i * step;

            while (simulatedTime < targetTime)
            {
                float deltaTime = Mathf.Min(physicsStep, targetTime - simulatedTime);

                if (!simulatedFallPhase &&
                    simulatedTime >= predictedApexTime)
                {
                    simulatedFallPhase = true;
                    simulatedFallTimer = 0f;
                }

                Vector3 totalGravity = Physics.gravity;
                if (simulatedFallPhase)
                {
                    simulatedFallTimer += deltaTime;

                    float blendT = apexFallBlendTime <= 0f
                        ? 1f
                        : Mathf.Clamp01(simulatedFallTimer / apexFallBlendTime);

                    float gravityMultiplier = Mathf.Lerp(1f, apexFallGravityMultiplier, blendT);
                    totalGravity = Physics.gravity * gravityMultiplier;
                }

                simulatedVelocity += totalGravity * deltaTime;
                simulatedPosition += simulatedVelocity * deltaTime;
                simulatedTime += deltaTime;
            }

            trajectoryLine.SetPosition(i, simulatedPosition);
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
        hasAimAngle = false;
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
        CaptureTowerSnapshot();
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

        CleanupInvalidStackedBlocks();

        if (pendingDestroyBlocks.Count > 0)
        {
            return;
        }

        if (!AreStackedBlocksFullySettled())
        {
            respawnTimer = balancedHoldTime;
            return;
        }

        if (respawnTimer <= 0f)
        {
            respawnTimer = balancedHoldTime;
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

    private void CaptureTowerSnapshot()
    {
        towerSnapshot.blocks.Clear();

        for (int i = 0; i < stackedBlocks.Count; i++)
        {
            TotemBlockDetector detector = stackedBlocks[i];

            if (detector == null)
            {
                continue;
            }

            Vector3 localPosition = GetTowerBaseReferenceTransform().InverseTransformPoint(detector.transform.position);
            Quaternion localRotation =
                Quaternion.Inverse(GetTowerBaseReferenceTransform().rotation) *
                detector.transform.rotation;

            towerSnapshot.blocks.Add(new BlockSnapshotEntry
            {
                block = detector,
                stackIndex = i,
                localPosition = localPosition,
                localRotation = localRotation,
                localY = localPosition.y,
                wasBalanced = detector.IsBalanced
            });
        }

        towerSnapshot.capturedAtTime = Time.time;
        hasTowerSnapshot = towerSnapshot.blocks.Count > 0;

        if (enableSnapshotDebugLogs)
        {
            Debug.Log(
                $"[Snapshot] Captured tower snapshot with {towerSnapshot.blocks.Count} block(s) at t={towerSnapshot.capturedAtTime:0.00}.");
        }
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

            BlockSnapshotEntry snapshotEntry = GetSnapshotEntry(detector);
            if (snapshotEntry == null)
            {
                continue;
            }

            if (!HasBlockDroppedOutOfTower(detector, snapshotEntry))
            {
                continue;
            }

            if (IsBlockCompletelyStill(detector))
            {
                if (detector == currentBlockDetector ||
                    (blockToLaunch != null &&
                     detector.TryGetComponent<Rigidbody>(out Rigidbody detectorRigidbody) &&
                     blockToLaunch == detectorRigidbody))
                {
                    ResetCurrentBlockRemovalState();
                }

                pendingDestroyBlocks.Remove(detector);
                stackedBlocks.RemoveAt(i);

                if (enableSnapshotDebugLogs)
                {
                    Debug.Log(
                        $"[Snapshot] Cleaned '{detector.name}' because it dropped below the snapshot Y tolerance and was already still.");
                }

                Destroy(detector.gameObject);
                listChanged = true;
                continue;
            }

            if (!pendingDestroyBlocks.Contains(detector))
            {
                pendingDestroyBlocks.Add(detector);

                if (enableSnapshotDebugLogs)
                {
                    Debug.Log(
                        $"[Snapshot] Queued '{detector.name}' for cleanup because it dropped below the snapshot Y tolerance but is still moving.");
                }
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

    private bool AreStackedBlocksFullySettled()
    {
        for (int i = 0; i < stackedBlocks.Count; i++)
        {
            TotemBlockDetector detector = stackedBlocks[i];

            if (detector == null)
            {
                return false;
            }

            if (!IsBlockCompletelyStill(detector))
            {
                return false;
            }

            if (detector.IsBalanced)
            {
                continue;
            }

            if (!IsBlockRecoveredBySnapshot(detector))
            {
                return false;
            }
        }

        return true;
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

            if (enableSnapshotDebugLogs)
            {
                Debug.Log(
                    $"[Snapshot] Destroyed '{detector.name}' after it settled outside the snapshot tower.");
            }

            return true;
        }

        return false;
    }

    private BlockSnapshotEntry GetSnapshotEntry(TotemBlockDetector detector)
    {
        if (detector == null || !hasTowerSnapshot)
        {
            return null;
        }

        for (int i = 0; i < towerSnapshot.blocks.Count; i++)
        {
            BlockSnapshotEntry entry = towerSnapshot.blocks[i];

            if (entry != null && entry.block == detector)
            {
                return entry;
            }
        }

        return null;
    }

    private bool HasBlockDroppedOutOfTower(
        TotemBlockDetector detector,
        BlockSnapshotEntry snapshotEntry)
    {
        if (detector == null || snapshotEntry == null)
        {
            return false;
        }

        Transform reference = GetTowerBaseReferenceTransform();
        Vector3 localPosition = reference.InverseTransformPoint(detector.transform.position);

        return localPosition.y < snapshotEntry.localY - snapshotYDropTolerance;
    }

    private bool IsBlockRecoveredBySnapshot(TotemBlockDetector detector)
    {
        if (detector == null || !IsBlockCompletelyStill(detector))
        {
            return false;
        }

        BlockSnapshotEntry snapshotEntry = GetSnapshotEntry(detector);
        if (snapshotEntry == null)
        {
            return false;
        }

        Transform reference = GetTowerBaseReferenceTransform();
        Vector3 localPosition = reference.InverseTransformPoint(detector.transform.position);
        float currentYDelta = Mathf.Abs(localPosition.y - snapshotEntry.localY);

        if (currentYDelta > snapshotYDropTolerance)
        {
            return false;
        }

        if (enableSnapshotDebugLogs && !detector.IsBalanced)
        {
            Debug.Log(
                $"[Snapshot] '{detector.name}' recovered tower stability through snapshot validation. CurrentY={localPosition.y:0.000}, SnapshotY={snapshotEntry.localY:0.000}, Delta={currentYDelta:0.000}");
        }

        return true;
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
        hasEnteredFallPhase = false;
        fallPhaseTimer = 0f;
        hasLaunchApexData = false;
        launchApexY = 0f;
        launchApexTime = 0f;
        launchStartFixedTime = 0f;
        hasAimAngle = false;
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

        int movementStepCount = GetBlockBaseMovementStepCount();
        float targetY = blockBaseInitialY + movementStepCount;
        Vector3 position = blockBase.position;
        position.y = targetY;
        blockBase.position = position;

        float targetRotation = blockBaseQuarterTurnDegrees * movementStepCount;
        blockBase.rotation = blockBaseInitialRotation * Quaternion.Euler(0f, targetRotation, 0f);
    }

    private void RefreshBlockBasePosition(bool spawnAfterAnimation)
    {
        if (blockBase == null)
        {
            if (spawnAfterAnimation)
            {
                StartPendingSpawn(balancedHoldTime);
            }

            return;
        }

        ProcessOnePendingDestroyBlock();
        CleanupInvalidStackedBlocks();

        int movementStepCount = GetBlockBaseMovementStepCount();
        Vector3 targetPosition = blockBase.position;
        targetPosition.y = blockBaseInitialY + movementStepCount;

        Quaternion targetRotation =
            blockBaseInitialRotation *
            Quaternion.Euler(0f, blockBaseQuarterTurnDegrees * movementStepCount, 0f);

        bool hasMeaningfulBaseChange =
            Vector3.Distance(blockBase.position, targetPosition) > 0.001f ||
            Quaternion.Angle(blockBase.rotation, targetRotation) > 0.1f;

        if (!hasMeaningfulBaseChange || blockBaseMoveDuration <= Mathf.Epsilon)
        {
            blockBase.position = targetPosition;
            blockBase.rotation = targetRotation;

            if (spawnAfterAnimation)
            {
                StartPendingSpawn(balancedHoldTime);
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

    private int GetBlockBaseMovementStepCount()
    {
        if (blockBase == null || !enableBlockBaseMovement)
        {
            return 0;
        }

        int stackedCount = stackedBlocks.Count;
        if (stackedCount < blockBaseMovementStartStackCount)
        {
            return 0;
        }

        return stackedCount - blockBaseMovementStartStackCount + 1;
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
            StartPendingSpawn(balancedHoldTime + spawnDelayAfterBlockBaseMove);
        }
    }

    private void StartPendingSpawn(float delay)
    {
        hasPendingRespawn = true;
        respawnTimer = Mathf.Max(0f, delay);
    }

    private Transform GetTowerBaseReferenceTransform()
    {
        if (totemBase != null)
        {
            return totemBase;
        }

        return transform;
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
        hasEnteredFallPhase = false;
        fallPhaseTimer = 0f;
        hasLaunchApexData = false;
    }

    private void UpdateLaunchFallBehavior()
    {
        if (!hasLaunchedCurrentBlock || blockToLaunch == null || !hasLaunchApexData)
        {
            hasEnteredFallPhase = false;
            fallPhaseTimer = 0f;
            return;
        }

        if (hasPendingRespawn)
        {
            return;
        }

        if (blockToLaunch.isKinematic)
        {
            hasEnteredFallPhase = false;
            fallPhaseTimer = 0f;
            return;
        }

        float elapsedSinceLaunch = Time.fixedTime - launchStartFixedTime;

        if (!hasEnteredFallPhase &&
            elapsedSinceLaunch >= launchApexTime)
        {
            hasEnteredFallPhase = true;
            fallPhaseTimer = 0f;
        }

        if (!hasEnteredFallPhase)
        {
            return;
        }

        fallPhaseTimer += Time.fixedDeltaTime;

        float blendT = apexFallBlendTime <= 0f
            ? 1f
            : Mathf.Clamp01(fallPhaseTimer / apexFallBlendTime);

        float gravityMultiplier = Mathf.Lerp(1f, apexFallGravityMultiplier, blendT);
        Vector3 extraGravity = Physics.gravity * (gravityMultiplier - 1f);
        blockToLaunch.AddForce(extraGravity, ForceMode.Acceleration);
    }

    private void CacheLaunchApexData(Vector3 launchDirection, float launchImpulse)
    {
        if (blockToLaunch == null)
        {
            hasLaunchApexData = false;
            launchApexY = 0f;
            launchApexTime = 0f;
            launchStartFixedTime = 0f;
            return;
        }

        float mass = Mathf.Max(0.0001f, blockToLaunch.mass);
        Vector3 initialVelocity = launchDirection * (launchImpulse / mass);
        float gravityY = Physics.gravity.y;

        launchStartFixedTime = Time.fixedTime;

        if (gravityY < -0.0001f && initialVelocity.y > 0f)
        {
            launchApexTime = initialVelocity.y / -gravityY;
            launchApexY = blockToLaunch.position.y +
                          (initialVelocity.y * launchApexTime) +
                          (0.5f * gravityY * launchApexTime * launchApexTime);
        }
        else
        {
            launchApexTime = 0f;
            launchApexY = blockToLaunch.position.y;
        }

        hasLaunchApexData = true;
    }

    private float GetPredictedLaunchApexTime(float initialVelocityY)
    {
        float gravityY = Physics.gravity.y;

        if (gravityY < -0.0001f && initialVelocityY > 0f)
        {
            return initialVelocityY / -gravityY;
        }

        return 0f;
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

    private bool IsPointerInsideAimArea(Vector2 pointerScreen)
    {
        Camera uiCamera = GetUiCamera();

        return IsPointerInsideCircle(pointerScreen, uiCamera) || IsPointerInsideOuterArea(pointerScreen, uiCamera);
    }

    private bool TryGetPointerScreenPosition(out Vector2 pointerScreen)
    {
        if (TryGetPointerFrameState(out PointerFrameState pointerState))
        {
            pointerScreen = pointerState.screenPosition;
            return true;
        }

        pointerScreen = Vector2.zero;
        return false;
    }

    private bool TryGetPointerFrameState(out PointerFrameState pointerState)
    {
        if (activePointerSource == PointerSource.Touch)
        {
            return TryGetTouchPointerState(out pointerState);
        }

        if (activePointerSource == PointerSource.Mouse)
        {
            return TryGetMousePointerState(out pointerState);
        }

        if (TryGetTouchPointerState(out pointerState))
        {
            return true;
        }

        if (TryGetMousePointerState(out pointerState))
        {
            return true;
        }

        pointerState = default;
        return false;
    }

    private bool TryGetTouchPointerState(out PointerFrameState pointerState)
    {
        pointerState = default;

        Touchscreen touchScreen = Touchscreen.current;
        if (touchScreen == null)
        {
            return false;
        }

        TouchControl touch = touchScreen.primaryTouch;
        if (touch == null)
        {
            return false;
        }

        bool isActive = touch.press.isPressed || touch.press.wasPressedThisFrame || touch.press.wasReleasedThisFrame;
        if (!isActive)
        {
            return false;
        }

        pointerState.source = PointerSource.Touch;
        pointerState.screenPosition = touch.position.ReadValue();
        pointerState.pressedThisFrame = touch.press.wasPressedThisFrame;
        pointerState.releasedThisFrame = touch.press.wasReleasedThisFrame;
        return true;
    }

    private bool TryGetMousePointerState(out PointerFrameState pointerState)
    {
        pointerState = default;

        Mouse mouse = Mouse.current;
        if (mouse == null)
        {
            return false;
        }

        bool isActive = mouse.leftButton.isPressed ||
                        mouse.leftButton.wasPressedThisFrame ||
                        mouse.leftButton.wasReleasedThisFrame;

        if (!isActive)
        {
            return false;
        }

        pointerState.source = PointerSource.Mouse;
        pointerState.screenPosition = mouse.position.ReadValue();
        pointerState.pressedThisFrame = mouse.leftButton.wasPressedThisFrame;
        pointerState.releasedThisFrame = mouse.leftButton.wasReleasedThisFrame;
        return true;
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
