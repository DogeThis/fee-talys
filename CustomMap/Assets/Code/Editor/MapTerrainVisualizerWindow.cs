using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using Bridge;

namespace Editor
{
    public enum DisplayMode
    {
        TextOnly,
        ColorOnly,
        Both
    }
    
    public enum TextDisplayMode
    {
        ShowTID,
        ShowName,
        ShowBoth
    }
    
    // Removed unused LabelInfo class
    
    // Class to represent a connected group of terrain tiles
    public class TerrainIsland
    {
        public string terrainId;
        public List<Vector2Int> tiles;
        public Vector2 center;
        public List<Vector2> labelPositions;
        
        public TerrainIsland(string id)
        {
            terrainId = id;
            tiles = new List<Vector2Int>();
            labelPositions = new List<Vector2>();
        }
        
        public void CalculateCenter()
        {
            if (tiles.Count == 0) return;
            
            float sumX = 0;
            float sumY = 0;
            foreach (var tile in tiles)
            {
                sumX += tile.x;
                sumY += tile.y;
            }
            center = new Vector2(sumX / tiles.Count, sumY / tiles.Count);
        }
        
        public void CalculateLabelPositions(float cameraDistance)
        {
            labelPositions.Clear();
            
            if (tiles.Count == 0) return;
            
            // Calculate center of mass first
            CalculateCenter();
            
            // Check if the center of mass actually falls within our tiles
            // This handles cases like sea that surrounds land
            Vector2Int centerInt = new Vector2Int(Mathf.RoundToInt(center.x), Mathf.RoundToInt(center.y));
            bool centerIsInTiles = tiles.Contains(centerInt);
            
            // Also check nearby tiles in case of rounding issues
            if (!centerIsInTiles)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        Vector2Int checkPos = new Vector2Int(centerInt.x + dx, centerInt.y + dy);
                        if (tiles.Contains(checkPos))
                        {
                            centerIsInTiles = true;
                            break;
                        }
                    }
                    if (centerIsInTiles) break;
                }
            }
            
            if (centerIsInTiles)
            {
                // Center is valid, use it
                labelPositions.Add(center);
            }
            else
            {
                // Center falls outside our tiles (e.g., sea surrounding land)
                // Find the largest contiguous section and place label there
                
                // Find bounds
                int minX = int.MaxValue, maxX = int.MinValue;
                int minY = int.MaxValue, maxY = int.MinValue;
                foreach (var tile in tiles)
                {
                    minX = Mathf.Min(minX, tile.x);
                    maxX = Mathf.Max(maxX, tile.x);
                    minY = Mathf.Min(minY, tile.y);
                    maxY = Mathf.Max(maxY, tile.y);
                }
                
                // Try placing label in corners/edges where we're likely to have solid sections
                Vector2Int[] candidatePositions = new Vector2Int[]
                {
                    new Vector2Int(minX + 2, minY + 2), // Bottom-left
                    new Vector2Int(maxX - 2, minY + 2), // Bottom-right
                    new Vector2Int(minX + 2, maxY - 2), // Top-left
                    new Vector2Int(maxX - 2, maxY - 2), // Top-right
                    new Vector2Int((minX + maxX) / 2, minY + 2), // Bottom-center
                    new Vector2Int((minX + maxX) / 2, maxY - 2), // Top-center
                    new Vector2Int(minX + 2, (minY + maxY) / 2), // Left-center
                    new Vector2Int(maxX - 2, (minY + maxY) / 2), // Right-center
                };
                
                // Find the candidate that has the most tiles around it
                Vector2Int bestPosition = tiles.First();
                int maxNeighbors = 0;
                
                foreach (var candidate in candidatePositions)
                {
                    if (!tiles.Contains(candidate)) continue;
                    
                    // Count tiles in a 5x5 area around this candidate
                    int neighborCount = 0;
                    for (int dx = -2; dx <= 2; dx++)
                    {
                        for (int dy = -2; dy <= 2; dy++)
                        {
                            Vector2Int checkPos = new Vector2Int(candidate.x + dx, candidate.y + dy);
                            if (tiles.Contains(checkPos))
                            {
                                neighborCount++;
                            }
                        }
                    }
                    
                    if (neighborCount > maxNeighbors)
                    {
                        maxNeighbors = neighborCount;
                        bestPosition = candidate;
                    }
                }
                
                labelPositions.Add(new Vector2(bestPosition.x, bestPosition.y));
            }
        }
    }
    
    public class MapTerrainVisualizerWindow : EditorWindow
    {
        private static MapTerrainVisualizerWindow instance;
        private static MapTerrain selectedTerrain;
        private static bool visualizationEnabled = true;
        private static bool showGridLines = true;
        private static float textSize = 0.5f;
        private static Color textColor = Color.white;
        private static Color gridColor = new Color(1f, 1f, 1f, 0.3f);
        private static float gridThickness = 1f;
        private static Vector3 worldOffset = Vector3.zero;
        private static DisplayMode displayMode = DisplayMode.Both;
        private static float colorOpacity = 0.5f;
        private static float colorBrightness = 1.0f;
        private static TerrainTypeDatabase terrainDatabase;
        private static bool autoContrastText = true;
        private static TextDisplayMode textDisplayMode = TextDisplayMode.ShowTID;
        private static bool showOnHoverOnly = false;
        private static bool groupConnectedLabels = true;
        
        // Island caching for smooth transitions
        private static Dictionary<MapTerrain, List<TerrainIsland>> islandCache = new Dictionary<MapTerrain, List<TerrainIsland>>();
        private static MapTerrain lastCachedTerrain = null;
        private static float lastIslandCameraDistance = -1f;
        private static float lastFrameTime = 0f;
        
        // Camera movement detection
        private static Vector3 lastCameraPosition;
        private static Quaternion lastCameraRotation;
        private static float lastCameraFOV;
        private static float cameraStillTime = 0f;
        private static bool cameraIsMoving = false;
        private const float CAMERA_STILL_THRESHOLD = 0.3f; // Wait this long after camera stops
        
        // Common 4-way neighbor directions
        private static readonly Vector2Int[] Directions4 = new Vector2Int[]
        {
            new Vector2Int(0, 1),   // up
            new Vector2Int(1, 0),   // right
            new Vector2Int(0, -1),  // down
            new Vector2Int(-1, 0)   // left
        };
        
        // Brush painting variables
        private static bool paintMode = false;
        private static string selectedBrushTerrain = "";
        private static int brushSize = 1;
        private static Vector2Int hoveredTile = new Vector2Int(-1, -1);
        private static bool isMouseOverGrid = false;
        
        // Hover highlight animation
        private static float hoverHighlightOpacity = 0f;
        private static float hoverHighlightTargetOpacity = 0f;
        private static float hoverHighlightFadeSpeed = 5f; // Adjustable fade speed
        private static float hoverHighlightMaxOpacity = 0.15f; // Maximum opacity for highlight effect
        
        private const string PREFS_PREFIX = "MapTerrainVisualizer_";
        private const string PREFS_ENABLED = PREFS_PREFIX + "Enabled";
        private const string PREFS_SHOW_GRID = PREFS_PREFIX + "ShowGrid";
        private const string PREFS_TEXT_SIZE = PREFS_PREFIX + "TextSize";
        private const string PREFS_TEXT_COLOR = PREFS_PREFIX + "TextColor";
        private const string PREFS_GRID_COLOR = PREFS_PREFIX + "GridColor";
        private const string PREFS_GRID_THICKNESS = PREFS_PREFIX + "GridThickness";
        private const string PREFS_WORLD_OFFSET = PREFS_PREFIX + "WorldOffset";
        private const string PREFS_SELECTED_TERRAIN = PREFS_PREFIX + "SelectedTerrain";
        private const string PREFS_DISPLAY_MODE = PREFS_PREFIX + "DisplayMode";
        private const string PREFS_COLOR_OPACITY = PREFS_PREFIX + "ColorOpacity";
        private const string PREFS_COLOR_BRIGHTNESS = PREFS_PREFIX + "ColorBrightness";
        private const string PREFS_AUTO_CONTRAST = PREFS_PREFIX + "AutoContrast";
        private const string PREFS_GROUP_CONNECTED = PREFS_PREFIX + "GroupConnected";
        private const string PREFS_HOVER_ONLY = PREFS_PREFIX + "HoverOnly";
        
        private Vector2 scrollPosition;
        private List<MapTerrain> availableTerrains = new List<MapTerrain>();
        private string[] terrainNames;
        private int selectedIndex = -1;
        private Vector2 paletteScrollPosition;
        private string terrainSearchFilter = "";
        
        private const float TILE_SIZE = 5f;
        
        [MenuItem("Window/Map Terrain Visualizer")]
        public static void ShowWindow()
        {
            instance = GetWindow<MapTerrainVisualizerWindow>("Map Terrain Visualizer");
            instance.minSize = new Vector2(300, 400);
        }
        
        private void OnEnable()
        {
            instance = this;
            SceneView.duringSceneGui -= OnSceneGUI;
            SceneView.duringSceneGui += OnSceneGUI;
            Undo.undoRedoPerformed -= OnUndoRedo;
            Undo.undoRedoPerformed += OnUndoRedo;
            LoadSettings();
            RefreshTerrainList();
            LoadTerrainDatabase();
        }
        
        private static void OnUndoRedo()
        {
            // Clear island cache when undo/redo is performed
            // This ensures borders are recalculated after terrain changes
            islandCache.Clear();
            lastCachedTerrain = null;
            SceneView.RepaintAll();
        }
        
        private void LoadTerrainDatabase()
        {
            terrainDatabase = TerrainTypeDatabase.Instance;
            if (terrainDatabase == null)
            {
                Debug.LogWarning("TerrainTypeDatabase not found. Run 'Tools/Parse Terrain XML' to create it.");
            }
        }
        
        private static Color GetContrastColor(Color backgroundColor)
        {
            // Calculate perceived luminance using the relative luminance formula
            // Using gamma-corrected values for better accuracy
            float r = backgroundColor.r;
            float g = backgroundColor.g;
            float b = backgroundColor.b;
            
            // Apply gamma correction for more accurate luminance calculation
            r = r <= 0.03928f ? r / 12.92f : Mathf.Pow((r + 0.055f) / 1.055f, 2.4f);
            g = g <= 0.03928f ? g / 12.92f : Mathf.Pow((g + 0.055f) / 1.055f, 2.4f);
            b = b <= 0.03928f ? b / 12.92f : Mathf.Pow((b + 0.055f) / 1.055f, 2.4f);
            
            // Calculate relative luminance
            float luminance = 0.2126f * r + 0.7152f * g + 0.0722f * b;
            
            // For mid-range colors, check if we need to add an outline
            if (luminance > 0.4f && luminance < 0.6f)
            {
                // For mid-range brightness, prefer white with black outline (handled in DrawLabelWithColoredIcon)
                return Color.white;
            }
            else if (luminance > 0.45f)
            {
                // For lighter backgrounds, use black
                return Color.black;
            }
            else
            {
                // For darker backgrounds, use white
                return Color.white;
            }
        }
        
        // Resolve label color for regular tile/island labels
        private static Color ResolveLabelColorForTile(string terrainId)
        {
            if (autoContrastText && displayMode == DisplayMode.Both && terrainDatabase != null)
            {
                Color tileColor = terrainDatabase.GetTerrainColor(terrainId, Color.gray);
                return GetContrastColor(tileColor);
            }
            return textColor;
        }

        // Resolve label color for hover labels (ignores DisplayMode constraint)
        private static Color ResolveHoverLabelColor(string terrainId)
        {
            if (autoContrastText && terrainDatabase != null)
            {
                Color tileColor = terrainDatabase.GetTerrainColor(terrainId, Color.gray);
                return GetContrastColor(tileColor);
            }
            return textColor;
        }
        
        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            Undo.undoRedoPerformed -= OnUndoRedo;
        }
        
        private void OnDestroy()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            Undo.undoRedoPerformed -= OnUndoRedo;
        }
        
        private void LoadSettings()
        {
            visualizationEnabled = EditorPrefs.GetBool(PREFS_ENABLED, true);
            showGridLines = EditorPrefs.GetBool(PREFS_SHOW_GRID, true);
            textSize = EditorPrefs.GetFloat(PREFS_TEXT_SIZE, 0.5f);
            gridThickness = EditorPrefs.GetFloat(PREFS_GRID_THICKNESS, 1f);
            displayMode = (DisplayMode)EditorPrefs.GetInt(PREFS_DISPLAY_MODE, (int)DisplayMode.Both);
            colorOpacity = EditorPrefs.GetFloat(PREFS_COLOR_OPACITY, 0.5f);
            colorBrightness = EditorPrefs.GetFloat(PREFS_COLOR_BRIGHTNESS, 1.0f);
            autoContrastText = EditorPrefs.GetBool(PREFS_AUTO_CONTRAST, true);
            groupConnectedLabels = EditorPrefs.GetBool(PREFS_GROUP_CONNECTED, true);
            showOnHoverOnly = EditorPrefs.GetBool(PREFS_HOVER_ONLY, false);
            
            string colorStr = EditorPrefs.GetString(PREFS_TEXT_COLOR, ColorUtility.ToHtmlStringRGBA(Color.white));
            ColorUtility.TryParseHtmlString("#" + colorStr, out textColor);
            
            colorStr = EditorPrefs.GetString(PREFS_GRID_COLOR, ColorUtility.ToHtmlStringRGBA(new Color(1f, 1f, 1f, 0.3f)));
            ColorUtility.TryParseHtmlString("#" + colorStr, out gridColor);
            
            float x = EditorPrefs.GetFloat(PREFS_WORLD_OFFSET + "_X", 0);
            float y = EditorPrefs.GetFloat(PREFS_WORLD_OFFSET + "_Y", 0);
            float z = EditorPrefs.GetFloat(PREFS_WORLD_OFFSET + "_Z", 0);
            worldOffset = new Vector3(x, y, z);
            
            string terrainPath = EditorPrefs.GetString(PREFS_SELECTED_TERRAIN, "");
            if (!string.IsNullOrEmpty(terrainPath))
            {
                selectedTerrain = AssetDatabase.LoadAssetAtPath<MapTerrain>(terrainPath);
            }
        }
        
        private void SaveSettings()
        {
            EditorPrefs.SetBool(PREFS_ENABLED, visualizationEnabled);
            EditorPrefs.SetBool(PREFS_SHOW_GRID, showGridLines);
            EditorPrefs.SetFloat(PREFS_TEXT_SIZE, textSize);
            EditorPrefs.SetFloat(PREFS_GRID_THICKNESS, gridThickness);
            EditorPrefs.SetInt(PREFS_DISPLAY_MODE, (int)displayMode);
            EditorPrefs.SetFloat(PREFS_COLOR_OPACITY, colorOpacity);
            EditorPrefs.SetFloat(PREFS_COLOR_BRIGHTNESS, colorBrightness);
            EditorPrefs.SetBool(PREFS_AUTO_CONTRAST, autoContrastText);
            EditorPrefs.SetBool(PREFS_GROUP_CONNECTED, groupConnectedLabels);
            EditorPrefs.SetBool(PREFS_HOVER_ONLY, showOnHoverOnly);
            EditorPrefs.SetString(PREFS_TEXT_COLOR, ColorUtility.ToHtmlStringRGBA(textColor));
            EditorPrefs.SetString(PREFS_GRID_COLOR, ColorUtility.ToHtmlStringRGBA(gridColor));
            EditorPrefs.SetFloat(PREFS_WORLD_OFFSET + "_X", worldOffset.x);
            EditorPrefs.SetFloat(PREFS_WORLD_OFFSET + "_Y", worldOffset.y);
            EditorPrefs.SetFloat(PREFS_WORLD_OFFSET + "_Z", worldOffset.z);
            
            if (selectedTerrain != null)
            {
                string path = AssetDatabase.GetAssetPath(selectedTerrain);
                EditorPrefs.SetString(PREFS_SELECTED_TERRAIN, path);
            }
            else
            {
                EditorPrefs.SetString(PREFS_SELECTED_TERRAIN, "");
            }
        }
        
        private void RefreshTerrainList()
        {
            availableTerrains.Clear();
            
            string[] guids = AssetDatabase.FindAssets("t:MapTerrain", new[] { "Assets" });
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                MapTerrain terrain = AssetDatabase.LoadAssetAtPath<MapTerrain>(path);
                if (terrain != null)
                {
                    availableTerrains.Add(terrain);
                }
            }
            
            terrainNames = new string[availableTerrains.Count];
            for (int i = 0; i < availableTerrains.Count; i++)
            {
                terrainNames[i] = availableTerrains[i].name;
                if (selectedTerrain == availableTerrains[i])
                {
                    selectedIndex = i;
                }
            }
        }
        
        private void OnGUI()
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Map Terrain Visualization", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);
            
            EditorGUI.BeginChangeCheck();
            visualizationEnabled = EditorGUILayout.Toggle("Enable Visualization", visualizationEnabled);
            if (EditorGUI.EndChangeCheck())
            {
                SaveSettings();
                SceneView.RepaintAll();
            }
            
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Terrain Selection", EditorStyles.boldLabel);
            
            EditorGUI.BeginChangeCheck();
            selectedTerrain = (MapTerrain)EditorGUILayout.ObjectField("Selected Terrain", 
                selectedTerrain, typeof(MapTerrain), false);
            if (EditorGUI.EndChangeCheck())
            {
                for (int i = 0; i < availableTerrains.Count; i++)
                {
                    if (availableTerrains[i] == selectedTerrain)
                    {
                        selectedIndex = i;
                        break;
                    }
                }
                SaveSettings();
                SceneView.RepaintAll();
            }
            
            EditorGUILayout.Space(5);
            
            if (GUILayout.Button("Refresh Terrain List"))
            {
                RefreshTerrainList();
            }
            
            if (availableTerrains.Count > 0)
            {
                EditorGUI.BeginChangeCheck();
                selectedIndex = EditorGUILayout.Popup("Quick Select", selectedIndex, terrainNames);
                if (EditorGUI.EndChangeCheck() && selectedIndex >= 0 && selectedIndex < availableTerrains.Count)
                {
                    selectedTerrain = availableTerrains[selectedIndex];
                    SaveSettings();
                    SceneView.RepaintAll();
                }
            }
            
            if (selectedTerrain != null)
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.HelpBox($"Grid Size: {selectedTerrain.m_Width} x {selectedTerrain.m_Height}\n" +
                                       $"Origin: ({selectedTerrain.m_X}, {selectedTerrain.m_Z})\n" +
                                       $"Total Tiles: {selectedTerrain.m_Terrains?.Length ?? 0}", 
                                       MessageType.Info);
            }
            
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Display Options", EditorStyles.boldLabel);
            
            EditorGUI.BeginChangeCheck();
            
            displayMode = (DisplayMode)EditorGUILayout.EnumPopup("Display Mode", displayMode);
            
            if (displayMode != DisplayMode.TextOnly)
            {
                EditorGUILayout.LabelField("Tile Color Settings", EditorStyles.miniBoldLabel);
                colorOpacity = EditorGUILayout.Slider("Tile Opacity", colorOpacity, 0.1f, 1f);
                colorBrightness = EditorGUILayout.Slider("Tile Brightness", colorBrightness, 0.1f, 2f);
                
                if (terrainDatabase == null)
                {
                    EditorGUILayout.HelpBox("Terrain colors not loaded. Click 'Parse Terrain XML' to load colors.", MessageType.Warning);
                    if (GUILayout.Button("Parse Terrain XML"))
                    {
                        TerrainXMLParser.ParseTerrainXML();
                        LoadTerrainDatabase();
                    }
                }
            }
            
            showGridLines = EditorGUILayout.Toggle("Show Grid Lines", showGridLines);
            
            if (displayMode != DisplayMode.ColorOnly)
            {
                EditorGUILayout.LabelField("Text Settings", EditorStyles.miniBoldLabel);
                textDisplayMode = (TextDisplayMode)EditorGUILayout.EnumPopup("Text Display", textDisplayMode);
                showOnHoverOnly = EditorGUILayout.Toggle("Show on Hover Only", showOnHoverOnly);
                
                // Disable grouping when hover-only is enabled
                EditorGUI.BeginDisabledGroup(showOnHoverOnly);
                groupConnectedLabels = EditorGUILayout.Toggle("Group Connected Labels", groupConnectedLabels);
                EditorGUI.EndDisabledGroup();
                
                if (showOnHoverOnly && groupConnectedLabels)
                {
                    EditorGUILayout.HelpBox("Label grouping is disabled in hover-only mode", MessageType.Info);
                }
                
                textSize = EditorGUILayout.Slider("Text Size", textSize, 0.1f, 2f);
                autoContrastText = EditorGUILayout.Toggle("Auto Contrast Text", autoContrastText);
                if (!autoContrastText)
                {
                    textColor = EditorGUILayout.ColorField("Text Color", textColor);
                }
            }
            
            gridColor = EditorGUILayout.ColorField("Grid Color", gridColor);
            gridThickness = EditorGUILayout.Slider("Grid Thickness", gridThickness, 0.5f, 50f);
            
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Hover Settings", EditorStyles.miniBoldLabel);
            hoverHighlightMaxOpacity = EditorGUILayout.Slider("Highlight Opacity", hoverHighlightMaxOpacity, 0.05f, 0.5f);
            hoverHighlightFadeSpeed = EditorGUILayout.Slider("Fade Speed", hoverHighlightFadeSpeed, 1f, 10f);
            
            if (EditorGUI.EndChangeCheck())
            {
                SaveSettings();
                SceneView.RepaintAll();
            }
            
            EditorGUILayout.Space(10);
            
            if (GUILayout.Button("Reset Display Settings"))
            {
                textSize = 0.5f;
                textColor = Color.white;
                gridColor = new Color(1f, 1f, 1f, 0.3f);
                gridThickness = 1f;
                worldOffset = Vector3.zero;
                SaveSettings();
                SceneView.RepaintAll();
            }
            
            // Brush Painting Section
            if (selectedTerrain != null)
            {
                EditorGUILayout.Space(10);
                EditorGUILayout.LabelField("Terrain Painting", EditorStyles.boldLabel);
                
                EditorGUI.BeginChangeCheck();
                
                GUI.backgroundColor = paintMode ? Color.green : Color.white;
                if (GUILayout.Button(paintMode ? "Exit Paint Mode" : "Enter Paint Mode"))
                {
                    paintMode = !paintMode;
                    if (paintMode)
                    {
                        // Make sure we have a default terrain selected
                        if (string.IsNullOrEmpty(selectedBrushTerrain) && terrainDatabase != null)
                        {
                            var allTypes = terrainDatabase.GetAllTerrainTypes();
                            if (allTypes.Count > 0)
                            {
                                selectedBrushTerrain = allTypes[0].tid;
                            }
                        }
                    }
                    SceneView.RepaintAll();
                }
                GUI.backgroundColor = Color.white;
                
                if (paintMode)
                {
                    EditorGUILayout.Space(5);
                    // Only odd numbers for brush size (1x1, 3x3, 5x5, 7x7)
                    int brushSteps = (brushSize - 1) / 2;
                    brushSteps = EditorGUILayout.IntSlider("Brush Size", brushSteps, 0, 3);
                    brushSize = brushSteps * 2 + 1;
                    EditorGUILayout.LabelField($"Brush: {brushSize}x{brushSize}", EditorStyles.miniLabel);
                    
                    EditorGUILayout.Space(5);
                    
                    // Selected terrain with color chip
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("Selected Terrain:", GUILayout.Width(100));
                    
                    if (!string.IsNullOrEmpty(selectedBrushTerrain))
                    {
                        // Draw color chip
                        if (terrainDatabase != null)
                        {
                            Color terrainColor = terrainDatabase.GetTerrainColor(selectedBrushTerrain, Color.gray);
                            Rect colorRect = GUILayoutUtility.GetRect(20, 20, GUILayout.Width(20));
                            EditorGUI.DrawRect(colorRect, terrainColor);
                            EditorGUI.DrawRect(colorRect, new Color(0, 0, 0, 0.2f)); // Border
                        }
                        
                        // Show terrain ID and name
                        string displayName = selectedBrushTerrain;
                        if (terrainDatabase != null)
                        {
                            var terrain = terrainDatabase.GetTerrainType(selectedBrushTerrain);
                            if (terrain != null && !string.IsNullOrEmpty(terrain.name) && terrain.name != terrain.tid)
                            {
                                displayName = $"{selectedBrushTerrain} ({terrain.name})";
                            }
                        }
                        EditorGUILayout.LabelField(displayName, EditorStyles.boldLabel);
                    }
                    else
                    {
                        EditorGUILayout.LabelField("None", EditorStyles.boldLabel);
                    }
                    EditorGUILayout.EndHorizontal();
                    
                    if (terrainDatabase != null)
                    {
                        EditorGUILayout.Space(5);
                        EditorGUILayout.LabelField("Terrain Palette", EditorStyles.miniBoldLabel);
                        
                        // Get terrains used in current map
                        HashSet<string> usedTerrains = new HashSet<string>();
                        if (selectedTerrain != null && selectedTerrain.m_Terrains != null)
                        {
                            foreach (string tid in selectedTerrain.m_Terrains)
                            {
                                if (!string.IsNullOrEmpty(tid))
                                {
                                    usedTerrains.Add(tid);
                                }
                            }
                        }
                        
                        var allTypes = terrainDatabase.GetAllTerrainTypes();
                        
                        // Used Terrains Section
                        if (usedTerrains.Count > 0)
                        {
                            EditorGUILayout.LabelField($"★ Used in Map ({usedTerrains.Count})", EditorStyles.miniBoldLabel);
                            
                            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                            
                            var usedTerrainsList = allTypes.Where(t => usedTerrains.Contains(t.tid)).ToList();
                            usedTerrainsList.Sort((a, b) => string.Compare(a.tid, b.tid));
                            
                            foreach (var terrain in usedTerrainsList)
                            {
                                DrawTerrainButton(terrain, true);
                            }
                            
                            EditorGUILayout.EndVertical();
                            EditorGUILayout.Space(5);
                        }
                        
                        // All Terrains Section
                        EditorGUILayout.LabelField("All Terrains", EditorStyles.miniBoldLabel);
                        terrainSearchFilter = EditorGUILayout.TextField("Search", terrainSearchFilter);
                        
                        paletteScrollPosition = EditorGUILayout.BeginScrollView(paletteScrollPosition, GUILayout.Height(150));
                        
                        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                        
                        foreach (var terrain in allTypes)
                        {
                            if (!string.IsNullOrEmpty(terrainSearchFilter) && 
                                !terrain.tid.ToLower().Contains(terrainSearchFilter.ToLower()) &&
                                !terrain.name.ToLower().Contains(terrainSearchFilter.ToLower()))
                            {
                                continue;
                            }
                            
                            DrawTerrainButton(terrain, usedTerrains.Contains(terrain.tid));
                        }
                        
                        EditorGUILayout.EndVertical();
                        EditorGUILayout.EndScrollView();
                    }
                    
                    EditorGUILayout.HelpBox("Left Click: Paint | Ctrl+Click: Sample/Pick", MessageType.Info);
                }
                
                if (EditorGUI.EndChangeCheck())
                {
                    SceneView.RepaintAll();
                }
            }
            
            EditorGUILayout.EndScrollView();
        }
        
        private static float GetCameraDistance(SceneView sceneView, float terrainCenterX, float terrainCenterZ, float terrainY)
        {
            if (sceneView == null || sceneView.camera == null)
                return 50f; // Default medium distance
            
            Vector3 terrainCenter = new Vector3(terrainCenterX, terrainY, terrainCenterZ);
            Vector3 cameraPos = sceneView.camera.transform.position;
            return Vector3.Distance(cameraPos, terrainCenter);
        }
        
        private static void OnSceneGUI(SceneView sceneView)
        {
            if (!visualizationEnabled || selectedTerrain == null || selectedTerrain.m_Terrains == null)
                return;
            
            int width = selectedTerrain.m_Width;
            int height = selectedTerrain.m_Height;
            float startX = selectedTerrain.m_X + worldOffset.x;
            float startZ = selectedTerrain.m_Z + worldOffset.z;
            float y = worldOffset.y;
            
            // Calculate camera distance for zoom-aware rendering
            float terrainCenterX = startX + (width * TILE_SIZE) / 2f;
            float terrainCenterZ = startZ + (height * TILE_SIZE) / 2f;
            float cameraDistance = GetCameraDistance(sceneView, terrainCenterX, terrainCenterZ, y);
            
            // Calculate frame delta time for smooth interpolation
            float currentTime = (float)EditorApplication.timeSinceStartup;
            float deltaTime = Mathf.Min(currentTime - lastFrameTime, 0.1f); // Cap at 100ms
            lastFrameTime = currentTime;
            
            // Detect camera movement
            bool cameraChanged = false;
            if (sceneView.camera != null)
            {
                Vector3 currentCamPos = sceneView.camera.transform.position;
                Quaternion currentCamRot = sceneView.camera.transform.rotation;
                float currentFOV = sceneView.camera.fieldOfView;
                
                // Check if camera has moved
                if (Vector3.Distance(currentCamPos, lastCameraPosition) > 0.01f ||
                    Quaternion.Angle(currentCamRot, lastCameraRotation) > 0.1f ||
                    Mathf.Abs(currentFOV - lastCameraFOV) > 0.1f)
                {
                    cameraChanged = true;
                    cameraIsMoving = true;
                    cameraStillTime = 0f;
                }
                else
                {
                    // Camera hasn't moved this frame
                    cameraStillTime += deltaTime;
                    if (cameraStillTime > CAMERA_STILL_THRESHOLD)
                    {
                        cameraIsMoving = false;
                    }
                }
                
                lastCameraPosition = currentCamPos;
                lastCameraRotation = currentCamRot;
                lastCameraFOV = currentFOV;
            }
            
            // Handle mouse input for hover detection and painting
            HandleMouseInput(width, height, startX, startZ, y);
            
            // Draw colored tiles if in color mode
            if (displayMode != DisplayMode.TextOnly && terrainDatabase != null)
            {
                for (int row = 0; row < height; row++)
                {
                    for (int col = 0; col < width; col++)
                    {
                        int index = row * width + col;
                        
                        if (index >= selectedTerrain.m_Terrains.Length)
                            break;
                        
                        string terrainId = selectedTerrain.m_Terrains[index];
                        
                        if (string.IsNullOrEmpty(terrainId))
                            continue;
                        
                        float tileX = startX + col * TILE_SIZE;
                        float tileZ = startZ + row * TILE_SIZE;
                        
                        Vector3[] verts = new Vector3[]
                        {
                            new Vector3(tileX, y, tileZ),
                            new Vector3(tileX + TILE_SIZE, y, tileZ),
                            new Vector3(tileX + TILE_SIZE, y, tileZ + TILE_SIZE),
                            new Vector3(tileX, y, tileZ + TILE_SIZE)
                        };
                        
                        Color tileColor = terrainDatabase.GetTerrainColor(terrainId, Color.gray);
                        
                        // Apply brightness adjustment
                        tileColor.r = Mathf.Clamp01(tileColor.r * colorBrightness);
                        tileColor.g = Mathf.Clamp01(tileColor.g * colorBrightness);
                        tileColor.b = Mathf.Clamp01(tileColor.b * colorBrightness);
                        tileColor.a = colorOpacity;
                        
                        Handles.DrawSolidRectangleWithOutline(verts, tileColor, Color.clear);
                    }
                }
            }
            
            // Draw grid lines with solid rendering
            if (showGridLines)
            {
                Handles.color = gridColor;
                
                // Draw horizontal lines using DrawLine for solid appearance
                for (int row = 0; row <= height; row++)
                {
                    Vector3 start = new Vector3(startX, y + 0.01f, startZ + row * TILE_SIZE);
                    Vector3 end = new Vector3(startX + width * TILE_SIZE, y + 0.01f, startZ + row * TILE_SIZE);
                    
                    // DrawLine uses solid lines without anti-aliasing
                    Handles.DrawLine(start, end, gridThickness);
                }
                
                // Draw vertical lines
                for (int col = 0; col <= width; col++)
                {
                    Vector3 start = new Vector3(startX + col * TILE_SIZE, y + 0.01f, startZ);
                    Vector3 end = new Vector3(startX + col * TILE_SIZE, y + 0.01f, startZ + height * TILE_SIZE);
                    
                    Handles.DrawLine(start, end, gridThickness);
                }
            }
            
            // Draw island borders when grouping is enabled (always draw borders, regardless of hover mode)
            if (groupConnectedLabels && terrainDatabase != null)
            {
                // Get cached islands or create new ones
                List<TerrainIsland> islands = GetOrCreateIslands(selectedTerrain, cameraDistance);
                
                foreach (var island in islands)
                {
                    if (string.IsNullOrEmpty(island.terrainId))
                        continue;
                    
                    // Get the base color for this terrain and darken it for the border
                    Color baseColor = terrainDatabase.GetTerrainColor(island.terrainId, Color.gray);
                    Color borderColor = new Color(
                        baseColor.r * 0.6f,
                        baseColor.g * 0.6f,
                        baseColor.b * 0.6f,
                        1f
                    );
                    
                    // Draw borders around the island
                    DrawIslandBorders(island, width, height, startX, startZ, y, borderColor);
                }
            }
            
            // Update hover highlight animation
            if (isMouseOverGrid && hoveredTile.x >= 0 && hoveredTile.y >= 0)
            {
                int hoveredIndex = hoveredTile.y * width + hoveredTile.x;
                if (hoveredIndex < selectedTerrain.m_Terrains.Length)
                {
                    string hoveredTerrainId = selectedTerrain.m_Terrains[hoveredIndex];
                    
                    // In paint mode, handle highlighting differently
                    if (paintMode && !Event.current.control && !string.IsNullOrEmpty(selectedBrushTerrain))
                    {
                        // Set opacity for animation
                        hoverHighlightTargetOpacity = hoverHighlightMaxOpacity;
                        
                        // We handle paint mode highlighting in DrawPaintModeHoverLabel
                        // which will show adjacent islands and exclude brush area
                    }
                    else if (!paintMode && !string.IsNullOrEmpty(hoveredTerrainId))
                    {
                        // Normal mode - highlight the hovered terrain's island
                        hoverHighlightTargetOpacity = hoverHighlightMaxOpacity;
                        
                        // Find all connected tiles of the same terrain type
                        HashSet<Vector2Int> region = FindConnectedRegion(selectedTerrain, hoveredTile, width, height);
                        
                        // Draw highlight for the entire region with animated opacity
                        DrawRegionHighlight(region, startX, startZ, y, hoveredTerrainId);
                    }
                    else
                    {
                        // No terrain or in sampling mode, fade out
                        hoverHighlightTargetOpacity = 0f;
                    }
                }
                else
                {
                    // Outside grid bounds, fade out
                    hoverHighlightTargetOpacity = 0f;
                }
            }
            else
            {
                // Not hovering, fade out
                hoverHighlightTargetOpacity = 0f;
            }
            
            // Animate the highlight opacity using deltaTime we already calculated
            if (Mathf.Abs(hoverHighlightOpacity - hoverHighlightTargetOpacity) > 0.001f)
            {
                hoverHighlightOpacity = Mathf.Lerp(hoverHighlightOpacity, hoverHighlightTargetOpacity, 
                    deltaTime * hoverHighlightFadeSpeed);
                sceneView.Repaint(); // Keep repainting during animation
            }
            else
            {
                hoverHighlightOpacity = hoverHighlightTargetOpacity; // Snap to target when close
            }
            
            // Draw text labels if not in color-only mode
            if (displayMode != DisplayMode.ColorOnly)
            {
                GUIStyle style = new GUIStyle();
                style.fontSize = Mathf.RoundToInt(12 * textSize);
                style.alignment = TextAnchor.MiddleCenter;
                style.fontStyle = FontStyle.Bold; // Make text bolder for better visibility
                
                // Check if we're hovering over a tile and should show hover label
                bool showHoverLabel = false;
                string hoveredTerrainId = "";
                if (isMouseOverGrid && hoveredTile.x >= 0 && hoveredTile.y >= 0)
                {
                    int hoveredIndex = hoveredTile.y * width + hoveredTile.x;
                    if (hoveredIndex < selectedTerrain.m_Terrains.Length)
                    {
                        hoveredTerrainId = selectedTerrain.m_Terrains[hoveredIndex];
                        if (!string.IsNullOrEmpty(hoveredTerrainId))
                        {
                            showHoverLabel = true;
                        }
                    }
                }
                
                // Use island grouping when enabled and not in hover-only mode
                if (groupConnectedLabels && !showOnHoverOnly)
                {
                    // Get cached islands (already retrieved above for borders)
                    List<TerrainIsland> islands = GetOrCreateIslands(selectedTerrain, cameraDistance);
                    
                    // Batch GUI for island labels
                    Handles.BeginGUI();
                    
                    // Draw labels at their natural positions
                    foreach (var island in islands)
                    {
                        if (string.IsNullOrEmpty(island.terrainId))
                            continue;
                        
                        // Skip this island's label if we're hovering over it (will draw hover/paint tooltip instead)
                        // Check if the hovered tile is part of THIS specific island
                        if (showHoverLabel && island.tiles.Contains(hoveredTile))
                            continue;
                        
                        foreach (var labelPos in island.labelPositions)
                        {
                            float centerX = startX + labelPos.x * TILE_SIZE + TILE_SIZE * 0.5f;
                            float centerZ = startZ + labelPos.y * TILE_SIZE + TILE_SIZE * 0.5f;
                            Vector3 worldPos = new Vector3(centerX, y, centerZ);
                            
                            string displayText = GetTerrainDisplayText(island.terrainId);
                            
                            Color labelColor = ResolveLabelColorForTile(island.terrainId);
                            
                            style.normal.textColor = labelColor;
                            
                            // Draw colored icon with the label
                            DrawLabelWithColoredIcon(worldPos, displayText, island.terrainId, style, labelColor, autoContrastText && displayMode == DisplayMode.Both);
                        }
                    }
                    
                    Handles.EndGUI();
                }
                else
                {
                    // Original per-tile labeling
                    // When painting with "Show on Hover Only" enabled, suppress the regular per-tile label
                    // to avoid overlapping with the paint tooltip (Current/Paint).
                    if (!(paintMode && showOnHoverOnly))
                    {
                        // Batch GUI for per-tile labels
                        Handles.BeginGUI();
                        for (int row = 0; row < height; row++)
                        {
                            for (int col = 0; col < width; col++)
                            {
                                // Skip if hover-only mode and not hovering this tile
                                if (showOnHoverOnly)
                                {
                                    if (!isMouseOverGrid || hoveredTile.x != col || hoveredTile.y != row)
                                        continue;
                                }
                                
                                int index = row * width + col;
                                
                                if (index >= selectedTerrain.m_Terrains.Length)
                                    break;
                                
                                string terrainId = selectedTerrain.m_Terrains[index];
                                
                                if (string.IsNullOrEmpty(terrainId))
                                    continue;
                                
                                Color labelColor = ResolveLabelColorForTile(terrainId);
                                
                                style.normal.textColor = labelColor;
                                
                                float centerX = startX + col * TILE_SIZE + TILE_SIZE * 0.5f;
                                float centerZ = startZ + row * TILE_SIZE + TILE_SIZE * 0.5f;
                                Vector3 position = new Vector3(centerX, y, centerZ);
                                
                                // Get display text based on mode
                                string displayText = GetTerrainDisplayText(terrainId);
                                
                                // Draw colored icon with the label
                                DrawLabelWithColoredIcon(position, displayText, terrainId, style, labelColor, autoContrastText && displayMode == DisplayMode.Both);
                            }
                        }
                        Handles.EndGUI();
                    }
                }
                
                // Draw hover label over the hovered tile
                if (showHoverLabel)
                {
                    float hoverX = startX + hoveredTile.x * TILE_SIZE + TILE_SIZE * 0.5f;
                    float hoverZ = startZ + hoveredTile.y * TILE_SIZE + TILE_SIZE * 0.5f;
                    Vector3 hoverPos = new Vector3(hoverX, y, hoverZ);
                    
                    // In paint mode, show Current/Paint as info above brush
                    if (paintMode)
                    {
                        // Always show paint mode tooltip
                        DrawPaintModeHoverLabel(hoverPos, hoveredTerrainId, style, hoveredTile, width, height, startX, startZ, y, cameraDistance);
                    }
                    else if (!showOnHoverOnly)  // Only show regular hover labels when NOT in hover-only mode
                    {
                        // Normal hover label
                        string hoverDisplayText = GetTerrainDisplayText(hoveredTerrainId);
                        
                        Color hoverLabelColor = ResolveHoverLabelColor(hoveredTerrainId);
                        
                        // Make hover label slightly larger and with a highlight
                        GUIStyle hoverStyle = new GUIStyle(style);
                        hoverStyle.fontSize = Mathf.RoundToInt(14 * textSize); // Slightly larger
                        hoverStyle.normal.textColor = hoverLabelColor;
                        
                        // Batch begin for single hover label
                        Handles.BeginGUI();
                        DrawLabelWithColoredIcon(hoverPos, hoverDisplayText, hoveredTerrainId, hoverStyle, hoverLabelColor, autoContrastText);
                        Handles.EndGUI();
                    }
                }
            }
            
            // Draw brush preview when in paint mode
            if (paintMode && isMouseOverGrid)
            {
                DrawBrushPreview(hoveredTile, width, height, startX, startZ, y);
            }
            
            // Repaint when camera is moving, just stopped, or highlight is animating
            if (cameraIsMoving || cameraStillTime < 1f || 
                Mathf.Abs(hoverHighlightOpacity - hoverHighlightTargetOpacity) > 0.001f)
            {
                sceneView.Repaint();
            }
        }
        
        private static void HandleMouseInput(int width, int height, float startX, float startZ, float y)
        {
            Event currentEvent = Event.current;
            
            // Get mouse position in world space
            Ray ray = HandleUtility.GUIPointToWorldRay(currentEvent.mousePosition);
            
            // Calculate intersection with grid plane
            float distance = (y - ray.origin.y) / ray.direction.y;
            if (distance < 0)
            {
                isMouseOverGrid = false;
                return;
            }
            
            Vector3 hitPoint = ray.origin + ray.direction * distance;
            
            // Convert world position to grid coordinates
            int gridX = Mathf.FloorToInt((hitPoint.x - startX) / TILE_SIZE);
            int gridZ = Mathf.FloorToInt((hitPoint.z - startZ) / TILE_SIZE);
            
            // Check if mouse is over valid grid tile
            if (gridX >= 0 && gridX < width && gridZ >= 0 && gridZ < height)
            {
                hoveredTile = new Vector2Int(gridX, gridZ);
                isMouseOverGrid = true;
                
                // Only handle painting clicks when in paint mode
                if (paintMode)
                {
                    // Handle mouse clicks
                    if (currentEvent.type == EventType.MouseDown || currentEvent.type == EventType.MouseDrag)
                    {
                        if (currentEvent.button == 0) // Left click
                        {
                            // Check for modifier keys
                            if (currentEvent.control && currentEvent.type == EventType.MouseDown) // Ctrl + Left click - pick/sample
                            {
                                PickTerrain(hoveredTile, width);
                            }
                            else // Normal left click - paint
                            {
                                PaintTerrain(hoveredTile, width, height);
                            }
                            currentEvent.Use();
                        }
                    }
                    
                    // Block scene navigation only for left mouse button when painting
                    if (currentEvent.type == EventType.Layout && currentEvent.button == 0)
                    {
                        HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
                    }
                }
            }
            else
            {
                isMouseOverGrid = false;
            }
        }
        
        private static void DrawPaintModeHoverLabel(Vector3 position, string currentTerrainId, GUIStyle baseStyle, Vector2Int hoveredTile, int width, int height, float startX, float startZ, float y, float cameraDistance)
        {
            // Handle highlighting in paint mode
            if (!Event.current.control && !string.IsNullOrEmpty(selectedBrushTerrain) && selectedTerrain != null)
            {
                // If we're painting the same terrain ("Already X" case), just show normal highlight
                if (currentTerrainId == selectedBrushTerrain)
                {
                    // Show the full island highlight like in normal hover mode
                    HashSet<Vector2Int> currentIsland = FindConnectedRegion(selectedTerrain, hoveredTile, width, height);
                    if (currentIsland.Count > 0)
                    {
                        DrawRegionHighlight(currentIsland, startX, startZ, y, currentTerrainId);
                    }
                }
                else
                {
                    // Different terrain - show adjacent islands that would be extended
                    HashSet<Vector2Int> adjacentIsland = FindAdjacentIsland(hoveredTile, selectedBrushTerrain, width, height);
                    
                    // Exclude brush area tiles from highlight
                    HashSet<Vector2Int> tilesToHighlight = new HashSet<Vector2Int>(adjacentIsland);
                    
                    int brushHalfSize = (brushSize - 1) / 2;
                    for (int dx = -brushHalfSize; dx <= brushHalfSize; dx++)
                    {
                        for (int dz = -brushHalfSize; dz <= brushHalfSize; dz++)
                        {
                            int x = hoveredTile.x + dx;
                            int z = hoveredTile.y + dz;
                            if (x >= 0 && x < width && z >= 0 && z < height)
                            {
                                tilesToHighlight.Remove(new Vector2Int(x, z));
                            }
                        }
                    }
                    
                    // Draw the highlight for adjacent islands (excluding brush area)
                    if (tilesToHighlight.Count > 0)
                    {
                        DrawRegionHighlight(tilesToHighlight, startX, startZ, y, selectedBrushTerrain);
                    }
                }
            }
            
            Handles.BeginGUI();
            
            bool isSampling = Event.current.control;
            bool isSameTerrain = !string.IsNullOrEmpty(currentTerrainId) && currentTerrainId == selectedBrushTerrain;
            
            // Get camera information
            Camera sceneCamera = SceneView.currentDrawingSceneView.camera;
            Vector3 cameraPos = sceneCamera.transform.position;
            
            // Calculate brush bounds
            int halfSize = (brushSize - 1) / 2;
            
            // Find the top edge of the brush area from camera's perspective
            // We'll place the tooltip as if there was a tile above the top row
            float brushTopZ = startZ + (hoveredTile.y + halfSize + 1) * TILE_SIZE;
            float brushBottomZ = startZ + (hoveredTile.y - halfSize) * TILE_SIZE;
            float brushCenterX = position.x;
            
            // Create test points at the top and bottom edges of the brush
            Vector3 topEdgePoint = new Vector3(brushCenterX, y, brushTopZ);
            Vector3 bottomEdgePoint = new Vector3(brushCenterX, y, brushBottomZ);
            
            // Convert to screen space to see which is visually "higher"
            Vector2 topScreenPos = HandleUtility.WorldToGUIPoint(topEdgePoint);
            Vector2 bottomScreenPos = HandleUtility.WorldToGUIPoint(bottomEdgePoint);
            
            // Choose the edge that appears higher on screen (lower Y value in GUI space)
            Vector3 tooltipBasePos;
            if (topScreenPos.y < bottomScreenPos.y)
            {
                // Top edge is visually higher - place tooltip above it
                tooltipBasePos = new Vector3(brushCenterX, y, brushTopZ + TILE_SIZE * 0.5f);
            }
            else
            {
                // Bottom edge is visually higher (camera is rotated) - place tooltip above it
                tooltipBasePos = new Vector3(brushCenterX, y, brushBottomZ - TILE_SIZE * 0.5f);
            }
            
            // Calculate clearance based on camera mode and distance
            bool isOrthographic = sceneCamera.orthographic;
            float baseClearance = TILE_SIZE; // Base clearance of one tile height
            float clearanceMultiplier;
            
            if (isOrthographic)
            {
                // In orthographic mode, scale based on orthographic size
                float orthoSize = sceneCamera.orthographicSize;
                clearanceMultiplier = Mathf.Clamp(orthoSize / 20f, 0.5f, 2.5f);
            }
            else
            {
                // In perspective mode, scale based on camera distance
                // Close up: less clearance needed (tiles appear larger)
                // Far away: more clearance needed (tiles appear smaller)
                clearanceMultiplier = Mathf.Clamp(cameraDistance / 50f, 0.5f, 2.5f);
                
                // Also consider the camera's field of view
                float fovMultiplier = sceneCamera.fieldOfView / 60f; // 60 is typical FOV
                clearanceMultiplier *= Mathf.Clamp(fovMultiplier, 0.8f, 1.2f);
            }
            
            // Calculate the tooltip height with dynamic clearance
            float tooltipHeight = y + baseClearance * clearanceMultiplier;
            
            // For perspective cameras, use ray-based positioning
            Vector3 tooltipWorldPos;
            if (!isOrthographic)
            {
                // Cast a ray from the camera through the desired tooltip position
                Vector3 dirToTooltip = (tooltipBasePos - cameraPos).normalized;
                
                // Calculate the final position along the ray at the desired height
                float t = (tooltipHeight - cameraPos.y) / dirToTooltip.y;
                
                if (Mathf.Abs(dirToTooltip.y) > 0.01f && t > 0)
                {
                    // Calculate position along ray at the target height
                    tooltipWorldPos = cameraPos + dirToTooltip * t;
                    
                    // Adjust X and Z to stay centered above the brush
                    tooltipWorldPos.x = tooltipBasePos.x;
                    tooltipWorldPos.z = tooltipBasePos.z;
                }
                else
                {
                    // Fallback if ray is nearly horizontal or pointing wrong direction
                    tooltipWorldPos = tooltipBasePos;
                    tooltipWorldPos.y = tooltipHeight;
                }
            }
            else
            {
                // For orthographic cameras, simply offset vertically
                tooltipWorldPos = tooltipBasePos;
                tooltipWorldPos.y = tooltipHeight;
            }
            
            // Convert world position to GUI position
            Vector2 guiPos = HandleUtility.WorldToGUIPoint(tooltipWorldPos);
            
            // Create style for the paint mode label
            GUIStyle style = new GUIStyle(baseStyle);
            style.fontSize = Mathf.RoundToInt(12 * textSize);
            style.alignment = TextAnchor.MiddleLeft;
            style.fontStyle = FontStyle.Bold;
            
            // Build label content and colors
            List<(string text, Color? chipColor, Color textColor)> labels = new List<(string, Color?, Color)>();
            
            if (isSampling)
            {
                labels.Add(("Click to sample", null, Color.cyan));
            }
            else if (isSameTerrain)
            {
                string terrainDisplay = GetTerrainDisplayText(currentTerrainId);
                terrainDisplay = terrainDisplay.Replace("\n", " / ");
                Color terrainColor = terrainDatabase?.GetTerrainColor(currentTerrainId, Color.gray) ?? Color.gray;
                labels.Add(($"Already {terrainDisplay}", terrainColor, new Color(0.7f, 0.7f, 0.7f, 1f)));
            }
            else
            {
                // Current terrain
                if (!string.IsNullOrEmpty(currentTerrainId))
                {
                    string currentDisplay = GetTerrainDisplayText(currentTerrainId);
                    currentDisplay = currentDisplay.Replace("\n", " / ");
                    Color currentColor = terrainDatabase?.GetTerrainColor(currentTerrainId, Color.gray) ?? Color.gray;
                    labels.Add(($"Current: {currentDisplay}", currentColor, new Color(0.9f, 0.9f, 0.9f, 1f)));
                }
                
                // Paint as terrain
                if (!string.IsNullOrEmpty(selectedBrushTerrain))
                {
                    string paintDisplay = GetTerrainDisplayText(selectedBrushTerrain);
                    paintDisplay = paintDisplay.Replace("\n", " / ");
                    Color paintColor = terrainDatabase?.GetTerrainColor(selectedBrushTerrain, Color.gray) ?? Color.gray;
                    labels.Add(($"Paint as: {paintDisplay}", paintColor, Color.white));
                }
                else
                {
                    labels.Add(("No terrain selected", null, new Color(0.6f, 0.6f, 0.6f, 1f)));
                }
            }
            
            // Calculate total size needed
            float maxWidth = 0;
            float totalHeight = 0;
            float lineHeight = 18f;
            float chipSize = 10f;
            float chipPadding = 4f;
            float padding = 6f;
            
            foreach (var label in labels)
            {
                GUIContent content = new GUIContent(label.text);
                Vector2 textSize = style.CalcSize(content);
                float labelWidth = textSize.x;
                if (label.chipColor.HasValue)
                    labelWidth += chipSize + chipPadding;
                maxWidth = Mathf.Max(maxWidth, labelWidth);
                totalHeight += lineHeight;
            }
            
            // Draw subtle background box
            Rect bgRect = new Rect(
                guiPos.x - maxWidth / 2 - padding,
                guiPos.y - totalHeight / 2 - padding,
                maxWidth + padding * 2,
                totalHeight + padding * 2
            );
            
            // Semi-transparent background
            Color bgColor = new Color(0.1f, 0.1f, 0.1f, 0.85f);
            EditorGUI.DrawRect(bgRect, bgColor);
            
            // Subtle border
            Handles.BeginGUI();
            Color borderColor = new Color(0.3f, 0.3f, 0.3f, 0.5f);
            Handles.DrawSolidRectangleWithOutline(new Vector3[] {
                new Vector3(bgRect.x, bgRect.y, 0),
                new Vector3(bgRect.x + bgRect.width, bgRect.y, 0),
                new Vector3(bgRect.x + bgRect.width, bgRect.y + bgRect.height, 0),
                new Vector3(bgRect.x, bgRect.y + bgRect.height, 0)
            }, Color.clear, borderColor);
            
            // Draw labels
            float yOffset = bgRect.y + padding;
            foreach (var label in labels)
            {
                float xPos = bgRect.x + padding;
                
                // Draw color chip if provided
                if (label.chipColor.HasValue)
                {
                    Rect chipRect = new Rect(xPos, yOffset + (lineHeight - chipSize) / 2, chipSize, chipSize);
                    EditorGUI.DrawRect(chipRect, label.chipColor.Value);
                    EditorGUI.DrawRect(chipRect, new Color(0, 0, 0, 0.3f)); // Border
                    xPos += chipSize + chipPadding;
                }
                
                // Draw text
                style.normal.textColor = label.textColor;
                GUIContent content = new GUIContent(label.text);
                Vector2 textSize = style.CalcSize(content);
                GUI.Label(new Rect(xPos, yOffset, textSize.x, lineHeight), label.text, style);
                
                yOffset += lineHeight;
            }
            
            Handles.EndGUI();
        }
        
        private static HashSet<Vector2Int> FindAdjacentIsland(Vector2Int centerTile, string targetTerrain, int width, int height)
        {
            HashSet<Vector2Int> island = new HashSet<Vector2Int>();
            if (selectedTerrain == null || selectedTerrain.m_Terrains == null)
                return island;
            
            // Get brush area
            int halfSize = (brushSize - 1) / 2;
            HashSet<Vector2Int> brushArea = new HashSet<Vector2Int>();
            
            for (int dx = -halfSize; dx <= halfSize; dx++)
            {
                for (int dz = -halfSize; dz <= halfSize; dz++)
                {
                    int x = centerTile.x + dx;
                    int z = centerTile.y + dz;
                    if (x >= 0 && x < width && z >= 0 && z < height)
                    {
                        brushArea.Add(new Vector2Int(x, z));
                    }
                }
            }
            
            // Check tiles adjacent to brush area
            HashSet<Vector2Int> visited = new HashSet<Vector2Int>();
            Queue<Vector2Int> toCheck = new Queue<Vector2Int>();
            
            // Start with tiles adjacent to brush area
            foreach (var brushTile in brushArea)
            {
                foreach (var dir in Directions4)
                {
                    var neighbor = brushTile + dir;
                    if (!brushArea.Contains(neighbor) &&
                        neighbor.x >= 0 && neighbor.x < width &&
                        neighbor.y >= 0 && neighbor.y < height &&
                        !visited.Contains(neighbor))
                    {
                        int index = neighbor.y * width + neighbor.x;
                        if (index < selectedTerrain.m_Terrains.Length &&
                            selectedTerrain.m_Terrains[index] == targetTerrain)
                        {
                            visited.Add(neighbor);
                            toCheck.Enqueue(neighbor);
                        }
                    }
                }
            }
            
            // Flood fill to find connected island
            while (toCheck.Count > 0)
            {
                var current = toCheck.Dequeue();
                island.Add(current);
                
                foreach (var dir in Directions4)
                {
                    var neighbor = current + dir;
                    if (neighbor.x >= 0 && neighbor.x < width &&
                        neighbor.y >= 0 && neighbor.y < height &&
                        !visited.Contains(neighbor))
                    {
                        int index = neighbor.y * width + neighbor.x;
                        if (index < selectedTerrain.m_Terrains.Length &&
                            selectedTerrain.m_Terrains[index] == targetTerrain)
                        {
                            visited.Add(neighbor);
                            toCheck.Enqueue(neighbor);
                        }
                    }
                }
            }
            
            return island;
        }
        
        private static void DrawBrushPreview(Vector2Int centerTile, int width, int height, float startX, float startZ, float y)
        {
            Event currentEvent = Event.current;
            bool isSampling = currentEvent.control;
            
            if (isSampling)
            {
                // Draw sampling indicator - single tile with different color
                float worldX = startX + centerTile.x * TILE_SIZE;
                float worldZ = startZ + centerTile.y * TILE_SIZE;
                
                Vector3[] verts = new Vector3[]
                {
                    new Vector3(worldX, y + 0.05f, worldZ),
                    new Vector3(worldX + TILE_SIZE, y + 0.05f, worldZ),
                    new Vector3(worldX + TILE_SIZE, y + 0.05f, worldZ + TILE_SIZE),
                    new Vector3(worldX, y + 0.05f, worldZ + TILE_SIZE)
                };
                
                // Cyan color for sampling mode
                Color sampleColor = new Color(0f, 1f, 1f, 0.5f);
                Handles.DrawSolidRectangleWithOutline(verts, sampleColor, Color.cyan);
            }
            else
            {
                // Paint preview with actual terrain color
                if (string.IsNullOrEmpty(selectedBrushTerrain) || terrainDatabase == null)
                {
                    // Fallback to yellow if no terrain selected
                    Color previewColor = new Color(1f, 1f, 0f, 0.3f);
                    DrawBrushTiles(centerTile, width, height, startX, startZ, y, previewColor, Color.yellow);
                }
                else
                {
                    // Get the actual color of the terrain we're painting
                    Color terrainColor = terrainDatabase.GetTerrainColor(selectedBrushTerrain, Color.gray);
                    
                    // Make it semi-transparent for preview
                    Color previewColor = new Color(terrainColor.r, terrainColor.g, terrainColor.b, 0.4f);
                    
                    // Darken the color for the outline
                    Color outlineColor = new Color(
                        terrainColor.r * 0.6f,
                        terrainColor.g * 0.6f,
                        terrainColor.b * 0.6f,
                        0.8f
                    );
                    
                    // Draw the preview tiles
                    DrawBrushTiles(centerTile, width, height, startX, startZ, y, previewColor, outlineColor);
                    
                    // Draw preview borders for the new terrain
                    DrawPreviewBorders(centerTile, width, height, startX, startZ, y, outlineColor);
                }
            }
        }
        
        private static void DrawBrushTiles(Vector2Int centerTile, int width, int height, float startX, float startZ, float y, Color fillColor, Color outlineColor)
        {
            int halfSize = (brushSize - 1) / 2;
            
            for (int dx = -halfSize; dx <= halfSize; dx++)
            {
                for (int dz = -halfSize; dz <= halfSize; dz++)
                {
                    int tileX = centerTile.x + dx;
                    int tileZ = centerTile.y + dz;
                    
                    if (tileX >= 0 && tileX < width && tileZ >= 0 && tileZ < height)
                    {
                        float worldX = startX + tileX * TILE_SIZE;
                        float worldZ = startZ + tileZ * TILE_SIZE;
                        
                        Vector3[] verts = new Vector3[]
                        {
                            new Vector3(worldX, y + 0.05f, worldZ),
                            new Vector3(worldX + TILE_SIZE, y + 0.05f, worldZ),
                            new Vector3(worldX + TILE_SIZE, y + 0.05f, worldZ + TILE_SIZE),
                            new Vector3(worldX, y + 0.05f, worldZ + TILE_SIZE)
                        };
                        
                        Handles.DrawSolidRectangleWithOutline(verts, fillColor, outlineColor);
                    }
                }
            }
        }
        
        private static void DrawPreviewBorders(Vector2Int centerTile, int width, int height, float startX, float startZ, float y, Color borderColor)
        {
            // Collect all tiles that would be painted
            HashSet<Vector2Int> paintedTiles = new HashSet<Vector2Int>();
            int halfSize = (brushSize - 1) / 2;
            
            for (int dx = -halfSize; dx <= halfSize; dx++)
            {
                for (int dz = -halfSize; dz <= halfSize; dz++)
                {
                    int tileX = centerTile.x + dx;
                    int tileZ = centerTile.y + dz;
                    
                    if (tileX >= 0 && tileX < width && tileZ >= 0 && tileZ < height)
                    {
                        paintedTiles.Add(new Vector2Int(tileX, tileZ));
                    }
                }
            }
            
            // Draw borders around the painted area
            Handles.color = borderColor;
            float borderThickness = 3f;
            
            foreach (var tile in paintedTiles)
            {
                float tileX = startX + tile.x * TILE_SIZE;
                float tileZ = startZ + tile.y * TILE_SIZE;
                
                // Check each edge to see if it's a border
                // Top edge
                if (!paintedTiles.Contains(new Vector2Int(tile.x, tile.y + 1)))
                {
                    Vector3 lineStart = new Vector3(tileX, y + 0.06f, tileZ + TILE_SIZE);
                    Vector3 lineEnd = new Vector3(tileX + TILE_SIZE, y + 0.06f, tileZ + TILE_SIZE);
                    Handles.DrawLine(lineStart, lineEnd, borderThickness);
                }
                
                // Right edge
                if (!paintedTiles.Contains(new Vector2Int(tile.x + 1, tile.y)))
                {
                    Vector3 lineStart = new Vector3(tileX + TILE_SIZE, y + 0.06f, tileZ);
                    Vector3 lineEnd = new Vector3(tileX + TILE_SIZE, y + 0.06f, tileZ + TILE_SIZE);
                    Handles.DrawLine(lineStart, lineEnd, borderThickness);
                }
                
                // Bottom edge
                if (!paintedTiles.Contains(new Vector2Int(tile.x, tile.y - 1)))
                {
                    Vector3 lineStart = new Vector3(tileX, y + 0.06f, tileZ);
                    Vector3 lineEnd = new Vector3(tileX + TILE_SIZE, y + 0.06f, tileZ);
                    Handles.DrawLine(lineStart, lineEnd, borderThickness);
                }
                
                // Left edge
                if (!paintedTiles.Contains(new Vector2Int(tile.x - 1, tile.y)))
                {
                    Vector3 lineStart = new Vector3(tileX, y + 0.06f, tileZ);
                    Vector3 lineEnd = new Vector3(tileX, y + 0.06f, tileZ + TILE_SIZE);
                    Handles.DrawLine(lineStart, lineEnd, borderThickness);
                }
            }
        }
        
        private static void PaintTerrain(Vector2Int centerTile, int width, int height)
        {
            if (string.IsNullOrEmpty(selectedBrushTerrain) || selectedTerrain == null)
                return;
            
            Undo.RecordObject(selectedTerrain, "Paint Terrain");
            
            int halfSize = (brushSize - 1) / 2;
            bool modified = false;
            
            for (int dx = -halfSize; dx <= halfSize; dx++)
            {
                for (int dz = -halfSize; dz <= halfSize; dz++)
                {
                    int tileX = centerTile.x + dx;
                    int tileZ = centerTile.y + dz;
                    
                    if (tileX >= 0 && tileX < width && tileZ >= 0 && tileZ < height)
                    {
                        int index = tileZ * width + tileX;
                        if (index < selectedTerrain.m_Terrains.Length)
                        {
                            selectedTerrain.m_Terrains[index] = selectedBrushTerrain;
                            modified = true;
                        }
                    }
                }
            }
            
            if (modified)
            {
                EditorUtility.SetDirty(selectedTerrain);
                // Clear island cache when terrain is modified
                if (islandCache.ContainsKey(selectedTerrain))
                {
                    islandCache.Remove(selectedTerrain);
                }
                SceneView.RepaintAll();
            }
        }
        
        private static void PickTerrain(Vector2Int tile, int width)
        {
            if (selectedTerrain == null)
                return;
            
            int index = tile.y * width + tile.x;
            if (index < selectedTerrain.m_Terrains.Length)
            {
                selectedBrushTerrain = selectedTerrain.m_Terrains[index];
                Debug.Log($"Picked terrain: {selectedBrushTerrain}");
                
                // Force UI refresh to show the newly selected terrain
                if (instance != null)
                {
                    instance.Repaint();
                }
            }
        }
        
        
        private static HashSet<Vector2Int> FindConnectedRegion(MapTerrain terrain, Vector2Int startTile, int width, int height)
        {
            HashSet<Vector2Int> region = new HashSet<Vector2Int>();
            int startIndex = startTile.y * width + startTile.x;
            
            if (startIndex >= terrain.m_Terrains.Length)
                return region;
                
            string targetTerrain = terrain.m_Terrains[startIndex];
            if (string.IsNullOrEmpty(targetTerrain))
                return region;
            
            Queue<Vector2Int> toVisit = new Queue<Vector2Int>();
            HashSet<Vector2Int> visited = new HashSet<Vector2Int>();
            
            toVisit.Enqueue(startTile);
            visited.Add(startTile);
            
            while (toVisit.Count > 0)
            {
                Vector2Int current = toVisit.Dequeue();
                region.Add(current);
                
                foreach (var dir in Directions4)
                {
                    Vector2Int neighbor = current + dir;
                    
                    if (neighbor.x >= 0 && neighbor.x < width &&
                        neighbor.y >= 0 && neighbor.y < height &&
                        !visited.Contains(neighbor))
                    {
                        int neighborIndex = neighbor.y * width + neighbor.x;
                        if (neighborIndex < terrain.m_Terrains.Length &&
                            terrain.m_Terrains[neighborIndex] == targetTerrain)
                        {
                            visited.Add(neighbor);
                            toVisit.Enqueue(neighbor);
                        }
                    }
                }
            }
            
            return region;
        }
        
        private static void DrawRegionHighlight(HashSet<Vector2Int> region, float startX, float startZ, float y, string terrainId)
        {
            if (region.Count == 0) return;
            if (hoverHighlightOpacity <= 0.001f) return; // Skip drawing if fully transparent
            
            // Create a subtle white overlay with animated opacity
            Color highlightColor = new Color(1f, 1f, 1f, hoverHighlightOpacity);
            
            // Draw highlight overlay for each tile in the region
            foreach (var tile in region)
            {
                float tileX = startX + tile.x * TILE_SIZE;
                float tileZ = startZ + tile.y * TILE_SIZE;
                
                Vector3[] verts = new Vector3[]
                {
                    new Vector3(tileX, y + 0.02f, tileZ),
                    new Vector3(tileX + TILE_SIZE, y + 0.02f, tileZ),
                    new Vector3(tileX + TILE_SIZE, y + 0.02f, tileZ + TILE_SIZE),
                    new Vector3(tileX, y + 0.02f, tileZ + TILE_SIZE)
                };
                
                Handles.DrawSolidRectangleWithOutline(verts, highlightColor, Color.clear);
            }
            
            // Draw a subtle border around the entire region with animated opacity
            HashSet<(Vector2Int, Vector2Int)> edges = new HashSet<(Vector2Int, Vector2Int)>();
            
            foreach (var tile in region)
            {
                // Check each edge
                Vector2Int[] neighbors = new Vector2Int[]
                {
                    tile + new Vector2Int(0, 1),   // top
                    tile + new Vector2Int(1, 0),   // right
                    tile + new Vector2Int(0, -1),  // bottom
                    tile + new Vector2Int(-1, 0)   // left
                };
                
                // Top edge
                if (!region.Contains(neighbors[0]))
                {
                    edges.Add((new Vector2Int(tile.x, tile.y + 1), new Vector2Int(tile.x + 1, tile.y + 1)));
                }
                // Right edge
                if (!region.Contains(neighbors[1]))
                {
                    edges.Add((new Vector2Int(tile.x + 1, tile.y), new Vector2Int(tile.x + 1, tile.y + 1)));
                }
                // Bottom edge
                if (!region.Contains(neighbors[2]))
                {
                    edges.Add((new Vector2Int(tile.x, tile.y), new Vector2Int(tile.x + 1, tile.y)));
                }
                // Left edge
                if (!region.Contains(neighbors[3]))
                {
                    edges.Add((new Vector2Int(tile.x, tile.y), new Vector2Int(tile.x, tile.y + 1)));
                }
            }
            
            // Draw the border edges with animated opacity
            float borderOpacity = Mathf.Min(0.3f, hoverHighlightOpacity * 4f); // Border fades in slightly faster
            Handles.color = new Color(1f, 1f, 1f, borderOpacity);
            foreach (var edge in edges)
            {
                Vector3 start = new Vector3(
                    startX + edge.Item1.x * TILE_SIZE,
                    y + 0.03f,
                    startZ + edge.Item1.y * TILE_SIZE
                );
                Vector3 end = new Vector3(
                    startX + edge.Item2.x * TILE_SIZE,
                    y + 0.03f,
                    startZ + edge.Item2.y * TILE_SIZE
                );
                
                Handles.DrawLine(start, end, 3f);
            }
        }
        
        private static void DrawLabelWithColoredIcon(Vector3 position, string text, string terrainId, GUIStyle textStyle, Color textColor, bool drawShadow)
        {
            // Convert world position to GUI position
            Vector2 guiPos = HandleUtility.WorldToGUIPoint(position);
            
            // Calculate text dimensions
            GUIContent content = new GUIContent(text);
            Vector2 textSize = textStyle.CalcSize(content);
            
            // Add padding for the colored icon
            float iconSize = 8f;
            float iconPadding = 3f;
            float totalWidth = textSize.x + iconSize + iconPadding * 2;
            
            // Position for the whole label (centered)
            Rect labelRect = new Rect(guiPos.x - totalWidth / 2, guiPos.y - textSize.y / 2, totalWidth, textSize.y);
            
            // Always draw an outline for better readability
            // Draw multiple outline passes for stronger effect
            GUIStyle outlineStyle = new GUIStyle(textStyle);
            Color outlineColor = (textColor == Color.black) ? Color.white : Color.black;
            outlineStyle.normal.textColor = new Color(outlineColor.r, outlineColor.g, outlineColor.b, 0.8f);
            
            // Draw outline in 8 directions for better coverage
            Vector2[] outlineOffsets = new Vector2[]
            {
                new Vector2(-1, -1), new Vector2(0, -1), new Vector2(1, -1),
                new Vector2(-1, 0),                      new Vector2(1, 0),
                new Vector2(-1, 1),  new Vector2(0, 1),  new Vector2(1, 1)
            };
            
            foreach (var offset in outlineOffsets)
            {
                Rect outlineTextRect = new Rect(
                    labelRect.x + iconSize + iconPadding * 2 + offset.x, 
                    labelRect.y + offset.y, 
                    textSize.x, 
                    labelRect.height
                );
                GUI.Label(outlineTextRect, text, outlineStyle);
                
                // Draw outline for icon too
                if (terrainDatabase != null)
                {
                    Rect outlineIconRect = new Rect(
                        labelRect.x + iconPadding + offset.x, 
                        labelRect.y + (labelRect.height - iconSize) / 2 + offset.y, 
                        iconSize, 
                        iconSize
                    );
                    EditorGUI.DrawRect(outlineIconRect, new Color(outlineColor.r, outlineColor.g, outlineColor.b, 0.3f));
                }
            }
            
            // Draw colored icon
            if (terrainDatabase != null)
            {
                Color terrainColor = terrainDatabase.GetTerrainColor(terrainId, Color.gray);
                Rect iconRect = new Rect(labelRect.x + iconPadding, 
                    labelRect.y + (labelRect.height - iconSize) / 2, iconSize, iconSize);
                
                // Draw icon background
                EditorGUI.DrawRect(iconRect, terrainColor);
                
                // Draw icon border for clarity
                Color borderColor = (textColor == Color.black) ? Color.black : Color.white;
                Handles.DrawBezier(
                    new Vector3(iconRect.x, iconRect.y, 0),
                    new Vector3(iconRect.x + iconSize, iconRect.y, 0),
                    new Vector3(iconRect.x, iconRect.y, 0),
                    new Vector3(iconRect.x + iconSize, iconRect.y, 0),
                    borderColor, null, 1f
                );
            }
            
            // Draw the text on top
            textStyle.normal.textColor = textColor;
            Rect textRect = new Rect(labelRect.x + iconSize + iconPadding * 2, labelRect.y, 
                textSize.x, labelRect.height);
            GUI.Label(textRect, text, textStyle);
        }
        
        private static void DrawIslandBorders(TerrainIsland island, int mapWidth, int mapHeight, float startX, float startZ, float y, Color borderColor)
        {
            Handles.color = borderColor;
            float borderThickness = 3f;
            
            // Create a set for quick lookup
            HashSet<Vector2Int> islandTiles = new HashSet<Vector2Int>(island.tiles);
            
            // Check each tile in the island for border edges
            foreach (var tile in island.tiles)
            {
                float tileX = startX + tile.x * TILE_SIZE;
                float tileZ = startZ + tile.y * TILE_SIZE;
                
                // Check all 4 edges
                // Top edge (z+)
                if (tile.y >= mapHeight - 1 || !islandTiles.Contains(new Vector2Int(tile.x, tile.y + 1)))
                {
                    Vector3 lineStart = new Vector3(tileX, y + 0.02f, tileZ + TILE_SIZE);
                    Vector3 lineEnd = new Vector3(tileX + TILE_SIZE, y + 0.02f, tileZ + TILE_SIZE);
                    Handles.DrawLine(lineStart, lineEnd, borderThickness);
                }
                
                // Right edge (x+)
                if (tile.x >= mapWidth - 1 || !islandTiles.Contains(new Vector2Int(tile.x + 1, tile.y)))
                {
                    Vector3 lineStart = new Vector3(tileX + TILE_SIZE, y + 0.02f, tileZ);
                    Vector3 lineEnd = new Vector3(tileX + TILE_SIZE, y + 0.02f, tileZ + TILE_SIZE);
                    Handles.DrawLine(lineStart, lineEnd, borderThickness);
                }
                
                // Bottom edge (z-)
                if (tile.y <= 0 || !islandTiles.Contains(new Vector2Int(tile.x, tile.y - 1)))
                {
                    Vector3 lineStart = new Vector3(tileX, y + 0.02f, tileZ);
                    Vector3 lineEnd = new Vector3(tileX + TILE_SIZE, y + 0.02f, tileZ);
                    Handles.DrawLine(lineStart, lineEnd, borderThickness);
                }
                
                // Left edge (x-)
                if (tile.x <= 0 || !islandTiles.Contains(new Vector2Int(tile.x - 1, tile.y)))
                {
                    Vector3 lineStart = new Vector3(tileX, y + 0.02f, tileZ);
                    Vector3 lineEnd = new Vector3(tileX, y + 0.02f, tileZ + TILE_SIZE);
                    Handles.DrawLine(lineStart, lineEnd, borderThickness);
                }
            }
        }
        
        private static List<TerrainIsland> GetOrCreateIslands(MapTerrain terrain, float cameraDistance)
        {
            // Check if terrain changed or we need to rebuild
            if (terrain != lastCachedTerrain || !islandCache.ContainsKey(terrain))
            {
                // Terrain changed, rebuild islands
                islandCache[terrain] = FindTerrainIslands(terrain, cameraDistance);
                lastCachedTerrain = terrain;
                lastIslandCameraDistance = cameraDistance;
            }
            else
            {
                var islands = islandCache[terrain];
                
                // Only recalculate positions when camera has stopped moving
                if (!cameraIsMoving)
                {
                    // Check if we need to update based on significant distance change
                    float distanceChange = Mathf.Abs(cameraDistance - lastIslandCameraDistance);
                    if (distanceChange > 5f) // Only update if zoom changed significantly
                    {
                        foreach (var island in islands)
                        {
                            island.CalculateLabelPositions(cameraDistance);
                        }
                        lastIslandCameraDistance = cameraDistance;
                    }
                }
            }
            
            return islandCache[terrain];
        }
        
        private static List<TerrainIsland> FindTerrainIslands(MapTerrain terrain, float cameraDistance)
        {
            if (terrain == null || terrain.m_Terrains == null)
                return new List<TerrainIsland>();
            
            int width = terrain.m_Width;
            int height = terrain.m_Height;
            bool[,] visited = new bool[width, height];
            List<TerrainIsland> islands = new List<TerrainIsland>();
            
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (!visited[x, y])
                    {
                        int index = y * width + x;
                        if (index >= terrain.m_Terrains.Length)
                            continue;
                        
                        string terrainId = terrain.m_Terrains[index];
                        if (string.IsNullOrEmpty(terrainId))
                        {
                            visited[x, y] = true;
                            continue;
                        }
                        
                        // Start flood fill for this island
                        TerrainIsland island = new TerrainIsland(terrainId);
                        Queue<Vector2Int> queue = new Queue<Vector2Int>();
                        queue.Enqueue(new Vector2Int(x, y));
                        visited[x, y] = true;
                        
                        while (queue.Count > 0)
                        {
                            Vector2Int current = queue.Dequeue();
                            island.tiles.Add(current);
                            
                            // Check 4 neighbors
                            foreach (var dir in Directions4)
                            {
                                int nx = current.x + dir.x;
                                int ny = current.y + dir.y;
                                
                                // Check bounds
                                if (nx >= 0 && nx < width && ny >= 0 && ny < height && !visited[nx, ny])
                                {
                                    int neighborIndex = ny * width + nx;
                                    if (neighborIndex < terrain.m_Terrains.Length)
                                    {
                                        string neighborTerrain = terrain.m_Terrains[neighborIndex];
                                        
                                        // If same terrain type, add to queue
                                        if (neighborTerrain == terrainId)
                                        {
                                            visited[nx, ny] = true;
                                            queue.Enqueue(new Vector2Int(nx, ny));
                                        }
                                    }
                                }
                            }
                        }
                        
                        island.CalculateLabelPositions(cameraDistance);
                        islands.Add(island);
                    }
                }
            }
            
            return islands;
        }
        
        private static string GetTerrainDisplayText(string terrainId)
        {
            string tid = terrainId.Replace("TID_", "");
            string name = null;
            
            if (terrainDatabase != null)
            {
                var terrain = terrainDatabase.GetTerrainType(terrainId);
                if (terrain != null && !string.IsNullOrEmpty(terrain.name))
                {
                    name = terrain.name;
                    if (name.StartsWith("MTID_"))
                        name = name.Substring(5);
                }
            }
            
            switch (textDisplayMode)
            {
                case TextDisplayMode.ShowTID:
                    return tid;
                    
                case TextDisplayMode.ShowName:
                    return name ?? tid; // Fallback to TID if no name
                    
                case TextDisplayMode.ShowBoth:
                    if (name != null && name != terrainId)
                    {
                        return tid + "\n" + name;
                    }
                    return tid;
                    
                default:
                    return tid;
            }
        }
        
        private static void DrawTerrainButton(TerrainType terrain, bool isUsed)
        {
            EditorGUILayout.BeginHorizontal();
            
            // Draw color swatch
            Rect colorRect = GUILayoutUtility.GetRect(20, 20, GUILayout.Width(20));
            EditorGUI.DrawRect(colorRect, terrain.color);
            EditorGUI.DrawRect(colorRect, new Color(0, 0, 0, 0.2f)); // Border
            
            // Star indicator for used terrains
            if (isUsed)
            {
                GUIStyle starStyle = new GUIStyle(EditorStyles.label);
                starStyle.normal.textColor = Color.yellow;
                starStyle.fontStyle = FontStyle.Bold;
                GUILayout.Label("★", starStyle, GUILayout.Width(20));
            }
            else
            {
                GUILayout.Label("", GUILayout.Width(20));
            }
            
            // Terrain button
            GUI.backgroundColor = terrain.tid == selectedBrushTerrain ? Color.cyan : Color.white;
            
            string displayName = terrain.tid.Replace("TID_", "");
            if (!string.IsNullOrEmpty(terrain.name) && terrain.name != terrain.tid)
            {
                displayName += $" ({terrain.name})";
            }
            
            if (GUILayout.Button(displayName, EditorStyles.toolbarButton))
            {
                selectedBrushTerrain = terrain.tid;
                SceneView.RepaintAll();
            }
            
            GUI.backgroundColor = Color.white;
            
            EditorGUILayout.EndHorizontal();
        }
    }
}
