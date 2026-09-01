using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(Rigidbody))]
public class TotemBlockDetector : MonoBehaviour
{
    private const float SupportRelaxedStillnessMultiplier = 4f;

    [SerializeField] private string supportingBlockTag = "TotemBlock";
    [SerializeField, Range(0f, 1f)] private float supportNormalThreshold = 0.5f;
    [SerializeField, Min(0f)] private float linearBalanceThreshold = 0.05f;
    [SerializeField, Min(0f)] private float angularBalanceThreshold = 0.05f;
    [SerializeField] private bool isBalanced;

    public bool IsSupported { get; private set; }
    public bool IsBalanced => isBalanced;

    private Rigidbody cachedRigidbody;
    private readonly HashSet<Collider> supportColliders = new HashSet<Collider>();

    private float LinearBalanceThresholdSqr => linearBalanceThreshold * linearBalanceThreshold;
    private float AngularBalanceThresholdSqr => angularBalanceThreshold * angularBalanceThreshold;

    private void Awake()
    {
        cachedRigidbody = GetComponent<Rigidbody>();
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
        isBalanced = false;
    }

    private void UpdateBalanceState()
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
}
