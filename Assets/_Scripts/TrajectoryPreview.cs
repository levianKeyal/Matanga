using UnityEngine;
using UnityEngine.UI;

public class TrajectoryPreview : MonoBehaviour
{
    [Header("Trajectory Preview")]
    [SerializeField] private bool showTrajectoryDebug = true;
    [SerializeField] private Button toggleTrajectoryButton;

    private LineRenderer trajectoryLine;
    private int trajectorySegmentCount;
    private float trajectoryTimeStep;
    private bool trajectoryVisible;

    public bool IsTrajectoryPreviewEnabled => showTrajectoryDebug;

    private void OnEnable()
    {
        if (toggleTrajectoryButton != null)
        {
            toggleTrajectoryButton.onClick.AddListener(ToggleTrajectoryPreview);
        }
    }

    private void OnDisable()
    {
        if (toggleTrajectoryButton != null)
        {
            toggleTrajectoryButton.onClick.RemoveListener(ToggleTrajectoryPreview);
        }

        HidePreview();
    }

    public bool Initialize(
        LineRenderer lineReference,
        int segmentCount,
        float timeStep,
        bool showDebug,
        Color debugColor)
    {
        trajectoryLine = lineReference;
        trajectorySegmentCount = segmentCount;
        trajectoryTimeStep = timeStep;

        EnsureTrajectoryLine(debugColor);
        return trajectoryLine != null;
    }

    public void SetTrajectoryPreviewEnabled(bool enabled)
    {
        showTrajectoryDebug = enabled;

        if (!showTrajectoryDebug)
        {
            HidePreview();
        }
    }

    public void ToggleTrajectoryPreview()
    {
        SetTrajectoryPreviewEnabled(!showTrajectoryDebug);
    }

    public void ShowPreview(
        Rigidbody block,
        Vector3 launchDirection,
        float launchImpulse,
        float apexFallGravityMultiplier,
        float apexFallBlendTime)
    {
        if (!showTrajectoryDebug || trajectoryLine == null || block == null)
        {
            HidePreview();
            return;
        }

        float mass = Mathf.Max(0.0001f, block.mass);
        Vector3 initialVelocity = launchDirection * (launchImpulse / mass);
        Vector3 startPosition = block.position;
        float predictedApexTime = GetPredictedLaunchApexTime(initialVelocity.y);

        int segmentCount = Mathf.Max(2, trajectorySegmentCount);
        float step = Mathf.Max(0.01f, trajectoryTimeStep);
        float physicsStep = Mathf.Max(0.0001f, Mathf.Min(Time.fixedDeltaTime, 0.01f));
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

    public void HidePreview()
    {
        if (trajectoryLine != null && trajectoryVisible)
        {
            trajectoryLine.enabled = false;
            trajectoryLine.positionCount = 0;
        }

        trajectoryVisible = false;
    }

    private void EnsureTrajectoryLine(Color debugColor)
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

        trajectoryLine.startColor = debugColor;
        trajectoryLine.endColor = debugColor;
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
}
