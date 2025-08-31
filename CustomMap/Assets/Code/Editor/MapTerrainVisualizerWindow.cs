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
        
        // Brush painting variables
        private static bool paintMode = false;
        private static string selectedBrushTerrain = "";
        private static int brushSize = 1;
        private static Vector2Int hoveredTile = new Vector2Int(-1, -1);
        private static bool isMouseOverGrid = false;
        
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
            LoadSettings();
            RefreshTerrainList();
            LoadTerrainDatabase();
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
            // Calculate luminance using the relative luminance formula
            float luminance = 0.299f * backgroundColor.r + 0.587f * backgroundColor.g + 0.114f * backgroundColor.b;
            
            // If the background is dark, use white; if light, use black
            if (luminance > 0.5f)
            {
                return Color.black;
            }
            else
            {
                return Color.white;
            }
        }
        
        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
        }
        
        private void OnDestroy()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
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
            EditorGUILayout.LabelField("World Offset", EditorStyles.miniBoldLabel);
            worldOffset = EditorGUILayout.Vector3Field("Position Offset", worldOffset);
            
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
        
        private static void OnSceneGUI(SceneView sceneView)
        {
            if (!visualizationEnabled || selectedTerrain == null || selectedTerrain.m_Terrains == null)
                return;
            
            int width = selectedTerrain.m_Width;
            int height = selectedTerrain.m_Height;
            float startX = selectedTerrain.m_X + worldOffset.x;
            float startZ = selectedTerrain.m_Z + worldOffset.z;
            float y = worldOffset.y;
            
            // Handle mouse input for painting
            if (paintMode)
            {
                HandlePaintingInput(width, height, startX, startZ, y);
            }
            
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
            
            // Draw text labels if not in color-only mode
            if (displayMode != DisplayMode.ColorOnly)
            {
                GUIStyle style = new GUIStyle();
                style.fontSize = Mathf.RoundToInt(12 * textSize);
                style.alignment = TextAnchor.MiddleCenter;
                style.fontStyle = FontStyle.Bold; // Make text bolder for better visibility
                
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
                        
                        // Determine text color based on background
                        Color labelColor = textColor;
                        if (autoContrastText && displayMode == DisplayMode.Both && terrainDatabase != null)
                        {
                            Color tileColor = terrainDatabase.GetTerrainColor(terrainId, Color.gray);
                            labelColor = GetContrastColor(tileColor);
                        }
                        else if (!autoContrastText)
                        {
                            labelColor = textColor;
                        }
                        
                        style.normal.textColor = labelColor;
                        
                        float centerX = startX + col * TILE_SIZE + TILE_SIZE * 0.5f;
                        float centerZ = startZ + row * TILE_SIZE + TILE_SIZE * 0.5f;
                        Vector3 position = new Vector3(centerX, y, centerZ);
                        
                        // Get display text based on mode
                        string displayText = GetTerrainDisplayText(terrainId);
                        
                        
                        // Draw text with outline for better visibility
                        if (autoContrastText && displayMode == DisplayMode.Both)
                        {
                            // Draw shadow/outline for better readability
                            Color shadowColor = labelColor == Color.white ? new Color(0, 0, 0, 0.5f) : new Color(1, 1, 1, 0.5f);
                            GUIStyle shadowStyle = new GUIStyle(style);
                            shadowStyle.normal.textColor = shadowColor;
                            
                            float shadowOffset = 0.05f;
                            Handles.Label(position + new Vector3(shadowOffset, 0, shadowOffset), displayText, shadowStyle);
                        }
                        
                        Handles.Label(position, displayText, style);
                    }
                }
            }
            
            // Draw brush preview when in paint mode
            if (paintMode && isMouseOverGrid)
            {
                DrawBrushPreview(hoveredTile, width, height, startX, startZ, y);
            }
            
            // Draw paint mode overlay in lower left corner
            if (paintMode)
            {
                DrawPaintModeOverlay(sceneView);
            }
        }
        
        private static void HandlePaintingInput(int width, int height, float startX, float startZ, float y)
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
                
                // Block scene navigation only for left mouse button
                if (currentEvent.type == EventType.Layout && currentEvent.button == 0)
                {
                    HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
                }
            }
            else
            {
                isMouseOverGrid = false;
            }
            
            SceneView.RepaintAll();
        }
        
        private static void DrawPaintModeOverlay(SceneView sceneView)
        {
            Handles.BeginGUI();
            
            // Check if sampling mode
            bool isSampling = Event.current.control;
            
            // Calculate position in lower left corner
            float padding = 10f;
            float overlayWidth = 250f;
            float overlayHeight = 70f;
            Rect overlayRect = new Rect(padding, sceneView.position.height - overlayHeight - padding - 30, overlayWidth, overlayHeight);
            
            // Draw solid background
            Color bgColor = new Color(0.2f, 0.2f, 0.2f, 0.95f);
            EditorGUI.DrawRect(overlayRect, bgColor);
            
            // Draw border
            Color borderColor = new Color(0.5f, 0.5f, 0.5f, 1f);
            GUI.Box(overlayRect, GUIContent.none, EditorStyles.helpBox);
            
            GUILayout.BeginArea(new Rect(overlayRect.x + 5, overlayRect.y + 5, overlayRect.width - 10, overlayRect.height - 10));
            GUILayout.BeginVertical();
            
            // Title - changes based on mode
            GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel);
            titleStyle.normal.textColor = isSampling ? Color.cyan : Color.white;
            GUILayout.Label(isSampling ? "Sampling Mode" : "Paint Mode", titleStyle);
            
            // Selected terrain info
            GUILayout.BeginHorizontal();
            GUIStyle labelStyle = new GUIStyle(EditorStyles.label);
            labelStyle.normal.textColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            GUILayout.Label(isSampling ? "Hover to pick:" : "Painting with:", labelStyle, GUILayout.Width(85));
            
            if (!string.IsNullOrEmpty(selectedBrushTerrain))
            {
                // Draw color chip
                if (terrainDatabase != null)
                {
                    Color terrainColor = terrainDatabase.GetTerrainColor(selectedBrushTerrain, Color.gray);
                    Rect colorRect = GUILayoutUtility.GetRect(16, 16, GUILayout.Width(16), GUILayout.Height(16));
                    EditorGUI.DrawRect(colorRect, terrainColor);
                    EditorGUI.DrawRect(colorRect, new Color(0, 0, 0, 0.5f)); // Border
                    
                    GUILayout.Space(4);
                }
                
                // Show terrain info based on text display mode
                string displayText = GetTerrainDisplayText(selectedBrushTerrain);
                // For overlay, show on single line if it's both mode
                displayText = displayText.Replace("\n", " / ");
                
                GUIStyle terrainStyle = new GUIStyle(EditorStyles.label);
                terrainStyle.fontStyle = FontStyle.Bold;
                terrainStyle.normal.textColor = Color.white;
                GUILayout.Label(displayText, terrainStyle);
            }
            else
            {
                GUIStyle noneStyle = new GUIStyle(EditorStyles.miniLabel);
                noneStyle.normal.textColor = new Color(0.6f, 0.6f, 0.6f, 1f);
                GUILayout.Label("None selected", noneStyle);
            }
            GUILayout.EndHorizontal();
            
            // Brush size info
            GUIStyle brushStyle = new GUIStyle(EditorStyles.miniLabel);
            brushStyle.normal.textColor = new Color(0.7f, 0.7f, 0.7f, 1f);
            GUILayout.Label($"Brush: {brushSize}x{brushSize}", brushStyle);
            
            GUILayout.EndVertical();
            GUILayout.EndArea();
            
            Handles.EndGUI();
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
                // Normal paint preview
                Color previewColor = new Color(1f, 1f, 0f, 0.3f); // Yellow preview
                
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
                            
                            Handles.DrawSolidRectangleWithOutline(verts, previewColor, Color.yellow);
                        }
                    }
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
                SceneView.RepaintAll();
            }
        }
        
        private static void EraseTerrain(Vector2Int centerTile, int width, int height)
        {
            // For now, erase sets to first terrain in database or empty
            string eraseTerrain = "";
            if (terrainDatabase != null)
            {
                var allTypes = terrainDatabase.GetAllTerrainTypes();
                if (allTypes.Count > 0)
                {
                    eraseTerrain = allTypes[0].tid; // Use first terrain as "eraser"
                }
            }
            
            string temp = selectedBrushTerrain;
            selectedBrushTerrain = eraseTerrain;
            PaintTerrain(centerTile, width, height);
            selectedBrushTerrain = temp;
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