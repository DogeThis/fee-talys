using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;

namespace Editor
{
    public class DisposToolWindow : EditorWindow
    {
        private static DisposToolWindow instance;
        public static DisposToolWindow Instance => instance;
        
        private DisposDocument currentDocument;
        private DisposSceneRenderer sceneRenderer;
        private DisposEntry selectedEntry;
        private Bridge.MapTerrain selectedTerrain;
        
        private Vector2 leftPanelScroll;
        private Vector2 rightPanelScroll;
        private float leftPanelWidth = 250f;
        private float rightPanelWidth = 300f;
        private bool isResizingLeft;
        private bool isResizingRight;
        
        private string[] availableFiles;
        private int selectedFileIndex = -1;
        private string disposFolderPath = "Assets/Share/Addressables/GameData/Dispos";
        
        private bool showGrid = true;
        private bool showLabels = true;
        private bool showDirections = true;
        private bool showIcons = true;
        
        private bool isDraggingUnit = false;
        private DisposEntry draggedEntry = null;
        private bool documentIsDirty = false;

        private enum DisposOverlayMode { Hide, Show, Edit }
        private DisposOverlayMode overlayMode = DisposOverlayMode.Edit;
        
        [MenuItem("Window/Dispos Tool")]
        public static void ShowWindow()
        {
            var window = GetWindow<DisposToolWindow>("Dispos Tool");
            window.minSize = new Vector2(800, 600);
        }
        
        private void OnEnable()
        {
            instance = this;
            
            sceneRenderer = new DisposSceneRenderer();
            sceneRenderer.Initialize();
            
            SceneView.duringSceneGui += OnSceneGUI;
            Selection.selectionChanged += OnSelectionChanged;

            // Try to ensure our scene GUI draws after others
            EditorApplication.delayCall += () =>
            {
                SceneView.duringSceneGui -= OnSceneGUI;
                SceneView.duringSceneGui += OnSceneGUI;
            };

            // Reflect current overlay mode in TerrainPaint tool
            TerrainPaintToolWindow.SetExternalInteractionLocked(overlayMode == DisposOverlayMode.Edit);
            
            RefreshFileList();
            
            if (availableFiles != null && availableFiles.Length > 0)
            {
                LoadFile(0);
            }
        }
        
        private void OnDisable()
        {
            instance = null;
            
            SceneView.duringSceneGui -= OnSceneGUI;
            Selection.selectionChanged -= OnSelectionChanged;
            
            // Restore tools visibility
            Tools.hidden = false;

            // Release any external locks
            TerrainPaintToolWindow.SetExternalInteractionLocked(false);

            if (sceneRenderer != null)
            {
                sceneRenderer.Cleanup();
            }
        }
        
        private void OnGUI()
        {
            DrawToolbar();
            
            EditorGUILayout.BeginHorizontal();
            
            DrawLeftPanel();
            DrawResizeHandle(ref isResizingLeft, ref leftPanelWidth, true);
            
            GUILayout.FlexibleSpace();
            
            DrawResizeHandle(ref isResizingRight, ref rightPanelWidth, false);
            DrawRightPanel();
            
            EditorGUILayout.EndHorizontal();
        }
        
        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            
            if (GUILayout.Button("Refresh Files", EditorStyles.toolbarButton, GUILayout.Width(100)))
            {
                RefreshFileList();
            }
            
            if (GUILayout.Button("Reload Data", EditorStyles.toolbarButton, GUILayout.Width(100)))
            {
                DisposDataLoader.Instance.ReloadData();
                if (currentDocument != null)
                {
                    Debug.Log("Reloading document in scene renderer");
                    sceneRenderer.RenderDocument(currentDocument, selectedTerrain);
                }
            }
            
            if (GUILayout.Button("Debug Refresh", EditorStyles.toolbarButton, GUILayout.Width(100)))
            {
                Debug.Log($"Debug: Document={currentDocument != null}, Terrain={selectedTerrain != null}");
                if (currentDocument != null)
                {
                    Debug.Log($"Groups: {currentDocument.Groups.Count}");
                    foreach (var group in currentDocument.Groups)
                    {
                        Debug.Log($"  Group {group.GroupName}: {group.Entries.Count} entries");
                    }
                }
                sceneRenderer?.RenderDocument(currentDocument, selectedTerrain);
            }
            
            if (currentDocument != null && documentIsDirty)
            {
                GUI.color = Color.yellow;
                GUILayout.Label("[Modified]", EditorStyles.toolbarButton);
                GUI.color = Color.white;
            }
            
            GUILayout.FlexibleSpace();

            // Overlay mode: Hide / Show / Edit
            EditorGUI.BeginChangeCheck();
            var modeNames = new[] { "Hide", "Show", "Edit" };
            int newMode = EditorGUILayout.Popup((int)overlayMode, modeNames, EditorStyles.toolbarPopup, GUILayout.Width(80));
            if (EditorGUI.EndChangeCheck())
            {
                overlayMode = (DisposOverlayMode)newMode;
                TerrainPaintToolWindow.SetExternalInteractionLocked(overlayMode == DisposOverlayMode.Edit);
                if (overlayMode == DisposOverlayMode.Hide)
                {
                    // Clear visuals when hidden
                    sceneRenderer.Cleanup();
                }
                else if (currentDocument != null)
                {
                    sceneRenderer.RenderDocument(currentDocument, selectedTerrain);
                }
                SceneView.RepaintAll();
            }
            
            showGrid = GUILayout.Toggle(showGrid, "Grid", EditorStyles.toolbarButton, GUILayout.Width(50));
            showLabels = GUILayout.Toggle(showLabels, "Labels", EditorStyles.toolbarButton, GUILayout.Width(50));
            showDirections = GUILayout.Toggle(showDirections, "Directions", EditorStyles.toolbarButton, GUILayout.Width(70));
            showIcons = GUILayout.Toggle(showIcons, "Icons", EditorStyles.toolbarButton, GUILayout.Width(50));
            
            GUI.enabled = currentDocument != null && documentIsDirty;
            if (GUILayout.Button("Save", EditorStyles.toolbarButton, GUILayout.Width(60)))
            {
                SaveDocument();
                documentIsDirty = false;
            }
            GUI.enabled = true;
            
            EditorGUILayout.EndHorizontal();
            
            sceneRenderer?.SetShowGrid(showGrid);
            sceneRenderer?.SetShowLabels(showLabels);
            sceneRenderer?.SetShowDirections(showDirections);
            sceneRenderer?.SetShowIcons(showIcons);
        }
        
        private void DrawLeftPanel()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(leftPanelWidth));
            
            EditorGUILayout.LabelField("Configuration", EditorStyles.boldLabel);
            
            // Terrain selection (for coordinate reference)
            EditorGUI.BeginChangeCheck();
            selectedTerrain = (Bridge.MapTerrain)EditorGUILayout.ObjectField("Map Terrain", 
                selectedTerrain, typeof(Bridge.MapTerrain), false);
            if (EditorGUI.EndChangeCheck() && currentDocument != null)
            {
                sceneRenderer.RenderDocument(currentDocument, selectedTerrain);
                SceneView.RepaintAll();
            }
            
            if (selectedTerrain != null)
            {
                EditorGUILayout.LabelField($"Map: {selectedTerrain.m_Width}x{selectedTerrain.m_Height}", EditorStyles.miniLabel);
            }
            
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Dispos Files", EditorStyles.boldLabel);
            
            if (availableFiles != null && availableFiles.Length > 0)
            {
                int newIndex = EditorGUILayout.Popup("File:", selectedFileIndex, availableFiles);
                if (newIndex != selectedFileIndex)
                {
                    LoadFile(newIndex);
                }
            }
            else
            {
                EditorGUILayout.HelpBox("No Dispos files found in " + disposFolderPath, MessageType.Warning);
            }
            
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Groups", EditorStyles.boldLabel);
            
            leftPanelScroll = EditorGUILayout.BeginScrollView(leftPanelScroll);
            
            if (currentDocument != null)
            {
                foreach (var group in currentDocument.Groups)
                {
                    DrawGroupItem(group);
                }
            }
            
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }
        
        private void DrawGroupItem(DisposGroup group)
        {
            EditorGUILayout.BeginHorizontal();
            
            group.IsExpanded = EditorGUILayout.Foldout(group.IsExpanded, group.GroupName);
            
            GUILayout.FlexibleSpace();
            
            bool newVisible = EditorGUILayout.Toggle(group.IsVisible, GUILayout.Width(20));
            if (newVisible != group.IsVisible)
            {
                group.IsVisible = newVisible;
                sceneRenderer.RenderDocument(currentDocument);
            }
            
            EditorGUILayout.EndHorizontal();
            
            if (group.IsExpanded)
            {
                EditorGUI.indentLevel++;
                
                int playerCount = 0, enemyCount = 0, allyCount = 0;
                foreach (var entry in group.Entries)
                {
                    if (!entry.IsGroupHeader)
                    {
                        switch (entry.Force)
                        {
                            case 0: playerCount++; break;
                            case 1: enemyCount++; break;
                            case 2: allyCount++; break;
                        }
                    }
                }
                
                EditorGUILayout.LabelField($"  Units: {group.Entries.Count} (P:{playerCount} E:{enemyCount} A:{allyCount})", 
                                          EditorStyles.miniLabel);
                
                foreach (var entry in group.Entries.Take(10))
                {
                    if (!entry.IsGroupHeader)
                    {
                        EditorGUILayout.BeginHorizontal();
                        
                        Color forceColor = DisposDataLoader.Instance.GetForceColor(entry.Force);
                        GUI.backgroundColor = forceColor;
                        
                        string displayName = DisposDataLoader.Instance.GetUnitDisplayName(entry);
                        if (GUILayout.Button(displayName, EditorStyles.miniButton))
                        {
                            SelectEntry(entry);
                        }
                        
                        GUI.backgroundColor = Color.white;
                        
                        EditorGUILayout.LabelField($"({entry.DisposX},{entry.DisposY})", 
                                                  EditorStyles.miniLabel, 
                                                  GUILayout.Width(60));
                        
                        EditorGUILayout.EndHorizontal();
                    }
                }
                
                if (group.Entries.Count > 10)
                {
                    EditorGUILayout.LabelField($"  ... and {group.Entries.Count - 10} more", 
                                              EditorStyles.miniLabel);
                }
                
                EditorGUI.indentLevel--;
            }
        }
        
        private void DrawRightPanel()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(rightPanelWidth));
            
            EditorGUILayout.LabelField("Unit Inspector", EditorStyles.boldLabel);
            
            rightPanelScroll = EditorGUILayout.BeginScrollView(rightPanelScroll);
            
            if (selectedEntry != null && !selectedEntry.IsGroupHeader)
            {
                DrawEntryInspector(selectedEntry);
            }
            else
            {
                EditorGUILayout.HelpBox("Select a unit to inspect", MessageType.Info);
            }
            
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }
        
        private void DrawEntryInspector(DisposEntry entry)
        {
            EditorGUILayout.LabelField("Basic Info", EditorStyles.boldLabel);
            
            EditorGUI.BeginChangeCheck();
            
            GUI.enabled = false;
            EditorGUILayout.TextField("PID", entry.Pid);
            GUI.enabled = true;
            
            var person = DisposDataLoader.Instance.GetPerson(entry.Pid);
            if (person != null)
            {
                EditorGUILayout.LabelField("Name", person.Name);
                EditorGUILayout.LabelField("Gender", person.IsFemale ? "Female" : "Male");
            }
            
            entry.Force = EditorGUILayout.IntPopup("Force", entry.Force, 
                new string[] { "Player", "Enemy", "Ally", "Other" }, 
                new int[] { 0, 1, 2, 3 });
            
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Position", EditorStyles.boldLabel);
            
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Dispos", GUILayout.Width(60));
            entry.DisposX = EditorGUILayout.IntField(entry.DisposX, GUILayout.Width(50));
            entry.DisposY = EditorGUILayout.IntField(entry.DisposY, GUILayout.Width(50));
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Appear", GUILayout.Width(60));
            entry.AppearX = EditorGUILayout.IntField(entry.AppearX, GUILayout.Width(50));
            entry.AppearY = EditorGUILayout.IntField(entry.AppearY, GUILayout.Width(50));
            EditorGUILayout.EndHorizontal();
            
            entry.Direction = EditorGUILayout.IntSlider("Direction", entry.Direction, 0, 8);
            
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Class & Level", EditorStyles.boldLabel);
            
            entry.Jid = EditorGUILayout.TextField("Job ID", entry.Jid);
            
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Level N/H/L", GUILayout.Width(80));
            entry.LevelN = EditorGUILayout.IntField(entry.LevelN, GUILayout.Width(40));
            entry.LevelH = EditorGUILayout.IntField(entry.LevelH, GUILayout.Width(40));
            entry.LevelL = EditorGUILayout.IntField(entry.LevelL, GUILayout.Width(40));
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Items", EditorStyles.boldLabel);
            
            for (int i = 0; i < 6; i++)
            {
                EditorGUILayout.BeginHorizontal();
                entry.Items[i].Iid = EditorGUILayout.TextField($"Item {i+1}", entry.Items[i].Iid);
                entry.Items[i].Drop = EditorGUILayout.IntField(entry.Items[i].Drop, GUILayout.Width(40));
                EditorGUILayout.EndHorizontal();
            }
            
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("AI Settings", EditorStyles.boldLabel);
            
            entry.AI_ActionName = EditorGUILayout.TextField("Action", entry.AI_ActionName);
            entry.AI_MindName = EditorGUILayout.TextField("Mind", entry.AI_MindName);
            entry.AI_AttackName = EditorGUILayout.TextField("Attack", entry.AI_AttackName);
            entry.AI_MoveName = EditorGUILayout.TextField("Move", entry.AI_MoveName);
            entry.AI_BattleRate = EditorGUILayout.TextField("Battle Rate", entry.AI_BattleRate);
            entry.AI_Priority = EditorGUILayout.IntField("Priority", entry.AI_Priority);
            entry.AI_BandNo = EditorGUILayout.IntField("Band No", entry.AI_BandNo);
            
            if (EditorGUI.EndChangeCheck())
            {
                sceneRenderer.RenderDocument(currentDocument);
                SceneView.RepaintAll();
            }
        }
        
        private void DrawResizeHandle(ref bool isResizing, ref float panelWidth, bool isLeft)
        {
            Rect resizeRect = GUILayoutUtility.GetRect(5f, 5f, GUILayout.ExpandHeight(true));
            EditorGUIUtility.AddCursorRect(resizeRect, MouseCursor.ResizeHorizontal);
            
            if (Event.current.type == EventType.MouseDown && resizeRect.Contains(Event.current.mousePosition))
            {
                isResizing = true;
            }
            
            if (isResizing && Event.current.type == EventType.MouseDrag)
            {
                if (isLeft)
                    panelWidth += Event.current.delta.x;
                else
                    panelWidth -= Event.current.delta.x;
                
                panelWidth = Mathf.Clamp(panelWidth, 150f, 400f);
                Repaint();
            }
            
            if (Event.current.type == EventType.MouseUp)
            {
                isResizing = false;
            }
            
            EditorGUI.DrawRect(resizeRect, new Color(0.5f, 0.5f, 0.5f, 0.5f));
        }
        
        private void OnSceneGUI(SceneView sceneView)
        {
            if (sceneRenderer != null && currentDocument != null)
            {
                if (overlayMode != DisposOverlayMode.Hide)
                {
                    sceneRenderer.DrawSceneGUI();
                    if (overlayMode == DisposOverlayMode.Edit)
                    {
                        HandleSceneInput();
                    }
                }

                // Disable default transform handles when a Dispos unit is selected
                GameObject active = Selection.activeGameObject;
                bool isUnit = active != null && active.GetComponent<DisposTool.DisposUnitComponent>() != null;
                Tools.hidden = isUnit || overlayMode == DisposOverlayMode.Edit;
                if (Tools.hidden) Tools.current = Tool.None;
            }
        }
        
        private void HandleSceneInput()
        {
            Event e = Event.current;
            
            // Keyboard nudging disabled per request; movement via drag handle only

            // Claim mouse focus to make icon dragging reliable in Edit mode
            if (e.type == EventType.Layout)
            {
                HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
            }

            if (e.type == EventType.MouseDown && e.button == 0)
            {
                // First try screen-space hit test against icon/plate
                var entry = sceneRenderer.GetEntryAtScreenPosition(e.mousePosition);
                if (entry == null)
                {
                    // Fallback to plane-space tile hit
                    Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
                    float planeY = sceneRenderer != null ? sceneRenderer.GetBasePlaneY() : 0f;
                    float distance = (planeY - ray.origin.y) / ray.direction.y;
                    if (distance > 0)
                    {
                        Vector3 worldPos = ray.origin + ray.direction * distance;
                        entry = sceneRenderer.GetEntryAtPosition(worldPos);
                    }
                }

                if (entry != null)
                {
                    SelectEntry(entry);
                    if (!e.shift)
                    {
                        isDraggingUnit = true;
                        draggedEntry = entry;
                    }
                    e.Use();
                }
            }
            else if (e.type == EventType.MouseDrag && isDraggingUnit && draggedEntry != null)
            {
                Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
                float planeY = sceneRenderer != null ? sceneRenderer.GetBasePlaneY() : 0f;
                float distance = (planeY - ray.origin.y) / ray.direction.y;
                if (distance > 0)
                {
                    Vector3 worldPos = ray.origin + ray.direction * distance;
                    sceneRenderer.MoveEntry(draggedEntry, worldPos);
                    documentIsDirty = true;
                    Repaint();
                    e.Use();
                }
            }
            else if (e.type == EventType.MouseUp)
            {
                isDraggingUnit = false;
                draggedEntry = null;
            }
        }
        
        private void RefreshFileList()
        {
            if (!Directory.Exists(disposFolderPath))
            {
                Debug.LogError($"Dispos folder not found: {disposFolderPath}");
                availableFiles = new string[0];
                return;
            }
            
            var files = Directory.GetFiles(disposFolderPath, "*.xml")
                                .Select(Path.GetFileName)
                                .OrderBy(f => f)
                                .ToArray();
            
            availableFiles = files;
            
            if (selectedFileIndex >= availableFiles.Length)
            {
                selectedFileIndex = availableFiles.Length - 1;
            }
        }
        
        private void LoadFile(int index)
        {
            if (availableFiles == null || index < 0 || index >= availableFiles.Length)
                return;
            
            selectedFileIndex = index;
            string filePath = Path.Combine(disposFolderPath, availableFiles[index]);
            
            currentDocument = DisposDocument.LoadFromFile(filePath);
            documentIsDirty = false;
            
            if (currentDocument != null && sceneRenderer != null)
            {
                // Try to auto-match terrain based on file name
                AutoSelectTerrain(availableFiles[index]);
                
                sceneRenderer.RenderDocument(currentDocument, selectedTerrain);
                SceneView.RepaintAll();
            }
            
            selectedEntry = null;
        }
        
        private void SaveDocument()
        {
            if (currentDocument != null)
            {
                currentDocument.SaveToFile();
                documentIsDirty = false;
                EditorUtility.DisplayDialog("Save Complete", "Dispos file saved successfully.", "OK");
            }
        }
        
        public void NotifyUnitMoved(GameObject unitObj)
        {
            if (unitObj != null)
            {
                DisposTool.DisposUnitComponent component = unitObj.GetComponent<DisposTool.DisposUnitComponent>();
                if (component != null)
                {
                    DisposEntry entry = FindEntryByComponent(component);
                    if (entry != null)
                    {
                        entry.DisposX = component.disposX;
                        entry.DisposY = component.disposY;
                        MarkDocumentDirty();
                    }
                }
            }
        }
        
        public void MarkDocumentDirty()
        {
            documentIsDirty = true;
            Repaint();
        }
        
        private void OnSelectionChanged()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected != null)
            {
                DisposTool.DisposUnitComponent unitComponent = selected.GetComponent<DisposTool.DisposUnitComponent>();
                if (unitComponent != null)
                {
                    // Find the entry that matches this component
                    DisposEntry matchingEntry = FindEntryByComponent(unitComponent);
                    if (matchingEntry != null)
                    {
                        SelectEntry(matchingEntry);
                        return;
                    }
                }
                
                DisposTool.DisposGroupComponent groupComponent = selected.GetComponent<DisposTool.DisposGroupComponent>();
                if (groupComponent != null)
                {
                    // Could highlight group in list if needed
                    selectedEntry = null;
                    sceneRenderer.SelectedEntry = null;
                    Repaint();
                }
            }
        }
        
        public DisposEntry FindEntryByComponent(DisposTool.DisposUnitComponent component)
        {
            if (currentDocument == null || component == null)
                return null;
                
            foreach (var group in currentDocument.Groups)
            {
                foreach (var entry in group.Entries)
                {
                    if (!entry.IsGroupHeader && 
                        entry.Pid == component.unitPid && 
                        entry.DisposX == component.disposX && 
                        entry.DisposY == component.disposY)
                    {
                        return entry;
                    }
                }
            }
            return null;
        }
        
        private void SelectEntry(DisposEntry entry)
        {
            selectedEntry = entry;
            sceneRenderer.SelectedEntry = entry;
            
            // Find and select the corresponding GameObject
            if (entry != null)
            {
                GameObject[] allObjects = FindObjectsOfType<GameObject>();
                foreach (GameObject obj in allObjects)
                {
                    DisposTool.DisposUnitComponent unitComponent = obj.GetComponent<DisposTool.DisposUnitComponent>();
                    if (unitComponent != null && 
                        unitComponent.unitPid == entry.Pid &&
                        unitComponent.disposX == entry.DisposX &&
                        unitComponent.disposY == entry.DisposY)
                    {
                        Selection.activeGameObject = obj;
                        SceneView.lastActiveSceneView?.Frame(new Bounds(obj.transform.position, Vector3.one * 10f), false);
                        break;
                    }
                }
            }
            Repaint();
        }
        
        private void AutoSelectTerrain(string disposFileName)
        {
            // Try to find a terrain with matching name pattern
            // e.g., S069.xml -> look for terrain containing "S069" or "Fld_S069"
            string baseName = System.IO.Path.GetFileNameWithoutExtension(disposFileName);
            
            string[] guids = AssetDatabase.FindAssets("t:MapTerrain", new[] { "Assets" });
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Bridge.MapTerrain terrain = AssetDatabase.LoadAssetAtPath<Bridge.MapTerrain>(path);
                if (terrain != null && terrain.name.Contains(baseName))
                {
                    selectedTerrain = terrain;
                    break;
                }
            }
        }
    }
}
