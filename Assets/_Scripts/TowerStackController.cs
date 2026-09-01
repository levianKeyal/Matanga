using System;
using System.Collections.Generic;
using UnityEngine;

public class TowerStackController : MonoBehaviour
{
    private readonly List<TotemBlockDetector> stackedBlocks = new List<TotemBlockDetector>();
    private readonly List<TotemBlockDetector> pendingDestroyBlocks = new List<TotemBlockDetector>();
    private int lastPendingDestroyProcessFrame = -1;

    public int Count => stackedBlocks.Count;
    public int PendingDestroyCount => pendingDestroyBlocks.Count;
    public IReadOnlyList<TotemBlockDetector> StackedBlocks => stackedBlocks;

    public void SynchronizeStackedBlocks(IReadOnlyList<TotemBlockDetector> legacyBlocks)
    {
        if (legacyBlocks == null)
        {
            return;
        }

        for (int i = 0; i < legacyBlocks.Count; i++)
        {
            RegisterBlock(legacyBlocks[i]);
        }
    }

    public bool Contains(TotemBlockDetector block)
    {
        return block != null && stackedBlocks.Contains(block);
    }

    public bool RegisterBlock(TotemBlockDetector block)
    {
        if (block == null || stackedBlocks.Contains(block))
        {
            return false;
        }

        stackedBlocks.Add(block);
        return true;
    }

    public bool RemoveBlock(TotemBlockDetector block)
    {
        return block != null && stackedBlocks.Remove(block);
    }

    public bool RemoveNullStackedBlocks()
    {
        bool listChanged = false;

        for (int i = stackedBlocks.Count - 1; i >= 0; i--)
        {
            if (stackedBlocks[i] == null)
            {
                stackedBlocks.RemoveAt(i);
                listChanged = true;
            }
        }

        return listChanged;
    }

    public bool RemoveNullPendingDestroyBlocks()
    {
        bool listChanged = false;

        for (int i = pendingDestroyBlocks.Count - 1; i >= 0; i--)
        {
            if (pendingDestroyBlocks[i] == null)
            {
                pendingDestroyBlocks.RemoveAt(i);
                listChanged = true;
            }
        }

        return listChanged;
    }

    public bool ContainsPendingDestroy(TotemBlockDetector block)
    {
        return block != null && pendingDestroyBlocks.Contains(block);
    }

    public bool TryQueuePendingDestroy(TotemBlockDetector block)
    {
        if (block == null || pendingDestroyBlocks.Contains(block))
        {
            return false;
        }

        pendingDestroyBlocks.Add(block);
        return true;
    }

    public bool RemovePendingDestroy(TotemBlockDetector block)
    {
        return block != null && pendingDestroyBlocks.Remove(block);
    }

    public bool ProcessOnePendingDestroyBlock(
        TotemBlockDetector currentBlockDetector,
        Rigidbody blockToLaunch,
        Action onCurrentBlockMatchedForRemoval,
        Func<TotemBlockDetector, bool> isBlockCompletelyStill,
        bool enableDebugLogs)
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

            if (!isBlockCompletelyStill(detector))
            {
                continue;
            }

            bool isCurrentBlock = detector == currentBlockDetector;
            if (!isCurrentBlock && blockToLaunch != null &&
                detector.TryGetComponent<Rigidbody>(out Rigidbody detectorRigidbody))
            {
                isCurrentBlock = blockToLaunch == detectorRigidbody;
            }

            if (isCurrentBlock)
            {
                onCurrentBlockMatchedForRemoval?.Invoke();
            }

            string detectorName = detector.name;
            Destroy(detector.gameObject);
            pendingDestroyBlocks.RemoveAt(i);
            lastPendingDestroyProcessFrame = Time.frameCount;

            if (enableDebugLogs)
            {
                Debug.Log(
                    $"[Snapshot] Destroyed '{detectorName}' after it settled outside the snapshot tower.");
            }

            return true;
        }

        return false;
    }

    public void Clear()
    {
        stackedBlocks.Clear();
        pendingDestroyBlocks.Clear();
        lastPendingDestroyProcessFrame = -1;
    }
}
