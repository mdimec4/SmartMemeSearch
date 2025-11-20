using System;

namespace SmartMemeSearch.Models
{
    public class MemeEmbedding
    {
        public string FilePath { get; set; } = string.Empty;
        public float[] Vector { get; set; } = Array.Empty<float>();
    }
}