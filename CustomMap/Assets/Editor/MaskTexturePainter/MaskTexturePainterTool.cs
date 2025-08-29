using UnityEngine;
using UnityEditor;

namespace MaskTexturePainter
{
    public class MaskTexturePainterTool
    {
        private MaskTexturePainterWindow window;
        private bool isPainting = false;
        private bool mouseWasDown = false;
        private Vector2 lastPaintedUV = Vector2.zero;
        private float minPaintDistance = 0.001f;
        
        public MaskTexturePainterTool(MaskTexturePainterWindow window)
        {
            this.window = window;
        }
        
        public void OnSceneGUI(SceneView sceneView)
        {
            var targetObject = window.GetTargetObject();
            if (targetObject == null)
            {
                return;
            }
            
            var paintingTexture = window.GetPaintingTexture();
            if (paintingTexture == null)
            {
                return;
            }
            
            // Block default Unity controls when painting
            int controlId = GUIUtility.GetControlID(FocusType.Passive);
            HandleUtility.AddDefaultControl(controlId);
            
            Event e = Event.current;
            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            
            // Try mesh-based raycasting first (for objects without colliders)
            bool hitTarget = false;
            Vector3 hitPoint = Vector3.zero;
            Vector3 hitNormal = Vector3.up;
            Vector2 hitUV = Vector2.zero;
            int hitTriangleIndex = -1;
            
            // Get MeshFilter from target
            MeshFilter meshFilter = targetObject.GetComponent<MeshFilter>();
            if (meshFilter != null)
            {
                MeshRaycast.MeshRaycastHit meshHit;
                if (MeshRaycast.Raycast(meshFilter, ray, out meshHit))
                {
                    hitTarget = true;
                    hitPoint = meshHit.point;
                    hitNormal = meshHit.normal;
                    hitUV = meshHit.textureCoord;
                    hitTriangleIndex = meshHit.triangleIndex;
                }
            }
            
            // Fallback to physics raycast if mesh raycast didn't work
            if (!hitTarget)
            {
                RaycastHit physicsHit;
                if (Physics.Raycast(ray, out physicsHit, Mathf.Infinity))
                {
                    // Check if hit object is our target or its children
                    Transform hitTransform = physicsHit.collider.transform;
                    while (hitTransform != null)
                    {
                        if (hitTransform.gameObject == targetObject)
                        {
                            hitTarget = true;
                            hitPoint = physicsHit.point;
                            hitNormal = physicsHit.normal;
                            hitUV = physicsHit.textureCoord;
                            break;
                        }
                        hitTransform = hitTransform.parent;
                    }
                }
            }
            
            if (hitTarget)
            {
                // Calculate world-space brush size based on UV space
                float worldBrushSize = CalculateWorldBrushSize(meshFilter, hitUV, hitTriangleIndex);
                
                // Always draw brush cursor when hovering over target
                DrawBrushCursor(hitPoint, hitNormal, worldBrushSize);
                
                // Handle painting
                if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
                {
                    isPainting = true;
                    mouseWasDown = true;
                    window.StartPaintStroke();
                    PaintAtUV(hitUV);
                    e.Use();
                    GUIUtility.hotControl = controlId;
                }
                else if (e.type == EventType.MouseDrag && e.button == 0 && isPainting && !e.alt)
                {
                    PaintAtUV(hitUV);
                    e.Use();
                }
            }

            // Handle mouse up anywhere
            if (e.type == EventType.MouseUp && e.button == 0)
            {
                if (isPainting)
                {
                    // End the current paint stroke and flush/save if needed
                    window.EndPaintStroke();
                    isPainting = false;
                    mouseWasDown = false;
                    e.Use();
                    GUIUtility.hotControl = 0;
                }
            }
            
            // Force continuous repaint for smooth cursor
            if (e.type == EventType.MouseMove)
            {
                sceneView.Repaint();
            }
            
            // Ensure scene view updates
            if (GUI.changed)
            {
                EditorUtility.SetDirty(targetObject);
                sceneView.Repaint();
            }
        }
        
        private void PaintAtUV(Vector2 uv)
        {
            if (Vector2.Distance(uv, lastPaintedUV) >= minPaintDistance || !mouseWasDown)
            {
                window.PaintAtUV(uv);
                lastPaintedUV = uv;
            }
        }
        
        private float CalculateWorldBrushSize(MeshFilter meshFilter, Vector2 centerUV, int triangleIndex)
        {
            float brushSize = window.GetBrushSize();
            var texture = window.GetPaintingTexture();
            if (texture == null || meshFilter == null) return brushSize * 0.5f;
            
            // Calculate brush radius in UV space (0-1 range)
            // Brush radius in pixels = size * texture.width * 0.01f
            // So UV radius = (size * texture.width * 0.01f) / texture.width = size * 0.01f
            float uvRadius = brushSize * 0.01f;
            
            // Try to estimate world size by sampling nearby UV coordinates
            if (meshFilter.sharedMesh != null && triangleIndex >= 0)
            {
                var mesh = meshFilter.sharedMesh;
                var vertices = mesh.vertices;
                var uvs = mesh.uv;
                var triangles = mesh.triangles;
                
                if (triangleIndex * 3 + 2 < triangles.Length)
                {
                    int i0 = triangles[triangleIndex * 3];
                    int i1 = triangles[triangleIndex * 3 + 1];
                    int i2 = triangles[triangleIndex * 3 + 2];
                    
                    Vector3 v0 = meshFilter.transform.TransformPoint(vertices[i0]);
                    Vector3 v1 = meshFilter.transform.TransformPoint(vertices[i1]);
                    Vector3 v2 = meshFilter.transform.TransformPoint(vertices[i2]);
                    
                    Vector2 uv0 = uvs[i0];
                    Vector2 uv1 = uvs[i1];
                    Vector2 uv2 = uvs[i2];
                    
                    // Calculate UV to world scale
                    float worldDist = Vector3.Distance(v0, v1);
                    float uvDist = Vector2.Distance(uv0, uv1);
                    
                    if (uvDist > 0.001f)
                    {
                        float scale = worldDist / uvDist;
                        return uvRadius * scale;
                    }
                }
            }
            
            // Fallback
            return brushSize * 0.5f;
        }
        
        private void DrawBrushCursor(Vector3 position, Vector3 normal, float worldBrushSize)
        {
            Color brushColor = window.GetBrushColor();
            
            // Draw outer ring
            Handles.color = new Color(brushColor.r, brushColor.g, brushColor.b, 0.8f);
            Handles.DrawWireDisc(position, normal, worldBrushSize);
            
            // Draw inner filled disc with transparency
            Handles.color = new Color(brushColor.r, brushColor.g, brushColor.b, 0.15f);
            Handles.DrawSolidDisc(position, normal, worldBrushSize);
            
            // Draw center dot
            Handles.color = brushColor;
            Handles.DrawSolidDisc(position, normal, worldBrushSize * 0.05f);
            
            // Draw normal indicator
            Handles.color = new Color(1, 1, 1, 0.5f);
            Handles.DrawLine(position, position + normal * worldBrushSize * 0.5f);
            
            // Add text label showing current mode
            var style = new GUIStyle(GUI.skin.label);
            style.normal.textColor = brushColor;
            style.fontStyle = FontStyle.Bold;
            style.fontSize = 12;
            style.alignment = TextAnchor.MiddleCenter;
            
            var screenPos = HandleUtility.WorldToGUIPoint(position + normal * worldBrushSize * 1.5f);
            Handles.BeginGUI();
            GUI.Label(new Rect(screenPos.x - 50, screenPos.y - 10, 100, 20), window.GetCurrentChannelName(), style);
            Handles.EndGUI();
        }
        
        public void Cleanup()
        {
            isPainting = false;
        }
    }
}
