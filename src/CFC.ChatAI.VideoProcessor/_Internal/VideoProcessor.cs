using System.Text.RegularExpressions;

namespace CFC.ChatAI.VideoProcessor._Internal;

/// <summary>Coordinates media extraction and Azure transcription without storage or indexing dependencies.</summary>
internal sealed class VideoProcessor : IVideoProcessor
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".3gp", ".avi", ".m4v", ".mkv", ".mov", ".mp4", ".mpeg", ".mpg", ".mts", ".m2ts", ".webm", ".wmv"
    };

    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly VideoProcessorOptions _options;
    private readonly IVideoAudioExtractor _audioExtractor;
    private readonly IAudioTranscriber _transcriber;

    public VideoProcessor(
        VideoProcessorOptions options,
        IVideoAudioExtractor audioExtractor,
        IAudioTranscriber transcriber)
    {
        _options = options;
        _audioExtractor = audioExtractor;
        _transcriber = transcriber;
    }

    public async Task<VideoProcessingResult> ProcessAsync(
        VideoInput video,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(video);
        ArgumentNullException.ThrowIfNull(video.Content);
        if (string.IsNullOrWhiteSpace(video.FileName))
        {
            throw new ArgumentException("A file name is required.", nameof(video));
        }

        var sourceName = Path.GetFileName(video.FileName.Replace('\\', '/'));
        var extension = Path.GetExtension(sourceName);
        if (!SupportedExtensions.Contains(extension))
        {
            throw new UnsupportedVideoTypeException(extension.Length == 0 ? "(none)" : extension);
        }

        var workingDirectory = Path.Combine(Path.GetTempPath(), $"cfc-video-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(workingDirectory);
            var audioPath = await _audioExtractor.ExtractAsync(
                video.Content,
                sourceName,
                workingDirectory,
                cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            var audioInfo = new FileInfo(audioPath);
            if (!audioInfo.Exists || audioInfo.Length <= 44)
            {
                throw new VideoProcessingException($"No audio track or extractable audio was found in '{sourceName}'.");
            }

            if (audioInfo.Length > _options.MaxAudioBytes)
            {
                throw new VideoProcessingException(
                    $"Extracted audio exceeds the configured {_options.MaxAudioBytes} byte size limit.");
            }

            var duration = ReadPcmWaveDuration(audioPath);
            if (duration <= TimeSpan.Zero)
            {
                throw new VideoProcessingException($"No audio samples were found in '{sourceName}'.");
            }

            if (duration > _options.MaxAudioDuration)
            {
                throw new VideoProcessingException(
                    $"Extracted audio duration exceeds the configured {_options.MaxAudioDuration} limit.");
            }

            string text;
            TimeSpan transcriptionDuration;
            await using (var audio = File.OpenRead(audioPath))
            {
                (text, transcriptionDuration) = await _transcriber.TranscribeAsync(audio, cancellationToken).ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();

            var normalizedText = Whitespace.Replace(text, " ").Trim();
            if (normalizedText.Length == 0)
            {
                throw new VideoProcessingException($"No speech was recognized in '{sourceName}'.");
            }

            var sectionId = Guid.NewGuid().ToString("D");
            const string title = "Transcript";
            var section = new VideoSection(sectionId, title, 1);
            var chunks = SplitIntoChunks(normalizedText, _options.MaxChunkCharacters)
                .Select(chunk => new VideoChunk(Guid.NewGuid().ToString("D"), sectionId, title, chunk))
                .ToArray();

            return new VideoProcessingResult(
                CreateDocumentId(Path.GetFileNameWithoutExtension(sourceName)),
                sourceName,
                "video",
                transcriptionDuration > TimeSpan.Zero ? transcriptionDuration : duration,
                [section],
                chunks);
        }
        catch (VideoProcessingException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new VideoProcessingException($"Failed to extract or transcribe '{sourceName}'.", exception);
        }
        finally
        {
            TryDeleteDirectory(workingDirectory);
        }
    }

    private static TimeSpan ReadPcmWaveDuration(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        if (new string(reader.ReadChars(4)) != "RIFF")
        {
            throw new VideoProcessingException("FFmpeg produced an invalid WAV audio file.");
        }

        _ = reader.ReadUInt32();
        if (new string(reader.ReadChars(4)) != "WAVE")
        {
            throw new VideoProcessingException("FFmpeg produced an invalid WAV audio file.");
        }

        uint byteRate = 0;
        uint dataSize = 0;
        while (stream.Position + 8 <= stream.Length)
        {
            var chunkName = new string(reader.ReadChars(4));
            var chunkSize = reader.ReadUInt32();
            var chunkEnd = Math.Min(stream.Position + chunkSize, stream.Length);
            if (chunkName == "fmt " && chunkSize >= 12)
            {
                _ = reader.ReadUInt16(); // format
                _ = reader.ReadUInt16(); // channels
                _ = reader.ReadUInt32(); // sample rate
                byteRate = reader.ReadUInt32();
            }
            else if (chunkName == "data")
            {
                dataSize = chunkSize;
                break;
            }

            stream.Position = chunkEnd + (chunkSize % 2);
        }

        return byteRate == 0 || dataSize == 0
            ? TimeSpan.Zero
            : TimeSpan.FromSeconds(dataSize / (double)byteRate);
    }

    private static IEnumerable<string> SplitIntoChunks(string text, int maximumLength)
    {
        var start = 0;
        while (start < text.Length)
        {
            if (text.Length - start <= maximumLength)
            {
                var final = text[start..].Trim();
                if (final.Length > 0)
                {
                    yield return final;
                }

                yield break;
            }

            var limit = start + maximumLength;
            var breakAt = -1;
            for (var index = limit - 1; index >= start + maximumLength / 2; index--)
            {
                if (text[index] is '.' or '!' or '?' or ';')
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
        return slug.Length == 0 ? "video" : slug;
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
            // Temporary media is best-effort cleanup if a process still holds a file open.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup if access was revoked.
        }
    }
}
