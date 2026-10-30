using System.Text;
using CFC.ChatAI.VideoProcessor;
using CFC.ChatAI.VideoProcessor._Internal;
using Microsoft.Extensions.DependencyInjection;
using Processor = CFC.ChatAI.VideoProcessor._Internal.VideoProcessor;

namespace CFC.ChatAI.VideoProcessor.Tests;

public sealed class VideoProcessorTests
{
    [Fact]
    public async Task TranscribesVideoAndReturnsBoundedChunks()
    {
        const string transcript = "A first sentence with enough words. A second sentence that is also long enough. Final words.";
        var transcriber = new FakeTranscriber(transcript);
        var processor = CreateProcessor(transcriber, maxChunkCharacters: 32);
        var stream = new MemoryStream(Encoding.UTF8.GetBytes("test video bytes"));

        var result = await processor.ProcessAsync(new VideoInput("My recording.mp4", stream));

        Assert.Equal("my-recording", result.DocumentId);
        Assert.Equal("My recording.mp4", result.SourceName);
        Assert.Equal("video", result.SourceType);
        Assert.Equal(TimeSpan.FromSeconds(1), result.Duration);
        Assert.Single(result.Sections);
        Assert.Equal("Transcript", result.Sections[0].Title);
        Assert.True(result.Chunks.Count > 1);
        Assert.All(result.Chunks, chunk => Assert.InRange(chunk.Text.Length, 1, 32));
        Assert.Equal(transcript, string.Join(' ', result.Chunks.Select(chunk => chunk.Text)));
        Assert.True(stream.CanRead);
        Assert.True(transcriber.ReceivedAudio);
    }

    [Fact]
    public async Task RejectsUnsupportedVideoExtensionsBeforeReadingInput()
    {
        var stream = new MemoryStream();
        var processor = CreateProcessor(new FakeTranscriber("unused"));

        await Assert.ThrowsAsync<UnsupportedVideoTypeException>(
            () => processor.ProcessAsync(new VideoInput("notes.txt", stream)));

        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public async Task RejectsVideoWithoutRecognizedSpeech()
    {
        var processor = CreateProcessor(new FakeTranscriber("  \n "));

        await Assert.ThrowsAsync<VideoProcessingException>(
            () => processor.ProcessAsync(new VideoInput("silent.mov", new MemoryStream([1, 2, 3]))));
    }

    [Fact]
    public async Task EnforcesExtractedAudioSizeLimit()
    {
        var processor = CreateProcessor(
            new FakeTranscriber("unused"),
            maxAudioBytes: 1024);

        var exception = await Assert.ThrowsAsync<VideoProcessingException>(
            () => processor.ProcessAsync(new VideoInput("large.mp4", new MemoryStream([1, 2, 3]))));

        Assert.Contains("audio", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RegistersProcessorAndAzureClientInDependencyInjection()
    {
        var services = new ServiceCollection();
        services.AddVideoProcessing(options =>
        {
            options.SpeechEndpoint = new Uri("https://eastus.api.cognitive.microsoft.com/");
            options.SpeechApiKey = "unit-test-key";
            options.MaxChunkCharacters = 240;
        });
        using var provider = services.BuildServiceProvider();

        Assert.IsAssignableFrom<IVideoProcessor>(provider.GetRequiredService<IVideoProcessor>());
        Assert.NotNull(provider.GetRequiredService<Azure.AI.Speech.Transcription.TranscriptionClient>());
        Assert.Equal(240, provider.GetRequiredService<VideoProcessorOptions>().MaxChunkCharacters);
    }

    [Fact]
    public void RequiresAzureSpeechConfigurationAtRegistration()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentException>(() => services.AddVideoProcessing(_ => { }));
    }

    private static Processor CreateProcessor(
        FakeTranscriber transcriber,
        int maxChunkCharacters = 1200,
        long maxAudioBytes = 240_000_000)
    {
        var options = new VideoProcessorOptions
        {
            SpeechEndpoint = new Uri("https://eastus.api.cognitive.microsoft.com/"),
            SpeechApiKey = "test-key",
            MaxChunkCharacters = maxChunkCharacters,
            MaxAudioBytes = maxAudioBytes
        };
        options.Validate();
        return new Processor(options, new FakeAudioExtractor(), transcriber);
    }

    private sealed class FakeAudioExtractor : IVideoAudioExtractor
    {
        public Task<string> ExtractAsync(
            Stream video,
            string sourceName,
            string workingDirectory,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var outputPath = Path.Combine(workingDirectory, "audio.wav");
            using var output = File.Create(outputPath);
            using var writer = new BinaryWriter(output, Encoding.ASCII, leaveOpen: true);
            const uint sampleBytes = 32000;
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write((uint)(36 + sampleBytes));
            writer.Write(Encoding.ASCII.GetBytes("WAVE"));
            writer.Write(Encoding.ASCII.GetBytes("fmt "));
            writer.Write((uint)16);
            writer.Write((ushort)1);
            writer.Write((ushort)1);
            writer.Write((uint)16000);
            writer.Write((uint)32000);
            writer.Write((ushort)2);
            writer.Write((ushort)16);
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(sampleBytes);
            writer.Write(new byte[(int)sampleBytes]);
            return Task.FromResult(outputPath);
        }
    }

    private sealed class FakeTranscriber(string text) : IAudioTranscriber
    {
        public bool ReceivedAudio { get; private set; }

        public Task<(string Text, TimeSpan Duration)> TranscribeAsync(
            Stream audio,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReceivedAudio = audio.CanRead && audio.Length > 44;
            return Task.FromResult((text, TimeSpan.FromSeconds(1)));
        }
    }
}
