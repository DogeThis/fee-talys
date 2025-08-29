using UnityEngine;
using System.Collections.Generic;

namespace MaskTexturePainter
{
    public class TexturePaintHistory
    {
        private Stack<Texture2D> undoStack = new Stack<Texture2D>();
        private Stack<Texture2D> redoStack = new Stack<Texture2D>();
        private int maxHistorySize = 20;
        
        public void RecordState(Texture2D texture)
        {
            if (texture == null) return;
            
            Texture2D copy = DuplicateTexture(texture);
            undoStack.Push(copy);
            redoStack.Clear();
            
            while (undoStack.Count > maxHistorySize)
            {
                var oldest = GetOldestFromStack(undoStack);
                if (oldest != null)
                {
                    Object.DestroyImmediate(oldest);
                }
            }
        }
        
        public Texture2D Undo()
        {
            if (undoStack.Count > 1)
            {
                Texture2D current = undoStack.Pop();
                redoStack.Push(current);
                
                Texture2D previous = undoStack.Peek();
                return DuplicateTexture(previous);
            }
            else if (undoStack.Count == 1)
            {
                return DuplicateTexture(undoStack.Peek());
            }
            
            return null;
        }
        
        public Texture2D Redo()
        {
            if (redoStack.Count > 0)
            {
                Texture2D redo = redoStack.Pop();
                undoStack.Push(redo);
                return DuplicateTexture(redo);
            }
            
            return null;
        }
        
        public bool CanUndo()
        {
            return undoStack.Count > 1;
        }
        
        public bool CanRedo()
        {
            return redoStack.Count > 0;
        }
        
        public void Clear()
        {
            while (undoStack.Count > 0)
            {
                var texture = undoStack.Pop();
                if (texture != null)
                {
                    Object.DestroyImmediate(texture);
                }
            }
            
            while (redoStack.Count > 0)
            {
                var texture = redoStack.Pop();
                if (texture != null)
                {
                    Object.DestroyImmediate(texture);
                }
            }
        }
        
        private Texture2D DuplicateTexture(Texture2D source)
        {
            if (source == null) return null;
            
            Texture2D copy = new Texture2D(source.width, source.height, source.format, false);
            copy.SetPixels(source.GetPixels());
            copy.Apply();
            return copy;
        }
        
        private Texture2D GetOldestFromStack(Stack<Texture2D> stack)
        {
            if (stack.Count == 0) return null;
            
            Texture2D[] array = stack.ToArray();
            Texture2D oldest = array[array.Length - 1];
            
            Stack<Texture2D> temp = new Stack<Texture2D>();
            while (stack.Count > 1)
            {
                temp.Push(stack.Pop());
            }
            
            stack.Pop();
            
            while (temp.Count > 0)
            {
                stack.Push(temp.Pop());
            }
            
            return oldest;
        }
    }
}