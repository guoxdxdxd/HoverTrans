using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace HoverTrans.Core.Model;

/// <summary>Streams one translation from llama-server's OpenAI-compatible chat endpoint.</summary>
public sealed class LlamaTranslationClient
{
    private readonly HttpClient _http = new() { Timeout = Timeout.InfiniteTimeSpan };

    public async IAsyncEnumerable<string> StreamAsync(
        string baseUrl,
        string? apiKey,
        string prompt,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var payload = new
        {
            model = "hy-mt2",
            messages = new[] { new { role = "user", content = prompt } },
            stream = true,
            temperature = 0.1,
            top_p = 0.6,
            max_tokens = 2048
        };

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            baseUrl.TrimEnd('/') + "/v1/chat/completions")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json")
        };
        request.Headers.Accept.ParseAdd("text/event-stream");

        // llama-server runs with a per-start --api-key, so another local process cannot use the model.
        if (apiKey is { Length: > 0 })
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        using var response = await _http
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new HttpRequestException(
                $"翻译服务返回 {(int)response.StatusCode} {response.StatusCode}：{Trim(detail)}");
        }

        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (!string.Equals(mediaType, "text/event-stream", StringComparison.OrdinalIgnoreCase))
        {
            // Some llama-server builds answer a stream request with a single JSON object.
            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var content = ReadMessageContent(json);
            if (!string.IsNullOrEmpty(content))
            {
                yield return content;
            }

            yield break;
        }

        await using var stream = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                yield break;
            }

            if (!line.StartsWith("data:", StringComparison.Ordinal))
            {
                continue;
            }

            var data = line[5..].Trim();
            if (data.Length == 0)
            {
                continue;
            }

            if (data == "[DONE]")
            {
                yield break;
            }

            var delta = ReadDelta(data);
            if (!string.IsNullOrEmpty(delta))
            {
                yield return delta;
            }
        }
    }

    private static string? ReadDelta(string data)
    {
        try
        {
            using var document = JsonDocument.Parse(data);
            if (!document.RootElement.TryGetProperty("choices", out var choices)
                || choices.ValueKind != JsonValueKind.Array
                || choices.GetArrayLength() == 0)
            {
                return null;
            }

            var choice = choices[0];
            if (choice.TryGetProperty("delta", out var delta)
                && delta.TryGetProperty("content", out var content)
                && content.ValueKind == JsonValueKind.String)
            {
                return content.GetString();
            }
        }
        catch (JsonException)
        {
            // A keepalive or a malformed SSE line: skip it.
        }

        return null;
    }

    private static string? ReadMessageContent(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("choices", out var choices)
                || choices.ValueKind != JsonValueKind.Array
                || choices.GetArrayLength() == 0)
            {
                return null;
            }

            var message = choices[0].GetProperty("message");
            return message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String
                ? content.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Trim(string value)
    {
        var singleLine = value.ReplaceLineEndings(" ").Trim();
        return singleLine.Length <= 400 ? singleLine : singleLine[..400] + "…";
    }
}