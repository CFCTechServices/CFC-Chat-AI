using System.IO.Compression;
using System.Security.Cryptography;
using System.Xml;
using System.Xml.Linq;

namespace CFC.ChatAI.DocumentProcessor;

internal sealed class DocxImages(ZipArchive archive, CancellationToken cancellationToken)
{
    private const int MaxReferences = 512;
    private const int MaxImageParts = 128;
    private const int MaxImageBytes = 8 * 1024 * 1024;
    private const int MaxTotalImageBytes = 32 * 1024 * 1024;
    private static readonly XNamespace Drawing = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace OfficeRel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRel = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace ContentTypes = "http://schemas.openxmlformats.org/package/2006/content-types";
    private static readonly XName VmlImage = XName.Get("imagedata", "urn:schemas-microsoft-com:vml");

    private Dictionary<string, XElement>? _relationships;
    private Dictionary<string, string>? _overrides;
    private Dictionary<string, string>? _defaults;
    private readonly Dictionary<string, DocumentImage> _byPart = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DocumentImage> _byId = new(StringComparer.Ordinal);
    private readonly List<DocumentImage> _images = [];
    private int _references;
    private int _totalBytes;

    public IEnumerable<string> ReadReferences(XElement element)
    {
        foreach (var child in element.Descendants())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (child.Name != Drawing + "blip" && child.Name != VmlImage) continue;
            if (++_references > MaxReferences) throw new DocumentProcessingException("DOCX has too many image references.");
            if (child.Attribute(OfficeRel + "link") is not null || child.Attribute(OfficeRel + "href") is not null)
                throw new DocumentProcessingException("Linked DOCX images are not supported.");

            var id = (string?)child.Attribute(OfficeRel + (child.Name == VmlImage ? "id" : "embed"));
            if (string.IsNullOrEmpty(id)) throw new DocumentProcessingException("DOCX image is missing its relationship ID.");
            yield return Resolve(id).Id;
        }
    }

    public IReadOnlyList<DocumentImage> Images => _images;

    private DocumentImage Resolve(string id)
    {
        _relationships ??= ReadMap("word/_rels/document.xml.rels", PackageRel + "Relationships", PackageRel + "Relationship", "Id", x => x);
        if (!_relationships.TryGetValue(id, out var relation) || (string?)relation.Attribute("Type") != OfficeRel.NamespaceName + "/image" ||
            (string?)relation.Attribute("TargetMode") is not (null or "Internal"))
            throw new DocumentProcessingException("DOCX image relationship is missing, external, or invalid.");

        var target = (string?)relation.Attribute("Target") ?? string.Empty;
        var path = target.StartsWith('/') ? target[1..] : "word/" + target;
        if (!path.StartsWith("word/media/", StringComparison.Ordinal) || path.Length > 256 ||
            path.Split('/').Any(segment => segment.Length == 0 || segment is "." or ".." ||
                segment.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_' or '.'))))
            throw new DocumentProcessingException("DOCX image part path is unsupported or invalid.");
        if (_byPart.TryGetValue(path, out var existing)) return existing;
        if (_byPart.Count >= MaxImageParts) throw new DocumentProcessingException("DOCX has too many image parts.");

        if (_overrides is null || _defaults is null)
        {
            var types = ReadXml(archive, "[Content_Types].xml", 1024 * 1024, cancellationToken);
            if (types.Root?.Name != ContentTypes + "Types") throw new DocumentProcessingException("Invalid DOCX content types.");
            _overrides = ToMap(types.Root.Elements(ContentTypes + "Override"), "PartName", x => (string?)x.Attribute("ContentType") ?? "");
            _defaults = ToMap(types.Root.Elements(ContentTypes + "Default"), "Extension", x => (string?)x.Attribute("ContentType") ?? "", StringComparer.OrdinalIgnoreCase);
        }
        var declaredType = _overrides.GetValueOrDefault("/" + path) ?? _defaults.GetValueOrDefault(Path.GetExtension(path).TrimStart('.'));
        var (extension, contentType) = declaredType?.ToLowerInvariant() switch
        {
            "image/png" => (".png", "image/png"),
            "image/jpeg" or "image/jpg" => (".jpg", "image/jpeg"),
            "image/gif" => (".gif", "image/gif"),
            _ => throw new DocumentProcessingException("DOCX embedded images must be PNG, JPEG, or GIF.")
        };
        var bytes = ReadPart(archive, path, Math.Min(MaxImageBytes, MaxTotalImageBytes - _totalBytes), cancellationToken);
        _totalBytes += bytes.Length;
        if (!MatchesType(bytes, contentType)) throw new DocumentProcessingException("DOCX image bytes do not match the declared image format.");

        var digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (!_byId.TryGetValue(digest, out var image))
        {
            image = new DocumentImage(digest, extension, contentType, bytes);
            _byId.Add(digest, image);
            _images.Add(image);
        }
        _byPart.Add(path, image);
        return image;
    }

    private static bool MatchesType(ReadOnlySpan<byte> bytes, string contentType) => contentType switch
    {
        "image/png" => bytes.Length >= 45 && bytes.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) &&
            bytes[12..].StartsWith("IHDR"u8) && bytes[^8..^4].SequenceEqual("IEND"u8),
        "image/jpeg" => bytes.Length >= 8 && bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[2] == 0xff && bytes[^2] == 0xff && bytes[^1] == 0xd9,
        "image/gif" => bytes.Length >= 14 && (bytes.StartsWith("GIF87a"u8) || bytes.StartsWith("GIF89a"u8)) && bytes[^1] == 0x3b,
        _ => false
    };

    private Dictionary<string, T> ReadMap<T>(string path, XName root, XName item, XName key, Func<XElement, T> value)
    {
        var xml = ReadXml(archive, path, 1024 * 1024, cancellationToken);
        if (xml.Root?.Name != root) throw new DocumentProcessingException("Invalid DOCX image relationship file.");
        return ToMap(xml.Root.Elements(item), key, value);
    }

    private static Dictionary<string, T> ToMap<T>(IEnumerable<XElement> entries, XName key, Func<XElement, T> value,
        StringComparer? comparer = null)
    {
        var result = new Dictionary<string, T>(comparer ?? StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if ((string?)entry.Attribute(key) is not { Length: > 0 } name || !result.TryAdd(name, value(entry)))
                throw new DocumentProcessingException("DOCX metadata has a missing or duplicate key.");
        }
        return result;
    }

    internal static XDocument ReadXml(ZipArchive archive, string path, int maxBytes, CancellationToken ct)
    {
        using var stream = new MemoryStream(ReadPart(archive, path, maxBytes, ct), writable: false);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, MaxCharactersInDocument = maxBytes });
        var document = XDocument.Load(reader);
        ct.ThrowIfCancellationRequested();
        return document;
    }

    private static byte[] ReadPart(ZipArchive archive, string path, int maxBytes, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var entry = archive.GetEntry(path);
        if (entry is null || entry.Length <= 0 || entry.Length > maxBytes)
            throw new DocumentProcessingException("A required DOCX part is missing, empty, or exceeds the extraction limit.");
        var data = new byte[(int)entry.Length];
        using var stream = entry.Open();
        for (var offset = 0; offset < data.Length;)
        {
            ct.ThrowIfCancellationRequested();
            var read = stream.Read(data.AsSpan(offset, Math.Min(data.Length - offset, 81920)));
            if (read == 0) throw new DocumentProcessingException("A required DOCX part is truncated.");
            offset += read;
        }
        if (stream.ReadByte() != -1) throw new DocumentProcessingException("A DOCX part exceeds its declared size.");
        return data;
    }
}
