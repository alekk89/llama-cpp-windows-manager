using System.Net;
using System.Net.Http.Headers;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;

namespace LocalLlmConsole.Tests;

public sealed class DownloadTransientFailureTests : ManagerRegressionTestBase
{
    public enum TransientFailure
    {
        ConnectionReset,
        ServerError,
        ReadStall,
        RequestTimeout
    }

    [Theory]
    [InlineData(TransientFailure.ConnectionReset, 16)]
    [InlineData(TransientFailure.ServerError, 16)]
    [InlineData(TransientFailure.ReadStall, 20)]
    [InlineData(TransientFailure.RequestTimeout, 16)]
    public async Task TransientFailureKeepsPartialAsFailedAndResumeContinuesFromIt(TransientFailure failure, int keptBytes)
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state.db"));
        await store.InitializeAsync();
        var settings = AppSettings.CreateDefault(root);
        var jobs = new JobEngine(store, Path.Combine(root, "logs"));
        var destination = Path.Combine(settings.ModelsRoot, "transient", "model.gguf");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var source = Path.Combine(root, "source.gguf");
        WriteMinimalGguf(source);
        var bytes = await File.ReadAllBytesAsync(source, TestContext.Current.CancellationToken);
        var file = new HuggingFaceFile("owner/repo", "model.gguf", "model.gguf", "", bytes.Length, 0) { Sha256 = Sha256Hex(bytes) };
        await File.WriteAllBytesAsync(destination + ".partial", bytes[..16], TestContext.Current.CancellationToken);
        var payload = new DownloadJobPayload(file, destination, 16, bytes.Length);
        var job = await jobs.CreateAsync("huggingface-download", System.Text.Json.JsonSerializer.Serialize(payload), TestContext.Current.CancellationToken);
        await jobs.UpdateAsync(job, JobStatus.Paused, job.PayloadJson, TestContext.Current.CancellationToken);

        using var release = new CancellationTokenSource();
        var requestedOffsets = new List<long>();
        using var service = new HuggingFaceService(store, jobs, new ModelCatalogService(store), new ScriptedHttpHandler(async (request, cancellationToken) =>
        {
            if (!request.RequestUri!.AbsolutePath.EndsWith("model.gguf", StringComparison.Ordinal))
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            var from = Assert.Single(request.Headers.Range!.Ranges).From!.Value;
            requestedOffsets.Add(from);
            if (requestedOffsets.Count == 1)
            {
                switch (failure)
                {
                    case TransientFailure.ConnectionReset:
                        throw new HttpRequestException("An existing connection was forcibly closed by the remote host.");
                    case TransientFailure.ServerError:
                        return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                    case TransientFailure.RequestTimeout:
                        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                        break;
                    case TransientFailure.ReadStall:
                        return RangeResponse(bytes, from, new StallAfterPrefixStream(bytes[(int)from..20], release.Token));
                }
            }
            return RangeResponse(bytes, from, new MemoryStream(bytes[(int)from..]));
        }))
        {
            DownloadReadIdleTimeout = TimeSpan.FromMilliseconds(200),
            RequestTimeout = TimeSpan.FromMilliseconds(200)
        };
        try
        {
            await service.ResumeDownloadAsync(job with { Status = JobStatus.Paused }, settings);
            await WaitForWorkerAsync(service, job);

            var failed = Assert.Single(await store.ListJobsAsync());
            var failedPayload = HuggingFaceService.ParseDownloadPayload(failed.PayloadJson)!;
            Assert.Equal(JobStatus.Failed, failed.Status);
            Assert.Contains("resume to continue", failedPayload.Error, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(keptBytes, failedPayload.DownloadedBytes);
            Assert.Equal(bytes[..keptBytes], await File.ReadAllBytesAsync(destination + ".partial", TestContext.Current.CancellationToken));
            Assert.Empty(await store.ListModelsAsync());

            await service.ResumeDownloadAsync(failed, settings);
            await WaitForWorkerAsync(service, job);

            Assert.Equal(JobStatus.Completed, Assert.Single(await store.ListJobsAsync()).Status);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(destination, TestContext.Current.CancellationToken));
            Assert.False(File.Exists(destination + ".partial"));
            Assert.Equal(new long[] { 16, keptBytes }, requestedOffsets);
        }
        finally
        {
            await release.CancelAsync();
        }
    }

    [Fact]
    public async Task ContentRangeMismatchFailsAndDiscardsPartial()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state.db"));
        await store.InitializeAsync();
        var settings = AppSettings.CreateDefault(root);
        var jobs = new JobEngine(store, Path.Combine(root, "logs"));
        var destination = Path.Combine(settings.ModelsRoot, "mismatch", "model.gguf");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var file = new HuggingFaceFile("owner/repo", "model.gguf", "model.gguf", "", 32, 0);
        await File.WriteAllBytesAsync(destination + ".partial", new byte[8], TestContext.Current.CancellationToken);
        var job = await jobs.CreateAsync("huggingface-download", System.Text.Json.JsonSerializer.Serialize(new DownloadJobPayload(file, destination, 8, 32)), TestContext.Current.CancellationToken);
        using var service = new HuggingFaceService(store, jobs, new ModelCatalogService(store), new ScriptedHttpHandler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(new byte[28]) };
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(4, 31, 32);
            return Task.FromResult(response);
        }));

        await service.ResumeDownloadAsync(job with { Status = JobStatus.Paused }, settings);
        await WaitForWorkerAsync(service, job);

        var persisted = Assert.Single(await store.ListJobsAsync());
        Assert.Equal(JobStatus.Failed, persisted.Status);
        Assert.DoesNotContain("resume to continue", HuggingFaceService.ParseDownloadPayload(persisted.PayloadJson)!.Error, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(destination + ".partial"));
    }

    [Theory]
    [InlineData(typeof(HttpRequestException), true)]
    [InlineData(typeof(IOException), true)]
    [InlineData(typeof(TaskCanceledException), true)]
    [InlineData(typeof(InvalidDataException), false)]
    [InlineData(typeof(InvalidOperationException), false)]
    public void TransientClassificationSeparatesIntegrityFailures(Type exceptionType, bool transient)
        => Assert.Equal(transient, HuggingFaceService.IsTransientDownloadFailure((Exception)Activator.CreateInstance(exceptionType)!));

    private static HttpResponseMessage RangeResponse(byte[] bytes, long from, Stream body)
    {
        var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new StreamContent(body) };
        response.Content.Headers.ContentLength = bytes.Length - from;
        response.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, bytes.Length - 1, bytes.Length);
        return response;
    }

    private static async Task WaitForWorkerAsync(HuggingFaceService service, JobRecord job)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        while (service.IsDownloadActive(job.Id)) await Task.Delay(10, timeout.Token);
    }

    private sealed class ScriptedHttpHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => respond(request, cancellationToken);
    }

    private sealed class StallAfterPrefixStream(byte[] prefix, CancellationToken release) : Stream
    {
        private bool _prefixRead;

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!_prefixRead)
            {
                _prefixRead = true;
                prefix.CopyTo(buffer);
                return prefix.Length;
            }
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, release);
            await Task.Delay(Timeout.InfiniteTimeSpan, linked.Token);
            return 0;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
