using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using UglyToad.PdfPig;

namespace CFC.ChatAI.DocumentProcessor;

/// <summary>Extracts text-only content from uploaded files without coupling to storage or indexing.</summary>
public sealed class DocumentProcessor : IDocumentProcessor
{
    private static readonly XNamespace WordNamespace = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly Regex HeadingStyle = new(@"^heading\s*(\d+)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex MarkdownHeading = new(@"^\s{0,3}(#{1,6})\s+(.+?)\s*#*\s*$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex HtmlTag = new("<[^>]*>", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex EncodedWord = new(@"=\?([^?]+)\?([bBqQ])\?([^?]*)\?=", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly DocumentProcessorOptions _options;

    public DocumentProcessor(DocumentProcessorOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        _options = options;
    }

    public async Task<DocumentProcessingResult> ProcessAsync(
        DocumentInput document,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(document.Content);
        if (string.IsNullOrWhiteSpace(document.FileName))
        {
            throw new ArgumentException("A file name is required.", nameof(document));
        }

        var sourceName = Path.GetFileName(document.FileName.Replace('\\', '/'));
        var extension = Path.GetExtension(sourceName).ToLowerInvariant();
        if (extension is not (".pdf" or ".docx" or ".doc" or ".txt" or ".md" or ".eml"))
        {
            throw new UnsupportedDocumentTypeException(extension.Length == 0 ? "(none)" : extension);
        }

        var data = await ReadDocumentAsync(document.Content, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        List<SectionContent> sections;
        try
        {
            sections = extension switch
            {
                ".pdf" => ExtractPdf(data, cancellationToken),
                ".docx" => ExtractDocx(data),
                ".doc" => await ExtractLegacyWordAsync(data, sourceName, cancellationToken).ConfigureAwait(false),
                ".txt" => [new SectionContent("Document", 1, DecodeText(data))],
                ".md" => ExtractMarkdown(DecodeText(data)),
                ".eml" => [ExtractEmail(data)],
                _ => throw new UnsupportedDocumentTypeException(extension)
            };
        }
        catch (DocumentProcessingException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new DocumentProcessingException($"Failed to extract text from '{sourceName}'.", exception);
        }

        var outputSections = new List<DocumentSection>();
        var chunks = new List<DocumentChunk>();
        foreach (var section in sections)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = Normalize(section.Text);
            if (text.Length == 0)
            {
                continue;
            }

            var sectionId = Guid.NewGuid().ToString("D");
            var title = string.IsNullOrWhiteSpace(section.Title) ? "Untitled" : section.Title.Trim();
            outputSections.Add(new DocumentSection(sectionId, title, Math.Max(section.Level, 1)));
            foreach (var chunkText in SplitIntoChunks(text, _options.MaxChunkCharacters))
            {
                chunks.Add(new DocumentChunk(Guid.NewGuid().ToString("D"), sectionId, title, chunkText));
            }
        }

        if (chunks.Count == 0)
        {
            throw new DocumentProcessingException($"No extractable text was found in '{sourceName}'.");
        }

        return new DocumentProcessingResult(
            CreateDocumentId(Path.GetFileNameWithoutExtension(sourceName)),
            sourceName,
            "document",
            outputSections,
            chunks);
    }

    private async Task<byte[]> ReadDocumentAsync(Stream input, CancellationToken cancellationToken)
    {
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        long totalBytes = 0;
        int bytesRead;
        while ((bytesRead = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
        {
            totalBytes += bytesRead;
            if (totalBytes > _options.MaxDocumentBytes)
            {
                throw new DocumentProcessingException(
                    $"Document exceeds the configured {_options.MaxDocumentBytes} byte size limit.");
            }

            await output.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
        }

        if (output.Length == 0)
        {
            throw new DocumentProcessingException("The uploaded document is empty.");
        }

        return output.ToArray();
    }

    private static List<SectionContent> ExtractPdf(byte[] data, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(data, writable: false);
        using var pdf = PdfDocument.Open(stream);
        var pageTexts = new List<string>();
        foreach (var page in pdf.GetPages())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.IsNullOrWhiteSpace(page.Text))
            {
                pageTexts.Add(page.Text);
            }
        }

        return [new SectionContent("Document", 1, string.Join(Environment.NewLine, pageTexts))];
    }

    private static List<SectionContent> ExtractDocx(byte[] data)
    {
        using var stream = new MemoryStream(data, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        var documentEntry = archive.GetEntry("word/document.xml")
            ?? throw new DocumentProcessingException("The DOCX file does not contain word/document.xml.");

        using var xmlStream = documentEntry.Open();
        using var reader = XmlReader.Create(xmlStream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
        var xml = XDocument.Load(reader);
        var body = xml.Root?.Element(WordNamespace + "body")
            ?? throw new DocumentProcessingException("The DOCX file has no document body.");

        var sections = new List<SectionContent>();
        SectionContent? current = null;
        foreach (var item in body.Elements())
        {
            if (item.Name == WordNamespace + "p")
            {
                var paragraphText = ReadWordParagraph(item);
                if (paragraphText.Length == 0)
                {
                    continue;
                }

                var style = (string?)item.Element(WordNamespace + "pPr")?.Element(WordNamespace + "pStyle")?.Attribute(WordNamespace + "val");
                var headingMatch = style is null ? Match.Empty : HeadingStyle.Match(style);
                if (headingMatch.Success)
                {
                    current = new SectionContent(paragraphText, int.TryParse(headingMatch.Groups[1].Value, out var level) ? level : 1, string.Empty);
                    sections.Add(current);
                }
                else
                {
                    current ??= AddDefaultSection(sections);
                    current.Append(paragraphText);
                }
            }
            else if (item.Name == WordNamespace + "tbl")
            {
                var rows = item.Elements(WordNamespace + "tr")
                    .Select(row => string.Join(" | ", row.Elements(WordNamespace + "tc").Select(ReadWordCell)))
                    .Where(row => !string.IsNullOrWhiteSpace(row));
                var tableText = string.Join(Environment.NewLine, rows);
                if (tableText.Length > 0)
                {
                    current ??= AddDefaultSection(sections);
                    current.Append(tableText);
                }
            }
        }

        return sections;
    }

    private static string ReadWordCell(XElement cell) => string.Join(" ",
        cell.Descendants(WordNamespace + "p")
            .Select(ReadWordParagraph)
            .Where(text => text.Length > 0));

    private static string ReadWordParagraph(XElement paragraph)
    {
        var text = new StringBuilder();
        foreach (var element in paragraph.Descendants())
        {
            if (element.Name == WordNamespace + "t")
            {
                text.Append(element.Value);
            }
            else if (element.Name == WordNamespace + "tab")
            {
                text.Append('\t');
            }
            else if (element.Name == WordNamespace + "br" || element.Name == WordNamespace + "cr")
            {
                text.Append(' ');
            }
        }

        return Normalize(text.ToString());
    }

    private async Task<List<SectionContent>> ExtractLegacyWordAsync(
        byte[] data,
        string sourceName,
        CancellationToken cancellationToken)
    {
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"cfc-document-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        var inputPath = Path.Combine(temporaryDirectory, sourceName);
        try
        {
            await File.WriteAllBytesAsync(inputPath, data, cancellationToken).ConfigureAwait(false);
            var startInfo = new ProcessStartInfo
            {
                FileName = _options.LibreOfficePath,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("--headless");
            startInfo.ArgumentList.Add("--convert-to");
            startInfo.ArgumentList.Add("docx");
            startInfo.ArgumentList.Add("--outdir");
            startInfo.ArgumentList.Add(temporaryDirectory);
            startInfo.ArgumentList.Add(inputPath);

            using var process = Process.Start(startInfo)
                ?? throw new DocumentProcessingException("LibreOffice could not be started to convert the .doc file.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.ConversionTimeout);
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // Process exited between cancellation and Kill.
                }

                cancellationToken.ThrowIfCancellationRequested();
                throw new DocumentProcessingException("LibreOffice timed out while converting the .doc file.");
            }

            var convertedPath = Path.Combine(temporaryDirectory, Path.GetFileNameWithoutExtension(sourceName) + ".docx");
            if (process.ExitCode != 0 || !File.Exists(convertedPath))
            {
                throw new DocumentProcessingException(
                    $"LibreOffice could not convert '{sourceName}' to DOCX (exit code {process.ExitCode}).");
            }

            return ExtractDocx(await File.ReadAllBytesAsync(convertedPath, cancellationToken).ConfigureAwait(false));
        }
        catch (DocumentProcessingException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new DocumentProcessingException(
                $"Could not convert '{sourceName}'. Install LibreOffice or configure LibreOfficePath.", exception);
        }
        finally
        {
            try
            {
                Directory.Delete(temporaryDirectory, recursive: true);
            }
            catch (IOException)
            {
                // Temporary files are best-effort cleanup if another process still holds them open.
            }
            catch (UnauthorizedAccessException)
            {
                // Temporary files are best-effort cleanup if access was revoked.
            }
        }
    }

    private static List<SectionContent> ExtractMarkdown(string markdown)
    {
        var sections = new List<SectionContent>();
        SectionContent? current = null;
        var body = new StringBuilder();
        foreach (var line in markdown.Split('\n'))
        {
            var match = MarkdownHeading.Match(line.TrimEnd('\r'));
            if (match.Success)
            {
                if (body.Length > 0)
                {
                    if (current is null)
                    {
                        current = new SectionContent("Document", 1, string.Empty);
                        sections.Add(current);
                    }

                    current.Append(body.ToString());
                    body.Clear();
                }

                current = new SectionContent(match.Groups[2].Value.Trim(), match.Groups[1].Length, string.Empty);
                sections.Add(current);
            }
            else
            {
                body.AppendLine(line);
            }
        }

        if (body.Length > 0)
        {
            if (current is null)
            {
                current = new SectionContent("Document", 1, string.Empty);
                sections.Add(current);
            }

            current.Append(body.ToString());
        }

        return sections;
    }

    private static SectionContent ExtractEmail(byte[] data)
    {
        var message = ParseMimePart(Encoding.Latin1.GetString(data));
        var headers = ParseHeaders(message.Headers);
        var plainBody = message.TextPlain;
        var body = plainBody ?? (message.TextHtml is null ? string.Empty : HtmlToText(message.TextHtml));
        var subject = DecodeEncodedWords(headers.GetValueOrDefault("subject") ?? string.Empty);
        var details = new List<string>();
        if (subject.Length > 0)
        {
            details.Add($"Subject: {subject}");
        }

        foreach (var header in new[] { "from", "to", "date" })
        {
            if (headers.TryGetValue(header, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                details.Add($"{CultureInvariantTitle(header)}: {DecodeEncodedWords(value)}");
            }
        }

        if (!string.IsNullOrWhiteSpace(body))
        {
            details.Add(body);
        }

        return new SectionContent(subject.Length > 0 ? subject : "Email", 1, string.Join(Environment.NewLine, details));
    }

    private static MimePart ParseMimePart(string part)
    {
        var split = FindHeaderBodySeparator(part);
        var headerText = split < 0 ? part : part[..split];
        var body = split < 0 ? string.Empty : part[(split + SeparatorLength(part, split))..];
        var headers = ParseHeaders(headerText);
        if (headers.TryGetValue("content-disposition", out var disposition)
            && (disposition.StartsWith("attachment", StringComparison.OrdinalIgnoreCase)
                || GetParameter(disposition, "filename") is not null))
        {
            return new MimePart(headerText);
        }

        var contentType = headers.GetValueOrDefault("content-type") ?? "text/plain; charset=utf-8";
        var boundary = GetParameter(contentType, "boundary");
        if (boundary is not null)
        {
            var result = new MimePart(headerText);
            foreach (var child in SplitMultipart(body, boundary))
            {
                var parsedChild = ParseMimePart(child);
                result.TextPlain ??= parsedChild.TextPlain;
                result.TextHtml ??= parsedChild.TextHtml;
            }

            return result;
        }

        var mediaType = contentType.Split(';', 2)[0].Trim();
        if (!mediaType.Equals("text/plain", StringComparison.OrdinalIgnoreCase)
            && !mediaType.Equals("text/html", StringComparison.OrdinalIgnoreCase))
        {
            return new MimePart(headerText);
        }

        var charset = GetParameter(contentType, "charset") ?? "utf-8";
        var transferEncoding = headers.GetValueOrDefault("content-transfer-encoding")?.Trim().ToLowerInvariant();
        var decoded = DecodeMimeBody(body, transferEncoding, charset);
        var leaf = new MimePart(headerText);
        if (mediaType.Equals("text/plain", StringComparison.OrdinalIgnoreCase))
        {
            leaf.TextPlain = decoded;
        }
        else
        {
            leaf.TextHtml = decoded;
        }

        return leaf;
    }

    private static int FindHeaderBodySeparator(string value)
    {
        var crlf = value.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        var lf = value.IndexOf("\n\n", StringComparison.Ordinal);
        if (crlf < 0)
        {
            return lf;
        }

        return lf < 0 ? crlf : Math.Min(crlf, lf);
    }

    private static int SeparatorLength(string value, int index) =>
        value.AsSpan(index).StartsWith("\r\n\r\n", StringComparison.Ordinal) ? 4 : 2;

    private static Dictionary<string, string> ParseHeaders(string rawHeaders)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? previousHeader = null;
        foreach (var line in rawHeaders.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if ((line.StartsWith(' ') || line.StartsWith('\t')) && previousHeader is not null)
            {
                headers[previousHeader] += " " + line.Trim();
                continue;
            }

            var separator = line.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }

            previousHeader = line[..separator].Trim().ToLowerInvariant();
            var value = line[(separator + 1)..].Trim();
            headers[previousHeader] = headers.TryGetValue(previousHeader, out var existing)
                ? existing + ", " + value
                : value;
        }

        return headers;
    }

    private static IEnumerable<string> SplitMultipart(string body, string boundary)
    {
        var marker = "--" + boundary;
        var lines = body.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var current = new StringBuilder();
        var inside = false;
        foreach (var line in lines)
        {
            if (line.Equals(marker, StringComparison.Ordinal) || line.Equals(marker + "--", StringComparison.Ordinal))
            {
                if (inside && current.Length > 0)
                {
                    yield return current.ToString();
                }

                if (line.EndsWith("--", StringComparison.Ordinal))
                {
                    yield break;
                }

                current.Clear();
                inside = true;
                continue;
            }

            if (inside)
            {
                current.AppendLine(line);
            }
        }

        if (inside && current.Length > 0)
        {
            yield return current.ToString();
        }
    }

    private static string? GetParameter(string value, string name)
    {
        var match = Regex.Match(value, $@"(?:^|;)\s*{Regex.Escape(name)}\s*=\s*(?:""([^""]*)""|([^;\s]*))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success ? (match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value) : null;
    }

    private static string DecodeMimeBody(string body, string? transferEncoding, string charset)
    {
        byte[] bytes;
        if (transferEncoding == "base64")
        {
            var compact = Whitespace.Replace(body, string.Empty);
            try
            {
                bytes = Convert.FromBase64String(compact);
            }
            catch (FormatException)
            {
                bytes = Encoding.Latin1.GetBytes(body);
            }
        }
        else if (transferEncoding == "quoted-printable")
        {
            bytes = DecodeQuotedPrintable(body);
        }
        else
        {
            bytes = Encoding.Latin1.GetBytes(body);
        }

        try
        {
            return ResolveEncoding(charset).GetString(bytes).Trim();
        }
        catch (ArgumentException)
        {
            return Encoding.UTF8.GetString(bytes).Trim();
        }
    }

    private static byte[] DecodeQuotedPrintable(string value)
    {
        var output = new List<byte>(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] == '=' && index + 1 < value.Length)
            {
                if (value[index + 1] == '\r' && index + 2 < value.Length && value[index + 2] == '\n')
                {
                    index += 2;
                    continue;
                }

                if (value[index + 1] == '\n')
                {
                    index++;
                    continue;
                }

                if (index + 2 < value.Length
                    && byte.TryParse(value.AsSpan(index + 1, 2), System.Globalization.NumberStyles.HexNumber, null, out var decoded))
                {
                    output.Add(decoded);
                    index += 2;
                    continue;
                }
            }

            output.Add((byte)value[index]);
        }

        return output.ToArray();
    }

    private static string DecodeEncodedWords(string value) => EncodedWord.Replace(value, match =>
    {
        try
        {
            var charset = ResolveEncoding(match.Groups[1].Value);
            var bytes = match.Groups[2].Value.Equals("B", StringComparison.OrdinalIgnoreCase)
                ? Convert.FromBase64String(match.Groups[3].Value)
                : DecodeQuotedPrintable(match.Groups[3].Value.Replace('_', ' '));
            return charset.GetString(bytes);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            return match.Value;
        }
    });

    private static Encoding ResolveEncoding(string charset)
    {
        if (charset.Equals("utf-8", StringComparison.OrdinalIgnoreCase))
        {
            return Encoding.UTF8;
        }

        if (charset.Equals("us-ascii", StringComparison.OrdinalIgnoreCase) || charset.Equals("ascii", StringComparison.OrdinalIgnoreCase))
        {
            return Encoding.ASCII;
        }

        if (charset.Contains("8859-1", StringComparison.OrdinalIgnoreCase)
            || charset.Equals("latin1", StringComparison.OrdinalIgnoreCase)
            || charset.Equals("windows-1252", StringComparison.OrdinalIgnoreCase))
        {
            return Encoding.Latin1;
        }

        return Encoding.GetEncoding(charset);
    }

    private static string HtmlToText(string html) =>
        WebUtility.HtmlDecode(HtmlTag.Replace(html, " "));

    private static string CultureInvariantTitle(string value) =>
        char.ToUpperInvariant(value[0]) + value[1..];

    private static string DecodeText(byte[] data)
    {
        using var stream = new MemoryStream(data, writable: false);
        try
        {
            using var reader = new StreamReader(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true), detectEncodingFromByteOrderMarks: true, leaveOpen: true);
            return reader.ReadToEnd();
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(data);
        }
    }

    private static string Normalize(string text) => Whitespace.Replace(text, " ").Trim();

    private static SectionContent AddDefaultSection(List<SectionContent> sections)
    {
        var section = new SectionContent("Document", 1, string.Empty);
        sections.Add(section);
        return section;
    }

    private static IEnumerable<string> SplitIntoChunks(string text, int maximumLength)
    {
        var start = 0;
        while (start < text.Length)
        {
            var remaining = text.Length - start;
            if (remaining <= maximumLength)
            {
                var final = text[start..].Trim();
                if (final.Length > 0)
                {
                    yield return final;
                }

                yield break;
            }

            var limit = start + maximumLength;
            var searchStart = start + (maximumLength / 2);
            var breakAt = -1;
            for (var index = limit - 1; index >= searchStart; index--)
            {
                if (text[index] is '.' or '!' or '?' or ';' or '\n')
                {
                    breakAt = index + 1;
                    break;
                }
            }

            if (breakAt <= start)
            {
                breakAt = text.LastIndexOf(' ', limit - 1, maximumLength);
                if (breakAt <= start)
                {
                    breakAt = limit;
                }
            }

            var chunk = text[start..breakAt].Trim();
            if (chunk.Length > 0)
            {
                yield return chunk;
            }

            start = breakAt;
            while (start < text.Length && char.IsWhiteSpace(text[start]))
            {
                start++;
            }
        }
    }

    private static string CreateDocumentId(string fileStem)
    {
        var slug = Regex.Replace(fileStem.Trim().ToLowerInvariant(), @"[^\w-]+", "-");
        slug = Regex.Replace(slug, @"-{2,}", "-").Trim('-');
        return slug.Length == 0 ? "document" : slug;
    }

    private sealed class SectionContent(string title, int level, string text)
    {
        public string Title { get; } = title;
        public int Level { get; } = level;
        private readonly StringBuilder _text = new(text);
        public string Text => _text.ToString();

        public void Append(string value)
        {
            if (_text.Length > 0)
            {
                _text.AppendLine();
            }

            _text.Append(value);
        }
    }

    private sealed class MimePart(string headers)
    {
        public string Headers { get; } = headers;
        public string? TextPlain { get; set; }
        public string? TextHtml { get; set; }
    }
}
