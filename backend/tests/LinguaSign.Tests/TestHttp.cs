using System.Net;
using System.Text;
using System.Text.Json;

namespace LinguaSign.Tests;

/// <summary>Returns one canned response for any request — for testing OpenAI-compatible clients offline.</summary>
public sealed class StubHttpMessageHandler(HttpResponseMessage response) : HttpMessageHandler
{
    public HttpRequestMessage? LastRequest { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        return Task.FromResult(response);
    }
}

public static class TestHttp
{
    /// <summary>
    /// HttpClient whose chat-completions response carries <paramref name="modelContent"/> as the
    /// assistant message content (mimicking Ollama's OpenAI-compatible endpoint).
    /// </summary>
    public static HttpClient ChatClient(string modelContent)
    {
        var body = JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { content = modelContent } } },
        });
        var resp = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        return new HttpClient(new StubHttpMessageHandler(resp)) { BaseAddress = new Uri("http://localhost/v1/") };
    }

    /// <summary>
    /// HttpClient whose /translate response returns <paramref name="responseJson"/> verbatim
    /// (mimicking the LinguaSign translation sidecar).
    /// </summary>
    public static HttpClient TranslationClient(string responseJson)
    {
        var resp = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
        };
        return new HttpClient(new StubHttpMessageHandler(resp)) { BaseAddress = new Uri("http://localhost/") };
    }
}
