using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace MaskTexturePainter
{
    public class MaskTexturePainterWindow : EditorWindow
    {
        private GameObject targetObject;
        private MeshRenderer targetRenderer;
        private Material targetMaterial;
        private Texture2D maskTexture;
        private Texture2D paintingTexture;
        
        public enum ChannelMode
        {
            Red,
            Green,
            Blue,
            Alpha,
            Eraser
        }
        
        private ChannelMode currentChannel = ChannelMode.Green;
        private float brushSize = 1.0f;
        private float brushStrength = 1.0f;
        private AnimationCurve brushFalloff = AnimationCurve.EaseInOut(0, 1, 1, 0);
        
        private bool isPreviewMode = false;
        private bool isPainting = false;
        
        private MaskTexturePainterTool painterTool;
        
        private Vector2 scrollPosition;
        
        [MenuItem("Tools/Mask Texture Painter")]
        public static void ShowWindow()
        {
            var window = GetWindow<MaskTexturePainterWindow>("Mask Texture Painter");
            window.minSize = new Vector2(350, 500);
        }
        
        private void OnEnable()
        {
            painterTool = new MaskTexturePainterTool(this);
            SceneView.duringSceneGui += OnSceneGUI;
            Undo.undoRedoPerformed += OnUndoRedo;
        }
        
        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            Undo.undoRedoPerformed -= OnUndoRedo;
            if (painterTool != null)
            {
                painterTool.Cleanup();
            }
        }
        
        private void OnGUI()
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            
            DrawTargetSection();
            EditorGUILayout.Space(10);
            
            if (targetObject != null && targetMaterial != null)
            {
                DrawTextureSection();
                EditorGUILayout.Space(10);
                
                DrawBrushSection();
                EditorGUILayout.Space(10);
                
                DrawChannelSection();
                EditorGUILayout.Space(10);
                
                DrawActionsSection();
            }
            
            EditorGUILayout.EndScrollView();
        }
        
        private void DrawTargetSection()
        {
            EditorGUILayout.LabelField("Target Object", EditorStyles.boldLabel);
            
            EditorGUI.BeginChangeCheck();
            targetObject = EditorGUILayout.ObjectField("GameObject", targetObject, typeof(GameObject), true) as GameObject;
            if (EditorGUI.EndChangeCheck())
            {
                UpdateTarget();
            }
            
            if (targetObject != null)
            {
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.ObjectField("Mesh Renderer", targetRenderer, typeof(MeshRenderer), true);
                EditorGUILayout.ObjectField("Material", targetMaterial, typeof(Material), false);
                EditorGUI.EndDisabledGroup();
                
                if (targetMaterial != null && !targetMaterial.shader.name.Contains("MapBlend"))
                {
                    EditorGUILayout.HelpBox("Warning: Selected material doesn't appear to use MapBlend shader", MessageType.Warning);
                }
            }
        }
        
        private void DrawTextureSection()
        {
            EditorGUILayout.LabelField("Mask Texture", EditorStyles.boldLabel);
            
            EditorGUI.BeginChangeCheck();
            maskTexture = EditorGUILayout.ObjectField("Mask Texture", maskTexture, typeof(Texture2D), false) as Texture2D;
            if (EditorGUI.EndChangeCheck())
            {
                LoadMaskTexture();
            }
            
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Create New Texture"))
            {
                CreateNewMaskTexture();
            }
            if (maskTexture != null && GUILayout.Button("Load from Material"))
            {
                LoadMaskFromMaterial();
            }
            EditorGUILayout.EndHorizontal();
            
            if (maskTexture != null)
            {
                EditorGUILayout.LabelField($"Texture Size: {maskTexture.width}x{maskTexture.height}");
                
                if (!maskTexture.isReadable)
                {
                    EditorGUILayout.HelpBox("Texture is not readable. Enable Read/Write in texture import settings.", MessageType.Error);
                }
            }
        }
        
        private void DrawBrushSection()
        {
            EditorGUILayout.LabelField("Brush Settings", EditorStyles.boldLabel);
            
            brushSize = EditorGUILayout.Slider("Brush Size", brushSize, 0.1f, 10.0f);
            brushStrength = EditorGUILayout.Slider("Brush Strength", brushStrength, 0.01f, 1.0f);
            
            EditorGUILayout.LabelField("Brush Falloff");
            brushFalloff = EditorGUILayout.CurveField(brushFalloff, Color.white, new Rect(0, 0, 1, 1));
        }
        
        private void DrawChannelSection()
        {
            EditorGUILayout.LabelField("Paint Channel", EditorStyles.boldLabel);
            
            EditorGUILayout.BeginHorizontal();
            
            GUI.backgroundColor = currentChannel == ChannelMode.Red ? Color.red : Color.white;
            if (GUILayout.Button("Red (Cut)", GUILayout.Height(30)))
            {
                currentChannel = ChannelMode.Red;
            }
            
            GUI.backgroundColor = currentChannel == ChannelMode.Green ? Color.green : Color.white;
            if (GUILayout.Button("Green", GUILayout.Height(30)))
            {
                currentChannel = ChannelMode.Green;
            }
            
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            
            GUI.backgroundColor = currentChannel == ChannelMode.Blue ? Color.blue : Color.white;
            if (GUILayout.Button("Blue", GUILayout.Height(30)))
            {
                currentChannel = ChannelMode.Blue;
            }
            
            GUI.backgroundColor = currentChannel == ChannelMode.Alpha ? Color.white : Color.gray;
            if (GUILayout.Button("Alpha", GUILayout.Height(30)))
            {
                currentChannel = ChannelMode.Alpha;
            }
            
            EditorGUILayout.EndHorizontal();
            
            GUI.backgroundColor = currentChannel == ChannelMode.Eraser ? Color.gray : Color.white;
            if (GUILayout.Button("Eraser (Clear)", GUILayout.Height(30)))
            {
                currentChannel = ChannelMode.Eraser;
            }
            
            GUI.backgroundColor = Color.white;
            
            EditorGUILayout.HelpBox(GetChannelDescription(), MessageType.Info);
        }
        
        private void DrawActionsSection()
        {
            EditorGUILayout.LabelField("Actions", EditorStyles.boldLabel);
            
            isPreviewMode = EditorGUILayout.Toggle("Preview Mode", isPreviewMode);
            
            EditorGUILayout.BeginHorizontal();
            
            if (paintingTexture != null && GUILayout.Button("Save to Asset", GUILayout.Height(30)))
            {
                SaveTextureToAsset();
            }
            
            if (paintingTexture != null && GUILayout.Button("Clear Texture", GUILayout.Height(30)))
            {
                ClearTexture();
            }
            
            EditorGUILayout.EndHorizontal();
            
            if (isPainting)
            {
                EditorGUILayout.HelpBox("Painting mode active. Click in Scene View to paint.", MessageType.Info);
            }
            
            EditorGUILayout.HelpBox("Use Ctrl+Z (Cmd+Z on Mac) to undo paint strokes", MessageType.Info);
        }
        
        private void UpdateTarget()
        {
            targetRenderer = null;
            targetMaterial = null;
            
            if (targetObject != null)
            {
                targetRenderer = targetObject.GetComponent<MeshRenderer>();
                if (targetRenderer != null && targetRenderer.sharedMaterial != null)
                {
                    targetMaterial = targetRenderer.sharedMaterial;
                    LoadMaskFromMaterial();
                }
            }
        }
        
        private void LoadMaskFromMaterial()
        {
            if (targetMaterial != null)
            {
                var maskProperty = targetMaterial.GetTexture("_MaskMap");
                if (maskProperty != null && maskProperty is Texture2D)
                {
                    maskTexture = maskProperty as Texture2D;
                    LoadMaskTexture();
                }
            }
        }
        
        private void LoadMaskTexture()
        {
            if (maskTexture != null)
            {
                if (!maskTexture.isReadable)
                {
                    Debug.LogError("Mask texture is not readable! Enable Read/Write in texture import settings.");
                    return;
                }
                
                // Create editable copy
                paintingTexture = new Texture2D(maskTexture.width, maskTexture.height, TextureFormat.RGBA32, false);
                paintingTexture.name = maskTexture.name + "_painting";
                paintingTexture.SetPixels(maskTexture.GetPixels());
                paintingTexture.Apply();
                
                // Immediately apply to material
                UpdateMaterialPreview();
                Debug.Log($"Loaded mask texture: {maskTexture.name} ({maskTexture.width}x{maskTexture.height})");
            }
        }
        
        private void CreateNewMaskTexture()
        {
            var path = EditorUtility.SaveFilePanelInProject("Create Mask Texture", "MaskTexture", "png", "Choose location for mask texture");
            if (!string.IsNullOrEmpty(path))
            {
                int size = 1024;
                var newTexture = new Texture2D(size, size, TextureFormat.RGBA32, false);
                var pixels = new Color[size * size];
                for (int i = 0; i < pixels.Length; i++)
                {
                    pixels[i] = Color.clear;
                }
                newTexture.SetPixels(pixels);
                newTexture.Apply();
                
                var bytes = newTexture.EncodeToPNG();
                System.IO.File.WriteAllBytes(path, bytes);
                AssetDatabase.ImportAsset(path);
                
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer != null)
                {
                    importer.isReadable = true;
                    importer.textureCompression = TextureImporterCompression.Uncompressed;
                    importer.SaveAndReimport();
                }
                
                maskTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                LoadMaskTexture();
            }
        }
        
        private void SaveTextureToAsset()
        {
            if (paintingTexture != null && maskTexture != null)
            {
                var path = AssetDatabase.GetAssetPath(maskTexture);
                if (!string.IsNullOrEmpty(path))
                {
                    var bytes = paintingTexture.EncodeToPNG();
                    System.IO.File.WriteAllBytes(path, bytes);
                    AssetDatabase.ImportAsset(path);
                    
                    EditorUtility.DisplayDialog("Saved", "Mask texture saved successfully!", "OK");
                }
            }
        }
        
        private void ClearTexture()
        {
            if (paintingTexture != null)
            {
                Undo.RegisterCompleteObjectUndo(this, "Clear Texture");
                
                var pixels = paintingTexture.GetPixels();
                for (int i = 0; i < pixels.Length; i++)
                {
                    pixels[i] = Color.clear;
                }
                paintingTexture.SetPixels(pixels);
                paintingTexture.Apply();
                
                UpdateMaterialPreview();
            }
        }
        
        private void OnUndoRedo()
        {
            // Restore texture from undo data
            if (paintingTexture != null && undoTextureData != null && undoTextureData.Length == paintingTexture.width * paintingTexture.height)
            {
                paintingTexture.SetPixels(undoTextureData);
                paintingTexture.Apply();
                UpdateMaterialPreview();
                Repaint();
            }
        }
        
        private void UpdateMaterialPreview()
        {
            if (targetRenderer != null && paintingTexture != null)
            {
                // Create a material instance if we haven't already
                if (targetRenderer.sharedMaterial != null)
                {
                    // Use material property block to avoid modifying the asset
                    MaterialPropertyBlock props = new MaterialPropertyBlock();
                    targetRenderer.GetPropertyBlock(props);
                    props.SetTexture("_MaskMap", paintingTexture);
                    targetRenderer.SetPropertyBlock(props);
                    
                    // Force scene view to repaint
                    SceneView.RepaintAll();
                    EditorUtility.SetDirty(targetRenderer);
                }
            }
        }
        
        private string GetChannelDescription()
        {
            switch (currentChannel)
            {
                case ChannelMode.Red:
                    return "Red channel: Shows AlbedoMap0 texture";
                case ChannelMode.Green:
                    return "Green channel: Shows AlbedoMap1 texture";
                case ChannelMode.Blue:
                    return "Blue channel: Shows AlbedoMap2 texture";
                case ChannelMode.Alpha:
                    return "Alpha channel: Shows AlbedoMap3 texture";
                case ChannelMode.Eraser:
                    return "Eraser: Clears all channels (shows AlbedoMap4)";
                default:
                    return "";
            }
        }
        
        private void OnSceneGUI(SceneView sceneView)
        {
            if (painterTool != null && targetObject != null && paintingTexture != null)
            {
                painterTool.OnSceneGUI(sceneView);
            }
        }
        
        public void StartPaintStroke()
        {
            if (paintingTexture != null)
            {
                // Create a copy of current texture state for undo
                var pixels = paintingTexture.GetPixels();
                Undo.RegisterCompleteObjectUndo(this, "Paint Stroke");
                
                // Store the texture state internally
                if (undoTextureData == null || undoTextureData.Length != pixels.Length)
                {
                    undoTextureData = new Color[pixels.Length];
                }
                pixels.CopyTo(undoTextureData, 0);
            }
        }
        
        [SerializeField]
        private Color[] undoTextureData;
        
        public void PaintAtUV(Vector2 uv)
        {
            if (paintingTexture == null)
            {
                Debug.LogWarning("PaintingTexture is null!");
                return;
            }
            
            var brush = new TexturePaintBrush(brushSize, brushStrength, brushFalloff);
            brush.Paint(paintingTexture, uv, currentChannel);
            paintingTexture.Apply();
            
            // Store the updated texture data for undo
            if (undoTextureData != null && undoTextureData.Length == paintingTexture.width * paintingTexture.height)
            {
                var currentPixels = paintingTexture.GetPixels();
                currentPixels.CopyTo(undoTextureData, 0);
            }
            
            UpdateMaterialPreview();
            
            // Force repaint
            Repaint();
        }
        
        public GameObject GetTargetObject() => targetObject;
        public Texture2D GetPaintingTexture() => paintingTexture;
        public float GetBrushSize() => brushSize;
        public Color GetBrushColor()
        {
            switch (currentChannel)
            {
                case ChannelMode.Red: return Color.red;
                case ChannelMode.Green: return Color.green;
                case ChannelMode.Blue: return Color.blue;
                case ChannelMode.Alpha: return Color.white;
                case ChannelMode.Eraser: return Color.gray;
                default: return Color.white;
            }
        }
        
        public string GetCurrentChannelName()
        {
            switch (currentChannel)
            {
                case ChannelMode.Red: return "RED";
                case ChannelMode.Green: return "GREEN";
                case ChannelMode.Blue: return "BLUE";
                case ChannelMode.Alpha: return "ALPHA";
                case ChannelMode.Eraser: return "ERASE";
                default: return "";
            }
        }
    }
}