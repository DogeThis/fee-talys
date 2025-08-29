using UnityEngine;
using UnityEditor;

namespace MaskTexturePainter
{
    public static class MaskTexturePreview
    {
        private static Material previewMaterial;
        private static Shader previewShader;
        
        public static void DrawPreviewOverlay(GameObject target, Texture2D maskTexture, bool showTextureMode)
        {
            if (target == null || maskTexture == null) return;
            
            MeshRenderer renderer = target.GetComponent<MeshRenderer>();
            MeshFilter meshFilter = target.GetComponent<MeshFilter>();
            
            if (renderer == null || meshFilter == null || meshFilter.sharedMesh == null) return;
            
            if (showTextureMode)
            {
                DrawTexturePreview(renderer, meshFilter.sharedMesh, maskTexture);
            }
            else
            {
                DrawMaskChannels(renderer, meshFilter.sharedMesh, maskTexture);
            }
        }
        
        private static void DrawMaskChannels(MeshRenderer renderer, Mesh mesh, Texture2D maskTexture)
        {
            CreatePreviewMaterial();
            
            if (previewMaterial != null)
            {
                previewMaterial.SetTexture("_MainTex", maskTexture);
                previewMaterial.SetFloat("_Mode", 0);
                
                for (int i = 0; i < mesh.subMeshCount; i++)
                {
                    Graphics.DrawMesh(mesh, renderer.transform.localToWorldMatrix, previewMaterial, 0, null, i);
                }
            }
        }
        
        private static void DrawTexturePreview(MeshRenderer renderer, Mesh mesh, Texture2D maskTexture)
        {
            Material originalMaterial = renderer.sharedMaterial;
            if (originalMaterial == null) return;
            
            CreatePreviewMaterial();
            
            if (previewMaterial != null)
            {
                Texture baseMap1 = originalMaterial.GetTexture("_BaseMap");
                Texture baseMap2 = originalMaterial.GetTexture("_BaseMap2");
                Texture baseMap3 = originalMaterial.GetTexture("_BaseMap3");
                Texture baseMap4 = originalMaterial.GetTexture("_BaseMap4");
                
                previewMaterial.SetTexture("_MainTex", maskTexture);
                previewMaterial.SetTexture("_Tex1", baseMap1);
                previewMaterial.SetTexture("_Tex2", baseMap2);
                previewMaterial.SetTexture("_Tex3", baseMap3);
                previewMaterial.SetTexture("_Tex4", baseMap4);
                previewMaterial.SetFloat("_Mode", 1);
                
                for (int i = 0; i < mesh.subMeshCount; i++)
                {
                    Graphics.DrawMesh(mesh, renderer.transform.localToWorldMatrix, previewMaterial, 0, null, i);
                }
            }
        }
        
        private static void CreatePreviewMaterial()
        {
            if (previewShader == null)
            {
                string shaderCode = @"
                    Shader ""Hidden/MaskTexturePreview""
                    {
                        Properties
                        {
                            _MainTex (""Mask"", 2D) = ""white"" {}
                            _Tex1 (""Texture 1"", 2D) = ""white"" {}
                            _Tex2 (""Texture 2"", 2D) = ""white"" {}
                            _Tex3 (""Texture 3"", 2D) = ""white"" {}
                            _Tex4 (""Texture 4"", 2D) = ""white"" {}
                            _Mode (""Mode"", Float) = 0
                        }
                        SubShader
                        {
                            Tags { ""RenderType""=""Transparent"" ""Queue""=""Overlay"" }
                            LOD 100
                            
                            Pass
                            {
                                ZWrite Off
                                ZTest LEqual
                                Blend SrcAlpha OneMinusSrcAlpha
                                
                                CGPROGRAM
                                #pragma vertex vert
                                #pragma fragment frag
                                
                                #include ""UnityCG.cginc""
                                
                                struct appdata
                                {
                                    float4 vertex : POSITION;
                                    float2 uv : TEXCOORD0;
                                };
                                
                                struct v2f
                                {
                                    float2 uv : TEXCOORD0;
                                    float4 vertex : SV_POSITION;
                                };
                                
                                sampler2D _MainTex;
                                sampler2D _Tex1, _Tex2, _Tex3, _Tex4;
                                float4 _MainTex_ST;
                                float _Mode;
                                
                                v2f vert (appdata v)
                                {
                                    v2f o;
                                    o.vertex = UnityObjectToClipPos(v.vertex);
                                    o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                                    return o;
                                }
                                
                                fixed4 frag (v2f i) : SV_Target
                                {
                                    fixed4 mask = tex2D(_MainTex, i.uv);
                                    
                                    if (_Mode < 0.5)
                                    {
                                        return fixed4(mask.rgb, 0.7);
                                    }
                                    else
                                    {
                                        fixed4 tex1 = tex2D(_Tex1, i.uv);
                                        fixed4 tex2 = tex2D(_Tex2, i.uv);
                                        fixed4 tex3 = tex2D(_Tex3, i.uv);
                                        fixed4 tex4 = tex2D(_Tex4, i.uv);
                                        
                                        float remaining = 1.0 - (mask.r + mask.g + mask.b);
                                        fixed3 result = tex4.rgb * remaining + 
                                                       tex1.rgb * mask.g + 
                                                       tex2.rgb * mask.b + 
                                                       tex3.rgb * mask.a;
                                        
                                        return fixed4(result, 0.9);
                                    }
                                }
                                ENDCG
                            }
                        }
                    }";
                
                previewShader = ShaderUtil.CreateShaderAsset(shaderCode, false);
            }
            
            if (previewMaterial == null && previewShader != null)
            {
                previewMaterial = new Material(previewShader);
                previewMaterial.hideFlags = HideFlags.HideAndDontSave;
            }
        }
        
        public static void DrawBrushPreview(Vector3 position, Vector3 normal, float size, Color color)
        {
            Handles.color = new Color(color.r, color.g, color.b, 0.5f);
            
            Matrix4x4 matrix = Matrix4x4.TRS(position, Quaternion.LookRotation(normal), Vector3.one);
            using (new Handles.DrawingScope(matrix))
            {
                Handles.DrawWireDisc(Vector3.zero, Vector3.forward, size);
                
                int segments = 32;
                for (int i = 0; i < segments; i++)
                {
                    float angle = (i / (float)segments) * 2 * Mathf.PI;
                    float nextAngle = ((i + 1) / (float)segments) * 2 * Mathf.PI;
                    
                    Vector3 p1 = new Vector3(Mathf.Cos(angle) * size * 0.9f, Mathf.Sin(angle) * size * 0.9f, 0);
                    Vector3 p2 = new Vector3(Mathf.Cos(nextAngle) * size * 0.9f, Mathf.Sin(nextAngle) * size * 0.9f, 0);
                    
                    if (i % 2 == 0)
                    {
                        Handles.DrawLine(p1, p2);
                    }
                }
            }
        }
        
        public static void Cleanup()
        {
            if (previewMaterial != null)
            {
                Object.DestroyImmediate(previewMaterial);
                previewMaterial = null;
            }
            
            if (previewShader != null)
            {
                Object.DestroyImmediate(previewShader);
                previewShader = null;
            }
        }
    }
}