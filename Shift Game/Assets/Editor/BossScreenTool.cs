using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Puts Assets/Art/Materials/Dean mustache.png on the boss screen for the fight (BossMachine "Fight Face");
/// the screen still switches to the broken-screen death face when the boss dies.
/// Menu: Final Shift > Boss > Use Mustache Face On Boss Screen (works on the open scene; not saved).
/// </summary>
[InitializeOnLoad]
public static class BossScreenTool
{
    private const string FacePath = "Assets/Art/Materials/Dean mustache.png";
    private const string TriggerFile = "Temp/FinalShift_BossScreen";

    static BossScreenTool()
    {
        EditorApplication.update += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (File.Exists(TriggerFile)) { File.Delete(TriggerFile); Apply(); }
        };
    }

    [MenuItem("Final Shift/Boss/Use Mustache Face On Boss Screen")]
    public static void Apply()
    {
        Texture face = AssetDatabase.LoadAssetAtPath<Texture>(FacePath);
        string msg;
        if (face == null) msg = FacePath + " not found";
        else
        {
            int n = 0;
            foreach (BossMachine boss in Object.FindObjectsByType<BossMachine>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var so = new SerializedObject(boss);
                so.FindProperty("fightFace").objectReferenceValue = face;
                so.ApplyModifiedProperties();
                EditorSceneManager.MarkSceneDirty(boss.gameObject.scene);
                n++;
            }
            msg = n > 0 ? "fight face set on " + n + " boss(es) in " + SceneManager.GetActiveScene().name : "no BossMachine in the open scene";
        }
        File.WriteAllText("Temp/FinalShift_BossScreen_report.txt", msg);
        Debug.Log("[FinalShift] Boss screen: " + msg);
    }
}
