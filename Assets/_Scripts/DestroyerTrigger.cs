using UnityEngine;

[RequireComponent(typeof(Collider))]
public class DestroyerTrigger : MonoBehaviour
{
    [SerializeField] private CoreManager coreManager;
    [SerializeField] private string targetTag = "TotemBlock";

    private void Awake()
    {
        Collider triggerCollider = GetComponent<Collider>();
        triggerCollider.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag(targetTag))
        {
            return;
        }

        TotemBlockDetector detector = other.GetComponentInParent<TotemBlockDetector>();
        if (detector == null)
        {
            return;
        }

        if (coreManager == null)
        {
            coreManager = FindFirstObjectByType<CoreManager>();
        }

        if (coreManager != null)
        {
            coreManager.RemoveStackedBlock(detector);
        }

        Destroy(detector.gameObject);
    }
}
