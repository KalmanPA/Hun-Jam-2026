using UnityEditor;
using UnityEngine;

/// <summary>Door inspector: groups closing and opening, and only shows the link list for the chosen open condition.</summary>
[CustomEditor(typeof(SlidingDoor))]
public class SlidingDoorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        SerializedProperty startOpen = serializedObject.FindProperty("startOpen");
        SerializedProperty closeTriggers = serializedObject.FindProperty("closeTriggers");
        SerializedProperty condition = serializedObject.FindProperty("openCondition");

        EditorGUILayout.LabelField("Closing", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(startOpen);
        EditorGUILayout.PropertyField(closeTriggers, true);

        EditorGUILayout.LabelField("Opening", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(condition);
        var mode = (SlidingDoor.OpenCondition)condition.enumValueIndex;
        if (mode != SlidingDoor.OpenCondition.None)
        {
            SerializedProperty list = serializedObject.FindProperty(mode == SlidingDoor.OpenCondition.ShootAllButtons ? "buttons" : "targets");
            EditorGUILayout.PropertyField(list, true);
            if (list.arraySize == 0)
                EditorGUILayout.HelpBox("Nothing linked yet. Place the prefabs from Assets/Prefabs/Interactables in the scene and drag them here.", MessageType.Warning);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("stayOpen"));
        }
        else if (!startOpen.boolValue)
        {
            EditorGUILayout.HelpBox("Nothing opens this door: tick Start Open or pick an open condition.", MessageType.Warning);
        }

        SerializedProperty it = serializedObject.GetIterator();
        it.NextVisible(true); // m_Script
        while (it.NextVisible(false))
        {
            switch (it.name)
            {
                case "startOpen": case "closeTriggers": case "openCondition": case "buttons": case "targets": case "stayOpen": continue;
            }
            EditorGUILayout.PropertyField(it, true);
        }
        serializedObject.ApplyModifiedProperties();
    }
}

/// <summary>Conveyor opening inspector: says whether it found its partner.</summary>
[CustomEditor(typeof(ConveyorOpening))]
public class ConveyorOpeningEditor : Editor
{
    public override void OnInspectorGUI()
    {
        var opening = (ConveyorOpening)target;
        ConveyorOpening partner = opening.FindPartner(out int count);
        if (partner == null)
            EditorGUILayout.HelpBox(count == 0
                ? $"No other opening has belt id {opening.BeltId}. Place a second opening with the same id to build a belt."
                : $"{count + 1} openings use belt id {opening.BeltId}. Only two may share an id.", MessageType.Warning);
        else if (partner.ItemFlow == opening.ItemFlow)
            EditorGUILayout.HelpBox($"Both openings are set to {opening.ItemFlow}. Set one to Items Come Out and the other to Items Go In.", MessageType.Warning);
        else
            EditorGUILayout.HelpBox($"Belt {opening.BeltId} is paired with '{partner.name}'.", MessageType.Info);

        DrawDefaultInspector();
    }
}
