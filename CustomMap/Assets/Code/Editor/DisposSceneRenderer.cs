using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace Editor
{
    public class DisposSceneRenderer
    {
        private Dictionary<DisposEntry, GameObject> unitObjects = new Dictionary<DisposEntry, GameObject>();
        private GameObject rootContainer;
        private DisposDocument currentDocument;
        private Bridge.MapTerrain currentTerrain;
        private const float TILE_SIZE = 5.0f; // Match TerrainPaintToolWindow
        private bool showGrid = true;
        private bool showLabels = true;
        private bool showDirections = true;
        private bool showIcons = true;
        private DisposEntry selectedEntry;
        private Vector3 worldOffset = Vector3.zero;
        
        public DisposEntry SelectedEntry
        {
            get => selectedEntry;
            set
            {
                if (selectedEntry != value)
                {
                    selectedEntry = value;
                    UpdateSelection();
                }
            }
        }
        
        public void Initialize()
        {
            if (rootContainer == null)
            {
                rootContainer = new GameObject("DisposTool_Units");
                rootContainer.hideFlags = HideFlags.HideAndDontSave;
            }
        }
        
        public void Cleanup()
        {
            ClearAllUnits();
            if (rootContainer != null)
            {
                GameObject.DestroyImmediate(rootContainer);
                rootContainer = null;
            }
        }
        
        public void RenderDocument(DisposDocument document, Bridge.MapTerrain terrain = null)
        {
            if (document == null)
            {
                ClearAllUnits();
                return;
            }
            
            currentDocument = document;
            currentTerrain = terrain;
            RefreshUnits();
        }
        
        private void RefreshUnits()
        {
            ClearAllUnits();
            
            if (currentDocument == null)
                return;
            
            foreach (var group in currentDocument.Groups)
            {
                if (!group.IsVisible)
                    continue;
                
                foreach (var entry in group.Entries)
                {
                    if (!entry.IsGroupHeader)
                    {
                        CreateUnitObject(entry);
                    }
                }
            }
        }
        
        private void CreateUnitObject(DisposEntry entry)
        {
            if (entry == null || entry.IsGroupHeader)
                return;
            
            GameObject unitObj = GameObject.CreatePrimitive(PrimitiveType.Quad);
            unitObj.name = $"Unit_{entry.Pid}";
            unitObj.transform.parent = rootContainer.transform;
            unitObj.hideFlags = HideFlags.HideAndDontSave;
            
            // Calculate world position based on terrain origin and tile size
            float startX = currentTerrain != null ? currentTerrain.m_X : 0;
            float startZ = currentTerrain != null ? currentTerrain.m_Z : 0;
            float worldX = startX + entry.DisposX * TILE_SIZE + TILE_SIZE * 0.5f;
            float worldZ = startZ + entry.DisposY * TILE_SIZE + TILE_SIZE * 0.5f;
            float worldY = worldOffset.y + 0.1f; // Slightly above terrain
            
            Vector3 position = new Vector3(worldX, worldY, worldZ);
            unitObj.transform.position = position;
            unitObj.transform.rotation = Quaternion.Euler(90, 0, 0);
            unitObj.transform.localScale = Vector3.one * TILE_SIZE * 0.7f; // Slightly smaller than tile
            
            MeshRenderer renderer = unitObj.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                Material mat = new Material(Shader.Find("Sprites/Default"));
                mat.hideFlags = HideFlags.HideAndDontSave;
                
                Texture2D icon = DisposDataLoader.Instance.GetUnitIcon(entry);
                if (icon != null && showIcons)
                {
                    mat.mainTexture = icon;
                    mat.color = Color.white;
                }
                else
                {
                    mat.color = DisposDataLoader.Instance.GetForceColor(entry.Force);
                }
                
                renderer.material = mat;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            
            unitObjects[entry] = unitObj;
        }
        
        private void ClearAllUnits()
        {
            foreach (var kvp in unitObjects)
            {
                if (kvp.Value != null)
                {
                    GameObject.DestroyImmediate(kvp.Value);
                }
            }
            unitObjects.Clear();
        }
        
        public void DrawSceneGUI()
        {
            if (currentDocument == null)
                return;
            
            if (showGrid)
                DrawGrid();
            
            foreach (var group in currentDocument.Groups)
            {
                if (!group.IsVisible)
                    continue;
                
                foreach (var entry in group.Entries)
                {
                    if (!entry.IsGroupHeader)
                    {
                        DrawUnitGUI(entry);
                    }
                }
            }
        }
        
        private void DrawGrid()
        {
            if (currentTerrain == null)
                return;
                
            int width = currentTerrain.m_Width;
            int height = currentTerrain.m_Height;
            float startX = currentTerrain.m_X + worldOffset.x;
            float startZ = currentTerrain.m_Z + worldOffset.z;
            float y = worldOffset.y;
            
            Color gridColor = new Color(1f, 1f, 1f, 0.3f);
            Handles.color = gridColor;
            
            // Draw horizontal lines
            for (int row = 0; row <= height; row++)
            {
                Vector3 start = new Vector3(startX, y + 0.01f, startZ + row * TILE_SIZE);
                Vector3 end = new Vector3(startX + width * TILE_SIZE, y + 0.01f, startZ + row * TILE_SIZE);
                Handles.DrawLine(start, end, 1f);
            }
            
            // Draw vertical lines
            for (int col = 0; col <= width; col++)
            {
                Vector3 start = new Vector3(startX + col * TILE_SIZE, y + 0.01f, startZ);
                Vector3 end = new Vector3(startX + col * TILE_SIZE, y + 0.01f, startZ + height * TILE_SIZE);
                Handles.DrawLine(start, end, 1f);
            }
        }
        
        private void DrawUnitGUI(DisposEntry entry)
        {
            float startX = currentTerrain != null ? currentTerrain.m_X : 0;
            float startZ = currentTerrain != null ? currentTerrain.m_Z : 0;
            float worldX = startX + entry.DisposX * TILE_SIZE + TILE_SIZE * 0.5f;
            float worldZ = startZ + entry.DisposY * TILE_SIZE + TILE_SIZE * 0.5f;
            Vector3 worldPos = new Vector3(worldX, worldOffset.y, worldZ);
            
            if (showDirections && entry.Direction >= 0 && entry.Direction <= 8)
            {
                DrawDirectionArrow(worldPos, entry.Direction);
            }
            
            if (showLabels)
            {
                Vector3 labelPos = worldPos + Vector3.up * 0.5f;
                string label = DisposDataLoader.Instance.GetUnitDisplayName(entry);
                
                GUIStyle style = new GUIStyle(GUI.skin.label);
                style.alignment = TextAnchor.MiddleCenter;
                style.normal.textColor = Color.white;
                style.fontSize = 10;
                
                Color bgColor = DisposDataLoader.Instance.GetForceColor(entry.Force);
                bgColor.a = 0.8f;
                
                Handles.BeginGUI();
                Vector3 screenPos = HandleUtility.WorldToGUIPoint(labelPos);
                
                Vector2 labelSize = style.CalcSize(new GUIContent(label));
                Rect bgRect = new Rect(screenPos.x - labelSize.x/2 - 2, 
                                       screenPos.y - labelSize.y/2 - 1, 
                                       labelSize.x + 4, 
                                       labelSize.y + 2);
                
                EditorGUI.DrawRect(bgRect, bgColor);
                
                Rect labelRect = new Rect(screenPos.x - labelSize.x/2, 
                                          screenPos.y - labelSize.y/2, 
                                          labelSize.x, 
                                          labelSize.y);
                GUI.Label(labelRect, label, style);
                
                if (entry == selectedEntry)
                {
                    Color borderColor = Color.yellow;
                    borderColor.a = 0.8f;
                    DrawBorder(bgRect, borderColor, 2);
                }
                
                Handles.EndGUI();
            }
        }
        
        private void DrawDirectionArrow(Vector3 position, int direction)
        {
            float arrowLength = TILE_SIZE * 0.3f;
            Vector3 arrowDir = GetDirectionVector(direction);
            Vector3 arrowStart = position + Vector3.up * 0.1f;
            Vector3 arrowEnd = arrowStart + arrowDir * arrowLength;
            
            Handles.color = Color.yellow;
            Handles.DrawLine(arrowStart, arrowEnd, 2f);
            
            Vector3 arrowRight = Vector3.Cross(arrowDir, Vector3.up) * 0.1f;
            Vector3 arrowBack = -arrowDir * 0.1f;
            Vector3[] arrowHead = new Vector3[]
            {
                arrowEnd,
                arrowEnd + arrowBack + arrowRight,
                arrowEnd + arrowBack - arrowRight
            };
            Handles.DrawAAConvexPolygon(arrowHead);
        }
        
        private Vector3 GetDirectionVector(int direction)
        {
            switch (direction)
            {
                case 0: return Vector3.forward;
                case 1: return new Vector3(1, 0, 1).normalized;
                case 2: return Vector3.right;
                case 3: return new Vector3(1, 0, -1).normalized;
                case 4: return Vector3.back;
                case 5: return new Vector3(-1, 0, -1).normalized;
                case 6: return Vector3.left;
                case 7: return new Vector3(-1, 0, 1).normalized;
                case 8: return Vector3.forward;
                default: return Vector3.forward;
            }
        }
        
        private void DrawBorder(Rect rect, Color color, float thickness)
        {
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, thickness), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, thickness, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
        }
        
        public DisposEntry GetEntryAtPosition(Vector3 worldPos)
        {
            if (currentTerrain == null)
                return null;
                
            float startX = currentTerrain.m_X + worldOffset.x;
            float startZ = currentTerrain.m_Z + worldOffset.z;
            
            int gridX = Mathf.FloorToInt((worldPos.x - startX) / TILE_SIZE);
            int gridY = Mathf.FloorToInt((worldPos.z - startZ) / TILE_SIZE);
            
            foreach (var group in currentDocument.Groups)
            {
                if (!group.IsVisible)
                    continue;
                
                foreach (var entry in group.Entries)
                {
                    if (!entry.IsGroupHeader && 
                        entry.DisposX == gridX && 
                        entry.DisposY == gridY)
                    {
                        return entry;
                    }
                }
            }
            
            return null;
        }
        
        public void MoveEntry(DisposEntry entry, Vector3 worldPos)
        {
            if (entry == null || entry.IsGroupHeader || currentTerrain == null)
                return;
            
            float startX = currentTerrain.m_X + worldOffset.x;
            float startZ = currentTerrain.m_Z + worldOffset.z;
            
            int newX = Mathf.FloorToInt((worldPos.x - startX) / TILE_SIZE);
            int newY = Mathf.FloorToInt((worldPos.z - startZ) / TILE_SIZE);
            
            entry.DisposX = newX;
            entry.DisposY = newY;
            
            if (unitObjects.TryGetValue(entry, out GameObject unitObj))
            {
                float worldX = startX + newX * TILE_SIZE + TILE_SIZE * 0.5f;
                float worldZ = startZ + newY * TILE_SIZE + TILE_SIZE * 0.5f;
                unitObj.transform.position = new Vector3(worldX, worldOffset.y + 0.1f, worldZ);
            }
        }
        
        private void UpdateSelection()
        {
            SceneView.RepaintAll();
        }
        
        public void SetShowGrid(bool show)
        {
            showGrid = show;
            SceneView.RepaintAll();
        }
        
        public void SetShowLabels(bool show)
        {
            showLabels = show;
            SceneView.RepaintAll();
        }
        
        public void SetShowDirections(bool show)
        {
            showDirections = show;
            SceneView.RepaintAll();
        }
        
        public void SetShowIcons(bool show)
        {
            showIcons = show;
            RefreshUnits();
            SceneView.RepaintAll();
        }
        
        public void SetWorldOffset(Vector3 offset)
        {
            worldOffset = offset;
            RefreshUnits();
            SceneView.RepaintAll();
        }
    }
}