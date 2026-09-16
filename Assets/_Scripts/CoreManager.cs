using UnityEngine;
using System.Collections.Generic;
using TMPro;
using UnityEngine.Serialization;

public class CoreManager : MonoBehaviour
{
    private enum BlockBalanceEvaluationMode
    {
        LegacyIsBalanced,
        BalanceState
    }

    private enum AimDirectionMode
    {
        RadialDrag,
        HorizontalReferenceBar
    }

    private enum HorizontalReferencePowerMode
    {
        RadialDistanceFromCenter,
        VerticalReferenceRect
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
    [SerializeField] private TrajectoryPreview trajectoryPreview;
    [SerializeField, Range(0.1f, 1f)] private float aimDragSensitivity = 0.6f;
    [SerializeField, Range(0.01f, 1f)] private float aimDirectionResponse = 0.08f;
    [SerializeField] private AimVisualController aimVisualController;
    [SerializeField] private AimInputController aimInputController;

    [Header("Aim Direction Mode")]
    [SerializeField] private AimDirectionMode aimDirectionMode = AimDirectionMode.RadialDrag;
    [SerializeField, HideInInspector] private AimDirectionMode lastAppliedAimDirectionMode;
    [SerializeField, HideInInspector] private bool hasAppliedAimDirectionMode;
    [SerializeField] private RectTransform horizontalAimReference;
    [SerializeField, Range(0f, 180f)] private float maxHorizontalAimAngle = 180f;
    [SerializeField, Min(0.01f)] private float horizontalAimReferenceWidthMultiplier = 1f;
    [SerializeField] private HorizontalReferencePowerMode horizontalReferencePowerMode =
        HorizontalReferencePowerMode.RadialDistanceFromCenter;
    [SerializeField] private RectTransform verticalPowerReference;

    [Header("Radial Aim Stability")]
    [SerializeField] private bool useRadialAimAngleDeadZone = false;
    [SerializeField, Min(0f)] private float radialAimMinAngleDeadZone = 0.25f;
    [SerializeField, Min(0f)] private float radialAimMaxAngleDeadZone = 2f;

    [SerializeField] private TMP_Text stackedBlocksText;

    [Header("Stacking Elements")]
    [SerializeField] private Rigidbody blockPrefab;
    [SerializeField] private Transform blockSpawnPoint;
    [SerializeField] private Transform baseTargetTransform;
    [SerializeField] private Transform totemBase;
    [SerializeField] private Transform blockBase;
    [SerializeField] private BlockBaseMover blockBaseMover;
    [SerializeField] private TowerStackController towerStackController;
    [SerializeField] private List<TotemBlockDetector> stackedBlocks = new List<TotemBlockDetector>();

    [Header("Stacking Settings")]
    [SerializeField, Min(0f), FormerlySerializedAs("maxLaunchImpulse")]
    private float radialDragMaxLaunchImpulse = 12f;
    [SerializeField, Min(0f)] private float horizontalReferenceMaxLaunchImpulse = 12f;
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
    [SerializeField] private bool enableBlockBaseDebugLogs = false;
    [SerializeField] private TowerSnapshotValidator towerSnapshotValidator;

    [Header("Balance Evaluation")]
    [SerializeField] private BlockBalanceEvaluationMode blockBalanceEvaluationMode =
        BlockBalanceEvaluationMode.LegacyIsBalanced;
    [SerializeField] private bool logBalanceEvaluationDifferences;

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
    private bool spawnBlockAfterBaseAnimation;
    private const float CompletelyStillVelocitySqrThreshold = 0.0001f;

    private void Awake()
    {
        if (circle == null || line == null)
        {
            enabled = false;
            return;
        }

        if (aimVisualController == null)
        {
            aimVisualController = GetComponent<AimVisualController>();
        }

        if (aimVisualController == null)
        {
            aimVisualController = gameObject.AddComponent<AimVisualController>();
        }

        if (!aimVisualController.Initialize(
                circle,
                line,
                outerAreaCircle,
                canvas,
                lineGraphicHeight,
                lineGraphicColor))
        {
            enabled = false;
            return;
        }

        ApplyAimDirectionModeIfChanged(true);

        if (aimInputController == null)
        {
            aimInputController = GetComponent<AimInputController>();
        }

        if (aimInputController == null)
        {
            aimInputController = gameObject.AddComponent<AimInputController>();
        }

        if (trajectoryPreview == null)
        {
            trajectoryPreview = GetComponent<TrajectoryPreview>();
        }

        if (trajectoryPreview == null)
        {
            trajectoryPreview = gameObject.AddComponent<TrajectoryPreview>();
        }

        Color trajectoryDebugColor = lineGraphicColor;
        trajectoryDebugColor.a = Mathf.Clamp(
            trajectoryDebugColor.a <= 0f ? 0.85f : trajectoryDebugColor.a,
            0.25f,
            1f);

        if (!trajectoryPreview.Initialize(
                trajectoryLine,
                trajectorySegmentCount,
                trajectoryTimeStep,
                showTrajectoryDebug,
                trajectoryDebugColor))
        {
            enabled = false;
            return;
        }
        if (blockBaseMover == null)
        {
            blockBaseMover = GetComponent<BlockBaseMover>();
        }

        if (blockBaseMover == null)
        {
            blockBaseMover = gameObject.AddComponent<BlockBaseMover>();
        }

        blockBaseMover.Configure(
            blockBase,
            enableBlockBaseMovement,
            blockBaseMovementStartStackCount,
            blockBaseMoveDuration,
            blockBaseQuarterTurnDegrees,
            enableBlockBaseDebugLogs);
        blockBaseMover.CacheInitialState();

        if (towerStackController == null)
        {
            towerStackController = GetComponent<TowerStackController>();
        }

        if (towerStackController == null)
        {
            towerStackController = gameObject.AddComponent<TowerStackController>();
        }

        towerStackController.SynchronizeStackedBlocks(stackedBlocks);

        if (towerSnapshotValidator == null)
        {
            towerSnapshotValidator = GetComponent<TowerSnapshotValidator>();
        }

        if (towerSnapshotValidator == null)
        {
            towerSnapshotValidator = gameObject.AddComponent<TowerSnapshotValidator>();
        }

        towerSnapshotValidator.Configure(
            snapshotYDropTolerance,
            enableSnapshotDebugLogs,
            GetTowerBaseReferenceTransform());

        RefreshStackedBlocksText();
        blockBaseMover.ApplyStateInstant(towerStackController.Count);
        SpawnBlock();
    }

    private void Update()
    {
        bool hasPointerState = aimInputController.TryGetPointerFrameState(
            out AimInputController.PointerFrameState pointerState);

        if (hasPointerState && pointerState.ReleasedThisFrame)
        {
            if (isAiming && !LaunchBlock())
            {
                CancelAimWithoutLaunch();
            }
            else
            {
                isAiming = false;
                aimInputController.ClearActivePointerSource();
                aimVisualController.HideAimLine();
            }
        }

        UpdatePendingRespawn();
        UpdateBlockBaseAnimation();
        UpdateBalancedBlockState();
        UpdateUnstackedLaunchedBlockState();
        UpdateTrajectoryPreview();
        if (hasPointerState && pointerState.PressedThisFrame)
        {
            if (!hasUsedAimForCurrentBlock && !hasLaunchedCurrentBlock && CanStartAim(pointerState.ScreenPosition))
            {
                isAiming = true;
                hasUsedAimForCurrentBlock = true;
                hasAimAngle = false;
                aimInputController.SetActivePointerSource(pointerState.Source);

                BeginHorizontalAimGuideDrag();
                aimVisualController.ShowAimLine();
                currentLineLength = 0f;
                aimVisualController.ResetAimLineLength();
            }
        }

        if (!isAiming)
        {
            trajectoryPreview.HidePreview();
            return;
        }

        if (!hasPointerState)
        {
            return;
        }

        Vector2 pointerScreen = pointerState.ScreenPosition;
        if (!aimVisualController.TryGetPointerLocalPosition(pointerScreen, out Vector2 pointerLocal))
        {
            return;
        }

        Vector2 center = aimVisualController.GetAimCenter();
        Vector2 dir = pointerLocal - center;
        float targetAngle = CalculateTargetAimAngle(pointerScreen, dir);

        if (!hasAimAngle)
        {
            hasAimAngle = true;
            currentAimAngle = targetAngle;
        }
        else if (aimDirectionMode == AimDirectionMode.RadialDrag)
        {
            if (ShouldUpdateRadialAimAngle(dir, targetAngle))
            {
                currentAimAngle = targetAngle;
            }
        }
        else
        {
            // Non-radial aim modes stay immediate for touch, mouse, desktop and WebGL.
            currentAimAngle = targetAngle;
        }

        // Length is already applied directly, preserving immediate touch response.
        if (ShouldUpdateAimLineLength(pointerScreen))
        {
            float maxLineLength = GetAimReferenceLength();
            currentLineLength = CalculateCurrentLineLength(pointerScreen, dir, maxLineLength);
        }

        UpdateHorizontalAimGuideDuringDrag(pointerScreen);
        aimVisualController.UpdateAimLine(currentAimAngle, currentLineLength);

        UpdateTrajectoryPreview();
    }

    private float CalculateTargetAimAngle(Vector2 pointerScreen, Vector2 radialDirection)
    {
        switch (aimDirectionMode)
        {
            case AimDirectionMode.HorizontalReferenceBar:
                return CalculateHorizontalReferenceAimAngle(pointerScreen, radialDirection);

            case AimDirectionMode.RadialDrag:
            default:
                return CalculateRadialAimAngle(radialDirection);
        }
    }

    private float CalculateHorizontalReferenceAimAngle(
        Vector2 pointerScreen,
        Vector2 fallbackRadialDirection)
    {
        if (!TryGetClampedHorizontalReferenceLocalPointer(
                pointerScreen,
                out Vector2 clampedLocalPointer))
        {
            return CalculateRadialAimAngle(fallbackRadialDirection);
        }

        float halfWidth = horizontalAimReference.rect.width * 0.5f;
        halfWidth *= horizontalAimReferenceWidthMultiplier;

        if (halfWidth <= Mathf.Epsilon)
        {
            return CalculateRadialAimAngle(fallbackRadialDirection);
        }

        float normalizedX = Mathf.Clamp(clampedLocalPointer.x / halfWidth, -1f, 1f);
        return 90f + normalizedX * maxHorizontalAimAngle;
    }

    private float CalculateCurrentLineLength(
        Vector2 pointerScreen,
        Vector2 radialDirection,
        float maxLineLength)
    {
        if (aimDirectionMode == AimDirectionMode.HorizontalReferenceBar &&
            horizontalReferencePowerMode == HorizontalReferencePowerMode.VerticalReferenceRect)
        {
            return CalculateVerticalReferenceLineLength(
                pointerScreen,
                radialDirection,
                maxLineLength);
        }

        return radialDirection.magnitude * 2f * aimDragSensitivity;
    }

    private bool ShouldUpdateAimLineLength(Vector2 pointerScreen)
    {
        if (aimDirectionMode == AimDirectionMode.HorizontalReferenceBar &&
            horizontalReferencePowerMode == HorizontalReferencePowerMode.VerticalReferenceRect)
        {
            return true;
        }

        return aimVisualController.IsPointerInsideOuterArea(pointerScreen);
    }

    private float CalculateVerticalReferenceLineLength(
        Vector2 pointerScreen,
        Vector2 fallbackRadialDirection,
        float maxLineLength)
    {
        RectTransform reference = verticalPowerReference != null
            ? verticalPowerReference
            : horizontalAimReference;

        if (reference == null || aimVisualController == null)
        {
            return fallbackRadialDirection.magnitude * 2f * aimDragSensitivity;
        }

        if (!TryGetClampedLocalPointerInReference(
                reference,
                pointerScreen,
                out Vector2 clampedLocalPointer))
        {
            return fallbackRadialDirection.magnitude * 2f * aimDragSensitivity;
        }

        float height = reference.rect.height;
        if (height <= Mathf.Epsilon)
        {
            return fallbackRadialDirection.magnitude * 2f * aimDragSensitivity;
        }

        float topY = height * 0.5f;
        float bottomY = -height * 0.5f;
        float power01 = Mathf.InverseLerp(topY, bottomY, clampedLocalPointer.y);

        return Mathf.Clamp01(power01) * maxLineLength;
    }

    private bool TryGetClampedHorizontalReferenceLocalPointer(
        Vector2 pointerScreen,
        out Vector2 clampedLocalPointer)
    {
        return TryGetClampedLocalPointerInReference(
            horizontalAimReference,
            pointerScreen,
            out clampedLocalPointer);
    }

    private bool TryGetClampedLocalPointerInReference(
        RectTransform reference,
        Vector2 pointerScreen,
        out Vector2 clampedLocalPointer)
    {
        clampedLocalPointer = Vector2.zero;

        if (reference == null || aimVisualController == null)
        {
            return false;
        }

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                reference,
                pointerScreen,
                aimVisualController.GetUiCamera(),
                out Vector2 localPointer))
        {
            return false;
        }

        Rect rect = reference.rect;
        clampedLocalPointer = new Vector2(
            Mathf.Clamp(localPointer.x, rect.xMin, rect.xMax),
            Mathf.Clamp(localPointer.y, rect.yMin, rect.yMax));

        return true;
    }

    private static float CalculateRadialAimAngle(Vector2 radialDirection)
    {
        return Mathf.Atan2(-radialDirection.y, -radialDirection.x) * Mathf.Rad2Deg;
    }

    private bool ShouldUpdateRadialAimAngle(Vector2 radialDirection, float targetAngle)
    {
        if (!useRadialAimAngleDeadZone || !hasAimAngle)
        {
            return true;
        }

        float angleDelta = Mathf.Abs(Mathf.DeltaAngle(currentAimAngle, targetAngle));
        float maxLineLength = GetAimReferenceLength();
        float targetLineLength = radialDirection.magnitude * 2f * aimDragSensitivity;
        float dragStrength = maxLineLength > Mathf.Epsilon
            ? Mathf.Clamp01(targetLineLength / maxLineLength)
            : 1f;

        float minDeadZone = Mathf.Min(radialAimMinAngleDeadZone, radialAimMaxAngleDeadZone);
        float maxDeadZone = Mathf.Max(radialAimMinAngleDeadZone, radialAimMaxAngleDeadZone);
        float currentDeadZone = Mathf.Lerp(maxDeadZone, minDeadZone, dragStrength);

        return angleDelta >= currentDeadZone;
    }

    private float GetAimReferenceLength()
    {
        if (aimVisualController != null)
        {
            return aimVisualController.GetMaxAimLineLength();
        }

        return 1f;
    }

    private float GetActiveMaxLaunchImpulse()
    {
        switch (aimDirectionMode)
        {
            case AimDirectionMode.HorizontalReferenceBar:
                return horizontalReferenceMaxLaunchImpulse;

            case AimDirectionMode.RadialDrag:
            default:
                return radialDragMaxLaunchImpulse;
        }
    }

    private void FixedUpdate()
    {
        UpdateLaunchFallBehavior();
    }

    private void OnValidate()
    {
        ApplyAimDirectionModeIfChanged();
    }

    public void ApplySelectedAimDirectionModeFromEditor()
    {
        ApplyAimDirectionModeIfChanged(true);
    }

    private bool LaunchBlock()
    {
        if (!TryGetLaunchParameters(out Vector3 launchDirection, out float launchImpulse))
        {
            return false;
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
        HideHorizontalAimGuideBlock();
        trajectoryPreview.HidePreview();
        hasAimAngle = false;
        return true;
    }

    private void CancelAimWithoutLaunch()
    {
        isAiming = false;
        hasAimAngle = false;
        currentLineLength = 0f;
        hasUsedAimForCurrentBlock = false;

        aimInputController.ClearActivePointerSource();
        aimVisualController.HideAimLine();
        trajectoryPreview.HidePreview();
        ReturnHorizontalAimGuideBlockToCenter();
    }

    private bool TryGetLaunchParameters(out Vector3 launchDirection, out float launchImpulse)
    {
        launchDirection = Vector3.zero;
        launchImpulse = 0f;

        if (blockToLaunch == null || hasLaunchedCurrentBlock || currentLineLength <= 0.001f)
        {
            return false;
        }

        if (!aimInputController.TryGetPointerScreenPosition(out Vector2 pointerScreen))
        {
            return false;
        }

        if (!aimVisualController.TryGetPointerLocalPosition(pointerScreen, out _))
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

        float maxLineLength = GetAimReferenceLength();
        float forceMultiplier = Mathf.Clamp01(currentLineLength / maxLineLength);
        float activeMaxLaunchImpulse = GetActiveMaxLaunchImpulse();

        launchDirection = new Vector3(launchDirection2D.x, launchDirection2D.y, 0f);
        launchImpulse = forceMultiplier * activeMaxLaunchImpulse;
        return true;
    }

    private void UpdateTrajectoryPreview()
    {
        if (!isAiming)
        {
            trajectoryPreview.HidePreview();
            return;
        }

        if (!TryGetLaunchParameters(out Vector3 launchDirection, out float launchImpulse))
        {
            trajectoryPreview.HidePreview();
            return;
        }

        trajectoryPreview.ShowPreview(
            blockToLaunch,
            launchDirection,
            launchImpulse,
            apexFallGravityMultiplier,
            apexFallBlendTime);
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

        if (towerStackController.PendingDestroyCount > 0)
        {
            StartPendingSpawn(spawnDelayAfterBlockBaseMove);
            return;
        }

        CleanupInvalidStackedBlocks();

        if (towerStackController.PendingDestroyCount > 0)
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

        ResetCurrentBlockRuntimeState();
        ResetAimState();
        ResetLaunchState();
        ResetRespawnState();
        RefreshHorizontalAimGuideBlockForReadyBlock();
    }

    private void UpdateBalancedBlockState()
    {
        if (hasPendingRespawn)
        {
            return;
        }

        if (blockBaseMover.IsAnimating)
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

        if (!IsBlockReadyToStack(currentBlockDetector))
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
        towerSnapshotValidator.CaptureSnapshot(
            towerStackController.StackedBlocks,
            GetTowerBaseReferenceTransform());
        hasSpawnedNextBlock = true;
        RefreshBlockBasePosition(true);
    }

    private void UpdateUnstackedLaunchedBlockState()
    {
        if (!hasLaunchedCurrentBlock || hasPendingRespawn || blockBaseMover.IsAnimating)
        {
            unstackedBlockQuietTimer = 0f;
            return;
        }

        if (currentBlockDetector == null || blockToLaunch == null)
        {
            unstackedBlockQuietTimer = 0f;
            return;
        }

        if (towerStackController.Contains(currentBlockDetector) ||
            IsBlockReadyToStack(currentBlockDetector))
        {
            unstackedBlockQuietTimer = 0f;
            return;
        }

        bool isCompletelyStill =
            blockToLaunch.linearVelocity.sqrMagnitude <= CompletelyStillVelocitySqrThreshold &&
            blockToLaunch.angularVelocity.sqrMagnitude <= CompletelyStillVelocitySqrThreshold;

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

    // Keep respawn pending until cleanup and tower settling are complete.
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

        if (towerStackController.PendingDestroyCount > 0)
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

        if (towerStackController.RegisterBlock(currentBlockDetector))
        {
            RefreshStackedBlocksText();
        }
    }

    // Queue or remove fallen blocks while preserving the one-at-a-time cleanup flow.
    private void CleanupInvalidStackedBlocks()
    {
        bool listChanged = towerStackController.RemoveNullStackedBlocks();

        for (int i = towerStackController.Count - 1; i >= 0; i--)
        {
            TotemBlockDetector detector = towerStackController.StackedBlocks[i];

            if (!towerSnapshotValidator.HasBlockDroppedOutOfTower(
                    detector,
                    GetTowerBaseReferenceTransform()))
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

                towerStackController.RemovePendingDestroy(detector);
                towerStackController.RemoveBlock(detector);

                if (enableSnapshotDebugLogs)
                {
                    Debug.Log(
                        $"[Snapshot] Cleaned '{detector.name}' because it dropped below the snapshot Y tolerance and was already still.");
                }

                Destroy(detector.gameObject);
                listChanged = true;
                continue;
            }

            if (!towerStackController.ContainsPendingDestroy(detector))
            {
                towerStackController.TryQueuePendingDestroy(detector);

                if (enableSnapshotDebugLogs)
                {
                    Debug.Log(
                        $"[Snapshot] Queued '{detector.name}' for cleanup because it dropped below the snapshot Y tolerance but is still moving.");
                }
            }

            towerStackController.RemoveBlock(detector);
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
        for (int i = 0; i < towerStackController.Count; i++)
        {
            TotemBlockDetector detector = towerStackController.StackedBlocks[i];

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

            if (!towerSnapshotValidator.IsBlockRecoveredBySnapshot(
                    detector,
                    GetTowerBaseReferenceTransform(),
                    IsBlockCompletelyStill))
            {
                return false;
            }
        }

        return true;
    }

    private bool ProcessOnePendingDestroyBlock()
    {
        return towerStackController.ProcessOnePendingDestroyBlock(
            currentBlockDetector,
            blockToLaunch,
            ResetCurrentBlockRemovalState,
            IsBlockCompletelyStill,
            enableSnapshotDebugLogs);
    }

    private bool IsBlockCompletelyStill(TotemBlockDetector detector)
    {
        if (detector == null || detector.TryGetComponent<Rigidbody>(out Rigidbody rigidbody) == false)
        {
            return false;
        }

        return rigidbody.linearVelocity.sqrMagnitude <= CompletelyStillVelocitySqrThreshold &&
               rigidbody.angularVelocity.sqrMagnitude <= CompletelyStillVelocitySqrThreshold;
    }

    private bool IsBlockReadyToStack(TotemBlockDetector detector)
    {
        if (detector == null)
        {
            return false;
        }

        if (logBalanceEvaluationDifferences)
        {
            LogBalanceEvaluationDifference(detector);
        }

        switch (blockBalanceEvaluationMode)
        {
            case BlockBalanceEvaluationMode.BalanceState:
                return detector.BalanceState == TotemBlockBalanceState.Balanced;

            case BlockBalanceEvaluationMode.LegacyIsBalanced:
            default:
                return detector.IsBalanced;
        }
    }

    private void LogBalanceEvaluationDifference(TotemBlockDetector detector)
    {
        bool legacyBalanced = detector.IsBalanced;
        bool stateBalanced = detector.BalanceState == TotemBlockBalanceState.Balanced;

        if (legacyBalanced == stateBalanced)
        {
            return;
        }

        Debug.Log(
            $"[BalanceEvaluation] Block='{detector.name}', " +
            $"LegacyIsBalanced={legacyBalanced}, " +
            $"BalanceState={detector.BalanceState}, " +
            $"StateBalanced={stateBalanced}");
    }

    private void ResetCurrentBlockRemovalState()
    {
        ResetCurrentBlockReferences();
        ResetAimState();
        ResetLaunchState();
        ResetRespawnState();
        ResetCurrentBlockRuntimeState();

        hasPendingRespawn = true;
        respawnTimer = balancedHoldTime;
    }

    private void ResetAimState()
    {
        isAiming = false;
        currentLineLength = 0f;
        hasUsedAimForCurrentBlock = false;
        hasAimAngle = false;

        aimInputController.ClearActivePointerSource();
        aimVisualController.HideAimLine();
    }

    private void ResetLaunchState()
    {
        hasLaunchedCurrentBlock = false;
        hasEnteredFallPhase = false;
        fallPhaseTimer = 0f;
        hasLaunchApexData = false;
        launchApexY = 0f;
        launchApexTime = 0f;
        launchStartFixedTime = 0f;
        unstackedBlockQuietTimer = 0f;
    }

    private void ResetRespawnState()
    {
        hasPendingRespawn = false;
        respawnTimer = 0f;
        spawnBlockAfterBaseAnimation = false;
    }

    private void ResetCurrentBlockReferences()
    {
        currentBlockDetector = null;
        blockToLaunch = null;
    }

    private void ResetCurrentBlockRuntimeState()
    {
        balancedTimer = 0f;
        hasSpawnedNextBlock = false;
    }

    public void RemoveStackedBlock(TotemBlockDetector blockDetector)
    {
        if (blockDetector == null)
        {
            return;
        }

        bool wasCurrentControlledBlock = false;

        if (towerStackController.RemoveBlock(blockDetector))
        {
            RefreshStackedBlocksText();
            RefreshBlockBasePosition(false);
        }

        if (currentBlockDetector == blockDetector)
        {
            wasCurrentControlledBlock = true;
        }

        if (blockToLaunch != null && blockDetector.TryGetComponent<Rigidbody>(out Rigidbody detectorRigidbody))
        {
            if (blockToLaunch == detectorRigidbody)
            {
                wasCurrentControlledBlock = true;
            }
        }

        if (wasCurrentControlledBlock)
        {
            ResetCurrentBlockReferences();
            ResetAimState();
            ResetLaunchState();
            ResetRespawnState();
            ResetCurrentBlockRuntimeState();

            hasPendingRespawn = true;
            respawnTimer = balancedHoldTime;
        }
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

        blockBaseMover.Configure(
            blockBase,
            enableBlockBaseMovement,
            blockBaseMovementStartStackCount,
            blockBaseMoveDuration,
            blockBaseQuarterTurnDegrees,
            enableBlockBaseDebugLogs);

        blockBaseMover.RefreshPosition(
        towerStackController.Count,
            true,
            out bool animationStarted);

        if (animationStarted)
        {
            spawnBlockAfterBaseAnimation = spawnAfterAnimation;
        }

        if (!animationStarted && spawnAfterAnimation)
        {
            StartPendingSpawn(balancedHoldTime);
        }
    }

    private void UpdateBlockBaseAnimation()
    {
        if (!blockBaseMover.UpdateAnimation())
        {
            return;
        }

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

        stackedBlocksText.text = towerStackController.Count.ToString();
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

    // Add extra gravity only after the predicted apex of the launch trajectory.
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
        return aimVisualController.IsPointerInsideAimArea(pointerScreen);
    }

    private bool CanStartAim(Vector2 pointerScreen)
    {
        bool insideAimArea = aimVisualController != null &&
                             aimVisualController.IsPointerInsideAimArea(pointerScreen);

        if (aimDirectionMode != AimDirectionMode.HorizontalReferenceBar)
        {
            return insideAimArea;
        }

        bool insideGuideBlock = aimVisualController != null &&
                                aimVisualController.IsPointerInsideAimGuideBlock(pointerScreen);

        return insideAimArea || insideGuideBlock;
    }

    private bool ShouldUseHorizontalAimGuideBlock()
    {
        return aimDirectionMode == AimDirectionMode.HorizontalReferenceBar &&
               aimVisualController != null &&
               aimVisualController.HasAimGuideBlock;
    }

    private void RefreshHorizontalAimGuideBlockForReadyBlock()
    {
        if (ShouldUseHorizontalAimGuideBlock() && blockToLaunch != null && !hasLaunchedCurrentBlock)
        {
            aimVisualController.SetUseAimGuideBlockAsLineOrigin(true);
            aimVisualController.ResetAimGuideBlockImmediate(GetHorizontalAimGuideIdlePosition());
            aimVisualController.ShowAimGuideBlock();
            return;
        }

        if (aimVisualController == null)
        {
            return;
        }

        aimVisualController.SetUseAimGuideBlockAsLineOrigin(false);
        if (aimDirectionMode == AimDirectionMode.HorizontalReferenceBar)
        {
            aimVisualController.HideAimGuideBlockIfNoBlockReady();
            return;
        }

        aimVisualController.HideAimGuideBlock();
    }

    private void ApplyAimDirectionModeIfChanged(bool force = false)
    {
        if (!force &&
            hasAppliedAimDirectionMode &&
            lastAppliedAimDirectionMode == aimDirectionMode)
        {
            return;
        }

        lastAppliedAimDirectionMode = aimDirectionMode;
        hasAppliedAimDirectionMode = true;

        ApplyAimDirectionMode();
    }

    private void ApplyAimDirectionMode()
    {
        bool useHorizontalMode = aimDirectionMode == AimDirectionMode.HorizontalReferenceBar;

        if (circle != null)
        {
            circle.gameObject.SetActive(!useHorizontalMode);
        }

        if (horizontalAimReference != null)
        {
            horizontalAimReference.gameObject.SetActive(useHorizontalMode);
        }

        if (aimVisualController != null)
        {
            aimVisualController.SetUseAimGuideBlockAsLineOrigin(useHorizontalMode);

            if (useHorizontalMode)
            {
                RefreshHorizontalAimGuideBlockForReadyBlock();
            }
            else
            {
                aimVisualController.HideAimGuideBlock();
                aimVisualController.SetUseAimGuideBlockAsLineOrigin(false);
            }
        }

        CancelAimStateAfterModeChange();
    }

    private void CancelAimStateAfterModeChange()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        isAiming = false;
        currentLineLength = 0f;
        hasAimAngle = false;
        hasUsedAimForCurrentBlock = false;

        if (aimInputController != null)
        {
            aimInputController.ClearActivePointerSource();
        }

        if (aimVisualController != null)
        {
            aimVisualController.HideAimLine();
            aimVisualController.ResetAimLineLength();
        }

        if (trajectoryPreview != null)
        {
            trajectoryPreview.HidePreview();
        }
    }

    private void BeginHorizontalAimGuideDrag()
    {
        if (!ShouldUseHorizontalAimGuideBlock())
        {
            if (aimVisualController != null)
            {
                aimVisualController.SetUseAimGuideBlockAsLineOrigin(false);
            }

            return;
        }

        aimVisualController.StopAimGuideBlockReturn();
        aimVisualController.ShowAimGuideBlock();
        aimVisualController.SetUseAimGuideBlockAsLineOrigin(true);
    }

    private void UpdateHorizontalAimGuideDuringDrag(Vector2 pointerScreen)
    {
        if (!ShouldUseHorizontalAimGuideBlock())
        {
            return;
        }

        if (!TryGetAimGuideLocalPosition(pointerScreen, out Vector2 guideLocalPosition))
        {
            return;
        }

        aimVisualController.SetAimGuideBlockPosition(guideLocalPosition);
        aimVisualController.SetAimGuideBlockRotation(currentAimAngle);
    }

    private void HideHorizontalAimGuideBlock()
    {
        if (!ShouldUseHorizontalAimGuideBlock())
        {
            return;
        }

        aimVisualController.HideAimGuideBlock();
    }

    private void ReturnHorizontalAimGuideBlockToCenter()
    {
        if (!ShouldUseHorizontalAimGuideBlock())
        {
            return;
        }

        aimVisualController.ReturnAimGuideBlockToCenter(GetHorizontalAimGuideIdlePosition());
    }

    private Vector2 GetHorizontalAimGuideIdlePosition()
    {
        if (aimVisualController == null)
        {
            return Vector2.zero;
        }

        return aimVisualController.GetAimGuideBlockIdlePosition(horizontalAimReference);
    }

    private bool TryGetAimGuideLocalPosition(Vector2 pointerScreen, out Vector2 guideLocalPosition)
    {
        return TryGetClampedHorizontalReferenceLocalPointer(
            pointerScreen,
            out guideLocalPosition);
    }

}
