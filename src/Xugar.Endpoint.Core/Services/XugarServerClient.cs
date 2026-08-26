using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xugar.Endpoint.Core.Interfaces;
using Xugar.Endpoint.Core.Models;

namespace Xugar.Endpoint.Core.Services;

public sealed class XugarServerClient(HttpClient httpClient) : IXugarServerClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<DeviceEnrollmentResponse> EnrollDeviceAsync(
        DeviceEnrollmentRequest request,
        string enrollmentToken,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(enrollmentToken);
        using var message = CreateJsonRequest(HttpMethod.Post, "api/v1/devices/enroll", request);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", enrollmentToken);
        using var response = await SendAsync(message, cancellationToken).ConfigureAwait(false);
        return await ReadRequiredJsonAsync<DeviceEnrollmentResponse>(response, cancellationToken)
            .ConfigureAwait(false);
    }

    public Task SendHeartbeatAsync(
        DeviceCredential credential,
        DeviceHeartbeatRequest request,
        CancellationToken cancellationToken) =>
        SendAuthenticatedJsonAsync(
            HttpMethod.Post,
            $"api/v1/devices/{credential.DeviceId:D}/heartbeat",
            credential,
            request,
            cancellationToken);

    public Task ReplaceCurrentProcessesAsync(
        DeviceCredential credential,
        CurrentProcessesRequest request,
        CancellationToken cancellationToken) =>
        SendAuthenticatedJsonAsync(
            HttpMethod.Put,
            $"api/v1/devices/{credential.DeviceId:D}/processes/current",
            credential,
            request,
            cancellationToken);

    public Task SendProcessEventsAsync(
        DeviceCredential credential,
        ProcessEventsRequest request,
        CancellationToken cancellationToken) =>
        SendAuthenticatedJsonAsync(
            HttpMethod.Post,
            $"api/v1/devices/{credential.DeviceId:D}/process-events",
            credential,
            request,
            cancellationToken);

    public Task SendAgentEventsAsync(
        DeviceCredential credential,
        AgentEventsRequest request,
        CancellationToken cancellationToken) =>
        SendAuthenticatedJsonAsync(
            HttpMethod.Post,
            $"api/v1/devices/{credential.DeviceId:D}/events",
            credential,
            request,
            cancellationToken);

    public async Task UploadScreenshotAsync(
        DeviceCredential credential,
        ScreenshotUpload upload,
        CancellationToken cancellationToken)
    {
        using var message = CreateAuthenticatedRequest(
            HttpMethod.Post,
            $"api/v1/devices/{credential.DeviceId:D}/screenshots",
            credential);
        await using var stream = new FileStream(
            upload.FilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            useAsync: true);
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(upload.CaptureId.ToString("D")), "captureId");
        form.Add(new StringContent(upload.CapturedAt.ToUniversalTime().ToString("O")), "capturedAt");
        form.Add(new StringContent(upload.MonitorIndex.ToString()), "monitorIndex");
        form.Add(new StringContent(upload.Width.ToString()), "width");
        form.Add(new StringContent(upload.Height.ToString()), "height");
        var fileContent = new StreamContent(stream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(upload.MimeType);
        form.Add(fileContent, "file", Path.GetFileName(upload.FilePath));
        message.Content = form;
        using var response = await SendAsync(message, cancellationToken).ConfigureAwait(false);
    }

    public async Task<MonitoringPolicy> GetPolicyAsync(
        DeviceCredential credential,
        CancellationToken cancellationToken)
    {
        using var message = CreateAuthenticatedRequest(
            HttpMethod.Get,
            $"api/v1/devices/{credential.DeviceId:D}/policy",
            credential);
        using var response = await SendAsync(message, cancellationToken).ConfigureAwait(false);
        return await ReadRequiredJsonAsync<MonitoringPolicy>(response, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task SendAuthenticatedJsonAsync<T>(
        HttpMethod method,
        string relativePath,
        DeviceCredential credential,
        T body,
        CancellationToken cancellationToken)
    {
        using var message = CreateJsonRequest(method, relativePath, body);
        AddDeviceAuthentication(message, credential);
        using var response = await SendAsync(message, cancellationToken).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage message,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await httpClient.SendAsync(
                message,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var status = (int)response.StatusCode;
                var kind = Classify(response.StatusCode);
                response.Dispose();
                throw new XugarServerException(kind, $"Xugar server request failed with HTTP {status}.", status);
            }

            return response;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new XugarServerException(ServerFailureKind.Retryable, "Xugar server request timed out.");
        }
        catch (HttpRequestException exception)
        {
            throw new XugarServerException(
                ServerFailureKind.Retryable,
                "Xugar server could not be reached.",
                innerException: exception);
        }
    }

    private static async Task<T> ReadRequiredJsonAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken)
                .ConfigureAwait(false);
            return result ?? throw new JsonException("Response body was empty.");
        }
        catch (JsonException exception)
        {
            throw new XugarServerException(
                ServerFailureKind.MalformedResponse,
                "Xugar server returned an invalid response.",
                (int)response.StatusCode,
                exception);
        }
    }

    private static HttpRequestMessage CreateJsonRequest<T>(HttpMethod method, string path, T body) =>
        new(method, path) { Content = JsonContent.Create(body, options: JsonOptions) };

    private static HttpRequestMessage CreateAuthenticatedRequest(
        HttpMethod method,
        string path,
        DeviceCredential credential)
    {
        var request = new HttpRequestMessage(method, path);
        AddDeviceAuthentication(request, credential);
        return request;
    }

    private static void AddDeviceAuthentication(HttpRequestMessage request, DeviceCredential credential)
    {
        credential.Validate();
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.DeviceSecret);
        request.Headers.Add("X-Xugar-Device-Id", credential.DeviceId.ToString("D"));
    }

    private static ServerFailureKind Classify(HttpStatusCode statusCode) =>
        statusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => ServerFailureKind.Authentication,
            HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests => ServerFailureKind.Retryable,
            >= HttpStatusCode.InternalServerError => ServerFailureKind.Retryable,
            _ => ServerFailureKind.NonRetryable
        };
}
