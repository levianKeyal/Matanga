using UnityEngine;

public class BlockBaseMover : MonoBehaviour
{
    private Transform blockBase;
    private bool enableBlockBaseMovement;
    private int blockBaseMovementStartStackCount;
    private float blockBaseMoveDuration;
    private float blockBaseQuarterTurnDegrees;
    private bool enableDebugLogs;

    private float blockBaseInitialY;
    private Quaternion blockBaseInitialRotation;
    private bool isBlockBaseAnimating;
    private float blockBaseAnimationTimer;
    private Vector3 blockBaseAnimationStartPosition;
    private Quaternion blockBaseAnimationStartRotation;
    private Vector3 blockBaseAnimationTargetPosition;
    private Quaternion blockBaseAnimationTargetRotation;

    public bool IsAnimating => isBlockBaseAnimating;

    public void Configure(
        Transform blockBaseReference,
        bool enableMovement,
        int movementStartStackCount,
        float moveDuration,
        float quarterTurnDegrees,
        bool debugLogs = false)
    {
        blockBase = blockBaseReference;
        enableBlockBaseMovement = enableMovement;
        blockBaseMovementStartStackCount = movementStartStackCount;
        blockBaseMoveDuration = moveDuration;
        blockBaseQuarterTurnDegrees = quarterTurnDegrees;
        enableDebugLogs = debugLogs;
    }

    public void CacheInitialState()
    {
        if (blockBase == null)
        {
            return;
        }

        blockBaseInitialY = blockBase.position.y;
        blockBaseInitialRotation = blockBase.rotation;
    }

    public void ApplyStateInstant(int stackedBlockCount)
    {
        if (blockBase == null)
        {
            return;
        }

        int movementStepCount = GetMovementStepCount(stackedBlockCount);
        float targetY = blockBaseInitialY + movementStepCount;

        if (enableDebugLogs)
        {
            Debug.Log(
                $"[BlockBase] count={stackedBlockCount}, " +
                $"enabled={enableBlockBaseMovement}, " +
                $"blockBase={(blockBase != null ? blockBase.name : "null")}, " +
                $"step={movementStepCount}, " +
                $"targetY={targetY:0.###}",
                this);
        }
        Vector3 position = blockBase.position;
        position.y = targetY;
        blockBase.position = position;

        float targetRotation = blockBaseQuarterTurnDegrees * movementStepCount;
        blockBase.rotation = blockBaseInitialRotation * Quaternion.Euler(0f, targetRotation, 0f);
        isBlockBaseAnimating = false;
    }

    public bool RefreshPosition(int stackedBlockCount, bool animate, out bool animationStarted)
    {
        animationStarted = false;

        if (blockBase == null)
        {
            if (enableDebugLogs)
            {
                Debug.Log("[BlockBaseMover] Refresh skipped because blockBase is null.", this);
            }

            isBlockBaseAnimating = false;
            return false;
        }

        int movementStepCount = GetMovementStepCount(stackedBlockCount);
        Vector3 targetPosition = blockBase.position;
        targetPosition.y = blockBaseInitialY + movementStepCount;

        if (enableDebugLogs)
        {
            Debug.Log(
                $"[BlockBaseMover] Refresh count={stackedBlockCount}, enabled={enableBlockBaseMovement}, base={blockBase.name}, step={movementStepCount}, targetY={targetPosition.y:0.000}.",
                this);
        }

        Quaternion targetRotation =
            blockBaseInitialRotation *
            Quaternion.Euler(0f, blockBaseQuarterTurnDegrees * movementStepCount, 0f);

        bool hasMeaningfulBaseChange =
            Vector3.Distance(blockBase.position, targetPosition) > 0.001f ||
            Quaternion.Angle(blockBase.rotation, targetRotation) > 0.1f;

        if (!animate || !hasMeaningfulBaseChange || blockBaseMoveDuration <= Mathf.Epsilon)
        {
            if (enableDebugLogs)
            {
                Debug.Log(
                    $"[BlockBaseMover] Animation skipped. meaningfulChange={hasMeaningfulBaseChange}, duration={blockBaseMoveDuration:0.000}.",
                    this);
            }

            blockBase.position = targetPosition;
            blockBase.rotation = targetRotation;
            isBlockBaseAnimating = false;
            return true;
        }

        blockBaseAnimationStartPosition = blockBase.position;
        blockBaseAnimationStartRotation = blockBase.rotation;
        blockBaseAnimationTargetPosition = targetPosition;
        blockBaseAnimationTargetRotation = targetRotation;
        blockBaseAnimationTimer = 0f;
        isBlockBaseAnimating = true;
        animationStarted = true;

        if (enableDebugLogs)
        {
            Debug.Log("[BlockBaseMover] Animation started.", this);
        }

        return true;
    }

    public bool UpdateAnimation()
    {
        if (!isBlockBaseAnimating || blockBase == null)
        {
            return false;
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
            return false;
        }

        isBlockBaseAnimating = false;
        return true;
    }

    private int GetMovementStepCount(int stackedBlockCount)
    {
        if (blockBase == null || !enableBlockBaseMovement)
        {
            return 0;
        }

        if (stackedBlockCount < blockBaseMovementStartStackCount)
        {
            return 0;
        }

        return stackedBlockCount - blockBaseMovementStartStackCount + 1;
    }
}
