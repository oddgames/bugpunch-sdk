using System;
using UnityEditor;
using UnityEngine;

namespace ODDGames.UIAutomation.VisualBuilder.Editor
{
    /// <summary>
    /// Simple popup window for text input.
    /// </summary>
    public class TextInputPopup : EditorWindow
    {
        private string label;
        private string value;
        private Action<string> onConfirm;
        private bool focusSet;

        public void Init(string label, string initialValue, Action<string> onConfirm)
        {
            this.label = label;
            this.value = initialValue ?? "";
            this.onConfirm = onConfirm;
            this.focusSet = false;
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);

            GUI.SetNextControlName("TextInput");
            value = EditorGUILayout.TextField(value);

            if (!focusSet)
            {
                EditorGUI.FocusTextInControl("TextInput");
                focusSet = true;
            }

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("OK") || (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return))
            {
                onConfirm?.Invoke(value);
                Close();
            }

            if (GUILayout.Button("Cancel") || (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape))
            {
                Close();
            }

            EditorGUILayout.EndHorizontal();
        }
    }
}
