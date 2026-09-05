using System.Diagnostics;
using System.Net;
using System.Text;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;

namespace LocalLlmConsole.Tests;

public abstract partial class ManagerRegressionTestBase
{
    protected sealed class AcceptingUpdateSignatureVerifier : IAppUpdateSignatureVerifier
    {
        public void Verify(string path, string expectedPublisher, string? expectedSignerPath = null)
        {
        }
    }

    protected sealed class FakeLocalAppServiceHost : ILocalAppServiceHost
    {
        private readonly Exception? _failure;

        public FakeLocalAppServiceHost(int port, Exception? failure = null)
        {
            _failure = failure;
            BaseUri = new Uri($"http://127.0.0.1:{port}/");
        }

        public Uri BaseUri { get; }
        public bool Started { get; private set; }
        public bool Disposed { get; private set; }

        public Task StartAsync()
        {
            if (_failure is not null) throw _failure;
            Started = true;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    protected sealed class FakeSingleInstanceLease(bool ownsInstance) : ISingleInstanceLease
    {
        public bool OwnsInstance { get; private set; } = ownsInstance;
        public bool Released { get; private set; }
        public bool Disposed { get; private set; }

        public void Release()
        {
            Released = true;
            OwnsInstance = false;
        }

        public void Dispose() => Disposed = true;
    }

    protected sealed class FakeDownloadOperations : IHuggingFaceDownloadOperations
    {
        private readonly HashSet<string> _activeJobIds = new(StringComparer.OrdinalIgnoreCase);

        public List<string> ResumedJobIds { get; } = [];
        public List<string> PausedJobIds { get; } = [];
        public List<string> StoppedJobIds { get; } = [];

        public Task ResumeDownloadAsync(JobRecord job, AppSettings settings)
        {
            ResumedJobIds.Add(job.Id);
            _activeJobIds.Add(job.Id);
            return Task.CompletedTask;
        }

        public Task PauseDownloadAsync(JobRecord job)
        {
            PausedJobIds.Add(job.Id);
            _activeJobIds.Remove(job.Id);
            return Task.CompletedTask;
        }

        public Task StopDownloadAsync(JobRecord job)
        {
            StoppedJobIds.Add(job.Id);
            _activeJobIds.Remove(job.Id);
            return Task.CompletedTask;
        }

        public bool IsDownloadActive(string jobId) => _activeJobIds.Contains(jobId);
    }

    protected sealed class CapturingHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestUri = request.RequestUri;
            return Task.FromResult(respond(request));
        }
    }

    protected sealed class UnknownLengthHttpContent(byte[] bytes) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => stream.WriteAsync(bytes).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    protected sealed class FakeModelGatewayRuntimeController : IModelGatewayRuntimeController
    {
        public Task<IReadOnlyList<ModelGatewayModelRoute>> ListModelsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ModelGatewayModelRoute>>([]);

        public Task<IReadOnlyList<LoadedModelSessionSnapshot>> RunningSessionsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<LoadedModelSessionSnapshot>>([]);

        public Task<LoadedModelSessionSnapshot> EnsureModelLoadedAsync(
            ModelGatewayModelRoute route,
            ModelGatewaySwapPolicy policy,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    protected sealed class FakeModelGatewayHost(Exception? startFailure = null) : IModelGatewayHost
    {
        public bool Started { get; private set; }
        public bool Disposed { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            if (startFailure is not null) throw startFailure;
            Started = true;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    protected sealed class ManualUiTimerFactory : IUiTimerFactory
    {
        public List<ManualUiTimer> Timers { get; } = [];

        public IUiTimer Create(TimeSpan interval)
        {
            var timer = new ManualUiTimer(interval);
            Timers.Add(timer);
            return timer;
        }
    }

    protected sealed class ManualUiTimer(TimeSpan interval) : IUiTimer
    {
        public TimeSpan Interval { get; } = interval;
        public bool Started { get; private set; }
        public event EventHandler? Tick;

        public void Start() => Started = true;
        public void Stop() => Started = false;
        public void Fire() => Tick?.Invoke(this, EventArgs.Empty);

        public async Task FireAsync()
        {
            Fire();
            await Task.Yield();
        }
    }

    protected sealed class ScriptedProcessRunner(Func<ProcessStartInfo, ProcessRunResult> handler) : IProcessRunner
    {
        public List<IReadOnlyList<string>> Commands { get; } = [];
        public List<string> StandardInputs { get; } = [];

        public Task<ProcessRunResult> RunAsync(
            ProcessStartInfo startInfo,
            TimeSpan timeout,
            CancellationToken cancellationToken = default,
            string? standardInput = null)
        {
            Commands.Add(startInfo.ArgumentList.ToArray());
            StandardInputs.Add(standardInput ?? "");
            return Task.FromResult(handler(startInfo));
        }
    }

    protected sealed class GatewayIntegrationRuntimeController(
        IReadOnlyList<ModelRecord> models,
        AppSettings launchSettings) : IModelGatewayRuntimeController
    {
        private readonly List<LoadedModelSessionSnapshot> _sessions = [];

        public Exception? LoadFailure { get; init; }
        public TaskCompletionSource? LoadStarted { get; init; }
        public TaskCompletionSource? ContinueLoad { get; init; }
        public bool IgnoreLoadCancellation { get; init; }
        public int EnsureLoadedCount { get; private set; }

        public Task<IReadOnlyList<ModelGatewayModelRoute>> ListModelsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ModelGatewayModelRoute>>(models.Select(model => new ModelGatewayModelRoute(
                model,
                new NamedModelLaunchProfile(
                    $"default:{model.Id}",
                    model.Id,
                    "Default",
                    ModelLaunchSettings.FromAppSettings(launchSettings),
                    model.UpdatedAt,
                    true))).ToArray());

        public Task<IReadOnlyList<LoadedModelSessionSnapshot>> RunningSessionsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<LoadedModelSessionSnapshot>>(_sessions.ToArray());

        public async Task<LoadedModelSessionSnapshot> EnsureModelLoadedAsync(
            ModelGatewayModelRoute route,
            ModelGatewaySwapPolicy policy,
            CancellationToken cancellationToken = default)
        {
            EnsureLoadedCount++;
            if (LoadFailure is not null) throw LoadFailure;
            LoadStarted?.TrySetResult();
            if (ContinueLoad is not null)
            {
                if (IgnoreLoadCancellation)
                    await ContinueLoad.Task;
                else
                    await ContinueLoad.Task.WaitAsync(cancellationToken);
            }
            var model = route.Model;
            var session = new LoadedModelSessionSnapshot(
                "gateway-session",
                model.Id,
                model.Name,
                "runtime-id",
                "CPU runtime",
                RuntimeMode.Native,
                RuntimeBackend.Cpu,
                launchSettings,
                "runtime.log",
                DateTimeOffset.UtcNow,
                "",
                123,
                LoadedModelSessionStatus.Running,
                IsRunning: true,
                IsSelected: true,
                LaunchProfileId: route.Profile.Id,
                LaunchProfileName: route.Profile.Name);
            _sessions.Add(session);
            return session;
        }
    }

    protected sealed class GatewayProxyHandler : HttpMessageHandler
    {
        public List<(string PathAndQuery, string Authorization, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add((
                request.RequestUri?.PathAndQuery ?? "",
                request.Headers.Authorization?.ToString() ?? "",
                request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken)));
            return new HttpResponseMessage(System.Net.HttpStatusCode.Accepted)
            {
                Content = new StringContent("{\"proxied\":true}", Encoding.UTF8, "application/json")
            };
        }
    }

    protected sealed class BlockingReadStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
