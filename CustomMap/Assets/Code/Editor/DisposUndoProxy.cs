using UnityEngine;
using UnityEditor;
using System.Linq;

namespace Editor
{
    // ScriptableObject proxy to enable undo/redo for DisposDocument
    // Since DisposEntry is not a UnityEngine.Object, we need this proxy
    public class DisposUndoProxy : ScriptableObject
    {
        [SerializeField]
        private DisposDocument document;
        
        // Store the document state as JSON for undo tracking
        [SerializeField, HideInInspector]
        private string serializedState = "";
        
        public DisposDocument Document 
        { 
            get => document;
            set
            {
                document = value;
                // Don't serialize on initial set
            }
        }
        
        // Call before making any changes
        public void RecordUndo(string operationName)
        {
            Undo.RecordObject(this, operationName);
            
            // Serialize current state before changes
            if (document != null)
            {
                serializedState = JsonUtility.ToJson(new DocumentState(document), true);
            }
        }
        
        // Restore document from serialized state after undo/redo
        public void RestoreFromSerializedState()
        {
            if (!string.IsNullOrEmpty(serializedState) && document != null)
            {
                var state = JsonUtility.FromJson<DocumentState>(serializedState);
                state.ApplyToDocument(document);
            }
        }
        
        // Helper class to serialize document state
        [System.Serializable]
        private class DocumentState
        {
            [System.Serializable]
            public class SerializedEntry
            {
                public string Group;
                public string Pid;
                public int Force;
                public int Flag;
                public int AppearX;
                public int AppearY;
                public int DisposX;
                public int DisposY;
                public int Direction;
                public int LevelN;
                public int LevelH;
                public int LevelL;
                public string Jid;
                public string Sid;
                [System.Serializable]
                public class SerializedItem
                {
                    public string Iid;
                    public int Drop;
                }
                
                public SerializedItem[] Items;
                
                public SerializedEntry() { }
                
                public SerializedEntry(DisposEntry entry)
                {
                    Group = entry.Group;
                    Pid = entry.Pid;
                    Force = entry.Force;
                    Flag = entry.Flag;
                    AppearX = entry.AppearX;
                    AppearY = entry.AppearY;
                    DisposX = entry.DisposX;
                    DisposY = entry.DisposY;
                    Direction = entry.Direction;
                    LevelN = entry.LevelN;
                    LevelH = entry.LevelH;
                    LevelL = entry.LevelL;
                    Jid = entry.Jid;
                    Sid = entry.Sid;
                    
                    // Copy items
                    Items = new SerializedItem[entry.Items.Length];
                    for (int i = 0; i < entry.Items.Length; i++)
                    {
                        if (entry.Items[i] != null)
                        {
                            Items[i] = new SerializedItem
                            {
                                Iid = entry.Items[i].Iid,
                                Drop = entry.Items[i].Drop
                            };
                        }
                    }
                }
                
                public void ApplyTo(DisposEntry entry)
                {
                    entry.Group = Group;
                    entry.Pid = Pid;
                    entry.Force = Force;
                    entry.Flag = Flag;
                    entry.AppearX = AppearX;
                    entry.AppearY = AppearY;
                    entry.DisposX = DisposX;
                    entry.DisposY = DisposY;
                    entry.Direction = Direction;
                    entry.LevelN = LevelN;
                    entry.LevelH = LevelH;
                    entry.LevelL = LevelL;
                    entry.Jid = Jid;
                    entry.Sid = Sid;
                    
                    // Copy items back
                    if (Items != null)
                    {
                        for (int i = 0; i < entry.Items.Length && i < Items.Length; i++)
                        {
                            if (Items[i] != null)
                            {
                                if (entry.Items[i] == null)
                                    entry.Items[i] = new DisposEntry.ItemData();
                                    
                                entry.Items[i].Iid = Items[i].Iid;
                                entry.Items[i].Drop = Items[i].Drop;
                            }
                        }
                    }
                }
            }
            
            public SerializedEntry[] entries;
            
            public DocumentState() { }
            
            public DocumentState(DisposDocument doc)
            {
                var allEntries = new System.Collections.Generic.List<SerializedEntry>();
                foreach (var group in doc.Groups)
                {
                    foreach (var entry in group.Entries)
                    {
                        if (!entry.IsGroupHeader)
                        {
                            allEntries.Add(new SerializedEntry(entry));
                        }
                    }
                }
                entries = allEntries.ToArray();
            }
            
            public void ApplyToDocument(DisposDocument doc)
            {
                // Match entries by group and index, apply changes
                if (entries == null) return;
                
                var entryIndex = 0;
                foreach (var group in doc.Groups)
                {
                    foreach (var entry in group.Entries)
                    {
                        if (!entry.IsGroupHeader && entryIndex < entries.Length)
                        {
                            entries[entryIndex].ApplyTo(entry);
                            entryIndex++;
                        }
                    }
                }
            }
        }
    }
}