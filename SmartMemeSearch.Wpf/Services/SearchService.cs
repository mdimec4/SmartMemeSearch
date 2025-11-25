using System;
using System.Collections.Generic;
using System.Linq;
using SmartMemeSearch.Models;

namespace SmartMemeSearch.Wpf.Services
{
    public class SearchService
    {
        private readonly ClipService _clip;
        private readonly DatabaseService _db;

        // Tunable weights: how much CLIP vs OCR influences final score
        private readonly double _clipWeight = 0.7;
        private readonly double _ocrWeight = 0.3;

        public SearchService(ClipService clip, DatabaseService db)
        {
            _clip = clip;
            _db = db;
        }

        /// <summary>
        /// Search memes by text query using CLIP + OCR.
        /// Returns results sorted by descending score.
        /// </summary>
        public List<SearchResult> Search(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return new List<SearchResult>();

            string q = query.ToLowerInvariant();

            // 1. Get CLIP text embedding for the query
            float[] queryVec = _clip.GetTextEmbedding(query);

            // 2. Pull all embeddings from DB
            var items = _db.GetAllEmbeddings();

            var results = new List<SearchResult>();

            foreach (var item in items)
            {
                // CLIP semantic similarity
                double clipScore = MathService.Cosine(queryVec, item.Vector);

                // OCR keyword match score
                double ocrScore = ComputeOcrScore(q, item.OcrText);

                double finalScore = _clipWeight * clipScore + _ocrWeight * ocrScore;

                // Skip very low scores if you want
                // if (finalScore < 0.05) continue;

                results.Add(new SearchResult
                {
                    FilePath = item.FilePath,
                    Score = finalScore,
                    OcrPreview = item.OcrText // optional, for UI later
                });
            }

            // 3. Sort highest-first
            return results
                .OrderByDescending(r => r.Score)
                .ToList();
        }

        /// <summary>
        /// Very simple OCR scoring:
        ///  - 1.0 if query substring appears in OCR
        ///  - 0.5 if any query word appears
        ///  - 0.0 otherwise
        /// You can make this smarter later.
        /// </summary>
        private static double ComputeOcrScore(string queryLower, string ocrText)
        {
            if (string.IsNullOrWhiteSpace(ocrText))
                return 0.0;

            string ocr = ocrText.ToLowerInvariant();

            if (ocr.Contains(queryLower))
                return 1.0;

            // basic partial-word match
            var qWords = queryLower.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var oWords = ocr.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (qWords.Length == 0 || oWords.Length == 0)
                return 0.0;

            var oSet = new HashSet<string>(oWords);

            foreach (var w in qWords)
            {
                if (oSet.Contains(w))
                    return 0.5;
            }

            return 0.0;
        }
    }
}
