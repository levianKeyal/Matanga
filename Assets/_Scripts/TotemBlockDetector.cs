using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class TotemBlockDetector : MonoBehaviour
{
    [SerializeField] private string supportingBlockTag = "TotemBlock";
    [SerializeField, Range(0f, 1f)] private float supportNormalThreshold = 0.5f;
    [SerializeField, Min(0f)] private float linearBalanceThreshold = 0.05f;
    [SerializeField, Min(0f)] private float angularBalanceThreshold = 0.05f;
    [SerializeField] private bool isBalanced;

    public bool IsSupported { get; private set; }
    public bool IsBalanced => isBalanced;

    private Rigidbody cachedRigidbody;
    private bool hasSupportingContactThisStep;

    private void Awake()
    {
        cachedRigidbody = GetComponent<Rigidbody>();
    }

    private void FixedUpdate()
    {
        IsSupported = hasSupportingContactThisStep;
        hasSupportingContactThisStep = false;
        UpdateBalanceState();
    }

    private void OnCollisionStay(Collision collision)
    {
        if (!collision.collider.CompareTag(supportingBlockTag))
        {
            return;
        }

        for (int i = 0; i < collision.contactCount; i++)
        {
            ContactPoint contact = collision.GetContact(i);

            if (contact.normal.y >= supportNormalThreshold)
            {
                hasSupportingContactThisStep = true;
                UpdateBalanceState();
                return;
            }
        }
    }

    private void OnCollisionExit(Collision collision)
    {
        if (!collision.collider.CompareTag(supportingBlockTag))
        {
            return;
        }

        isBalanced = false;
    }

    private void UpdateBalanceState()
    {
        if (cachedRigidbody == null)
        {
            isBalanced = false;
            return;
        }

        bool isMovingSlowly =
            cachedRigidbody.linearVelocity.sqrMagnitude <= linearBalanceThreshold * linearBalanceThreshold &&
            cachedRigidbody.angularVelocity.sqrMagnitude <= angularBalanceThreshold * angularBalanceThreshold;

        isBalanced = IsSupported && isMovingSlowly;
    }
}
