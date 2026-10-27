# Video processing

`CFC.ChatAI.VideoProcessor` is an independent video-to-text component. It accepts a video filename and readable stream, extracts the first audio track with FFmpeg, transcribes that audio with the Azure AI Speech Transcription .NET SDK, and returns bounded transcript chunks. It does not depend on the API, Supabase, Pinecone, document parsing, or storage/indexing services. The supplied input stream remains open.

## Requirements and limits

- Install FFmpeg and make `ffmpeg` available on `PATH` (or configure `FfmpegPath`). The processor uses FFmpeg as an external executable; it does not bundle codecs.
- Provision an Azure AI Speech resource and provide its endpoint and key. Store the key in a secret manager/environment configuration, not source control.
- Azure's transcription endpoint accepts audio shorter than two hours and smaller than 250 MB. The default local limits are 500 MiB for video input, 240,000,000 bytes for extracted WAV audio, and 1 hour 59 minutes for audio duration. Limits can be lowered to match upload/storage policy.
- Supported filename extensions are `.3gp`, `.avi`, `.m4v`, `.mkv`, `.mov`, `.mp4`, `.mpeg`, `.mpg`, `.mts`, `.m2ts`, `.webm`, and `.wmv`. FFmpeg still validates the actual media content and the presence of an audio track.

FFmpeg converts the first audio stream to mono 16 kHz PCM WAV before sending it to Azure. Input size, extracted audio size/duration, FFmpeg execution time, transcription network time, and chunk size are bounded/configurable. Azure locale detection is used unless locale candidates are configured. Speaker diarization is optional.

## API

```csharp
using CFC.ChatAI.VideoProcessor;

services.AddVideoProcessing(options =>
{
    options.SpeechEndpoint = new Uri(configuration["Speech:Endpoint"]!);
    options.SpeechApiKey = configuration["Speech:ApiKey"]!;
    options.FfmpegPath = "ffmpeg";
    options.MaxVideoBytes = 500L * 1024 * 1024;
    options.MaxAudioBytes = 240_000_000;
    options.MaxChunkCharacters = 1200;
    options.Locales.Add("en-US"); // Optional; omit to let Azure detect the language.
    options.EnableSpeakerDiarization = false;
});

var result = await videoProcessor.ProcessAsync(
    new VideoInput(upload.FileName, upload.OpenReadStream()), cancellationToken);
```

`VideoProcessingResult` includes a filename-derived ID, source name/type, detected duration, transcript section metadata, and chunks. Unsupported extensions raise `UnsupportedVideoTypeException`; empty, oversized, audio-less, textless, malformed, or failed FFmpeg/Azure requests raise `VideoProcessingException`. Cancellation propagates while reading and extracting media and during the Azure SDK request. Temporary source/audio files are removed on completion (best-effort if held open by an external process).

The implementation uses the `Azure.AI.Speech.Transcription` SDK package. FFmpeg extracts only audio; it does not perform speech recognition locally.

## Verification

From `dotnet/` run:

```sh
dotnet test tests/CFC.ChatAI.VideoProcessor.Tests/CFC.ChatAI.VideoProcessor.Tests.csproj
```

Tests use in-memory WAV generation and fake internal media/transcription adapters, so they do not require FFmpeg, Azure credentials, or network access. They cover transcript chunking, stream ownership, input validation, output limits, and dependency-injection registration.
