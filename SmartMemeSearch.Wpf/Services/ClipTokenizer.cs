using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SmartMemeSearch.Wpf.Services
{
    /// <summary>
    /// Real CLIP tokenizer compatible with Xenova/clip-vit-base-patch32.
    /// Loads vocab.json + merges.txt and performs byte-level BPE.
    /// Outputs Int64 IDs padded/truncated to 77 tokens.
    /// </summary>
    public class ClipTokenizer
    {
        // CLIP standard sequence length
        private const int MaxLength = 77;

        // Special token IDs used by CLIP (common values for ViT-B/32)
        // If these do not match your vocab, we’ll map them from vocab.json by name.
        private const string StartToken = "<|startoftext|>";
        private const string EndToken = "<|endoftext|>";

        private readonly Dictionary<string, int> _vocab;
        private readonly Dictionary<(string, string), int> _bpeRanks;
        private readonly Regex _tokenPattern;

        // byte encoder/decoder maps
        private readonly Dictionary<byte, char> _byteEncoder;
        private readonly Dictionary<char, byte> _byteDecoder;

        public ClipTokenizer(string vocabPath, string mergesPath)
        {
            _vocab = LoadVocab(vocabPath);
            _bpeRanks = LoadMerges(mergesPath);

            _tokenPattern = new Regex(
                @"'s|'t|'re|'ve|'m|'ll|'d| ?\p{L}+| ?\p{N}+| ?[^\s\p{L}\p{N}]+|\s+(?!\S)|\s+",
                RegexOptions.Compiled);

            (_byteEncoder, _byteDecoder) = BuildByteEncoderDecoder();
        }

        #region Public API

        public long[] EncodeToIds(string text)
        {
            // 1. Pre-tokenize into “words” using CLIP regex
            var tokens = new List<int>();

            // Add start token
            if (_vocab.TryGetValue(StartToken, out int startId))
                tokens.Add(startId);

            foreach (Match m in _tokenPattern.Matches(text))
            {
                string piece = m.Value;
                // 2. Convert piece to “bytes” then to CLIP byte-level chars
                string encoded = BytesToUnicode(Encoding.UTF8.GetBytes(piece));
                // 3. Run BPE on that
                foreach (var bpeToken in Bpe(encoded).Split(' '))
                {
                    if (_vocab.TryGetValue(bpeToken, out int id))
                        tokens.Add(id);
                }
            }

            // Add end token
            if (_vocab.TryGetValue(EndToken, out int endId))
                tokens.Add(endId);

            // Pad / truncate to MaxLength
            if (tokens.Count > MaxLength)
            {
                tokens = tokens.Take(MaxLength).ToList();
                tokens[MaxLength - 1] = endId; // make sure sequence ends with </end>
            }

            var result = new long[MaxLength];
            for (int i = 0; i < tokens.Count; i++)
                result[i] = tokens[i];

            // Remaining positions are 0 (usually pad token)
            return result;
        }

        #endregion

        #region Vocab / merges loading

        private static Dictionary<string, int> LoadVocab(string path)
        {
            using var fs = File.OpenRead(path);
            var json = JsonDocument.Parse(fs);

            var dict = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var prop in json.RootElement.EnumerateObject())
            {
                dict[prop.Name] = prop.Value.GetInt32();
            }
            return dict;
        }

        private static Dictionary<(string, string), int> LoadMerges(string path)
        {
            var ranks = new Dictionary<(string, string), int>();
            var lines = File.ReadAllLines(path);

            int index = 0;
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (line.StartsWith("#")) continue;

                var parts = line.Split(' ');
                if (parts.Length != 2) continue;

                ranks[(parts[0], parts[1])] = index;
                index++;
            }

            return ranks;
        }

        #endregion

        #region Byte encoder/decoder

        private static (Dictionary<byte, char>, Dictionary<char, byte>) BuildByteEncoderDecoder()
        {
            // This reproduces OpenAI’s bytes_to_unicode mapping.
            var bs = new List<int>();
            for (int i = '!'; i <= '~'; i++) bs.Add(i);
            for (int i = '¡'; i <= '¬'; i++) bs.Add(i);
            for (int i = '®'; i <= 'ÿ'; i++) bs.Add(i);

            var cs = new List<int>(bs);
            int n = 0;
            for (int b = 0; b < 256; b++)
            {
                if (!bs.Contains(b))
                {
                    bs.Add(b);
                    cs.Add(256 + n);
                    n++;
                }
            }

            var byteEncoder = new Dictionary<byte, char>();
            var byteDecoder = new Dictionary<char, byte>();

            for (int i = 0; i < bs.Count; i++)
            {
                byteEncoder[(byte)bs[i]] = (char)cs[i];
                byteDecoder[(char)cs[i]] = (byte)bs[i];
            }

            return (byteEncoder, byteDecoder);
        }

        private string BytesToUnicode(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length);
            foreach (var b in bytes)
                sb.Append(_byteEncoder[b]);
            return sb.ToString();
        }

        #endregion

        #region BPE core

        private static IEnumerable<(string, string)> GetPairs(IList<string> symbols)
        {
            var pairs = new HashSet<(string, string)>();
            string prev = symbols[0];
            for (int i = 1; i < symbols.Count; i++)
            {
                string cur = symbols[i];
                pairs.Add((prev, cur));
                prev = cur;
            }
            return pairs;
        }

        private string Bpe(string token)
        {
            // If token already processed
            if (token.Length == 0)
                return token;

            // Split into characters
            var word = token.Select(c => c.ToString()).ToList();
            if (word.Count == 1)
                return token;

            var pairs = GetPairs(word).ToHashSet();

            while (true)
            {
                (string, string)? bigram = null;
                int bestRank = int.MaxValue;

                foreach (var pair in pairs)
                {
                    if (_bpeRanks.TryGetValue(pair, out int rank))
                    {
                        if (rank < bestRank)
                        {
                            bestRank = rank;
                            bigram = pair;
                        }
                    }
                }

                if (bigram == null)
                    break;

                string first = bigram.Value.Item1;
                string second = bigram.Value.Item2;
                var newWord = new List<string>();

                int i = 0;
                while (i < word.Count)
                {
                    int j = word.IndexOf(first, i);
                    if (j == -1 || j == word.Count - 1)
                    {
                        newWord.AddRange(word.Skip(i));
                        break;
                    }

                    newWord.AddRange(word.Skip(i).Take(j - i));
                    if (word[j] == first && word[j + 1] == second)
                    {
                        newWord.Add(first + second);
                        i = j + 2;
                    }
                    else
                    {
                        newWord.Add(word[j]);
                        i = j + 1;
                    }
                }

                word = newWord;
                if (word.Count == 1)
                    break;

                pairs = GetPairs(word).ToHashSet();
            }

            return string.Join(" ", word);
        }

        #endregion
    }
}
