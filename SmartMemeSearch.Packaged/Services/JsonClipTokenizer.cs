using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SmartMemeSearch.Services
{
    public class JsonClipTokenizer
    {
        private const int MaxLen = 77;

        private readonly Dictionary<string, int> _vocab;
        private readonly Dictionary<(string, string), int> _merges;
        private readonly Regex _pattern;

        private readonly Dictionary<byte, char> _byteEnc;
        private readonly Dictionary<char, byte> _byteDec;

        private readonly int _startId;
        private readonly int _endId;

        public JsonClipTokenizer(string tokenizerJsonPath)
        {
            using var fs = File.OpenRead(tokenizerJsonPath);
            var json = JsonDocument.Parse(fs);
            var root = json.RootElement;

            // --------------------------------------------------------
            // Load vocab + merges from tokenizer.json
            // --------------------------------------------------------
            var model = root.GetProperty("model");

            // vocabulary
            _vocab = new Dictionary<string, int>();
            foreach (var kv in model.GetProperty("vocab").EnumerateObject())
                _vocab[kv.Name] = kv.Value.GetInt32();

            // merges
            _merges = new Dictionary<(string, string), int>();
            int rank = 0;
            foreach (var merge in model.GetProperty("merges").EnumerateArray())
            {
                var parts = merge.GetString()!.Split(' ');
                _merges[(parts[0], parts[1])] = rank++;
            }

            // --------------------------------------------------------
            // special tokens (default CLIP uses 49406 + 49407)
            // --------------------------------------------------------
            _startId = _vocab.ContainsKey("<|startoftext|>") ? _vocab["<|startoftext|>"] : 49406;
            _endId = _vocab.ContainsKey("<|endoftext|>") ? _vocab["<|endoftext|>"] : 49407;

            // --------------------------------------------------------
            // Regex pattern = same as HuggingFace CLIP
            // --------------------------------------------------------
            _pattern = new Regex(
                @"'s|'t|'re|'ve|'m|'ll|'d| ?\p{L}+| ?\p{N}+| ?[^\s\p{L}\p{N}]+|\s+(?!\S)|\s+",
                RegexOptions.Compiled);

            // --------------------------------------------------------
            // Byte encoder/decoder mapping (OpenAI CLIP standard)
            // --------------------------------------------------------
            (_byteEnc, _byteDec) = BuildByteMaps();
        }

        public (long[] inputIds, long[] attentionMask) EncodeWithMask(string text)
        {
            long[] ids = EncodeToIds(text);
            long[] mask = new long[ids.Length];

            bool inPad = false;
            for (int i = 0; i < ids.Length; i++)
            {
                if (!inPad)
                {
                    mask[i] = 1;
                    if (ids[i] == _endId)
                        inPad = true;
                }
                else
                {
                    mask[i] = 0;
                }
            }

            return (ids, mask);
        }


        // ========================================================================
        // Public API — Convert text → 77×Int64 token ids
        // ========================================================================
        public long[] EncodeToIds(string text)
        {
            var tokens = new List<int>();
            tokens.Add(_startId);

            foreach (Match m in _pattern.Matches(text))
            {
                string word = BytesToUnicode(Encoding.UTF8.GetBytes(m.Value));
                foreach (var bp in Bpe(word).Split(' '))
                    if (_vocab.TryGetValue(bp, out int id))
                        tokens.Add(id);
            }

            tokens.Add(_endId);

            // pad or truncate to 77 tokens
            if (tokens.Count > MaxLen)
            {
                tokens = tokens.Take(MaxLen).ToList();
                tokens[MaxLen - 1] = _endId;
            }

            var arr = new long[MaxLen];
            for (int i = 0; i < tokens.Count; i++)
                arr[i] = tokens[i];

            return arr;
        }


        // ========================================================================
        // Byte-pair encoding implementation
        // ========================================================================
        private IEnumerable<(string, string)> Pairs(List<string> symbols)
        {
            for (int i = 0; i < symbols.Count - 1; i++)
                yield return (symbols[i], symbols[i + 1]);
        }

        private string Bpe(string token)
        {
            var word = token.Select(c => c.ToString()).ToList();
            var pairs = new HashSet<(string, string)>(Pairs(word));

            while (true)
            {
                (string, string)? best = null;
                int bestRank = int.MaxValue;

                foreach (var p in pairs)
                {
                    if (_merges.TryGetValue(p, out int r) && r < bestRank)
                    {
                        bestRank = r;
                        best = p;
                    }
                }

                if (best == null)
                    break;

                var newWord = new List<string>();
                int i = 0;

                while (i < word.Count)
                {
                    int j = word.IndexOf(best.Value.Item1, i);
                    if (j == -1 || j == word.Count - 1)
                    {
                        newWord.AddRange(word.Skip(i));
                        break;
                    }

                    newWord.AddRange(word.Skip(i).Take(j - i));

                    if (word[j] == best.Value.Item1 && word[j + 1] == best.Value.Item2)
                    {
                        newWord.Add(word[j] + word[j + 1]);
                        i = j + 2;
                    }
                    else
                    {
                        newWord.Add(word[j]);
                        i = j + 1;
                    }
                }

                word = newWord;
                pairs = new HashSet<(string, string)>(Pairs(word));
            }

            return string.Join(" ", word);
        }


        // ========================================================================
        // Byte encoder and decoder (OpenAI CLIP standard)
        // ========================================================================
        private static (Dictionary<byte, char>, Dictionary<char, byte>) BuildByteMaps()
        {
            var bs = new List<int>();
            for (int i = '!'; i <= '~'; i++) bs.Add(i);
            for (int i = '¡'; i <= '¬'; i++) bs.Add(i);
            for (int i = '®'; i <= 'ÿ'; i++) bs.Add(i);

            var cs = new List<int>(bs);
            int n = 0;

            for (int b = 0; b < 256; b++)
                if (!bs.Contains(b))
                {
                    bs.Add(b);
                    cs.Add(256 + n);
                    n++;
                }

            var enc = new Dictionary<byte, char>();
            var dec = new Dictionary<char, byte>();

            for (int i = 0; i < bs.Count; i++)
            {
                enc[(byte)bs[i]] = (char)cs[i];
                dec[(char)cs[i]] = (byte)bs[i];
            }

            return (enc, dec);
        }

        private string BytesToUnicode(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length);
            foreach (var b in bytes)
                sb.Append(_byteEnc[b]);
            return sb.ToString();
        }
    }
}
