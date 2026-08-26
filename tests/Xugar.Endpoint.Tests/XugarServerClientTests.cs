using System.Net;
using System.Text;
using Xugar.Endpoint.Core.Models;
using Xugar.Endpoint.Core.Services;

namespace Xugar.Endpoint.Tests;

public sealed class XugarServerClientTests
{
    [Fact]
    public async Task EnrollmentUsesBootstrapBearerAndParsesCredential()
    {
        HttpRequestMessage? captured = null;
        var issuedId = Guid.NewGuid();
        var handler = new DelegateHandler(async request =>
        {
            captured = request;
            _ = await request.Content!.ReadAsStringAsync();
            return JsonResponse($$"""{"deviceId":"{{issuedId:D}}","deviceSecret":"issued-secret"}""");
        });
        var client = CreateClient(handler);

        var result = await client.EnrollDeviceAsync(
            new DeviceEnrollmentRequest(Guid.NewGuid(), "host", "user", null, "Windows", "1.0"),
            "bootstrap-token",
            CancellationToken.None);

        Assert.Equal(issuedId, result.DeviceId);
        Assert.Equal("issued-secret", result.DeviceSecret);
        Assert.Equal("Bearer", captured?.Headers.Authorization?.Scheme);
        Assert.Equal("bootstrap-token", captured?.Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task DeviceRequestUsesExistingAuthenticationContractWithoutLeakingSecretInErrors()
    {
        HttpRequestMessage? captured = null;
        var handler = new DelegateHandler(request =>
        {
            captured = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        });
        var secret = "never-log-this-device-secret";
        var credential = new DeviceCredential(Guid.NewGuid(), secret);

        var exception = await Assert.ThrowsAsync<XugarServerException>(() =>
            CreateClient(handler).SendHeartbeatAsync(
                credential,
                new DeviceHeartbeatRequest(DateTimeOffset.UtcNow, "1", "Windows", 1),
                CancellationToken.None));

        Assert.Equal(ServerFailureKind.Authentication, exception.Kind);
        Assert.DoesNotContain(secret, exception.ToString(), StringComparison.Ordinal);
        Assert.Equal(secret, captured?.Headers.Authorization?.Parameter);
        Assert.Equal(credential.DeviceId.ToString("D"), captured?.Headers.GetValues("X-Xugar-Device-Id").Single());
    }

    [Fact]
    public async Task MalformedResponseAndUnavailableServerAreClassified()
    {
        var malformed = CreateClient(new DelegateHandler(_ => Task.FromResult(JsonResponse("not-json"))));
        var malformedError = await Assert.ThrowsAsync<XugarServerException>(() => malformed.EnrollDeviceAsync(
            new DeviceEnrollmentRequest(Guid.NewGuid(), "host", null, null, "Windows", "1"),
            "token",
            CancellationToken.None));
        Assert.Equal(ServerFailureKind.MalformedResponse, malformedError.Kind);

        var offline = CreateClient(new DelegateHandler(_ => throw new HttpRequestException("offline")));
        var offlineError = await Assert.ThrowsAsync<XugarServerException>(() => offline.EnrollDeviceAsync(
            new DeviceEnrollmentRequest(Guid.NewGuid(), "host", null, null, "Windows", "1"),
            "token",
            CancellationToken.None));
        Assert.Equal(ServerFailureKind.Retryable, offlineError.Kind);
    }

    [Fact]
    public async Task ScreenshotUploadUsesMultipartMetadataAndCorrectMimeType()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "capture.png");
        await File.WriteAllBytesAsync(path, [137, 80, 78, 71, 13, 10, 26, 10]);
        string? contentType = null;
        string? body = null;
        var handler = new DelegateHandler(async request =>
        {
            contentType = request.Content?.Headers.ContentType?.MediaType;
            body = await request.Content!.ReadAsStringAsync();
            return JsonResponse("{}");
        });
        var captureId = Guid.NewGuid();

        await CreateClient(handler).UploadScreenshotAsync(
            new DeviceCredential(Guid.NewGuid(), "secret"),
            new ScreenshotUpload(captureId, DateTimeOffset.UtcNow, 1, 100, 50, path, "image/png"),
            CancellationToken.None);

        Assert.Equal("multipart/form-data", contentType);
        Assert.Contains(captureId.ToString("D"), body, StringComparison.Ordinal);
        Assert.Contains("image/png", body, StringComparison.Ordinal);
    }

    private static XugarServerClient CreateClient(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:3000/") });

    private static HttpResponseMessage JsonResponse(string body) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

    private sealed class DelegateHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => send(request);
    }
}
