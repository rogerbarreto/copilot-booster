using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

namespace CopilotBooster.Tests.Services;

public sealed class CopilotModelsServiceTests : IDisposable
{
    private const string Token = "gho_fake_token";

    private static readonly DateTime s_now = new(2026, 5, 9, 10, 30, 0, DateTimeKind.Utc);

    private readonly string _originalLocalAppData;
    private readonly string _localAppData = Path.Combine(Path.GetTempPath(), $"cb-models-{Guid.NewGuid():N}");

    public CopilotModelsServiceTests()
    {
        this._originalLocalAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? string.Empty;
        Environment.SetEnvironmentVariable("LOCALAPPDATA", this._localAppData);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("LOCALAPPDATA", string.IsNullOrEmpty(this._originalLocalAppData) ? null : this._originalLocalAppData);
        DeleteDirectory(this._localAppData);
    }

    [Fact]
    public async Task GetModelsAsync_FreshCacheHit_ReturnsCachedModelsWithoutProcessOrHttpAsync()
    {
        await this.WriteCacheAsync(s_now.AddHours(-1), ["cached-model-1", "cached-model-2"]).ConfigureAwait(false);
        var handler = new RecordingHttpMessageHandler(_ => throw new InvalidOperationException("HTTP should not be called for fresh cache."));
        var processCallCount = 0;
        var service = new CopilotModelsService(handler, () => s_now, (_, _) =>
        {
            processCallCount++;
            return Task.FromResult((0, Token, string.Empty));
        });

        var models = await service.GetModelsAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);

        Assert.Equal(["cached-model-1", "cached-model-2"], models);
        Assert.Empty(handler.Requests);
        Assert.Equal(0, processCallCount);
    }

    [Fact]
    public async Task GetModelsAsync_StaleCacheAndHttpSuccess_ReturnsFetchedModelsAndPersistsFreshCacheAsync()
    {
        await this.WriteCacheAsync(s_now.AddHours(-25), ["old-model"]).ConfigureAwait(false);
        var handler = new RecordingHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, ModelsJson("claude-sonnet-4.6", "gpt-5.5", "text-embedding-3-small")));
        var service = new CopilotModelsService(handler, () => s_now, SuccessfulTokenRunner);

        var models = await service.GetModelsAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);

        Assert.Equal(["claude-sonnet-4.6", "gpt-5.5"], models);
        var cache = await this.ReadCacheAsync().ConfigureAwait(false);
        Assert.Equal(s_now, cache.FetchedAt);
        Assert.Equal(["claude-sonnet-4.6", "gpt-5.5"], cache.Models);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("https://api.githubcopilot.com/models", request.RequestUri!.ToString());
        Assert.Equal(Token, Assert.Single(request.Headers.GetValues("Authorization")));
    }

    [Fact]
    public async Task GetModelsAsync_NoCacheAndHttpSuccess_ReturnsFetchedModelsAndCreatesCacheAsync()
    {
        var handler = new RecordingHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, ModelsJson("claude-sonnet-4.6", "gpt-5.5", "text-embedding-3-small")));
        var service = new CopilotModelsService(handler, () => s_now, SuccessfulTokenRunner);

        var models = await service.GetModelsAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);

        Assert.Equal(["claude-sonnet-4.6", "gpt-5.5"], models);
        Assert.True(File.Exists(this.GetCacheFile()));
        var cache = await this.ReadCacheAsync().ConfigureAwait(false);
        Assert.Equal(s_now, cache.FetchedAt);
        Assert.Equal(["claude-sonnet-4.6", "gpt-5.5"], cache.Models);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData(1, "gh auth failed")]
    [InlineData(0, "")]
    public async Task GetModelsAsync_GhAuthTokenFails_ReturnsHardcodedFallbackWithoutHttpAsync(int exitCode, string stdout)
    {
        var handler = new RecordingHttpMessageHandler(_ => throw new InvalidOperationException("HTTP should not be called without a token."));
        var service = new CopilotModelsService(handler, () => s_now, (_, _) => Task.FromResult((exitCode, stdout, string.Empty)));

        var models = await service.GetModelsAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);

        Assert.Equal(FallbackModels(), models);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task GetModelsAsync_HttpUnauthorized_ReturnsHardcodedFallbackAsync()
    {
        var handler = new RecordingHttpMessageHandler(_ => JsonResponse(HttpStatusCode.Unauthorized, "{}"));
        var service = new CopilotModelsService(handler, () => s_now, SuccessfulTokenRunner);

        var models = await service.GetModelsAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);

        Assert.Equal(FallbackModels(), models);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task GetModelsAsync_HttpServerErrorWithStaleCache_ReturnsStaleCacheAsync()
    {
        await this.WriteCacheAsync(s_now.AddHours(-25), ["custom-model-1", "custom-model-2"]).ConfigureAwait(false);
        var handler = new RecordingHttpMessageHandler(_ => JsonResponse(HttpStatusCode.InternalServerError, "{}"));
        var service = new CopilotModelsService(handler, () => s_now, SuccessfulTokenRunner);

        var models = await service.GetModelsAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);

        Assert.Equal(["custom-model-1", "custom-model-2"], models);
        Assert.NotEqual(FallbackModels(), models);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task GetModelsAsync_HttpServerErrorWithoutCache_ReturnsHardcodedFallbackAsync()
    {
        var handler = new RecordingHttpMessageHandler(_ => JsonResponse(HttpStatusCode.InternalServerError, "{}"));
        var service = new CopilotModelsService(handler, () => s_now, SuccessfulTokenRunner);

        var models = await service.GetModelsAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);

        Assert.Equal(FallbackModels(), models);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task GetModelsAsync_MalformedJsonResponse_ReturnsHardcodedFallbackAsync()
    {
        var handler = new RecordingHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, "not valid json"));
        var service = new CopilotModelsService(handler, () => s_now, SuccessfulTokenRunner);

        var models = await service.GetModelsAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);

        Assert.Equal(FallbackModels(), models);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task GetModelsAsync_ResponseWithoutDataArray_ReturnsHardcodedFallbackAsync()
    {
        var handler = new RecordingHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, "{\"object\":\"list\"}"));
        var service = new CopilotModelsService(handler, () => s_now, SuccessfulTokenRunner);

        var models = await service.GetModelsAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);

        Assert.Equal(FallbackModels(), models);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task GetModelsAsync_PreCancelledToken_ThrowsOperationCanceledExceptionAsync()
    {
        var handler = new RecordingHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, ModelsJson("claude-sonnet-4.6")));
        var service = new CopilotModelsService(handler, () => s_now, SuccessfulTokenRunner);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync().ConfigureAwait(false);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetModelsAsync(cts.Token)).ConfigureAwait(false);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task GetModelsAsync_ApiReturnsEmbeddingModels_DropsEmbeddingIdsAsync()
    {
        var handler = new RecordingHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, ModelsJson(
            "text-embedding-ada-002",
            "claude-sonnet-4.6",
            "text-embedding-3-small",
            "gpt-5.5")));
        var service = new CopilotModelsService(handler, () => s_now, SuccessfulTokenRunner);

        var models = await service.GetModelsAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);

        Assert.Equal(["claude-sonnet-4.6", "gpt-5.5"], models);
    }

    private static IReadOnlyList<string> FallbackModels()
    {
        return
        [
            "claude-sonnet-4.6",
            "claude-sonnet-4.5",
            "claude-haiku-4.5",
            "claude-opus-4.7",
            "claude-opus-4.6",
            "claude-opus-4.6-fast",
            "claude-opus-4.5",
            "claude-sonnet-4",
            "gpt-5.5",
            "gpt-5.4",
            "gpt-5.3-codex",
            "gpt-5.2-codex",
            "gpt-5.2",
            "gpt-5.1",
            "gpt-5.4-mini",
            "gpt-5-mini",
            "gpt-4.1"
        ];
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private static string ModelsJson(params string[] ids)
    {
        return JsonSerializer.Serialize(new
        {
            @object = "list",
            data = ids.Select(id => new { id }).ToArray()
        });
    }

    private static Task<(int ExitCode, string Stdout, string Stderr)> SuccessfulTokenRunner(string fileName, string? arguments)
    {
        Assert.Equal("gh", fileName);
        Assert.Equal("auth token", arguments);
        return Task.FromResult((0, $"{Token}\n", string.Empty));
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private string GetCacheFile()
    {
        return Path.Combine(this._localAppData, "CopilotBooster", "models-cache.json");
    }

    private async Task WriteCacheAsync(DateTime fetchedAt, IReadOnlyList<string> models)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(this.GetCacheFile())!);
        var json = JsonSerializer.Serialize(new { fetchedAt = fetchedAt.ToString("O"), models });
        await File.WriteAllTextAsync(this.GetCacheFile(), json).ConfigureAwait(false);
    }

    private async Task<(DateTime FetchedAt, IReadOnlyList<string> Models)> ReadCacheAsync()
    {
        var json = await File.ReadAllTextAsync(this.GetCacheFile()).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var fetchedAt = DateTime.Parse(doc.RootElement.GetProperty("fetchedAt").GetString()!, null, DateTimeStyles.RoundtripKind);
        var models = doc.RootElement.GetProperty("models").EnumerateArray().Select(element => element.GetString()!).ToArray();
        return (fetchedAt, models);
    }

    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        internal List<HttpRequestMessage> Requests { get; } = [];

        internal RecordingHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            this._responseFactory = responseFactory;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            this.Requests.Add(request);
            return Task.FromResult(this._responseFactory(request));
        }
    }
}
