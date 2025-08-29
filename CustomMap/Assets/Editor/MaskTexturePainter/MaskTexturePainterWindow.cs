using UnityEngine;
using UnityEditor;
using System.IO;
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
        private string maskTexturePath; // Store the path separately
        private bool warnedAutoSaveInvalidPath = false;
        
        // Textures from the material
        private Texture2D albedoMap0;
        private Texture2D albedoMap1;
        private Texture2D albedoMap2;
        private Texture2D albedoMap3;
        private Texture2D albedoMap4;
        
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
        // Create a smooth falloff curve: strong at center (0), weak at edge (1)
        private AnimationCurve brushFalloff = CreateDefaultFalloffCurve();
        
        private bool isPainting = false;
        private bool autoSave = true; // Auto-save by default
        
        private MaskTexturePainterTool painterTool;
        
        private Vector2 scrollPosition;
        
        [MenuItem("Tools/Mask Texture Painter")]
        public static void ShowWindow()
        {
            var window = GetWindow<MaskTexturePainterWindow>("Mask Texture Painter");
            window.minSize = new Vector2(350, 500);
        }
        
        private static AnimationCurve CreateDefaultFalloffCurve()
        {
            // Default to soft falloff
            return CreateSoftFalloffCurve();
        }
        
        private static AnimationCurve CreateSoftFalloffCurve()
        {
            // Very soft falloff with smooth gradient
            AnimationCurve curve = new AnimationCurve();
            curve.AddKey(0f, 1f);
            curve.AddKey(0.3f, 0.9f);
            curve.AddKey(0.6f, 0.4f);
            curve.AddKey(0.8f, 0.1f);
            curve.AddKey(1f, 0f);
            
            // Make it smooth using Auto mode
            for (int i = 0; i < curve.keys.Length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Auto);
                AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Auto);
            }
            
            return curve;
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

            // Finalize any pending stroke and persist on close
            if (paintingTexture != null)
            {
                // Flush texture changes and persist regardless of Auto-Save toggle
                paintingTexture.Apply();
                QuickSave(force: true);

                // Clear preview override so the material shows the saved asset
                if (targetRenderer != null)
                {
                    var props = new MaterialPropertyBlock();
                    targetRenderer.GetPropertyBlock(props);
                    props.Clear();
                    targetRenderer.SetPropertyBlock(props);
                }
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
                // Store the path when user manually selects a texture
                if (maskTexture != null)
                {
                    maskTexturePath = AssetDatabase.GetAssetPath(maskTexture);
                    Debug.Log($"User selected mask texture at: {maskTexturePath}");
                }
                else
                {
                    maskTexturePath = null;
                }
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
            
            // Preset buttons
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Soft", GUILayout.Width(60)))
            {
                brushFalloff = CreateSoftFalloffCurve();
            }
            if (GUILayout.Button("Linear", GUILayout.Width(60)))
            {
                brushFalloff = CreateLinearFalloffCurve();
            }
            if (GUILayout.Button("Hard", GUILayout.Width(60)))
            {
                brushFalloff = CreateHardFalloffCurve();
            }
            EditorGUILayout.EndHorizontal();
        }
        
        private static AnimationCurve CreateLinearFalloffCurve()
        {
            // Linear falloff
            return AnimationCurve.Linear(0f, 1f, 1f, 0f);
        }
        
        private static AnimationCurve CreateHardFalloffCurve()
        {
            // Hard edge with minimal falloff
            AnimationCurve curve = new AnimationCurve();
            curve.AddKey(0f, 1f);
            curve.AddKey(0.8f, 1f);
            curve.AddKey(0.95f, 0.5f);
            curve.AddKey(1f, 0f);
            return curve;
        }
        
        private void DrawChannelSection()
        {
            EditorGUILayout.LabelField("Select Texture to Paint", EditorStyles.boldLabel);
            
            // Show texture preview buttons in a grid
            const int buttonSize = 64;
            const int buttonsPerRow = 3;
            
            GUIStyle textureButtonStyle = new GUIStyle(GUI.skin.button);
            textureButtonStyle.padding = new RectOffset(2, 2, 2, 2);
            
            // Create array of textures and their corresponding channels
            var textureOptions = new (Texture2D tex, ChannelMode mode, string label, Color tint)[]
            {
                (albedoMap0, ChannelMode.Red, "Texture 0\n(Red)", Color.red),
                (albedoMap1, ChannelMode.Green, "Texture 1\n(Green)", Color.green),
                (albedoMap2, ChannelMode.Blue, "Texture 2\n(Blue)", Color.blue),
                (albedoMap3, ChannelMode.Alpha, "Texture 3\n(Alpha)", Color.white),
                (albedoMap4, ChannelMode.Eraser, "Base/Erase\n(Default)", Color.gray)
            };
            
            for (int i = 0; i < textureOptions.Length; i++)
            {
                if (i % buttonsPerRow == 0)
                {
                    EditorGUILayout.BeginHorizontal();
                }
                
                var option = textureOptions[i];
                bool isSelected = currentChannel == option.mode;
                
                // Draw button with texture preview
                GUI.backgroundColor = isSelected ? option.tint : Color.white;
                
                if (GUILayout.Button(new GUIContent(), textureButtonStyle, 
                    GUILayout.Width(buttonSize + 10), GUILayout.Height(buttonSize + 20)))
                {
                    currentChannel = option.mode;
                }
                
                Rect buttonRect = GUILayoutUtility.GetLastRect();
                
                // Draw texture preview or placeholder
                Rect textureRect = new Rect(buttonRect.x + 5, buttonRect.y + 5, buttonSize, buttonSize);
                if (option.tex != null)
                {
                    GUI.DrawTexture(textureRect, option.tex, ScaleMode.ScaleToFit);
                }
                else
                {
                    EditorGUI.DrawRect(textureRect, new Color(0.3f, 0.3f, 0.3f, 1f));
                    GUI.Label(textureRect, "No\nTexture", new GUIStyle(EditorStyles.centeredGreyMiniLabel) 
                    { 
                        alignment = TextAnchor.MiddleCenter,
                        normal = { textColor = Color.white }
                    });
                }
                
                // Draw label below texture
                Rect labelRect = new Rect(buttonRect.x, buttonRect.y + buttonSize + 5, buttonRect.width, 15);
                GUI.Label(labelRect, option.label, new GUIStyle(EditorStyles.miniLabel) 
                { 
                    alignment = TextAnchor.UpperCenter,
                    fontSize = 9
                });
                
                // Draw selection border
                if (isSelected)
                {
                    Handles.color = option.tint;
                    Handles.DrawSolidRectangleWithOutline(
                        new Vector3[] {
                            new Vector3(buttonRect.x, buttonRect.y),
                            new Vector3(buttonRect.x + buttonRect.width, buttonRect.y),
                            new Vector3(buttonRect.x + buttonRect.width, buttonRect.y + buttonRect.height),
                            new Vector3(buttonRect.x, buttonRect.y + buttonRect.height)
                        },
                        Color.clear,
                        option.tint
                    );
                }
                
                if ((i + 1) % buttonsPerRow == 0 || i == textureOptions.Length - 1)
                {
                    EditorGUILayout.EndHorizontal();
                }
            }
            
            GUI.backgroundColor = Color.white;
            
            EditorGUILayout.Space(5);
            EditorGUILayout.HelpBox(GetChannelDescription(), MessageType.Info);
        }
        
        private void DrawActionsSection()
        {
            EditorGUILayout.LabelField("Actions", EditorStyles.boldLabel);
            
            autoSave = EditorGUILayout.Toggle("Auto-Save", autoSave);
            if (autoSave)
            {
                EditorGUILayout.HelpBox("Changes are saved to disk immediately after each paint stroke", MessageType.Info);
            }
            
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
            albedoMap0 = null;
            albedoMap1 = null;
            albedoMap2 = null;
            albedoMap3 = null;
            albedoMap4 = null;
            
            if (targetObject != null)
            {
                targetRenderer = targetObject.GetComponent<MeshRenderer>();
                if (targetRenderer != null && targetRenderer.sharedMaterial != null)
                {
                    targetMaterial = targetRenderer.sharedMaterial;
                    LoadMaskFromMaterial();
                    LoadAlbedoMapsFromMaterial();
                }
            }
        }
        
        private void LoadAlbedoMapsFromMaterial()
        {
            if (targetMaterial != null)
            {
                albedoMap0 = targetMaterial.GetTexture("_AlbedoMap0") as Texture2D;
                albedoMap1 = targetMaterial.GetTexture("_AlbedoMap1") as Texture2D;
                albedoMap2 = targetMaterial.GetTexture("_AlbedoMap2") as Texture2D;
                albedoMap3 = targetMaterial.GetTexture("_AlbedoMap3") as Texture2D;
                albedoMap4 = targetMaterial.GetTexture("_AlbedoMap4") as Texture2D;
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
                    maskTexturePath = AssetDatabase.GetAssetPath(maskTexture);
                    LoadMaskTexture();
                    Debug.Log($"Loaded mask from material: {maskTexture?.name}, path: {maskTexturePath}");
                }
                else
                {
                    Debug.Log("Material has no _MaskMap texture - you'll need to create one");
                }
            }
        }
        
        private void LoadMaskTexture()
        {
            if (maskTexture != null)
            {
                // Store the path when we load the texture
                maskTexturePath = AssetDatabase.GetAssetPath(maskTexture);
                Debug.Log($"Mask texture path: {maskTexturePath}");
                
                if (!maskTexture.isReadable)
                {
                    Debug.LogError("Mask texture is not readable! Enable Read/Write in texture import settings.");
                    return;
                }
                
                // Create editable copy
                Color[] originalPixels = maskTexture.GetPixels();
                paintingTexture = new Texture2D(maskTexture.width, maskTexture.height, TextureFormat.RGBA32, false);
                paintingTexture.name = maskTexture.name + "_painting";
                paintingTexture.SetPixels(originalPixels);
                paintingTexture.Apply();
                
                Debug.Log($"Loaded mask texture: {maskTexture.name} ({maskTexture.width}x{maskTexture.height})");
                Debug.Log($"First pixel color: {originalPixels[0]}");
                
                // Apply to preview using MaterialPropertyBlock (non-destructive)
                UpdateMaterialPreview();
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
                maskTexturePath = path; // Store the path
                LoadMaskTexture();
            }
        }
        
        private void SaveTextureToAsset()
        {
            if (paintingTexture == null)
            {
                Debug.LogError("No painting texture to save!");
                return;
            }
            
            // Get the original mask texture from the material
            string path = null;
            Texture2D originalTexture = null;
            
            if (targetMaterial != null)
            {
                originalTexture = targetMaterial.GetTexture("_MaskMap") as Texture2D;
                if (originalTexture != null)
                {
                    path = AssetDatabase.GetAssetPath(originalTexture);
                    Debug.Log($"Saving to original mask texture: {originalTexture.name} at path: {path}");
                }
            }
            
            // If no path from material, try our stored reference
            if (string.IsNullOrEmpty(path) && maskTexture != null)
            {
                path = AssetDatabase.GetAssetPath(maskTexture);
                originalTexture = maskTexture;
                Debug.Log($"Using mask texture reference: {path}");
            }
            
            if (!string.IsNullOrEmpty(path))
            {
                // Decide final save path/format. If original is not encodable (e.g., .dds, .psd),
                // write a PNG alongside and relink the material.
                string extension = Path.GetExtension(path).ToLower();
                string writePath = path;
                string writeExt = extension;

                bool supported = extension == ".png" || extension == ".jpg" || extension == ".jpeg" || extension == ".tga" || extension == ".exr";
                if (!supported)
                {
                    writePath = Path.ChangeExtension(path, ".png");
                    writeExt = ".png";
                    Debug.LogWarning($"[MaskPainter] Source extension '{extension}' not encodable. Saving PNG to '{writePath}' and updating material.");
                }

                // Encode according to target extension
                byte[] bytes = null;
                if (writeExt == ".png") bytes = paintingTexture.EncodeToPNG();
                else if (writeExt == ".jpg" || writeExt == ".jpeg") bytes = paintingTexture.EncodeToJPG(95);
                else if (writeExt == ".tga") bytes = paintingTexture.EncodeToTGA();
                else if (writeExt == ".exr") bytes = paintingTexture.EncodeToEXR();
                else bytes = paintingTexture.EncodeToPNG();

                // Write the file (absolute path)
                var fullPath = GetAbsoluteProjectPath(writePath);
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
                    File.WriteAllBytes(fullPath, bytes);
                    Debug.Log($"[MaskPainter] Saved {bytes?.Length ?? 0} bytes to {writePath} ({fullPath})");
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[MaskPainter] Failed to write to '{fullPath}': {ex.Message}");
                    return;
                }

                // Force Unity to reimport the asset we wrote
                AssetDatabase.ImportAsset(writePath, ImportAssetOptions.ForceUpdate);

                // Ensure the texture import settings are correct
                var importer = AssetImporter.GetAtPath(writePath) as TextureImporter;
                    if (importer != null)
                    {
                        bool needsReimport = false;
                        
                        if (!importer.isReadable)
                        {
                            importer.isReadable = true;
                            needsReimport = true;
                        }
                        
                        if (importer.textureCompression != TextureImporterCompression.Uncompressed)
                        {
                            importer.textureCompression = TextureImporterCompression.Uncompressed;
                            needsReimport = true;
                        }
                        
                        if (importer.sRGBTexture)
                        {
                            importer.sRGBTexture = false; // Mask textures should be linear
                            needsReimport = true;
                        }
                        
                        if (needsReimport)
                        {
                            importer.SaveAndReimport();
                        }
                    }
                    
                    // Refresh the asset database
                    AssetDatabase.Refresh();
                    
                    // Reload the mask texture to ensure it's up to date
                    maskTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(writePath);
                    maskTexturePath = writePath;
                
                // Update the material to use the saved texture
                if (targetMaterial != null)
                {
                    targetMaterial.SetTexture("_MaskMap", maskTexture);
                    EditorUtility.SetDirty(targetMaterial);
                }
                    
                    Debug.Log($"Saved mask texture to: {writePath}");
                    EditorUtility.DisplayDialog("Saved", $"Mask texture saved successfully to:\n{writePath}", "OK");
                }
                else
                {
                    Debug.LogWarning("No existing path found for mask texture. Prompting for save location...");
                    
                    // Prompt user to save as a new file
                    string newPath = EditorUtility.SaveFilePanelInProject(
                        "Save Mask Texture As", 
                        "MaskTexture", 
                        "png", 
                        "Choose location to save the mask texture");
                    
                    if (!string.IsNullOrEmpty(newPath))
                    {
                        // Save to the new path
                        byte[] bytes = paintingTexture.EncodeToPNG();
                        var fullNewPath = GetAbsoluteProjectPath(newPath);
                        Directory.CreateDirectory(Path.GetDirectoryName(fullNewPath));
                        File.WriteAllBytes(fullNewPath, bytes);
                        AssetDatabase.ImportAsset(newPath);
                        
                        // Update import settings
                        var importer = AssetImporter.GetAtPath(newPath) as TextureImporter;
                        if (importer != null)
                        {
                            importer.isReadable = true;
                            importer.textureCompression = TextureImporterCompression.Uncompressed;
                            importer.sRGBTexture = false;
                            importer.SaveAndReimport();
                        }
                        
                        // Load the newly saved texture
                        maskTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(newPath);
                        maskTexturePath = newPath;
                        
                        // Update material
                        if (targetMaterial != null)
                        {
                            targetMaterial.SetTexture("_MaskMap", maskTexture);
                            EditorUtility.SetDirty(targetMaterial);
                        }
                        
                        Debug.Log($"Saved new mask texture to: {newPath}");
                        EditorUtility.DisplayDialog("Saved", $"Mask texture saved as new file:\n{newPath}", "OK");
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
                // Use MaterialPropertyBlock for non-destructive preview
                // This shows changes without modifying the actual material asset
                MaterialPropertyBlock props = new MaterialPropertyBlock();
                targetRenderer.GetPropertyBlock(props);
                props.SetTexture("_MaskMap", paintingTexture);
                targetRenderer.SetPropertyBlock(props);
                
                // Force scene view to repaint
                SceneView.RepaintAll();
                EditorUtility.SetDirty(targetRenderer);
            }
        }
        
        private string GetChannelDescription()
        {
            switch (currentChannel)
            {
                case ChannelMode.Red:
                    string tex0Name = albedoMap0 != null ? albedoMap0.name : "AlbedoMap0";
                    return $"Red channel: Paints '{tex0Name}' texture";
                case ChannelMode.Green:
                    string tex1Name = albedoMap1 != null ? albedoMap1.name : "AlbedoMap1";
                    return $"Green channel: Paints '{tex1Name}' texture";
                case ChannelMode.Blue:
                    string tex2Name = albedoMap2 != null ? albedoMap2.name : "AlbedoMap2";
                    return $"Blue channel: Paints '{tex2Name}' texture";
                case ChannelMode.Alpha:
                    string tex3Name = albedoMap3 != null ? albedoMap3.name : "AlbedoMap3";
                    return $"Alpha channel: Paints '{tex3Name}' texture";
                case ChannelMode.Eraser:
                    string tex4Name = albedoMap4 != null ? albedoMap4.name : "AlbedoMap4";
                    return $"Eraser: Clears all channels (shows base texture '{tex4Name}')";
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
            
            // Auto-save moved to occur at end of stroke (on mouse up)
            
            // Force repaint
            Repaint();
        }
        
        private void QuickSave(bool force = false)
        {
            if (paintingTexture == null) return;

            // Always use the material's assigned _MaskMap if available
            string path = GetPreferredSavePath();

            if (string.IsNullOrEmpty(path))
            {
                if (!warnedAutoSaveInvalidPath)
                {
                    Debug.LogWarning("[MaskPainter] Auto-Save skipped: No valid asset path for material _MaskMap.");
                    warnedAutoSaveInvalidPath = true;
                }
                return;
            }

            // Determine format and handle unsupported original extensions (e.g., .dds, .psd)
            string extension = Path.GetExtension(path).ToLower();
            string writePath = path;
            string writeExt = extension;
            bool supported = extension == ".png" || extension == ".jpg" || extension == ".jpeg" || extension == ".tga" || extension == ".exr";
            if (!supported)
            {
                writePath = Path.ChangeExtension(path, ".png");
                writeExt = ".png";
                Debug.LogWarning($"[MaskPainter] Source extension '{extension}' not encodable. Auto-saving as PNG to '{writePath}' and updating material.");
            }

            byte[] bytes = null;
            if (writeExt == ".png") bytes = paintingTexture.EncodeToPNG();
            else if (writeExt == ".jpg" || writeExt == ".jpeg") bytes = paintingTexture.EncodeToJPG(95);
            else if (writeExt == ".tga") bytes = paintingTexture.EncodeToTGA();
            else if (writeExt == ".exr") bytes = paintingTexture.EncodeToEXR();
            else bytes = paintingTexture.EncodeToPNG();

            var fullPath = GetAbsoluteProjectPath(writePath);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
                File.WriteAllBytes(fullPath, bytes);
                // Reimport the asset to ensure changes are visible
                AssetDatabase.ImportAsset(writePath, ImportAssetOptions.ForceUpdate);
                Debug.Log($"[MaskPainter] Auto-saved {bytes?.Length ?? 0} bytes to {writePath} ({fullPath})");
                maskTexturePath = writePath;

                // If redirected to PNG, ensure importer settings and relink material
                var importer = AssetImporter.GetAtPath(writePath) as TextureImporter;
                if (importer != null)
                {
                    bool needsReimport = false;
                    if (!importer.isReadable) { importer.isReadable = true; needsReimport = true; }
                    if (importer.textureCompression != TextureImporterCompression.Uncompressed) { importer.textureCompression = TextureImporterCompression.Uncompressed; needsReimport = true; }
                    if (importer.sRGBTexture) { importer.sRGBTexture = false; needsReimport = true; }
                    if (needsReimport) importer.SaveAndReimport();
                }

                if (writePath != path && targetMaterial != null)
                {
                    var newTex = AssetDatabase.LoadAssetAtPath<Texture2D>(writePath);
                    if (newTex != null)
                    {
                        targetMaterial.SetTexture("_MaskMap", newTex);
                        EditorUtility.SetDirty(targetMaterial);
                        maskTexture = newTex;
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[MaskPainter] Auto-Save failed for '{fullPath}': {ex.Message}");
            }
        }

        // Choose the best path to save back to: prefer where we loaded from
        private string GetPreferredSavePath()
        {
            // Prefer the material's assigned texture path (original source)
            if (targetMaterial != null)
            {
                var materialMask = targetMaterial.GetTexture("_MaskMap") as Texture2D;
                if (materialMask != null)
                {
                    var path = AssetDatabase.GetAssetPath(materialMask);
                    if (!string.IsNullOrEmpty(path))
                        return path;
                }
            }
            // Fallback to stored path if any
            if (!string.IsNullOrEmpty(maskTexturePath))
                return maskTexturePath;
            return null;
        }

        // Convert a project-relative asset path (e.g., Assets/foo.png) to an absolute filesystem path
        private static string GetAbsoluteProjectPath(string assetPath)
        {
            var projectRoot = Application.dataPath.Substring(0, Application.dataPath.Length - "Assets".Length);
            return Path.GetFullPath(Path.Combine(projectRoot, assetPath));
        }

        // Called by the scene tool when a stroke completes (mouse up)
        public void EndPaintStroke()
        {
            if (paintingTexture == null)
                return;

            // Ensure the preview has the latest pixels
            paintingTexture.Apply();
            UpdateMaterialPreview();

            // Persist to disk per completed stroke
            if (autoSave)
            {
                QuickSave();
            }
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
                case ChannelMode.Red: 
                    return albedoMap0 != null ? albedoMap0.name : "Texture 0";
                case ChannelMode.Green: 
                    return albedoMap1 != null ? albedoMap1.name : "Texture 1";
                case ChannelMode.Blue: 
                    return albedoMap2 != null ? albedoMap2.name : "Texture 2";
                case ChannelMode.Alpha: 
                    return albedoMap3 != null ? albedoMap3.name : "Texture 3";
                case ChannelMode.Eraser: 
                    return "ERASE";
                default: 
                    return "";
            }
        }
    }
}
