using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace Editor
{
    public class DisposSceneRenderer
    {
        // Immediate-mode renderer has no persistent GameObjects
        private DisposDocument currentDocument;
        private Bridge.MapTerrain currentTerrain;
        private const float TILE_SIZE = 5.0f; // Match TerrainPaintToolWindow
        private bool showGrid = true;
        private bool showLabels = true;
        private bool showDirections = true;
        private bool showIcons = true;
        private DisposEntry selectedEntry;
        private Vector3 worldOffset = Vector3.zero;
        private const float LABEL_SCREEN_OFFSET_Y = 22f; // pixels above sprite
        private const float ICON_TILE_SCALE = 0.9f;      // fraction of tile used for icon size
        
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
        
        public void Initialize() { }
        
        public void Cleanup() { }
        
        public void RenderDocument(DisposDocument document, Bridge.MapTerrain terrain = null)
        {
            if (document == null) return;
            
            currentDocument = document;
            if (terrain != null)
                currentTerrain = terrain;
            RefreshUnits();
        }
        
        private void RefreshUnits()
        {
            Debug.Log($"RefreshUnits called. Document: {currentDocument != null}, Groups: {currentDocument?.Groups?.Count ?? 0}");
            
            if (currentDocument == null)
            {
                Debug.LogWarning("No document to refresh");
                return;
            }
            
            // Immediate-mode: nothing to instantiate
            int entries = 0;
            foreach (var group in currentDocument.Groups)
            {
                if (!group.IsVisible) continue;
                foreach (var entry in group.Entries)
                    if (!entry.IsGroupHeader) entries++;
            }
            Debug.Log($"RefreshUnits immediate-mode. Entries visible: {entries}");
        }
        
        private GameObject CreateGroupObject(DisposGroup group) { return null; }
        private void CreateUnitObject(DisposEntry entry, GameObject parentGroup) { }
        
        public void DrawSceneGUI()
        {
            if (currentDocument == null)
                return;
            
            if (showGrid)
                DrawGrid();
            
            var prevZTest = Handles.zTest;
            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;

            foreach (var group in currentDocument.Groups)
            {
                if (!group.IsVisible)
                    continue;
                
                foreach (var entry in group.Entries)
                {
                    if (!entry.IsGroupHeader)
                    {
                        DrawUnitPlate(entry);
                        DrawUnitGUI(entry);
                    }
                }
            }

            Handles.zTest = prevZTest;
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
            Vector3 worldPos = new Vector3(worldX, worldOffset.y + 0.02f, worldZ);

            // Screen-space icon overlay sized to tile
            if (showIcons)
            {
                Texture2D icon = DisposDataLoader.Instance.GetUnitIcon(entry);
                if (icon != null)
                {
                    float tileX = startX + entry.DisposX * TILE_SIZE;
                    float tileZ = startZ + entry.DisposY * TILE_SIZE;
                    Vector3 blW = new Vector3(tileX, worldPos.y, tileZ);
                    Vector3 brW = new Vector3(tileX + TILE_SIZE, worldPos.y, tileZ);
                    Vector3 tlW = new Vector3(tileX, worldPos.y, tileZ + TILE_SIZE);

                    Vector2 bl = HandleUtility.WorldToGUIPoint(blW);
                    Vector2 br = HandleUtility.WorldToGUIPoint(brW);
                    Vector2 tl = HandleUtility.WorldToGUIPoint(tlW);

                    float tileWidthPx = (br - bl).magnitude;
                    float tileHeightPx = (tl - bl).magnitude;
                    float size = Mathf.Min(tileWidthPx, tileHeightPx) * ICON_TILE_SCALE;

                    Vector2 center = HandleUtility.WorldToGUIPoint(worldPos);
                    Rect iconRect = new Rect(center.x - size * 0.5f, center.y - size * 0.5f, size, size);

                    Handles.BeginGUI();
                    GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit, true);
                    Handles.EndGUI();
                }
            }

            // No visible handle; dragging is handled by DisposToolWindow input over tiles/icons

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
                // Offset label upward in screen space so it doesn't cover sprite
                screenPos.y -= LABEL_SCREEN_OFFSET_Y;
                
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

        // Screen-space hit test against a unit's tile rectangle (icon/plate area)
        public DisposEntry GetEntryAtScreenPosition(Vector2 mouseGui)
        {
            if (currentTerrain == null || currentDocument == null)
                return null;

            float startX = currentTerrain.m_X + worldOffset.x;
            float startZ = currentTerrain.m_Z + worldOffset.z;
            float y = worldOffset.y + 0.02f;

            DisposEntry best = null;
            float bestDist = float.MaxValue;

            foreach (var group in currentDocument.Groups)
            {
                if (!group.IsVisible) continue;
                foreach (var entry in group.Entries)
                {
                    if (entry.IsGroupHeader) continue;

                    float tileX = startX + entry.DisposX * TILE_SIZE;
                    float tileZ = startZ + entry.DisposY * TILE_SIZE;
                    Vector2 p0 = HandleUtility.WorldToGUIPoint(new Vector3(tileX, y, tileZ));
                    Vector2 p1 = HandleUtility.WorldToGUIPoint(new Vector3(tileX + TILE_SIZE, y, tileZ));
                    Vector2 p2 = HandleUtility.WorldToGUIPoint(new Vector3(tileX + TILE_SIZE, y, tileZ + TILE_SIZE));
                    Vector2 p3 = HandleUtility.WorldToGUIPoint(new Vector3(tileX, y, tileZ + TILE_SIZE));
                    float minX = Mathf.Min(Mathf.Min(p0.x, p1.x), Mathf.Min(p2.x, p3.x));
                    float maxX = Mathf.Max(Mathf.Max(p0.x, p1.x), Mathf.Max(p2.x, p3.x));
                    float minY = Mathf.Min(Mathf.Min(p0.y, p1.y), Mathf.Min(p2.y, p3.y));
                    float maxY = Mathf.Max(Mathf.Max(p0.y, p1.y), Mathf.Max(p2.y, p3.y));
                    Rect r = new Rect(minX, minY, maxX - minX, maxY - minY);

                    if (r.Contains(mouseGui))
                    {
                        // Prefer the closest tile center in screen space when overlapping
                        Vector2 center = HandleUtility.WorldToGUIPoint(new Vector3(tileX + TILE_SIZE * 0.5f, y, tileZ + TILE_SIZE * 0.5f));
                        float d = (center - mouseGui).sqrMagnitude;
                        if (d < bestDist)
                        {
                            bestDist = d;
                            best = entry;
                        }
                    }
                }
            }
            return best;
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
        }

        public void NudgeEntry(DisposEntry entry, int dx, int dy)
        {
            if (entry == null || entry.IsGroupHeader || currentTerrain == null)
                return;
            int width = currentTerrain.m_Width;
            int height = currentTerrain.m_Height;
            int nx = Mathf.Clamp(entry.DisposX + dx, 0, Mathf.Max(0, width - 1));
            int ny = Mathf.Clamp(entry.DisposY + dy, 0, Mathf.Max(0, height - 1));
            if (nx == entry.DisposX && ny == entry.DisposY)
                return;
            entry.DisposX = nx;
            entry.DisposY = ny;
            SceneView.RepaintAll();
        }

        private void DrawUnitPlate(DisposEntry entry)
        {
            if (currentTerrain == null) return;
            float startX = currentTerrain.m_X + worldOffset.x;
            float startZ = currentTerrain.m_Z + worldOffset.z;
            float y = worldOffset.y + 0.02f;
            float tileX = startX + entry.DisposX * TILE_SIZE;
            float tileZ = startZ + entry.DisposY * TILE_SIZE;
            Vector3[] verts = new Vector3[]
            {
                new Vector3(tileX, y, tileZ),
                new Vector3(tileX + TILE_SIZE, y, tileZ),
                new Vector3(tileX + TILE_SIZE, y, tileZ + TILE_SIZE),
                new Vector3(tileX, y, tileZ + TILE_SIZE)
            };
            Color c = DisposDataLoader.Instance.GetForceColor(entry.Force);
            Handles.DrawSolidRectangleWithOutline(verts, c, Color.black);
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
            SceneView.RepaintAll();
        }


        // No fallback sprites or overlay materials needed in immediate-mode
        
        public void SetWorldOffset(Vector3 offset)
        {
            worldOffset = offset;
            RefreshUnits();
            SceneView.RepaintAll();
        }

        public float GetBasePlaneY()
        {
            return worldOffset.y;
        }
    }
}
