using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(CoreManager))]
public class CoreManagerEditor : Editor
{
    private SerializedProperty aimDirectionModeProperty;
    private SerializedProperty radialDragMaxLaunchImpulseProperty;
    private SerializedProperty horizontalReferenceMaxLaunchImpulseProperty;

    private void OnEnable()
    {
        aimDirectionModeProperty = serializedObject.FindProperty("aimDirectionMode");
        radialDragMaxLaunchImpulseProperty = serializedObject.FindProperty("radialDragMaxLaunchImpulse");
        horizontalReferenceMaxLaunchImpulseProperty =
            serializedObject.FindProperty("horizontalReferenceMaxLaunchImpulse");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        int previousAimDirectionMode = aimDirectionModeProperty != null
            ? aimDirectionModeProperty.enumValueIndex
            : -1;

        DrawPropertiesExcluding(
            serializedObject,
            "m_Script",
            "radialDragMaxLaunchImpulse",
            "horizontalReferenceMaxLaunchImpulse");

        DrawActiveLaunchImpulseField();

        serializedObject.ApplyModifiedProperties();

        if (Application.isPlaying &&
            aimDirectionModeProperty != null &&
            previousAimDirectionMode != aimDirectionModeProperty.enumValueIndex)
        {
            CoreManager coreManager = (CoreManager)target;
            coreManager.ApplySelectedAimDirectionModeFromEditor();
        }
    }

    private void DrawActiveLaunchImpulseField()
    {
        if (aimDirectionModeProperty == null ||
            radialDragMaxLaunchImpulseProperty == null ||
            horizontalReferenceMaxLaunchImpulseProperty == null)
        {
            return;
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Active Launch Impulse", EditorStyles.boldLabel);

        string enumName = aimDirectionModeProperty.enumNames[aimDirectionModeProperty.enumValueIndex];
        SerializedProperty activeImpulseProperty = enumName == "HorizontalReferenceBar"
            ? horizontalReferenceMaxLaunchImpulseProperty
            : radialDragMaxLaunchImpulseProperty;

        EditorGUILayout.PropertyField(
            activeImpulseProperty,
            new GUIContent("Max Launch Impulse"));
    }
}
