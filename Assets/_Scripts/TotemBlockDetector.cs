using UnityEngine;
using System.Collections.Generic;

public enum TotemBlockBalanceState
{
    Unsupported,
    Balancing,
    Balanced
}

[RequireComponent(typeof(Rigidbody))]
public class TotemBlockDetector : MonoBehaviour
{
    private const float SupportRelaxedStillnessMultiplier = 4f;

    [SerializeField] private string supportingBlockTag = "TotemBlock";
    [SerializeField, Range(0f, 1f)] private float supportNormalThreshold = 0.5f;
    [SerializeField, Min(0f)] private float linearBalanceThreshold = 0.05f;
    [SerializeField, Min(0f)] private float angularBalanceThreshold = 0.05f;
    [SerializeField] private bool isBalanced;
    [SerializeField] private TotemBlockBalanceState balanceState = TotemBlockBalanceState.Unsupported;

    [Header("Balance State Settings")]
    [SerializeField, Min(0f)] private float balanceStateLinearVelocityThreshold = 0.05f;
    [SerializeField, Min(0f)] private float balanceStateAngularVelocityThreshold = 0.05f;

    public bool IsSupported { get; private set; }
    public bool IsBalanced => isBalanced;
    public TotemBlockBalanceState BalanceState => balanceState;
    public bool IsBalancing => balanceState == TotemBlockBalanceState.Balancing;

    private Rigidbody cachedRigidbody;
    private readonly HashSet<Collider> supportColliders = new HashSet<Collider>();

    private float LinearBalanceThresholdSqr => linearBalanceThreshold * linearBalanceThreshold;
    private float AngularBalanceThresholdSqr => angularBalanceThreshold * angularBalanceThreshold;
    private float BalanceStateLinearVelocityThresholdSqr =>
        balanceStateLinearVelocityThreshold * balanceStateLinearVelocityThreshold;
    private float BalanceStateAngularVelocityThresholdSqr =>
        balanceStateAngularVelocityThreshold * balanceStateAngularVelocityThreshold;

    private void Awake()
    {
        cachedRigidbody = GetComponent<Rigidbody>();
    }

    public void ConfigureBalanceStateThresholds(
        float linearVelocityThreshold,
        float angularVelocityThreshold)
    {
        balanceStateLinearVelocityThreshold = Mathf.Max(0f, linearVelocityThreshold);
        balanceStateAngularVelocityThreshold = Mathf.Max(0f, angularVelocityThreshold);
    }

    private void FixedUpdate()
    {
        UpdateBalanceState();
    }

    private void OnCollisionStay(Collision collision)
    {
        if (!collision.collider.CompareTag(supportingBlockTag))
        {
            return;
        }

        bool isSupportingContact = false;
        // Support detection is intentionally more forgiving while the block settles.
        bool isNearlyStill = cachedRigidbody != null &&
                             cachedRigidbody.linearVelocity.sqrMagnitude <= LinearBalanceThresholdSqr * SupportRelaxedStillnessMultiplier &&
                             cachedRigidbody.angularVelocity.sqrMagnitude <= AngularBalanceThresholdSqr * SupportRelaxedStillnessMultiplier;

        for (int i = 0; i < collision.contactCount; i++)
        {
            ContactPoint contact = collision.GetContact(i);

            if (contact.normal.y >= supportNormalThreshold ||
                (isNearlyStill && contact.normal.y > 0.05f))
            {
                isSupportingContact = true;
                break;
            }
        }

        if (isSupportingContact)
        {
            supportColliders.Add(collision.collider);
        }
        else
        {
            supportColliders.Remove(collision.collider);
        }

        // IsSupported means that a valid supporting block contact is present.
        IsSupported = supportColliders.Count > 0;
        UpdateBalanceState();
    }

    private void OnCollisionExit(Collision collision)
    {
        if (!collision.collider.CompareTag(supportingBlockTag))
        {
            return;
        }

        supportColliders.Remove(collision.collider);
        IsSupported = supportColliders.Count > 0;
        UpdateBalanceState();
    }

    private void UpdateBalanceState()
    {
        UpdateLegacyBalanceState();
        UpdateEnumBalanceState();
    }

    private void UpdateLegacyBalanceState()
    {
        if (cachedRigidbody == null)
        {
            isBalanced = false;
            return;
        }

        // IsBalanced combines valid support with the strict movement thresholds.
        bool isMovingSlowly =
            cachedRigidbody.linearVelocity.sqrMagnitude <= LinearBalanceThresholdSqr &&
            cachedRigidbody.angularVelocity.sqrMagnitude <= AngularBalanceThresholdSqr;

        isBalanced = IsSupported && isMovingSlowly;
    }

    private void UpdateEnumBalanceState()
    {
        if (cachedRigidbody == null || !IsSupported)
        {
            balanceState = TotemBlockBalanceState.Unsupported;
            return;
        }

        bool isLinearSlowEnough =
            cachedRigidbody.linearVelocity.sqrMagnitude <= BalanceStateLinearVelocityThresholdSqr;

        bool isAngularSlowEnough =
            cachedRigidbody.angularVelocity.sqrMagnitude <= BalanceStateAngularVelocityThresholdSqr;

        balanceState = isLinearSlowEnough && isAngularSlowEnough
            ? TotemBlockBalanceState.Balanced
            : TotemBlockBalanceState.Balancing;
    }
}
