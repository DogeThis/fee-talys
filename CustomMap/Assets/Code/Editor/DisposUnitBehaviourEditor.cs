using UnityEngine;
using UnityEditor;

namespace Editor
{
    [CustomEditor(typeof(DisposTool.DisposUnitComponent))]
    public class DisposUnitBehaviourEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox("Edit units via the Dispos Tool window.", MessageType.Info);
        }
    }
}
