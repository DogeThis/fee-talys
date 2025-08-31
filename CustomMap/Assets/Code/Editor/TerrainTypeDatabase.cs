using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace Editor
{
    [CreateAssetMenu(fileName = "TerrainTypeDatabase", menuName = "MapEditor/TerrainTypeDatabase")]
    public class TerrainTypeDatabase : ScriptableObject
    {
        [SerializeField]
        private List<TerrainType> terrainTypes = new List<TerrainType>();
        
        private Dictionary<string, TerrainType> tidLookup;
        
        private static TerrainTypeDatabase instance;
        
        public static TerrainTypeDatabase Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = AssetDatabase.LoadAssetAtPath<TerrainTypeDatabase>("Assets/Code/Editor/TerrainTypeDatabase.asset");
                }
                return instance;
            }
        }
        
        public void Initialize(List<TerrainType> types)
        {
            terrainTypes = types;
            RebuildLookup();
            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssets();
        }
        
        private void RebuildLookup()
        {
            tidLookup = new Dictionary<string, TerrainType>();
            foreach (var terrain in terrainTypes)
            {
                if (!string.IsNullOrEmpty(terrain.tid))
                {
                    tidLookup[terrain.tid] = terrain;
                }
            }
        }
        
        public TerrainType GetTerrainType(string tid)
        {
            if (tidLookup == null)
            {
                RebuildLookup();
            }
            
            if (tidLookup.TryGetValue(tid, out TerrainType terrain))
            {
                return terrain;
            }
            
            return null;
        }
        
        public Color GetTerrainColor(string tid, Color defaultColor)
        {
            var terrain = GetTerrainType(tid);
            return terrain != null ? terrain.color : defaultColor;
        }
        
        public string GetTerrainName(string tid)
        {
            var terrain = GetTerrainType(tid);
            return terrain != null ? terrain.name : tid;
        }
        
        public List<TerrainType> GetAllTerrainTypes()
        {
            return new List<TerrainType>(terrainTypes);
        }
        
        public int Count => terrainTypes.Count;
    }
}