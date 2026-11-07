using UnityEditor;
using UnityEngine;

/// <summary>Door inspector: only shows the link list for the chosen open condition.</summary>
[CustomEditor(typeof(SlidingDoor))]
public class SlidingDoorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        SerializedProperty condition = serializedObject.FindProperty("openCondition");
        EditorGUILayout.PropertyField(condition);

        var mode = (SlidingDoor.OpenCondition)condition.enumValueIndex;
        string listName = mode == SlidingDoor.OpenCondition.PlayerEntersTrigger ? "triggerZones"
            : mode == SlidingDoor.OpenCondition.ShootAllButtons ? "buttons" : "targets";
        SerializedProperty list = serializedObject.FindProperty(listName);
        EditorGUILayout.PropertyField(list, true);
        if (list.arraySize == 0)
            EditorGUILayout.HelpBox("Nothing linked yet. Place the prefabs from Assets/Prefabs/Interactables in the scene and drag them here.", MessageType.Warning);

        SerializedProperty it = serializedObject.GetIterator();
        it.NextVisible(true); // m_Script
        while (it.NextVisible(false))
        {
            if (it.name == "openCondition" || it.name == "triggerZones" || it.name == "buttons" || it.name == "targets") continue;
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
