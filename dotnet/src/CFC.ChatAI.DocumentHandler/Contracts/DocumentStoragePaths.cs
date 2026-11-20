using System.Buffers.Text;
using System.Text;

namespace CFC.ChatAI.DocumentHandler;

/// <summary>Document storage layout for a validated, exact document ID.</summary>
public static class DocumentStoragePaths
{
    public static string GetRoot(string documentId)
    {
        if (!DocumentId.IsValid(documentId))
            throw new ArgumentException("Invalid document ID.", nameof(documentId));
        return UsesEncodedKey(documentId)
            ? $"docs/!u-{Base64Url.EncodeToString(Encoding.UTF8.GetBytes(documentId))}"
            : GetLegacyRoot(documentId);
    }

    // Supabase Storage permits '!' but not Unicode in object keys. The marker cannot
    // appear in a valid document ID, so the encoded and literal namespaces cannot collide.
    public static bool UsesEncodedKey(string documentId) => documentId.Any(c => !char.IsAscii(c));

    // Keep literal paths addressable for earlier imports on providers that accepted them.
    public static IEnumerable<string> GetLookupRoots(string documentId)
    {
        yield return GetRoot(documentId);
        if (UsesEncodedKey(documentId)) yield return GetLegacyRoot(documentId);
    }

    private static string GetLegacyRoot(string documentId) => $"docs/{documentId}";
}
