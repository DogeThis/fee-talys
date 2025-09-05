using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Editor
{
    public class PersonLookupWindow : EditorWindow
    {
        private string search = "";
        private Vector2 scroll;
        private Action<string> onSelectPid;
        private bool requestFocus;

        public static void Show(Action<string> onSelect)
        {
            var win = CreateInstance<PersonLookupWindow>();
            win.titleContent = new GUIContent("Select Person");
            win.minSize = new Vector2(420, 300);
            win.onSelectPid = onSelect;
            win.requestFocus = true;
            win.ShowUtility();
        }

        private IEnumerable<(string pid, string display, string mpid)> GetCandidates()
        {
            var persons = DisposDataLoader.Instance.GetAllPersons();
            if (persons == null) yield break;
            string q = (search ?? "").Trim();
            foreach (var p in persons)
            {
                string mpid = p.Name ?? string.Empty;
                string nice = null;
                if (!string.IsNullOrEmpty(mpid) && mpid.StartsWith("MPID_"))
                    nice = DisposDataLoader.Instance.GetDisplayNameFromMpid(mpid);
                string display = !string.IsNullOrEmpty(nice) ? nice : (string.IsNullOrEmpty(mpid) ? p.Pid : mpid);

                if (string.IsNullOrEmpty(q) ||
                    (display?.IndexOf(q, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0 ||
                    (p.Pid?.IndexOf(q, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0 ||
                    (!string.IsNullOrEmpty(mpid) && mpid.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    yield return (p.Pid, display, mpid);
                }
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("Search:", GUILayout.Width(48));
            GUI.SetNextControlName("SearchField");
            search = GUILayout.TextField(search, EditorStyles.toolbarTextField, GUILayout.ExpandWidth(true), GUILayout.MinWidth(320));
            if (requestFocus)
            {
                EditorGUI.FocusTextInControl("SearchField");
                requestFocus = false;
            }
            if (GUILayout.Button("X", EditorStyles.toolbarButton, GUILayout.Width(24)))
            {
                search = string.Empty;
                GUI.FocusControl(null);
            }
            EditorGUILayout.EndHorizontal();

            var list = GetCandidates()
                .OrderBy(t => t.display)
                .ThenBy(t => t.pid)
                .Take(500) // safety
                .ToList();

            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (var c in list)
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(c.display, GUILayout.MinWidth(220)))
                {
                    onSelectPid?.Invoke(c.pid);
                    Close();
                }
                GUILayout.Label(c.pid, EditorStyles.miniLabel);
                if (!string.IsNullOrEmpty(c.mpid))
                {
                    GUILayout.FlexibleSpace();
                    GUILayout.Label(c.mpid, EditorStyles.miniLabel);
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
        }
    }
}
