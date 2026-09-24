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

    private enum BlockSpawnValidationMode
    {
        LegacyValidation,
        ImmediateAfterLaunch
    }

    private class LaunchedBlockFallData
    {
        public Rigidbody Rigidbody;
        public float LaunchStartFixedTime;
        public float LaunchApexTime;
        public bool HasEnteredFallPhase;
        public float FallPhaseTimer;
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

    [Header("Balance State Thresholds")]
    [SerializeField, Min(0f)] private float balanceStateLinearVelocityThreshold = 0.08f;
    [SerializeField, Min(0f)] private float balanceStateAngularVelocityThreshold = 0.10f;

    [Header("Block Spawn Validation")]
    [SerializeField] private BlockSpawnValidationMode blockSpawnValidationMode =
        BlockSpawnValidationMode.LegacyValidation;
    [SerializeField, Min(0f)] private float immediateSpawnDelayAfterLaunch = 0.35f;
    [SerializeField, Min(1)] private int maxImmediatePendingLaunchedBlocks = 3;

    [Header("Spawn Safety Cleanup")]
    [SerializeField] private bool enableSpawnAreaCleanup = true;
    [SerializeField] private Vector3 spawnBlockCleanupHalfExtents = new Vector3(0.45f, 0.45f, 0.45f);
    [SerializeField] private LayerMask spawnCleanupLayers = ~0;
    [SerializeField, Min(0f)] private float spawnAreaCleanupRetryDelay = 0.05f;

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
    private float unstackedBlockQuietTimer;
    private float respawnTimer;
    private bool hasPendingRespawn;
    private bool spawnBlockAfterBaseAnimation;
    private readonly List<TotemBlockDetector> immediatePendingLaunchedBlocks =
        new List<TotemBlockDetector>();
    private readonly List<LaunchedBlockFallData> launchedBlockFallData =
        new List<LaunchedBlockFallData>();
    private readonly List<TotemBlockDetector> supportDetectorBuffer =
        new List<TotemBlockDetector>();
    private readonly Stack<TotemBlockDetector> supportDetectorStack =
        new Stack<TotemBlockDetector>();
    private readonly HashSet<TotemBlockDetector> visitedSupportDetectors =
        new HashSet<TotemBlockDetector>();
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
        UpdateImmediatePendingLaunchedBlocksState();
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
        UpdateLaunchedBlocksFallBehavior();
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
        blockToLaunch.AddForce(launchDirection * launchImpulse, ForceMode.Impulse);
        RegisterLaunchedBlockFallData(blockToLaunch, launchDirection, launchImpulse);
        hasLaunchedCurrentBlock = true;
        AddCurrentBlockToImmediatePendingLaunchedBlocks();
        TryScheduleImmediateSpawnAfterLaunch();
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
        CleanupImmediatePendingLaunchedBlocks();

        if (towerStackController.PendingDestroyCount > 0)
        {
            StartPendingSpawn(spawnDelayAfterBlockBaseMove);
            return;
        }

        if (CleanupBlocksOccupyingSpawnArea())
        {
            StartPendingSpawn(spawnAreaCleanupRetryDelay);
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

        currentBlockDetector.ConfigureBalanceStateThresholds(
            balanceStateLinearVelocityThreshold,
            balanceStateAngularVelocityThreshold);

        ResetCurrentBlockRuntimeState();
        ResetAimState();
        ResetLaunchState();
        ResetRespawnState();
        RefreshHorizontalAimGuideBlockForReadyBlock();
    }

    private void UpdateBalancedBlockState()
    {
        if (blockSpawnValidationMode == BlockSpawnValidationMode.ImmediateAfterLaunch)
        {
            return;
        }

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

        if (TryCleanupInvalidBlockBaseSupportedBlock(currentBlockDetector))
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
        CleanupImmediatePendingLaunchedBlocks();

        if (towerStackController.PendingDestroyCount > 0)
        {
            return;
        }

        if (blockSpawnValidationMode == BlockSpawnValidationMode.LegacyValidation &&
            !AreStackedBlocksFullySettled())
        {
            respawnTimer = balancedHoldTime;
            return;
        }

        if (blockSpawnValidationMode == BlockSpawnValidationMode.ImmediateAfterLaunch &&
            !CanSpawnImmediateModeBlockNow())
        {
            return;
        }

        if (respawnTimer <= 0f)
        {
            respawnTimer = blockSpawnValidationMode == BlockSpawnValidationMode.ImmediateAfterLaunch
                ? immediateSpawnDelayAfterLaunch
                : balancedHoldTime;
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
            immediatePendingLaunchedBlocks.Remove(currentBlockDetector);
            RefreshStackedBlocksText();
        }
    }

    private void AddCurrentBlockToImmediatePendingLaunchedBlocks()
    {
        if (blockSpawnValidationMode != BlockSpawnValidationMode.ImmediateAfterLaunch ||
            currentBlockDetector == null)
        {
            return;
        }

        CleanupImmediatePendingLaunchedBlocks();

        if (!immediatePendingLaunchedBlocks.Contains(currentBlockDetector))
        {
            immediatePendingLaunchedBlocks.Add(currentBlockDetector);
        }
    }

    private void UpdateImmediatePendingLaunchedBlocksState()
    {
        if (blockSpawnValidationMode != BlockSpawnValidationMode.ImmediateAfterLaunch)
        {
            return;
        }

        bool registeredBlock = false;

        for (int i = immediatePendingLaunchedBlocks.Count - 1; i >= 0; i--)
        {
            TotemBlockDetector detector = immediatePendingLaunchedBlocks[i];

            if (detector == null)
            {
                immediatePendingLaunchedBlocks.RemoveAt(i);
                continue;
            }

            if (towerStackController.Contains(detector) ||
                towerStackController.ContainsPendingDestroy(detector))
            {
                immediatePendingLaunchedBlocks.RemoveAt(i);
                continue;
            }

            if (TryCleanupInvalidBlockBaseSupportedBlock(detector))
            {
                continue;
            }

            if (!IsBlockReadyToStack(detector))
            {
                continue;
            }

            if (towerStackController.RegisterBlock(detector))
            {
                immediatePendingLaunchedBlocks.RemoveAt(i);
                registeredBlock = true;
            }
        }

        if (registeredBlock)
        {
            RefreshStackedBlocksText();
            towerSnapshotValidator.CaptureSnapshot(
                towerStackController.StackedBlocks,
                GetTowerBaseReferenceTransform());
            RefreshBlockBasePosition(false);
        }

        if (!hasPendingRespawn && hasLaunchedCurrentBlock && CanSpawnImmediateModeBlockNow())
        {
            StartPendingSpawn(immediateSpawnDelayAfterLaunch);
        }
    }

    private void CleanupImmediatePendingLaunchedBlocks()
    {
        for (int i = immediatePendingLaunchedBlocks.Count - 1; i >= 0; i--)
        {
            TotemBlockDetector detector = immediatePendingLaunchedBlocks[i];

            if (detector == null ||
                towerStackController.Contains(detector) ||
                towerStackController.ContainsPendingDestroy(detector))
            {
                immediatePendingLaunchedBlocks.RemoveAt(i);
            }
        }
    }

    private void TryScheduleImmediateSpawnAfterLaunch()
    {
        if (!CanSpawnImmediateModeBlockNow())
        {
            return;
        }

        StartPendingSpawn(immediateSpawnDelayAfterLaunch);
    }

    private bool CanSpawnImmediateModeBlockNow()
    {
        if (blockSpawnValidationMode != BlockSpawnValidationMode.ImmediateAfterLaunch)
        {
            return false;
        }

        CleanupImmediatePendingLaunchedBlocks();

        return immediatePendingLaunchedBlocks.Count < Mathf.Max(1, maxImmediatePendingLaunchedBlocks);
    }

    private bool CleanupBlocksOccupyingSpawnArea()
    {
        if (!enableSpawnAreaCleanup)
        {
            return false;
        }

        Collider[] hits = Physics.OverlapBox(
            GetBlockSpawnPosition(),
            spawnBlockCleanupHalfExtents,
            GetBlockSpawnRotation(),
            spawnCleanupLayers,
            QueryTriggerInteraction.Ignore);

        bool cleanedAny = false;
        List<TotemBlockDetector> cleanedDetectors = new List<TotemBlockDetector>();

        for (int i = 0; i < hits.Length; i++)
        {
            Collider hit = hits[i];
            if (hit == null)
            {
                continue;
            }

            TotemBlockDetector detector = hit.GetComponentInParent<TotemBlockDetector>();
            if (detector == null || cleanedDetectors.Contains(detector))
            {
                continue;
            }

            cleanedDetectors.Add(detector);
            if (RemoveBlockAfterBlockBaseContact(detector))
            {
                cleanedAny = true;
            }
        }

        return cleanedAny;
    }

    private Vector3 GetBlockSpawnPosition()
    {
        return blockSpawnPoint != null ? blockSpawnPoint.position : Vector3.zero;
    }

    private Quaternion GetBlockSpawnRotation()
    {
        return blockSpawnPoint != null ? blockSpawnPoint.rotation : Quaternion.identity;
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

            if (blockBalanceEvaluationMode == BlockBalanceEvaluationMode.BalanceState)
            {
                if (!IsBlockBalancedForCurrentEvaluationMode(detector))
                {
                    return false;
                }

                continue;
            }

            if (!IsBlockCompletelyStill(detector))
            {
                return false;
            }

            if (IsBlockBalancedForCurrentEvaluationMode(detector))
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

        if (!IsBlockBalancedForCurrentEvaluationMode(detector))
        {
            return false;
        }

        return IsBlockPartOfValidTotemTower(detector);
    }

    private bool IsBlockBalancedForCurrentEvaluationMode(TotemBlockDetector detector)
    {
        if (detector == null)
        {
            return false;
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

    private bool IsBlockPartOfValidTotemTower(TotemBlockDetector detector)
    {
        if (detector == null)
        {
            return false;
        }

        if (totemBase == null)
        {
            return true;
        }

        return IsBlockConnectedToTransform(detector, totemBase, true);
    }

    private bool IsBlockConnectedToBlockBase(TotemBlockDetector detector)
    {
        if (detector == null || blockBase == null)
        {
            return false;
        }

        return IsBlockConnectedToTransform(detector, blockBase, false);
    }

    private bool IsBlockConnectedToTransform(
        TotemBlockDetector startDetector,
        Transform target,
        bool allowKnownStackedSupport)
    {
        if (startDetector == null || target == null)
        {
            return false;
        }

        supportDetectorStack.Clear();
        visitedSupportDetectors.Clear();
        supportDetectorStack.Push(startDetector);

        while (supportDetectorStack.Count > 0)
        {
            TotemBlockDetector detector = supportDetectorStack.Pop();

            if (detector == null || !visitedSupportDetectors.Add(detector))
            {
                continue;
            }

            if (detector.HasSupportTransform(target))
            {
                return true;
            }

            supportDetectorBuffer.Clear();
            detector.GetSupportedByBlockDetectors(supportDetectorBuffer);

            for (int i = 0; i < supportDetectorBuffer.Count; i++)
            {
                TotemBlockDetector supportDetector = supportDetectorBuffer[i];

                if (supportDetector == null)
                {
                    continue;
                }

                if (allowKnownStackedSupport &&
                    towerStackController != null &&
                    towerStackController.Contains(supportDetector))
                {
                    return true;
                }

                supportDetectorStack.Push(supportDetector);
            }
        }

        return false;
    }

    private bool TryCleanupInvalidBlockBaseSupportedBlock(TotemBlockDetector detector)
    {
        if (detector == null || IsCurrentReadyBlock(detector))
        {
            return false;
        }

        if (IsBlockPartOfValidTotemTower(detector))
        {
            return false;
        }

        if (IsBlockSupportedByCurrentReadyBlock(detector))
        {
            return RemoveBlockAfterBlockBaseContact(detector);
        }

        if (!IsBlockConnectedToBlockBase(detector))
        {
            return false;
        }

        return RemoveBlockAfterBlockBaseContact(detector);
    }

    private bool IsBlockSupportedByCurrentReadyBlock(TotemBlockDetector detector)
    {
        if (detector == null || IsCurrentReadyBlock(detector))
        {
            return false;
        }

        if (currentBlockDetector == null || blockToLaunch == null || hasLaunchedCurrentBlock)
        {
            return false;
        }

        return detector.HasSupportDetector(currentBlockDetector) ||
               IsBlockPhysicallyTouchingCurrentReadyBlock(detector);
    }

    private bool IsBlockPhysicallyTouchingCurrentReadyBlock(TotemBlockDetector detector)
    {
        if (detector == null || currentBlockDetector == null || IsCurrentReadyBlock(detector))
        {
            return false;
        }

        Collider[] detectorColliders = detector.GetComponentsInChildren<Collider>();
        Collider[] readyColliders = currentBlockDetector.GetComponentsInChildren<Collider>();

        for (int i = 0; i < detectorColliders.Length; i++)
        {
            Collider detectorCollider = detectorColliders[i];
            if (detectorCollider == null)
            {
                continue;
            }

            for (int j = 0; j < readyColliders.Length; j++)
            {
                Collider readyCollider = readyColliders[j];
                if (readyCollider == null)
                {
                    continue;
                }

                if (Physics.ComputePenetration(
                        detectorCollider,
                        detectorCollider.transform.position,
                        detectorCollider.transform.rotation,
                        readyCollider,
                        readyCollider.transform.position,
                        readyCollider.transform.rotation,
                        out _,
                        out _))
                {
                    return true;
                }
            }
        }

        return false;
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

    public void SetLegacySpawnValidationMode()
    {
        blockSpawnValidationMode = BlockSpawnValidationMode.LegacyValidation;
        ApplyBlockSpawnValidationMode();
    }

    public void SetImmediateAfterLaunchSpawnValidationMode()
    {
        blockSpawnValidationMode = BlockSpawnValidationMode.ImmediateAfterLaunch;
        ApplyBlockSpawnValidationMode();
    }

    public void ToggleBlockSpawnValidationMode()
    {
        blockSpawnValidationMode =
            blockSpawnValidationMode == BlockSpawnValidationMode.LegacyValidation
                ? BlockSpawnValidationMode.ImmediateAfterLaunch
                : BlockSpawnValidationMode.LegacyValidation;

        ApplyBlockSpawnValidationMode();
    }

    private void ApplyBlockSpawnValidationMode()
    {
        CleanupImmediatePendingLaunchedBlocks();
    }

    public void RemoveStackedBlock(TotemBlockDetector blockDetector)
    {
        if (blockDetector == null)
        {
            return;
        }

        RemoveBlockFromAllRuntimeTracking(blockDetector, true);
    }

    public bool RemoveBlockAfterBlockBaseContact(TotemBlockDetector blockDetector)
    {
        if (blockDetector == null)
        {
            return false;
        }

        if (IsCurrentReadyBlock(blockDetector))
        {
            return false;
        }

        RemoveBlockFromAllRuntimeTracking(blockDetector, true);

        if (blockDetector.gameObject != null)
        {
            Destroy(blockDetector.gameObject);
        }

        return true;
    }

    private void RemoveBlockFromAllRuntimeTracking(
        TotemBlockDetector blockDetector,
        bool refreshBlockBasePosition)
    {
        if (blockDetector == null)
        {
            return;
        }

        immediatePendingLaunchedBlocks.Remove(blockDetector);

        if (blockDetector.TryGetComponent<Rigidbody>(out Rigidbody trackedRigidbody))
        {
            RemoveLaunchedBlockFallData(trackedRigidbody);
        }

        bool removedFromStack = towerStackController.RemoveBlock(blockDetector);
        towerStackController.RemovePendingDestroy(blockDetector);

        if (removedFromStack)
        {
            RefreshStackedBlocksText();

            if (refreshBlockBasePosition)
            {
                RefreshBlockBasePosition(false);
            }
        }

        if (IsCurrentControlledBlock(blockDetector))
        {
            ResetCurrentBlockRemovalState();
        }
    }

    private bool IsCurrentControlledBlock(TotemBlockDetector blockDetector)
    {
        if (blockDetector == null)
        {
            return false;
        }

        if (currentBlockDetector == blockDetector)
        {
            return true;
        }

        return blockToLaunch != null &&
               blockDetector.TryGetComponent<Rigidbody>(out Rigidbody detectorRigidbody) &&
               blockToLaunch == detectorRigidbody;
    }

    private bool IsCurrentReadyBlock(TotemBlockDetector blockDetector)
    {
        if (blockDetector == null || hasLaunchedCurrentBlock)
        {
            return false;
        }

        if (currentBlockDetector == blockDetector)
        {
            return true;
        }

        return blockToLaunch != null &&
               blockDetector.TryGetComponent<Rigidbody>(out Rigidbody detectorRigidbody) &&
               blockToLaunch == detectorRigidbody;
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
        RemoveLaunchedBlockFallData(blockToLaunch);
    }

    // Add extra gravity only after the predicted apex of the launch trajectory.
    private void UpdateLaunchedBlocksFallBehavior()
    {
        for (int i = launchedBlockFallData.Count - 1; i >= 0; i--)
        {
            LaunchedBlockFallData data = launchedBlockFallData[i];

            if (data == null || data.Rigidbody == null)
            {
                launchedBlockFallData.RemoveAt(i);
                continue;
            }

            Rigidbody launchedRigidbody = data.Rigidbody;
            if (launchedRigidbody.isKinematic)
            {
                launchedBlockFallData.RemoveAt(i);
                continue;
            }

            float elapsedSinceLaunch = Time.fixedTime - data.LaunchStartFixedTime;

            if (!data.HasEnteredFallPhase &&
                elapsedSinceLaunch >= data.LaunchApexTime)
            {
                data.HasEnteredFallPhase = true;
                data.FallPhaseTimer = 0f;
            }

            if (!data.HasEnteredFallPhase)
            {
                continue;
            }

            data.FallPhaseTimer += Time.fixedDeltaTime;

            float blendT = apexFallBlendTime <= 0f
                ? 1f
                : Mathf.Clamp01(data.FallPhaseTimer / apexFallBlendTime);

            float gravityMultiplier = Mathf.Lerp(1f, apexFallGravityMultiplier, blendT);
            Vector3 extraGravity = Physics.gravity * (gravityMultiplier - 1f);
            launchedRigidbody.AddForce(extraGravity, ForceMode.Acceleration);
        }
    }

    private void RegisterLaunchedBlockFallData(
        Rigidbody launchedRigidbody,
        Vector3 launchDirection,
        float launchImpulse)
    {
        if (launchedRigidbody == null)
        {
            return;
        }

        RemoveLaunchedBlockFallData(launchedRigidbody);

        float mass = Mathf.Max(0.0001f, launchedRigidbody.mass);
        Vector3 initialVelocity = launchDirection * (launchImpulse / mass);
        float gravityY = Physics.gravity.y;
        float apexTime = 0f;

        if (gravityY < -0.0001f && initialVelocity.y > 0f)
        {
            apexTime = initialVelocity.y / -gravityY;
        }

        launchedBlockFallData.Add(new LaunchedBlockFallData
        {
            Rigidbody = launchedRigidbody,
            LaunchStartFixedTime = Time.fixedTime,
            LaunchApexTime = apexTime,
            HasEnteredFallPhase = false,
            FallPhaseTimer = 0f
        });
    }

    private void RemoveLaunchedBlockFallData(Rigidbody rigidbody)
    {
        if (rigidbody == null)
        {
            return;
        }

        for (int i = launchedBlockFallData.Count - 1; i >= 0; i--)
        {
            LaunchedBlockFallData data = launchedBlockFallData[i];

            if (data == null ||
                data.Rigidbody == null ||
                data.Rigidbody == rigidbody)
            {
                launchedBlockFallData.RemoveAt(i);
            }
        }
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
