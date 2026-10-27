# Document processing

`CFC.ChatAI.DocumentProcessor` is an independent text-extraction component. It accepts a filename and readable stream, extracts document text, groups text into sections, and returns bounded chunks. It has no dependencies on Supabase, Pinecone, embeddings, or the API layer.

## Supported formats

| Extension | Extraction behavior |
|---|---|
| `.pdf` | Extracts selectable text from PDF pages using PdfPig. |
| `.docx` | Reads paragraphs, heading styles, and table cells from the OOXML document body. |
| `.doc` | Converts to DOCX with LibreOffice, then uses the DOCX extractor. |
| `.txt` | Reads text with BOM-aware UTF-8 decoding and a Latin-1 fallback. |
| `.md` | Reads Markdown and turns ATX headings (`#` through `######`) into sections. Markdown formatting in the body is otherwise retained as text. |
| `.eml` | Extracts common message headers and a plain-text MIME body; falls back to text converted from HTML. Supports multipart messages, base64 and quoted-printable bodies, and encoded-word subjects. |

Video formats are deliberately not handled here; video transcription belongs in `VideoProcessor`. PDF OCR, embedded images, and DOC files that require LibreOffice are not silently approximated. Scanned PDFs with no selectable text return a `DocumentProcessingException`; `.doc` conversion requires LibreOffice to be installed and available on `PATH` (or configured explicitly).

## API

```csharp
using CFC.ChatAI.DocumentProcessor;

services.AddDocumentProcessing(options =>
{
    options.MaxDocumentBytes = 50L * 1024 * 1024;
    options.MaxChunkCharacters = 1200;
    options.LibreOfficePath = "soffice";
});

var result = await processor.ProcessAsync(
    new DocumentInput(upload.FileName, upload.OpenReadStream()), cancellationToken);
```

`DocumentProcessingResult` contains a filename-derived `DocumentId`, `SourceName`, `SourceType`, ordered section metadata, and chunks. Each chunk has a unique ID, section ID/title, and text. The component leaves the supplied stream open. Unsupported extensions raise `UnsupportedDocumentTypeException`; malformed, empty, oversized, or textless inputs raise `DocumentProcessingException`. Cancellation is honored while reading the input, iterating PDF pages, and converting legacy DOC files.

The default input limit is 50 MiB, matching the current upload/storage limit. Chunks are at most 1200 characters by default and split preferentially at sentence punctuation or whitespace. No overlap is added. Document IDs use the existing filename slug convention.

## Verification

Run from `dotnet/`:

```sh
dotnet test tests/CFC.ChatAI.DocumentProcessor.Tests/CFC.ChatAI.DocumentProcessor.Tests.csproj
```

The tests cover text chunk limits, Markdown headings, DOCX paragraphs/headings/tables, PDF text extraction, multipart email decoding, validation, size limits, and dependency-injection registration. The PDF test generates a small PDF fixture in memory.
