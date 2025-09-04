using System;
using System.Collections.Generic;
using System.Xml;
using System.IO;
using UnityEngine;
using UnityEditor;

namespace Editor
{
    [Serializable]
    public class PersonInfo
    {
        public string Pid;
        public string Name;
        public string Jid;
        public string Fid;
        public int Gender;
        public int Level;
        public string UnitIconID;
        
        public bool IsFemale => Gender == 2;
    }
    
    [Serializable]
    public class JobInfo
    {
        public string Jid;
        public string Name;
        public string UnitIconID_M;
        public string UnitIconID_F;
        public string UnitIconWeaponID;
        public int MoveType;
        public int Rank;
    }
    
    public class DisposDataLoader
    {
        private static DisposDataLoader _instance;
        public static DisposDataLoader Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new DisposDataLoader();
                    _instance.LoadAllData();
                }
                return _instance;
            }
        }
        
        private Dictionary<string, PersonInfo> personData = new Dictionary<string, PersonInfo>();
        private Dictionary<string, JobInfo> jobData = new Dictionary<string, JobInfo>();
        private Dictionary<string, Texture2D> iconCache = new Dictionary<string, Texture2D>();
        
        private const string PERSON_XML_PATH = "Assets/Person.xml";
        private const string JOB_XML_PATH = "Assets/Job.xml";
        private const string ICON_FOLDER = "Assets/Editor/Unit Icons and the Last Engage";
        
        public void LoadAllData()
        {
            LoadPersonData();
            LoadJobData();
            Debug.Log($"Loaded {personData.Count} persons and {jobData.Count} jobs");
        }
        
        private void LoadPersonData()
        {
            if (!File.Exists(PERSON_XML_PATH))
            {
                Debug.LogError($"Person.xml not found at {PERSON_XML_PATH}");
                return;
            }
            
            try
            {
                XmlDocument doc = new XmlDocument();
                doc.Load(PERSON_XML_PATH);
                
                XmlNodeList dataNodes = doc.SelectNodes("//Sheet[@Name='個人']/Data/Param");
                
                foreach (XmlNode node in dataNodes)
                {
                    var person = new PersonInfo
                    {
                        Pid = node.Attributes["Pid"]?.Value ?? "",
                        Name = node.Attributes["Name"]?.Value ?? "",
                        Jid = node.Attributes["Jid"]?.Value ?? "",
                        Fid = node.Attributes["Fid"]?.Value ?? "",
                        Gender = ParseInt(node.Attributes["Gender"]?.Value, 1),
                        Level = ParseInt(node.Attributes["Level"]?.Value, 1),
                        UnitIconID = node.Attributes["UnitIconID"]?.Value ?? ""
                    };
                    
                    if (!string.IsNullOrEmpty(person.Pid))
                    {
                        personData[person.Pid] = person;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Error loading Person.xml: {e.Message}");
            }
        }
        
        private void LoadJobData()
        {
            if (!File.Exists(JOB_XML_PATH))
            {
                Debug.LogError($"Job.xml not found at {JOB_XML_PATH}");
                return;
            }
            
            try
            {
                XmlDocument doc = new XmlDocument();
                doc.Load(JOB_XML_PATH);
                
                XmlNodeList dataNodes = doc.SelectNodes("//Sheet[@Name='兵種']/Data/Param");
                
                foreach (XmlNode node in dataNodes)
                {
                    var job = new JobInfo
                    {
                        Jid = node.Attributes["Jid"]?.Value ?? "",
                        Name = node.Attributes["Name"]?.Value ?? "",
                        UnitIconID_M = node.Attributes["UnitIconID_M"]?.Value ?? "",
                        UnitIconID_F = node.Attributes["UnitIconID_F"]?.Value ?? "",
                        UnitIconWeaponID = node.Attributes["UnitIconWeaponID"]?.Value ?? "",
                        MoveType = ParseInt(node.Attributes["MoveType"]?.Value, 1),
                        Rank = ParseInt(node.Attributes["Rank"]?.Value, 0)
                    };
                    
                    if (!string.IsNullOrEmpty(job.Jid))
                    {
                        jobData[job.Jid] = job;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Error loading Job.xml: {e.Message}");
            }
        }
        
        public PersonInfo GetPerson(string pid)
        {
            if (string.IsNullOrEmpty(pid))
                return null;
            
            personData.TryGetValue(pid, out PersonInfo person);
            return person;
        }
        
        public JobInfo GetJob(string jid)
        {
            if (string.IsNullOrEmpty(jid))
                return null;
            
            jobData.TryGetValue(jid, out JobInfo job);
            return job;
        }
        
        public string GetUnitIconPath(DisposEntry entry)
        {
            if (entry == null)
                return null;
            
            var person = GetPerson(entry.Pid);
            if (person == null)
                return null;
            
            string jid = !string.IsNullOrEmpty(entry.Jid) ? entry.Jid : person.Jid;
            var job = GetJob(jid);
            if (job == null)
                return null;
            
            string iconId = person.IsFemale ? job.UnitIconID_F : job.UnitIconID_M;
            if (string.IsNullOrEmpty(iconId))
            {
                iconId = job.UnitIconID_M;
            }
            
            if (string.IsNullOrEmpty(iconId))
                return null;
            
            string weaponId = job.UnitIconWeaponID;
            if (string.IsNullOrEmpty(weaponId))
                weaponId = "NoWeapon";
            
            return $"{iconId}_{weaponId}";
        }
        
        public Texture2D GetUnitIcon(DisposEntry entry)
        {
            string iconPath = GetUnitIconPath(entry);
            if (string.IsNullOrEmpty(iconPath))
                return null;
            
            if (iconCache.TryGetValue(iconPath, out Texture2D cached))
                return cached;
            
            string[] possiblePaths = new string[]
            {
                $"{ICON_FOLDER}/*{iconPath}.png",
                $"{ICON_FOLDER}/*_{iconPath}.png",
                $"{ICON_FOLDER}/*{iconPath.Replace("_", "_*")}.png"
            };
            
            foreach (string pattern in possiblePaths)
            {
                string[] files = Directory.GetFiles(Path.GetDirectoryName(pattern), Path.GetFileName(pattern));
                if (files.Length > 0)
                {
                    Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(files[0]);
                    if (texture != null)
                    {
                        iconCache[iconPath] = texture;
                        return texture;
                    }
                }
            }
            
            string directPath = $"{ICON_FOLDER}/{iconPath}.png";
            if (File.Exists(directPath))
            {
                Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(directPath);
                if (texture != null)
                {
                    iconCache[iconPath] = texture;
                    return texture;
                }
            }
            
            return null;
        }
        
        public string GetUnitDisplayName(DisposEntry entry)
        {
            if (entry == null)
                return "Unknown";
            
            if (entry.IsGroupHeader)
                return entry.Group;
            
            var person = GetPerson(entry.Pid);
            if (person != null && !string.IsNullOrEmpty(person.Name))
            {
                return person.Name;
            }
            
            if (!string.IsNullOrEmpty(entry.Pid))
            {
                string pid = entry.Pid;
                if (pid.StartsWith("PID_"))
                    pid = pid.Substring(4);
                return pid;
            }
            
            return "Unknown";
        }
        
        public Color GetForceColor(int force)
        {
            switch (force)
            {
                case 0: return new Color(0.2f, 0.4f, 0.8f, 1f);
                case 1: return new Color(0.8f, 0.2f, 0.2f, 1f);
                case 2: return new Color(0.2f, 0.8f, 0.2f, 1f);
                case 3: return new Color(0.8f, 0.8f, 0.2f, 1f);
                default: return Color.gray;
            }
        }
        
        public string GetForceName(int force)
        {
            switch (force)
            {
                case 0: return "Player";
                case 1: return "Enemy";
                case 2: return "Ally";
                case 3: return "Other";
                default: return "Unknown";
            }
        }
        
        private int ParseInt(string value, int defaultValue = 0)
        {
            if (string.IsNullOrEmpty(value))
                return defaultValue;
            
            if (int.TryParse(value, out int result))
                return result;
            
            return defaultValue;
        }
        
        public void ClearCache()
        {
            iconCache.Clear();
        }
        
        public void ReloadData()
        {
            personData.Clear();
            jobData.Clear();
            iconCache.Clear();
            LoadAllData();
        }
    }
}