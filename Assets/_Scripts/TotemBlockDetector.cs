using UnityEngine;
using System.Collections.Generic;

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
    private readonly HashSet<Collider> supportColliders = new HashSet<Collider>();

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
        bool isNearlyStill = cachedRigidbody != null &&
                             cachedRigidbody.linearVelocity.sqrMagnitude <= linearBalanceThreshold * linearBalanceThreshold * 4f &&
                             cachedRigidbody.angularVelocity.sqrMagnitude <= angularBalanceThreshold * angularBalanceThreshold * 4f;

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

        bool isMovingSlowly =
            cachedRigidbody.linearVelocity.sqrMagnitude <= linearBalanceThreshold * linearBalanceThreshold &&
            cachedRigidbody.angularVelocity.sqrMagnitude <= angularBalanceThreshold * angularBalanceThreshold;

        isBalanced = IsSupported && isMovingSlowly;
    }
}
