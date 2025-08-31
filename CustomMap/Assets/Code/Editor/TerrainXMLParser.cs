using System.Collections.Generic;
using System.Xml;
using UnityEngine;
using UnityEditor;
using System.IO;

namespace Editor
{
    public static class TerrainXMLParser
    {
        private const string TERRAIN_XML_PATH = "Assets/Terrain.xml";
        private const string DATABASE_PATH = "Assets/Code/Editor/TerrainTypeDatabase.asset";
        
        [MenuItem("Tools/Parse Terrain XML")]
        public static void ParseTerrainXML()
        {
            if (!File.Exists(TERRAIN_XML_PATH))
            {
                Debug.LogError($"Terrain.xml not found at {TERRAIN_XML_PATH}");
                return;
            }
            
            List<TerrainType> terrainTypes = new List<TerrainType>();
            
            try
            {
                XmlDocument doc = new XmlDocument();
                doc.Load(TERRAIN_XML_PATH);
                
                XmlNodeList dataNodes = doc.SelectNodes("//Sheet[@Name='地形']/Data/Param");
                
                foreach (XmlNode node in dataNodes)
                {
                    string tid = node.Attributes["Tid"]?.Value;
                    string name = node.Attributes["Name"]?.Value;
                    string colorRStr = node.Attributes["ColorR"]?.Value;
                    string colorGStr = node.Attributes["ColorG"]?.Value;
                    string colorBStr = node.Attributes["ColorB"]?.Value;
                    
                    if (!string.IsNullOrEmpty(tid))
                    {
                        int r = 128, g = 128, b = 128;
                        
                        if (!string.IsNullOrEmpty(colorRStr))
                            int.TryParse(colorRStr, out r);
                        if (!string.IsNullOrEmpty(colorGStr))
                            int.TryParse(colorGStr, out g);
                        if (!string.IsNullOrEmpty(colorBStr))
                            int.TryParse(colorBStr, out b);
                        
                        terrainTypes.Add(new TerrainType(tid, name ?? tid, r, g, b));
                    }
                }
                
                TerrainTypeDatabase database = AssetDatabase.LoadAssetAtPath<TerrainTypeDatabase>(DATABASE_PATH);
                
                if (database == null)
                {
                    database = ScriptableObject.CreateInstance<TerrainTypeDatabase>();
                    AssetDatabase.CreateAsset(database, DATABASE_PATH);
                }
                
                database.Initialize(terrainTypes);
                
                Debug.Log($"Successfully parsed {terrainTypes.Count} terrain types from Terrain.xml");
                
                EditorUtility.DisplayDialog("Success", 
                    $"Parsed {terrainTypes.Count} terrain types from Terrain.xml\n" +
                    $"Database saved to {DATABASE_PATH}", 
                    "OK");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error parsing Terrain.xml: {e.Message}");
                EditorUtility.DisplayDialog("Error", 
                    $"Failed to parse Terrain.xml:\n{e.Message}", 
                    "OK");
            }
        }
        
        [MenuItem("Tools/Validate Terrain Database")]
        public static void ValidateDatabase()
        {
            TerrainTypeDatabase database = AssetDatabase.LoadAssetAtPath<TerrainTypeDatabase>(DATABASE_PATH);
            
            if (database == null)
            {
                Debug.LogError($"No terrain database found at {DATABASE_PATH}. Run 'Tools/Parse Terrain XML' first.");
                return;
            }
            
            Debug.Log($"Terrain database contains {database.Count} terrain types");
            
            string[] testTids = { "TID_平地", "TID_海", "TID_山", "TID_茂み", "TID_SEA069" };
            foreach (string tid in testTids)
            {
                var terrain = database.GetTerrainType(tid);
                if (terrain != null)
                {
                    Debug.Log($"{tid}: Name='{terrain.name}', Color={terrain.color}");
                }
                else
                {
                    Debug.LogWarning($"{tid}: Not found in database");
                }
            }
        }
    }
}