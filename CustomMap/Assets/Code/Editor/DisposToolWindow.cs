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
        private DisposUndoProxy undoProxy;
        
        private Vector2 leftPanelScroll;
        private Vector2 rightPanelScroll;
        private float leftPanelWidth = 250f;
        private float rightPanelWidth = 300f;
        private bool isResizingLeft;
        private bool isResizingRight;

        // UI state for creating groups
        private string newGroupName = "";
        
        private string[] availableFiles;
        private int selectedFileIndex = -1;
        private string disposFolderPath = "Assets/Share/Addressables/GameData/Dispos";
        
        private bool showGrid = true;
        private bool showLabels = true;
        private bool showDirections = true;
        private bool showIcons = true;
        private bool showSimplifiedNames = true;
        
        private bool isDraggingUnit = false;
        private DisposEntry draggedEntry = null;
        private bool documentIsDirty = false;
        // Empty-tile selection state (for placing new units)
        private bool hasSelectedTile = false;
        private Vector2Int selectedTile;
        
        // Difficulty filter toggles
        private bool filterNormal = true;
        private bool filterHard = true;
        private bool filterLunatic = true;

        // Quick Picker state
        private bool pickerOpen = false;
        private List<DisposEntry> pickerEntries = new List<DisposEntry>();
        private Rect pickerRect;
        private int pickerHoverIndex = -1;
        private Vector2Int pickerTile;
        private DisposEntry pickerHighlightEntry;

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
            
            // Create undo proxy
            undoProxy = ScriptableObject.CreateInstance<DisposUndoProxy>();
            
            SceneView.duringSceneGui += OnSceneGUI;
            Selection.selectionChanged += OnSelectionChanged;
            Undo.undoRedoPerformed += OnUndoRedoPerformed;

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

            // Load difficulty filter prefs and apply
            filterNormal = EditorPrefs.GetBool("Dispos_Filter_N", true);
            filterHard = EditorPrefs.GetBool("Dispos_Filter_H", true);
            filterLunatic = EditorPrefs.GetBool("Dispos_Filter_L", true);
            sceneRenderer.SetDifficultyFilter(filterNormal, filterHard, filterLunatic);
        }
        
        private void OnDisable()
        {
            instance = null;
            
            SceneView.duringSceneGui -= OnSceneGUI;
            Selection.selectionChanged -= OnSelectionChanged;
            Undo.undoRedoPerformed -= OnUndoRedoPerformed;
            
            // Restore tools visibility
            Tools.hidden = false;

            // Release any external locks
            TerrainPaintToolWindow.SetExternalInteractionLocked(false);

            if (sceneRenderer != null)
            {
                sceneRenderer.Cleanup();
            }
            
            if (undoProxy != null)
            {
                DestroyImmediate(undoProxy);
                undoProxy = null;
            }
        }
        
        private void OnUndoRedoPerformed()
        {
            if (undoProxy != null && currentDocument != null)
            {
                // Restore document state from undo proxy
                undoProxy.RestoreFromSerializedState();
                
                // Mark document as dirty
                documentIsDirty = true;
                
                // Refresh the scene
                sceneRenderer?.RenderDocument(currentDocument, selectedTerrain);
                SceneView.RepaintAll();
                Repaint();
            }
        }
        
        private void OnGUI()
        {
            // Also allow closing the quick picker with Escape when the editor window has focus
            var evt = Event.current;
            if (pickerOpen && evt != null && (evt.type == EventType.KeyDown || evt.type == EventType.KeyUp) && evt.keyCode == KeyCode.Escape)
            {
                pickerOpen = false;
                evt.Use();
                SceneView.RepaintAll();
                Repaint();
                // Early return to avoid drawing one more frame with picker state
                return;
            }

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
            
            if (showLabels)
            {
                showSimplifiedNames = GUILayout.Toggle(showSimplifiedNames, "Simple Names", EditorStyles.toolbarButton, GUILayout.Width(90));
            }

            // Difficulty filter toggles
            GUILayout.Space(6);
            GUILayout.Label("Diff:", EditorStyles.miniLabel, GUILayout.Width(30));
            bool n = GUILayout.Toggle(filterNormal, "N", EditorStyles.toolbarButton, GUILayout.Width(24));
            bool h = GUILayout.Toggle(filterHard, "H", EditorStyles.toolbarButton, GUILayout.Width(24));
            bool l = GUILayout.Toggle(filterLunatic, "L", EditorStyles.toolbarButton, GUILayout.Width(24));
            if (n != filterNormal || h != filterHard || l != filterLunatic)
            {
                filterNormal = n; filterHard = h; filterLunatic = l;
                EditorPrefs.SetBool("Dispos_Filter_N", filterNormal);
                EditorPrefs.SetBool("Dispos_Filter_H", filterHard);
                EditorPrefs.SetBool("Dispos_Filter_L", filterLunatic);
                sceneRenderer.SetDifficultyFilter(filterNormal, filterHard, filterLunatic);
            }
            
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
            sceneRenderer?.SetShowSimplifiedNames(showSimplifiedNames);
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
            EditorGUILayout.BeginHorizontal();
            newGroupName = EditorGUILayout.TextField("New Group", newGroupName);
            if (GUILayout.Button("Add", GUILayout.Width(60)))
            {
                TryAddGroup(newGroupName);
            }
            EditorGUILayout.EndHorizontal();
            
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

            // Add Unit / Move Selected Here quick actions
            if (GUILayout.Button("+ Unit", EditorStyles.miniButton, GUILayout.Width(60)))
            {
                AddUnitToGroup(group);
            }
            bool canMoveSelected = selectedEntry != null && !selectedEntry.IsGroupHeader && selectedEntry.Group != group.GroupName;
            GUI.enabled = canMoveSelected;
            if (GUILayout.Button("Move Here", EditorStyles.miniButton, GUILayout.Width(80)))
            {
                MoveSelectedToGroup(group);
            }
            GUI.enabled = true;
            
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
                
                // Show all entries (no truncation)
                foreach (var entry in group.Entries)
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
                
                // No truncation footer — we show all rows within the scroll view
                
                EditorGUI.indentLevel--;
            }
        }

        private void TryAddGroup(string name)
        {
            if (currentDocument == null) return;
            string trimmed = (name ?? "").Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                EditorUtility.DisplayDialog("Add Group", "Please enter a non-empty group name.", "OK");
                return;
            }
            // Ensure uniqueness
            if (currentDocument.Groups.Any(g => g.GroupName == trimmed))
            {
                EditorUtility.DisplayDialog("Add Group", $"Group '{trimmed}' already exists.", "OK");
                return;
            }

            // Record undo
            undoProxy?.RecordUndo("Add Group");

            // Create header entry to mark the group boundary in XML
            var headerEntry = new DisposEntry
            {
                Group = trimmed,
                Pid = "" // header marker via empty PID
            };

            // Create group container
            var newGroup = new DisposGroup
            {
                GroupName = trimmed,
                Entries = new List<DisposEntry>(), // header not included in group entries
                IsExpanded = true,
                IsVisible = true
            };

            currentDocument.Groups.Add(newGroup);
            currentDocument.AllEntries.Add(headerEntry);
            documentIsDirty = true;
            newGroupName = "";
            sceneRenderer.RenderDocument(currentDocument);
            Repaint();
        }

        private void AddUnitToGroup(DisposGroup group)
        {
            if (currentDocument == null || group == null) return;
            undoProxy?.RecordUndo("Add Unit to Group");

            var e = new DisposEntry
            {
                Group = group.GroupName,
                // Empty PID allowed in-memory; this remains a unit (not a header)
                Pid = "",
                Force = 0,
                Flag = 0,
                AppearX = hasSelectedTile ? selectedTile.x : 0,
                AppearY = hasSelectedTile ? selectedTile.y : 0,
                DisposX = hasSelectedTile ? selectedTile.x : 0,
                DisposY = hasSelectedTile ? selectedTile.y : 0,
                Direction = 0,
                LevelN = 0,
                LevelH = 0,
                LevelL = 0,
                Jid = "",
            };
            e.IsGroupHeader = false;

            // Insert into AllEntries after the group's last entry if possible
            int insertIndex = -1;
            for (int i = 0; i < currentDocument.AllEntries.Count; i++)
            {
                var ae = currentDocument.AllEntries[i];
                if (ae.Group == group.GroupName)
                    insertIndex = i; // keep updating to last occurrence
            }
            if (insertIndex >= 0 && insertIndex + 1 <= currentDocument.AllEntries.Count)
                currentDocument.AllEntries.Insert(insertIndex + 1, e);
            else
                currentDocument.AllEntries.Add(e);

            group.Entries.Add(e);
            documentIsDirty = true;
            sceneRenderer.RenderDocument(currentDocument);
            SelectEntry(e);
            SceneView.RepaintAll();
            // Keep the tile highlight for further adds if desired
        }

        private void MoveSelectedToGroup(DisposGroup target)
        {
            if (currentDocument == null || target == null || selectedEntry == null) return;
            var entry = selectedEntry;
            if (entry.IsGroupHeader) return;
            if (entry.Group == target.GroupName) return;

            undoProxy?.RecordUndo("Move Unit To Group");

            // Remove from old group container
            var oldGroup = currentDocument.Groups.FirstOrDefault(g => g.GroupName == entry.Group);
            if (oldGroup != null)
                oldGroup.Entries.Remove(entry);

            // Update the entry's group field
            entry.Group = target.GroupName;

            // Insert into AllEntries after target group's last entry to preserve order
            int currentIndex = currentDocument.AllEntries.IndexOf(entry);
            if (currentIndex >= 0)
                currentDocument.AllEntries.RemoveAt(currentIndex);

            int insertIndex = -1;
            for (int i = 0; i < currentDocument.AllEntries.Count; i++)
            {
                var ae = currentDocument.AllEntries[i];
                if (ae.Group == target.GroupName)
                    insertIndex = i;
            }
            if (insertIndex >= 0 && insertIndex + 1 <= currentDocument.AllEntries.Count)
                currentDocument.AllEntries.Insert(insertIndex + 1, entry);
            else
                currentDocument.AllEntries.Add(entry);

            // Add to target group container
            target.Entries.Add(entry);

            documentIsDirty = true;
            sceneRenderer.RenderDocument(currentDocument);
            SelectEntry(entry);
            SceneView.RepaintAll();
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
            
            // Record undo before any changes
            if (undoProxy != null)
            {
                undoProxy.RecordUndo("Change Unit Properties");
            }
            
            EditorGUI.BeginChangeCheck();
            
            // Dispos identifier (only PID is editable here)
            EditorGUILayout.BeginHorizontal();
            GUI.SetNextControlName("PIDField");
            string newPid = EditorGUILayout.TextField("PID", entry.Pid);
            if (newPid != entry.Pid)
            {
                entry.Pid = newPid;
            }
            if (GUILayout.Button("Browse", GUILayout.Width(70)))
            {
                PersonLookupWindow.Show(pid => ApplyPidToSelected(pid));
            }
            EditorGUILayout.EndHorizontal();
            // Inline suggestions removed; use Browse popup instead
            
            // Intentionally omit Person/Job readouts like Name/Gender to keep focus on Dispos data
            
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

            // Icon resolution readout
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Icon", EditorStyles.boldLabel);
            string resolvedIcon = DisposDataLoader.Instance.GetUnitIconPath(entry);
            EditorGUILayout.LabelField("Resolved File", string.IsNullOrEmpty(resolvedIcon) ? "(none)" : resolvedIcon + ".png");
            
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("IDs & Stock", EditorStyles.boldLabel);
            entry.Sid = EditorGUILayout.TextField("Sid", entry.Sid);
            entry.Bid = EditorGUILayout.TextField("Bid", entry.Bid);
            entry.Gid = EditorGUILayout.TextField("Gid", entry.Gid);
            entry.HpStockCount = EditorGUILayout.IntField("HP Stock Count", entry.HpStockCount);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("States", EditorStyles.boldLabel);
            // Draw as 2 rows of 3 with comfortable spacing
            float prevLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 20f;
            EditorGUILayout.BeginVertical();
            for (int row = 0; row < 2; row++)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(6);
                for (int col = 0; col < 3; col++)
                {
                    int idx = row * 3 + col;
                    entry.States[idx] = EditorGUILayout.IntField($"S{idx}", entry.States[idx], GUILayout.Width(90));
                    GUILayout.Space(10);
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndVertical();
            EditorGUIUtility.labelWidth = prevLabelWidth;

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
            entry.AI_ActionVal = EditorGUILayout.TextField("Action Arg", entry.AI_ActionVal);
            entry.AI_MindName = EditorGUILayout.TextField("Mind", entry.AI_MindName);
            entry.AI_MindVal = EditorGUILayout.TextField("Mind Arg", entry.AI_MindVal);
            entry.AI_AttackName = EditorGUILayout.TextField("Attack", entry.AI_AttackName);
            entry.AI_AttackVal = EditorGUILayout.TextField("Attack Arg", entry.AI_AttackVal);
            entry.AI_MoveName = EditorGUILayout.TextField("Move", entry.AI_MoveName);
            entry.AI_MoveVal = EditorGUILayout.TextField("Move Arg", entry.AI_MoveVal);
            entry.AI_BattleRate = EditorGUILayout.TextField("Battle Rate", entry.AI_BattleRate);
            entry.AI_Priority = EditorGUILayout.IntField("Priority", entry.AI_Priority);
            entry.AI_HealRateA = EditorGUILayout.IntField("Heal Rate A", entry.AI_HealRateA);
            entry.AI_HealRateB = EditorGUILayout.IntField("Heal Rate B", entry.AI_HealRateB);
            entry.AI_BandNo = EditorGUILayout.IntField("Band No", entry.AI_BandNo);
            entry.AI_MoveLimit = EditorGUILayout.TextField("Move Limit", entry.AI_MoveLimit);
            entry.AI_Flag = EditorGUILayout.IntField("AI Flag", entry.AI_Flag);
            entry.AI_Active = EditorGUILayout.TextField("AI Active", entry.AI_Active);
            entry.AI_ActiveTurn = EditorGUILayout.IntField("AI Active Turn", entry.AI_ActiveTurn);
            entry.AI_ActiveFlag = EditorGUILayout.TextField("AI Active Flag", entry.AI_ActiveFlag);

            // Additional attributes (if present)
            var extras = entry.GetAllAdditionalAttributes();
            if (extras != null && extras.Count > 0)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Additional Attributes", EditorStyles.boldLabel);
                foreach (var kv in extras)
                {
                    string newVal = EditorGUILayout.TextField(kv.Key, kv.Value);
                    if (newVal != kv.Value)
                    {
                        entry.SetAdditionalAttribute(kv.Key, newVal);
                    }
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Flags", EditorStyles.boldLabel);
            // Show numeric view
            EditorGUILayout.LabelField($"Value: {entry.Flag} (0x{entry.Flag:X})", EditorStyles.miniLabel);

            // Difficulty flags
            EditorGUILayout.LabelField("Difficulty", EditorStyles.miniBoldLabel);
            entry.Flag = DrawFlagToggle(entry.Flag, "Normal", (int)DisposFlags.Normal);
            entry.Flag = DrawFlagToggle(entry.Flag, "Hard", (int)DisposFlags.Hard);
            entry.Flag = DrawFlagToggle(entry.Flag, "Lunatic", (int)DisposFlags.Lunatic);

            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField("Sortie Mask", EditorStyles.miniBoldLabel);
            entry.Flag = DrawFlagToggle(entry.Flag, "Pos", (int)DisposFlags.Pos);
            entry.Flag = DrawFlagToggle(entry.Flag, "Must", (int)DisposFlags.Must);
            entry.Flag = DrawFlagToggle(entry.Flag, "Fix", (int)DisposFlags.Fix);

            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField("Properties", EditorStyles.miniBoldLabel);
            entry.Flag = DrawFlagToggle(entry.Flag, "Create", (int)DisposFlags.Create);
            entry.Flag = DrawFlagToggle(entry.Flag, "Leader", (int)DisposFlags.Leader);
            entry.Flag = DrawFlagToggle(entry.Flag, "Not Move", (int)DisposFlags.NotMove);
            entry.Flag = DrawFlagToggle(entry.Flag, "Edge", (int)DisposFlags.Edge);
            entry.Flag = DrawFlagToggle(entry.Flag, "Guest", (int)DisposFlags.Guest);

            if (EditorGUI.EndChangeCheck())
            {
                documentIsDirty = true;
                sceneRenderer.RenderDocument(currentDocument);
                SceneView.RepaintAll();
            }
        }

        private void ApplyPidToSelected(string pid)
        {
            if (selectedEntry == null) return;
            undoProxy?.RecordUndo("Change PID");
            selectedEntry.Pid = pid;
            // If Jid is empty, seed with person default Jid
            var person = DisposDataLoader.Instance.GetPerson(pid);
            if (person != null && string.IsNullOrEmpty(selectedEntry.Jid))
            {
                selectedEntry.Jid = person.Jid;
            }
            MarkDocumentDirty();
            sceneRenderer.RenderDocument(currentDocument);
            Repaint();
        }

        private int DrawFlagToggle(int flags, string label, int bit)
        {
            bool has = (flags & bit) != 0;
            bool newHas = EditorGUILayout.ToggleLeft(label, has);
            if (newHas != has)
            {
                if (newHas) flags |= bit; else flags &= ~bit;
                MarkDocumentDirty();
            }
            return flags;
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
                    // Reposition picker to follow camera movement
                    if (pickerOpen)
                    {
                        UpdatePickerRectPlacement();
                    }
                    // Inform renderer about picker to avoid label overlap (uses updated rect)
                    sceneRenderer.SetGuiOcclusionRect(pickerOpen ? pickerRect : Rect.zero);
                    sceneRenderer.DrawSceneGUI();
                    if (overlayMode == DisposOverlayMode.Edit)
                    {
                        HandleSceneInput();
                        DrawQuickPicker();
                    }
                }

                // Disable default transform handles when a Dispos unit is selected
                GameObject active = Selection.activeGameObject;
                bool isUnit = active != null && active.GetComponent<DisposTool.DisposUnitComponent>() != null;
                Tools.hidden = isUnit || overlayMode == DisposOverlayMode.Edit;
                if (Tools.hidden) Tools.current = Tool.None;
            }
        }

        private void UpdatePickerRectPlacement()
        {
            if (!pickerOpen || pickerEntries == null || pickerEntries.Count <= 1) return;
            Rect tileRect = sceneRenderer.GetTileScreenRect(pickerTile);
            float width = 240f;
            float rowH = 22f;
            float headerH = 18f;
            int rowsWanted = Mathf.Clamp(pickerEntries.Count, 2, 8);
            float height = headerH + rowsWanted * rowH + 8f; // header + rows + padding
            float x = tileRect.xMax + 8f;
            if (x + width > Screen.width) x = tileRect.xMin - 8f - width;
            float y = tileRect.yMin;
            // Clamp to screen bounds to avoid going off-screen vertically
            y = Mathf.Clamp(y, 0, Screen.height - height - 8f);
            pickerRect = new Rect(x, y, width, height);
        }
        
        private void HandleSceneInput()
        {
            Event e = Event.current;
            
            // Handle Escape key to close picker (scene view focus)
            if (pickerOpen && (e.type == EventType.KeyDown || e.type == EventType.KeyUp) && e.keyCode == KeyCode.Escape)
            {
                pickerOpen = false;
                e.Use();
                SceneView.RepaintAll();
                return;
            }
            
            // Keyboard nudging disabled per request; movement via drag handle only

            // If the quick picker is open and the mouse is over it, let the picker consume events
            if (pickerOpen && pickerRect.Contains(e.mousePosition))
            {
                if (e.type == EventType.MouseDrag)
                {
                    e.Use();
                }
                return;
            }

            // Claim mouse focus to make icon dragging reliable in Edit mode
            if (e.type == EventType.Layout)
            {
                HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
            }

            if (e.type == EventType.MouseDown && e.button == 0)
            {
                // First check if clicking on a stack badge
                if (sceneRenderer.TryGetStackBadgeClick(e.mousePosition, out var badgeTile, out var badgeEntries))
                {
                    pickerOpen = true;
                    pickerEntries = badgeEntries;
                    pickerTile = badgeTile;
                    Rect tileRect = sceneRenderer.GetTileScreenRect(badgeTile);
                    float width = 240f;
                    float rowH = 22f;
                    float headerH = 18f;
                    int rowsWanted = Mathf.Clamp(badgeEntries.Count, 2, 8);
                    float height = headerH + rowsWanted * rowH + 8f;
                    float x = tileRect.xMax + 8f;
                    if (x + width > Screen.width) x = tileRect.xMin - 8f - width;
                    float y = tileRect.yMin;
                    pickerRect = new Rect(x, y, width, height);
                    pickerHoverIndex = -1;
                    // Pre-highlight: prefer current selection on this tile, otherwise tile's current top entry, otherwise first
                    if (selectedEntry != null && selectedEntry.DisposX == pickerTile.x && selectedEntry.DisposY == pickerTile.y && pickerEntries.Contains(selectedEntry))
                        pickerHighlightEntry = selectedEntry;
                    else
                        pickerHighlightEntry = sceneRenderer.GetTopEntryOnTile(pickerTile) ?? pickerEntries[0];
                    e.Use();
                    return;
                }
                
                // Then try screen-space hit test to get all entries at cursor
                var entries = sceneRenderer.GetEntriesAtScreenPosition(e.mousePosition);
                if (entries == null || entries.Count == 0)
                {
                    // Fallback to plane-space tile hit
                    Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
                    float planeY = sceneRenderer != null ? sceneRenderer.GetBasePlaneY() : 0f;
                    float distance = (planeY - ray.origin.y) / ray.direction.y;
                    if (distance > 0)
                    {
                        Vector3 worldPos = ray.origin + ray.direction * distance;
                        var e1 = sceneRenderer.GetEntryAtPosition(worldPos);
                        if (e1 != null) {
                            entries = new List<DisposEntry> { e1 };
                        } else if (sceneRenderer.TryGetTileFromWorld(worldPos, out var emptyTile)) {
                            // Select an empty tile for placement
                            hasSelectedTile = true;
                            selectedTile = emptyTile;
                            sceneRenderer.SetSelectedTile(emptyTile);
                            SelectEntry(null);
                            e.Use();
                            return;
                        }
                    }
                }

                if (entries != null && entries.Count > 0)
                {
                    if (entries.Count == 1)
                    {
                        SelectEntry(entries[0]);
                        if (!e.shift)
                        {
                            isDraggingUnit = true;
                            draggedEntry = entries[0];
                        }
                        e.Use();
                    }
                    else
                    {
                        // Multiple entries on this tile; default to editing the current top unit
                        var tile = new Vector2Int(entries[0].DisposX, entries[0].DisposY);
                        pickerTile = tile; // keep updated for potential future picker opens

                        // Choose the renderer's top entry, or fall back to the first
                        DisposEntry target = sceneRenderer.GetTopEntryOnTile(tile) ?? entries[0];

                        if (target != null)
                        {
                            SelectEntry(target);
                            if (!e.shift)
                            {
                                isDraggingUnit = true;
                                draggedEntry = target;
                            }
                        }
                        // Clear any empty tile highlight when selecting a unit
                        hasSelectedTile = false;
                        sceneRenderer.SetSelectedTile(null);
                        e.Use();
                    }
                }
                else
                {
                    // Clicked on empty space - clear selection and any tile highlight
                    SelectEntry(null);
                    hasSelectedTile = false;
                    sceneRenderer.SetSelectedTile(null);
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
                    sceneRenderer.MoveEntry(draggedEntry, worldPos, undoProxy);
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

        private void DrawQuickPicker()
        {
            if (!pickerOpen || pickerEntries == null || pickerEntries.Count <= 1) return;
            
            Handles.BeginGUI();
            // Opaque background and header
            EditorGUI.DrawRect(pickerRect, new Color(0.12f, 0.12f, 0.12f, 1f));
            Rect headerRect = new Rect(pickerRect.x, pickerRect.y, pickerRect.width, 18f);
            EditorGUI.DrawRect(headerRect, new Color(0.18f, 0.18f, 0.18f, 1f));
            GUIStyle headerStyle = new GUIStyle(GUI.skin.label);
            headerStyle.alignment = TextAnchor.MiddleLeft;
            headerStyle.fontStyle = FontStyle.Bold;
            headerStyle.normal.textColor = Color.white;
            headerStyle.padding = new RectOffset(6, 4, 0, 0);
            // Header text (no tooltip) and reserved space for close button
            string headerText = $"{pickerEntries.Count} units here - choose which to edit";
            Rect headerTextRect = new Rect(headerRect.x, headerRect.y, headerRect.width - 22f, headerRect.height);
            GUI.Label(headerTextRect, headerText, headerStyle);
            // Close button on header (top-right)
            Rect closeRect = new Rect(headerRect.xMax - 18f, headerRect.y + 1f, 16f, 16f);
            if (GUI.Button(closeRect, "x"))
            {
                pickerOpen = false;
                Event.current.Use();
                Handles.EndGUI();
                return;
            }

            Rect listRect = new Rect(pickerRect.x + 6, pickerRect.y + headerRect.height + 2, pickerRect.width - 12, pickerRect.height - headerRect.height - 8);
            float rowH = 22f;
            int rows = Mathf.Min(pickerEntries.Count, Mathf.Max(1, Mathf.FloorToInt(listRect.height / rowH)));
            // Default: no hover highlight until we detect it
            sceneRenderer.SetHoverEntry(null);
            for (int i = 0; i < rows; i++)
            {
                var pe = pickerEntries[i];
                Rect r = new Rect(listRect.x, listRect.y + i * rowH, listRect.width, rowH - 2);
                bool hover = r.Contains(Event.current.mousePosition);
                if (hover) pickerHoverIndex = i;
                if (hover)
                {
                    // Preview highlight in scene while hovering
                    sceneRenderer.SetHoverEntry(pe);
                }
                if (pe == pickerHighlightEntry)
                {
                    EditorGUI.DrawRect(r, new Color(0.25f, 0.45f, 0.85f, 0.35f));
                }
                else if (hover)
                {
                    EditorGUI.DrawRect(r, new Color(1f,1f,1f,0.12f));
                }
                string label = DisposDataLoader.Instance.GetUnitDisplayName(pe);
                string diff = DiffString(pe.Flag);
                GUIStyle rowStyle = new GUIStyle(GUI.skin.label);
                if (pe == pickerHighlightEntry) rowStyle.fontStyle = FontStyle.Bold;
                GUI.Label(r, $"{label}  [{pe.Group}]  {diff}", rowStyle);
                if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && hover)
                {
                    SelectEntry(pe);
                    pickerOpen = false;
                    sceneRenderer.SetHoverEntry(null);
                    Event.current.Use();
                }
            }
            if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && !pickerRect.Contains(Event.current.mousePosition))
            {
                pickerOpen = false;
                sceneRenderer.SetHoverEntry(null);
                Event.current.Use();
            }
            Handles.EndGUI();
        }

        private string DiffString(int flag)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            if ((flag & (int)DisposFlags.Normal) != 0) sb.Append("N");
            if ((flag & (int)DisposFlags.Hard) != 0) sb.Append("H");
            if ((flag & (int)DisposFlags.Lunatic) != 0) sb.Append("L");
            return sb.Length == 0 ? "ALL" : sb.ToString();
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
            
            // Set document on undo proxy
            if (undoProxy != null)
            {
                undoProxy.Document = currentDocument;
            }
            
            // Clear undo history when loading new document
            Undo.ClearAll();
            
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
            // Clear any empty tile selection when focusing a unit
            hasSelectedTile = false;
            sceneRenderer.SetSelectedTile(null);
            
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
