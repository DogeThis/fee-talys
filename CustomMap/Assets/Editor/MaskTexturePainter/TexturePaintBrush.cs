using UnityEngine;

namespace MaskTexturePainter
{
    public class TexturePaintBrush
    {
        private float size;
        private float strength;
        private AnimationCurve falloff;
        
        public TexturePaintBrush(float size, float strength, AnimationCurve falloff)
        {
            this.size = size;
            this.strength = strength;
            this.falloff = falloff ?? AnimationCurve.EaseInOut(0, 1, 1, 0);
        }
        
        public void Paint(Texture2D texture, Vector2 uv, MaskTexturePainterWindow.ChannelMode channel)
        {
            if (texture == null) return;
            
            int centerX = Mathf.RoundToInt(uv.x * texture.width);
            int centerY = Mathf.RoundToInt(uv.y * texture.height);
            
            // Brush radius in pixels - size is 0-10, map to reasonable pixel range
            // For a 1024x1024 texture: size 1 = ~10 pixels, size 10 = ~100 pixels
            int brushRadius = Mathf.RoundToInt(size * texture.width * 0.01f);
            
            int minX = Mathf.Max(0, centerX - brushRadius);
            int maxX = Mathf.Min(texture.width - 1, centerX + brushRadius);
            int minY = Mathf.Max(0, centerY - brushRadius);
            int maxY = Mathf.Min(texture.height - 1, centerY + brushRadius);
            
            Color[] pixels = texture.GetPixels(minX, minY, maxX - minX + 1, maxY - minY + 1);
            
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x, y), new Vector2(centerX, centerY));
                    
                    if (distance <= brushRadius)
                    {
                        // Normalize distance from 0 (center) to 1 (edge)
                        float normalizedDistance = distance / brushRadius;
                        
                        // Apply falloff curve - expecting curve to go from 1 at x=0 (center) to 0 at x=1 (edge)
                        float falloffValue = falloff.Evaluate(normalizedDistance);
                        
                        // Apply brush strength
                        float paintStrength = strength * falloffValue;
                        
                        int pixelIndex = (y - minY) * (maxX - minX + 1) + (x - minX);
                        Color currentColor = pixels[pixelIndex];
                        
                        switch (channel)
                        {
                            case MaskTexturePainterWindow.ChannelMode.Red:
                                currentColor.r = Mathf.Lerp(currentColor.r, 1f, paintStrength);
                                break;
                            case MaskTexturePainterWindow.ChannelMode.Green:
                                currentColor.g = Mathf.Lerp(currentColor.g, 1f, paintStrength);
                                break;
                            case MaskTexturePainterWindow.ChannelMode.Blue:
                                currentColor.b = Mathf.Lerp(currentColor.b, 1f, paintStrength);
                                break;
                            case MaskTexturePainterWindow.ChannelMode.Alpha:
                                currentColor.a = Mathf.Lerp(currentColor.a, 1f, paintStrength);
                                break;
                            case MaskTexturePainterWindow.ChannelMode.Eraser:
                                currentColor = Color.Lerp(currentColor, Color.clear, paintStrength);
                                break;
                        }
                        
                        pixels[pixelIndex] = currentColor;
                    }
                }
            }
            
            texture.SetPixels(minX, minY, maxX - minX + 1, maxY - minY + 1, pixels);
        }
    }
}