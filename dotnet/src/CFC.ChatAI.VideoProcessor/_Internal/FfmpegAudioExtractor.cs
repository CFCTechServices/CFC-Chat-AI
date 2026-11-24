using System.Diagnostics;
using System.Text;

namespace CFC.ChatAI.VideoProcessor._Internal;

internal sealed class FfmpegAudioExtractor : IVideoAudioExtractor
{
    private readonly VideoProcessorOptions _options;

    public FfmpegAudioExtractor(VideoProcessorOptions options) => _options = options;

    public async Task<string> ExtractAsync(
        Stream video,
        string sourceName,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var videoPath = Path.Combine(workingDirectory, "source" + Path.GetExtension(sourceName));
        var audioPath = Path.Combine(workingDirectory, "audio.wav");
        await CopyVideoAsync(video, videoPath, cancellationToken).ConfigureAwait(false);

        var startInfo = new ProcessStartInfo
        {
            FileName = _options.FfmpegPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            StandardErrorEncoding = Encoding.UTF8
        };
        startInfo.ArgumentList.Add("-hide_banner");
        startInfo.ArgumentList.Add("-loglevel");
        startInfo.ArgumentList.Add("error");
        startInfo.ArgumentList.Add("-nostdin");
        startInfo.ArgumentList.Add("-y");
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(videoPath);
        startInfo.ArgumentList.Add("-map");
        startInfo.ArgumentList.Add("0:a:0");
        startInfo.ArgumentList.Add("-vn");
        startInfo.ArgumentList.Add("-ac");
        startInfo.ArgumentList.Add("1");
        startInfo.ArgumentList.Add("-ar");
        startInfo.ArgumentList.Add("16000");
        startInfo.ArgumentList.Add("-c:a");
        startInfo.ArgumentList.Add("pcm_s16le");
        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add("wav");
        startInfo.ArgumentList.Add(audioPath);

        using var process = Process.Start(startInfo)
            ?? throw new VideoProcessingException("FFmpeg could not be started to extract video audio.");
        var standardError = process.StandardError.ReadToEndAsync();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.AudioExtractionTimeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            _ = await standardError.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            throw new VideoProcessingException(
                $"FFmpeg exceeded the configured {_options.AudioExtractionTimeout} audio extraction timeout.");
        }

        var error = await standardError.ConfigureAwait(false);
        if (process.ExitCode != 0 || !File.Exists(audioPath))
        {
            var details = error.Trim();
            if (details.Length > 1500)
            {
                details = details[..1500];
            }

            throw new VideoProcessingException(details.Length == 0
                ? $"FFmpeg could not extract an audio track from '{sourceName}' (exit code {process.ExitCode})."
                : $"FFmpeg could not extract an audio track from '{sourceName}': {details}");
        }

        return audioPath;
    }

    private async Task CopyVideoAsync(Stream input, string path, CancellationToken cancellationToken)
    {
        await using var output = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            useAsync: true);
        var buffer = new byte[81920];
        long totalBytes = 0;
        int bytesRead;
        while ((bytesRead = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
        {
            totalBytes += bytesRead;
            if (totalBytes > _options.MaxVideoBytes)
            {
                throw new VideoProcessingException(
                    $"Video exceeds the configured {_options.MaxVideoBytes} byte size limit.");
            }

            await output.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
        }

        if (totalBytes == 0)
        {
            throw new VideoProcessingException("The uploaded video is empty.");
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // The process exited between cancellation and Kill.
        }
    }
}
