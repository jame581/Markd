using System.Net;
using System.Text;

namespace Markd.Tests;

/// <summary>
/// A scripted HTTP fake: answers requests in order from a queue and records each request with its body.
/// A request beyond the script fails the test.
/// </summary>
public sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, CancellationToken, HttpResponseMessage>> _script = new();

    public List<RecordedRequest> Requests { get; } = [];

    public sealed record RecordedRequest(HttpMethod Method, string Url, string? Authorization, string? IfMatch, byte[] Body, string? ContentType);

    public FakeHttpHandler Respond(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> respond)
    {
        _script.Enqueue(respond);
        return this;
    }

    public FakeHttpHandler Respond(HttpStatusCode status, string? json = null, string? etagHeader = null) =>
        Respond((_, _) =>
        {
            var response = new HttpResponseMessage(status);
            if (json is not null)
                response.Content = new StringContent(json, Encoding.UTF8, "application/json");
            if (etagHeader is not null)
                response.Headers.TryAddWithoutValidation("ETag", etagHeader);
            return response;
        });

    public FakeHttpHandler RespondBytes(byte[] data) =>
        Respond((_, _) => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(data) });

    public FakeHttpHandler Throw(Exception ex) => Respond((_, _) => throw ex);

    public int Remaining => _script.Count;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken);
        Requests.Add(new RecordedRequest(
            request.Method,
            request.RequestUri!.AbsoluteUri,
            request.Headers.Authorization?.ToString(),
            request.Headers.TryGetValues("If-Match", out var ifMatch) ? string.Join(",", ifMatch) : null,
            body,
            request.Content?.Headers.ContentType?.ToString()));

        if (_script.Count == 0)
            throw new InvalidOperationException($"Unscripted request {request.Method} {request.RequestUri}");
        cancellationToken.ThrowIfCancellationRequested();
        return _script.Dequeue()(request, cancellationToken);
    }
}
