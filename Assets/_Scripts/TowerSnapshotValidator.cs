using System;
using System.Collections.Generic;
using UnityEngine;

public class TowerSnapshotValidator : MonoBehaviour
{
    [Serializable]
    private class BlockSnapshotEntry
    {
        public TotemBlockDetector block;
        public int stackIndex;
        public Vector3 localPosition;
        public Quaternion localRotation;
        public float localY;
        public bool wasBalanced;
    }

    [Serializable]
    private class TowerSnapshot
    {
        public List<BlockSnapshotEntry> blocks = new List<BlockSnapshotEntry>();
        public float capturedAtTime;
    }

    private float snapshotYDropTolerance;
    private bool enableSnapshotDebugLogs;
    private readonly TowerSnapshot towerSnapshot = new TowerSnapshot();

    public bool HasSnapshot { get; private set; }

    public void Configure(
        float yDropTolerance,
        bool snapshotDebugLogs,
        Transform baseReference)
    {
        snapshotYDropTolerance = yDropTolerance;
        enableSnapshotDebugLogs = snapshotDebugLogs;
    }

    public void CaptureSnapshot(
        IReadOnlyList<TotemBlockDetector> stackedBlocks,
        Transform baseReference)
    {
        towerSnapshot.blocks.Clear();

        for (int i = 0; i < stackedBlocks.Count; i++)
        {
            TotemBlockDetector detector = stackedBlocks[i];

            if (detector == null)
            {
                continue;
            }

            Vector3 localPosition = baseReference.InverseTransformPoint(detector.transform.position);
            Quaternion localRotation =
                Quaternion.Inverse(baseReference.rotation) *
                detector.transform.rotation;

            towerSnapshot.blocks.Add(new BlockSnapshotEntry
            {
                block = detector,
                stackIndex = i,
                localPosition = localPosition,
                localRotation = localRotation,
                localY = localPosition.y,
                wasBalanced = detector.IsBalanced
            });
        }

        towerSnapshot.capturedAtTime = Time.time;
        HasSnapshot = towerSnapshot.blocks.Count > 0;

        if (enableSnapshotDebugLogs)
        {
            Debug.Log(
                $"[Snapshot] Captured tower snapshot with {towerSnapshot.blocks.Count} block(s) at t={towerSnapshot.capturedAtTime:0.00}.");
        }
    }

    public bool HasBlockDroppedOutOfTower(
        TotemBlockDetector detector,
        Transform baseReference)
    {
        BlockSnapshotEntry snapshotEntry = GetSnapshotEntry(detector);
        if (detector == null || snapshotEntry == null)
        {
            return false;
        }

        Vector3 localPosition = baseReference.InverseTransformPoint(detector.transform.position);
        return localPosition.y < snapshotEntry.localY - snapshotYDropTolerance;
    }

    public bool IsBlockRecoveredBySnapshot(
        TotemBlockDetector detector,
        Transform baseReference,
        Func<TotemBlockDetector, bool> isBlockCompletelyStill)
    {
        if (detector == null || !isBlockCompletelyStill(detector))
        {
            return false;
        }

        BlockSnapshotEntry snapshotEntry = GetSnapshotEntry(detector);
        if (snapshotEntry == null)
        {
            return false;
        }

        Vector3 localPosition = baseReference.InverseTransformPoint(detector.transform.position);
        float currentYDelta = Mathf.Abs(localPosition.y - snapshotEntry.localY);

        if (currentYDelta > snapshotYDropTolerance)
        {
            return false;
        }

        if (enableSnapshotDebugLogs && !detector.IsBalanced)
        {
            Debug.Log(
                $"[Snapshot] '{detector.name}' recovered tower stability through snapshot validation. CurrentY={localPosition.y:0.000}, SnapshotY={snapshotEntry.localY:0.000}, Delta={currentYDelta:0.000}");
        }

        return true;
    }

    private BlockSnapshotEntry GetSnapshotEntry(TotemBlockDetector detector)
    {
        if (detector == null || !HasSnapshot)
        {
            return null;
        }

        for (int i = 0; i < towerSnapshot.blocks.Count; i++)
        {
            BlockSnapshotEntry entry = towerSnapshot.blocks[i];

            if (entry != null && entry.block == detector)
            {
                return entry;
            }
        }

        return null;
    }
}
