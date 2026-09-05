using System.Net;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;

namespace LocalLlmConsole.Tests;

public sealed class DownloadTransferTests : ManagerRegressionTestBase
{
    [Theory]
    [InlineData(3, 8, HttpStatusCode.OK)]
    [InlineData(12, 8, HttpStatusCode.OK)]
    [InlineData(0, 8, HttpStatusCode.Forbidden)]
    public async Task InvalidTransfersPersistFailureWithoutRegisteringOrLeavingPartialFiles(int actual, int expected, HttpStatusCode status)
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state.db"));
        await store.InitializeAsync();
        var jobs = new JobEngine(store, Path.Combine(root, "logs"));
        using var service = new HuggingFaceService(store, jobs, new ModelCatalogService(store), new CapturingHttpHandler(_ =>
            new HttpResponseMessage(status) { Content = new ByteArrayContent(new byte[actual]) }));
        var job = await service.StartDownloadAsync(ModelFile(expected), AppSettings.CreateDefault(root), TestContext.Current.CancellationToken);
        await WaitForWorkerAsync(service, job);

        var persisted = Assert.Single(await store.ListJobsAsync());
        var payload = HuggingFaceService.ParseDownloadPayload(persisted.PayloadJson)!;
        Assert.Equal(JobStatus.Failed, persisted.Status);
        Assert.False(string.IsNullOrWhiteSpace(payload.Error));
        Assert.False(File.Exists(payload.Destination));
        Assert.False(File.Exists(payload.Destination + ".partial"));
        Assert.Empty(await store.ListModelsAsync());
        Assert.Equal(0, service.ActiveDownloadCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PausePreservesPartialBytesAndStopRemovesThem(bool pause)
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state.db"));
        await store.InitializeAsync();
        var jobs = new JobEngine(store, Path.Combine(root, "logs"));
        using var stream = new PrefixThenWaitStream();
        using var service = new HuggingFaceService(store, jobs, new ModelCatalogService(store), new CapturingHttpHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) }));
        var settings = AppSettings.CreateDefault(root);
        var job = await service.StartDownloadAsync(ModelFile(8), settings, TestContext.Current.CancellationToken);
        try
        {
            await stream.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartDownloadAsync(ModelFile(8), settings, TestContext.Current.CancellationToken));
            Assert.Single(await store.ListJobsAsync());
            if (pause) await service.PauseDownloadAsync(job);
            else await service.StopDownloadAsync(job);
            await WaitForWorkerAsync(service, job);

            var persisted = Assert.Single(await store.ListJobsAsync());
            var payload = HuggingFaceService.ParseDownloadPayload(persisted.PayloadJson)!;
            Assert.Equal(pause ? JobStatus.Paused : JobStatus.Cancelled, persisted.Status);
            Assert.Equal(4, payload.DownloadedBytes);
            Assert.Equal(pause, File.Exists(payload.Destination + ".partial"));
            if (pause) Assert.Equal(new byte[] { 1, 2, 3, 4 }, await File.ReadAllBytesAsync(payload.Destination + ".partial", TestContext.Current.CancellationToken));
            Assert.Empty(await store.ListModelsAsync());
            Assert.False(File.Exists(payload.Destination));
        }
        finally
        {
            await service.StopDownloadAsync(job);
            await WaitForWorkerAsync(service, job);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ResumeHandlesRangeSupportAndServerRestartWithoutDuplicatingBytes(bool acceptsRange)
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state.db"));
        await store.InitializeAsync();
        var settings = AppSettings.CreateDefault(root);
        var jobs = new JobEngine(store, Path.Combine(root, "logs"));
        var destination = Path.Combine(settings.ModelsRoot, "resumed", "model.gguf");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        // A valid minimal GGUF exercises final verification and registration, not just HTTP copying.
        var source = Path.Combine(root, "source.gguf");
        WriteMinimalGguf(source);
        var bytes = await File.ReadAllBytesAsync(source, TestContext.Current.CancellationToken);
        var file = ModelFile(bytes.Length) with { Sha256 = Sha256Hex(bytes) };
        await File.WriteAllBytesAsync(destination + ".partial", bytes[..16], TestContext.Current.CancellationToken);
        var payload = new DownloadJobPayload(file, destination, 16, bytes.Length);
        var job = await jobs.CreateAsync("huggingface-download", System.Text.Json.JsonSerializer.Serialize(payload), TestContext.Current.CancellationToken);
        await jobs.UpdateAsync(job, JobStatus.Paused, job.PayloadJson, TestContext.Current.CancellationToken);
        var transferRequests = 0;
        using var service = new HuggingFaceService(store, jobs, new ModelCatalogService(store), new CapturingHttpHandler(request =>
        {
            if (!request.RequestUri!.AbsolutePath.EndsWith("model.gguf", StringComparison.Ordinal))
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            transferRequests++;
            Assert.Equal(16, Assert.Single(request.Headers.Range!.Ranges).From);
            var response = new HttpResponseMessage(acceptsRange ? HttpStatusCode.PartialContent : HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(acceptsRange ? bytes[16..] : bytes)
            };
            if (acceptsRange) response.Content.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(16, bytes.Length - 1, bytes.Length);
            return response;
        }));
        await service.ResumeDownloadAsync(job with { Status = JobStatus.Paused }, settings);
        await WaitForWorkerAsync(service, job);

        Assert.Equal(JobStatus.Completed, Assert.Single(await store.ListJobsAsync()).Status);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(destination, TestContext.Current.CancellationToken));
        Assert.False(File.Exists(destination + ".partial"));
        Assert.Equal(destination, Assert.Single(await store.ListModelsAsync()).ModelPath);
        Assert.Equal(1, transferRequests);
    }

    private static HuggingFaceFile ModelFile(long size)
        => new("owner/repo", "model.gguf", "model.gguf", "", size, 0);

    private static async Task WaitForWorkerAsync(HuggingFaceService service, JobRecord job)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        while (service.IsDownloadActive(job.Id)) await Task.Delay(10, timeout.Token);
    }

    private sealed class PrefixThenWaitStream : Stream
    {
        private bool _prefixRead;
        internal TaskCompletionSource Waiting { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!_prefixRead)
            {
                _prefixRead = true;
                new byte[] { 1, 2, 3, 4 }.CopyTo(buffer);
                return 4;
            }
            Waiting.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
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
