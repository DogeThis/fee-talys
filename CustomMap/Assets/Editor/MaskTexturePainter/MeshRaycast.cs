using UnityEngine;
using UnityEditor;

namespace MaskTexturePainter
{
    public static class MeshRaycast
    {
        public struct MeshRaycastHit
        {
            public bool hit;
            public Vector3 point;
            public Vector3 normal;
            public Vector2 textureCoord;
            public float distance;
            public int triangleIndex;
        }
        
        public static bool Raycast(MeshFilter meshFilter, Ray ray, out MeshRaycastHit hit, float maxDistance = Mathf.Infinity)
        {
            hit = new MeshRaycastHit();
            
            if (meshFilter == null || meshFilter.sharedMesh == null)
                return false;
            
            Mesh mesh = meshFilter.sharedMesh;
            Transform transform = meshFilter.transform;
            
            // Transform ray to local space
            Ray localRay = new Ray(
                transform.InverseTransformPoint(ray.origin),
                transform.InverseTransformDirection(ray.direction)
            );
            
            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            Vector2[] uvs = mesh.uv;
            int[] triangles = mesh.triangles;
            
            float closestDistance = maxDistance;
            bool foundHit = false;
            
            // Check each triangle
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int i0 = triangles[i];
                int i1 = triangles[i + 1];
                int i2 = triangles[i + 2];
                
                Vector3 v0 = vertices[i0];
                Vector3 v1 = vertices[i1];
                Vector3 v2 = vertices[i2];
                
                // Raycast against triangle
                if (RaycastTriangle(localRay, v0, v1, v2, out Vector3 hitPoint, out float distance))
                {
                    if (distance < closestDistance && distance > 0)
                    {
                        closestDistance = distance;
                        
                        // Calculate barycentric coordinates
                        Vector3 baryCoords = GetBarycentricCoordinates(hitPoint, v0, v1, v2);
                        
                        // Interpolate normal
                        Vector3 n0 = normals.Length > i0 ? normals[i0] : Vector3.up;
                        Vector3 n1 = normals.Length > i1 ? normals[i1] : Vector3.up;
                        Vector3 n2 = normals.Length > i2 ? normals[i2] : Vector3.up;
                        Vector3 interpolatedNormal = n0 * baryCoords.x + n1 * baryCoords.y + n2 * baryCoords.z;
                        
                        // Interpolate UV
                        Vector2 uv0 = uvs.Length > i0 ? uvs[i0] : Vector2.zero;
                        Vector2 uv1 = uvs.Length > i1 ? uvs[i1] : Vector2.zero;
                        Vector2 uv2 = uvs.Length > i2 ? uvs[i2] : Vector2.zero;
                        Vector2 interpolatedUV = uv0 * baryCoords.x + uv1 * baryCoords.y + uv2 * baryCoords.z;
                        
                        // Transform back to world space
                        hit.point = transform.TransformPoint(hitPoint);
                        hit.normal = transform.TransformDirection(interpolatedNormal).normalized;
                        hit.textureCoord = interpolatedUV;
                        hit.distance = closestDistance;
                        hit.triangleIndex = i / 3;
                        hit.hit = true;
                        foundHit = true;
                    }
                }
            }
            
            return foundHit;
        }
        
        private static bool RaycastTriangle(Ray ray, Vector3 v0, Vector3 v1, Vector3 v2, out Vector3 hitPoint, out float distance)
        {
            hitPoint = Vector3.zero;
            distance = 0;
            
            // Möller–Trumbore intersection algorithm
            Vector3 edge1 = v1 - v0;
            Vector3 edge2 = v2 - v0;
            Vector3 h = Vector3.Cross(ray.direction, edge2);
            float a = Vector3.Dot(edge1, h);
            
            if (a > -0.00001f && a < 0.00001f)
                return false;
            
            float f = 1.0f / a;
            Vector3 s = ray.origin - v0;
            float u = f * Vector3.Dot(s, h);
            
            if (u < 0.0f || u > 1.0f)
                return false;
            
            Vector3 q = Vector3.Cross(s, edge1);
            float v = f * Vector3.Dot(ray.direction, q);
            
            if (v < 0.0f || u + v > 1.0f)
                return false;
            
            float t = f * Vector3.Dot(edge2, q);
            
            if (t > 0.00001f)
            {
                hitPoint = ray.origin + ray.direction * t;
                distance = t;
                return true;
            }
            
            return false;
        }
        
        private static Vector3 GetBarycentricCoordinates(Vector3 p, Vector3 v0, Vector3 v1, Vector3 v2)
        {
            Vector3 v0v1 = v1 - v0;
            Vector3 v0v2 = v2 - v0;
            Vector3 v0p = p - v0;
            
            float d00 = Vector3.Dot(v0v1, v0v1);
            float d01 = Vector3.Dot(v0v1, v0v2);
            float d11 = Vector3.Dot(v0v2, v0v2);
            float d20 = Vector3.Dot(v0p, v0v1);
            float d21 = Vector3.Dot(v0p, v0v2);
            
            float denom = d00 * d11 - d01 * d01;
            float v = (d11 * d20 - d01 * d21) / denom;
            float w = (d00 * d21 - d01 * d20) / denom;
            float u = 1.0f - v - w;
            
            return new Vector3(u, v, w);
        }
    }
}