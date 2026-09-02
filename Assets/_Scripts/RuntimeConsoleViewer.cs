using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

public class RuntimeConsoleViewer : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GameObject logPanel;
    [SerializeField] private TMP_Text outputText;
    [SerializeField] private Button toggleButton;
    [FormerlySerializedAs("clearButton")]
    [SerializeField] private Button clearLogs;
    [SerializeField] private ScrollRect scrollRect;

    [Header("Display")]
    // Controls whether incoming logs are captured, not panel visibility.
    [SerializeField] private bool showLogs = true;
    [SerializeField] private bool includeLog = true;
    [SerializeField] private bool includeWarning = true;
    [SerializeField] private bool includeError = true;
    [SerializeField] private bool includeException = true;
    [SerializeField] private bool includeStackTraceForErrors = false;
    [SerializeField, Min(1)] private int maxLines = 200;
    [SerializeField] private bool autoScrollToBottom = true;
    [SerializeField] private bool startVisible = false;

    private readonly Queue<string> lines = new Queue<string>();
    private bool logCallbackRegistered;
    private bool toggleListenerRegistered;
    private bool clearListenerRegistered;
    private bool isVisible = true;

    private void Awake()
    {
        ConfigureScrollRect();
        SetVisible(startVisible);
    }

    private void OnEnable()
    {
        ConfigureScrollRect();

        if (!logCallbackRegistered)
        {
            Application.logMessageReceived += HandleLogMessage;
            logCallbackRegistered = true;
        }

        if (toggleButton != null && !toggleListenerRegistered)
        {
            toggleButton.onClick.AddListener(ToggleVisible);
            toggleListenerRegistered = true;
        }

        if (clearLogs != null && !clearListenerRegistered)
        {
            clearLogs.onClick.AddListener(Clear);
            clearListenerRegistered = true;
        }

        SetVisible(isVisible);
    }

    private void OnDisable()
    {
        if (logCallbackRegistered)
        {
            Application.logMessageReceived -= HandleLogMessage;
            logCallbackRegistered = false;
        }

        if (toggleButton != null && toggleListenerRegistered)
        {
            toggleButton.onClick.RemoveListener(ToggleVisible);
            toggleListenerRegistered = false;
        }

        if (clearLogs != null && clearListenerRegistered)
        {
            clearLogs.onClick.RemoveListener(Clear);
            clearListenerRegistered = false;
        }
    }

    private void ConfigureScrollRect()
    {
        if (scrollRect == null)
        {
            return;
        }

        scrollRect.vertical = true;
        scrollRect.horizontal = false;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
    }

    public void ToggleVisible()
    {
        SetVisible(!isVisible);
    }

    public void SetVisible(bool visible)
    {
        isVisible = visible;

        GameObject targetPanel = logPanel != null
            ? logPanel
            : outputText != null ? outputText.gameObject : null;

        if (targetPanel != null && targetPanel != gameObject)
        {
            targetPanel.SetActive(isVisible);
        }

        if (isVisible)
        {
            RefreshOutput();
        }
    }

    public void Clear()
    {
        lines.Clear();
        RefreshOutput();
    }

    private void HandleLogMessage(string message, string stackTrace, LogType type)
    {
        if (!showLogs || !ShouldInclude(type))
        {
            return;
        }

        string label = GetLogLabel(type);
        string line = $"[{label}] {message}";

        if (includeStackTraceForErrors &&
            (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) &&
            !string.IsNullOrEmpty(stackTrace))
        {
            line += $"\n{stackTrace}";
        }

        int lineLimit = Mathf.Max(1, maxLines);
        while (lines.Count >= lineLimit)
        {
            lines.Dequeue();
        }

        lines.Enqueue(line);
        RefreshOutput();
    }

    private bool ShouldInclude(LogType type)
    {
        switch (type)
        {
            case LogType.Log:
                return includeLog;
            case LogType.Warning:
                return includeWarning;
            case LogType.Error:
            case LogType.Assert:
                return includeError;
            case LogType.Exception:
                return includeException;
            default:
                return false;
        }
    }

    private static string GetLogLabel(LogType type)
    {
        switch (type)
        {
            case LogType.Warning:
                return "Warning";
            case LogType.Error:
            case LogType.Assert:
                return "Error";
            case LogType.Exception:
                return "Exception";
            default:
                return "Log";
        }
    }

    private void RefreshOutput()
    {
        if (outputText == null)
        {
            return;
        }

        outputText.text = string.Join("\n", lines);

        if (scrollRect != null && autoScrollToBottom)
        {
            Canvas.ForceUpdateCanvases();
            scrollRect.verticalNormalizedPosition = 0f;
        }
    }

}
