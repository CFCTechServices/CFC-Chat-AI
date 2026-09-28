using System.Globalization;
using System.Text;

namespace CFC.Rag;

/// <summary>
/// Exact port of Hugging Face's uncased BERT tokenizer
/// (BertNormalizer + BertPreTokenizer + WordPiece), as used by all-MiniLM-L6-v2.
/// Replaces Microsoft.ML.Tokenizers, which drops symbols such as '°' instead of
/// word-piecing them the way Hugging Face does.
/// </summary>
public sealed class WordPieceTokenizer
{
    private const int MaxCharsPerWord = 100;
    private readonly Dictionary<string, int> _vocab = new(StringComparer.Ordinal);

    public int ClsId { get; }
    public int SepId { get; }
    public int UnkId { get; }

    public WordPieceTokenizer(string vocabPath)
    {
        int id = 0;
        foreach (var line in File.ReadLines(vocabPath))
            _vocab.TryAdd(line, id++);

        ClsId = _vocab["[CLS]"];
        SepId = _vocab["[SEP]"];
        UnkId = _vocab["[UNK]"];
    }

    /// <summary>Token ids wrapped in [CLS] ... [SEP], truncated to maxTokens total.</summary>
    public List<int> Encode(string text, int maxTokens)
    {
        var ids = new List<int> { ClsId };
        foreach (var word in PreTokenize(Normalize(text)))
            AppendWordPieces(word, ids);

        if (ids.Count > maxTokens - 1)
            ids.RemoveRange(maxTokens - 1, ids.Count - (maxTokens - 1));
        ids.Add(SepId);
        return ids;
    }

    // --- Step 1: normalize (clean, pad CJK, strip accents, lowercase) ---------
    private static string Normalize(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var r in text.EnumerateRunes())
        {
            int cp = r.Value;
            if (cp == 0 || cp == 0xFFFD || IsControl(r)) continue;
            if (IsWhitespace(r)) { sb.Append(' '); continue; }
            if (IsChineseChar(cp)) { sb.Append(' ').Append(r.ToString()).Append(' '); continue; }
            sb.Append(r.ToString());
        }

        // Strip accents: decompose (NFD), then drop non-spacing marks.
        var decomposed = sb.ToString().Normalize(NormalizationForm.FormD);
        var stripped = new StringBuilder(decomposed.Length);
        foreach (var r in decomposed.EnumerateRunes())
            if (Rune.GetUnicodeCategory(r) != UnicodeCategory.NonSpacingMark)
                stripped.Append(r.ToString());

        return stripped.ToString().ToLowerInvariant();
    }

    // --- Step 2: split on whitespace; punctuation becomes its own token ------
    private static List<string> PreTokenize(string text)
    {
        var words = new List<string>();
        var current = new StringBuilder();

        foreach (var r in text.EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(r))
            {
                Flush();
            }
            else if (IsPunctuation(r))
            {
                Flush();
                words.Add(r.ToString());
            }
            else
            {
                current.Append(r.ToString());
            }
        }
        Flush();
        return words;

        void Flush()
        {
            if (current.Length > 0) { words.Add(current.ToString()); current.Clear(); }
        }
    }

    // --- Step 3: WordPiece (greedy longest match, "##" for continuations) ----
    private void AppendWordPieces(string word, List<int> ids)
    {
        if (word.EnumerateRunes().Count() > MaxCharsPerWord) { ids.Add(UnkId); return; }

        var pieces = new List<int>();
        int start = 0;
        while (start < word.Length)
        {
            int end = word.Length;
            int found = -1;
            while (start < end)
            {
                var sub = word.Substring(start, end - start);
                if (start > 0) sub = "##" + sub;
                if (_vocab.TryGetValue(sub, out var pieceId)) { found = pieceId; break; }
                end--;
            }
            if (found < 0) { ids.Add(UnkId); return; } // whole word becomes [UNK]
            pieces.Add(found);
            start = end;
        }
        ids.AddRange(pieces);
    }

    // --- Character classes (match Hugging Face's definitions) ----------------
    private static bool IsWhitespace(Rune r) =>
        r.Value is '\t' or '\n' or '\r' || Rune.IsWhiteSpace(r);

    private static bool IsControl(Rune r)
    {
        if (r.Value is '\t' or '\n' or '\r') return false;
        return Rune.GetUnicodeCategory(r) is UnicodeCategory.Control or UnicodeCategory.Format
            or UnicodeCategory.OtherNotAssigned or UnicodeCategory.PrivateUse or UnicodeCategory.Surrogate;
    }

    private static bool IsPunctuation(Rune r)
    {
        int cp = r.Value;
        if (cp is >= 33 and <= 47 or >= 58 and <= 64 or >= 91 and <= 96 or >= 123 and <= 126)
            return true;
        return Rune.GetUnicodeCategory(r) is UnicodeCategory.ConnectorPunctuation
            or UnicodeCategory.DashPunctuation or UnicodeCategory.OpenPunctuation
            or UnicodeCategory.ClosePunctuation or UnicodeCategory.InitialQuotePunctuation
            or UnicodeCategory.FinalQuotePunctuation or UnicodeCategory.OtherPunctuation;
    }

    private static bool IsChineseChar(int cp) =>
        cp is >= 0x4E00 and <= 0x9FFF or >= 0x3400 and <= 0x4DBF
            or >= 0x20000 and <= 0x2A6DF or >= 0x2A700 and <= 0x2B73F
            or >= 0x2B740 and <= 0x2B81F or >= 0x2B820 and <= 0x2CEAF
            or >= 0xF900 and <= 0xFAFF or >= 0x2F800 and <= 0x2FA1F;
}