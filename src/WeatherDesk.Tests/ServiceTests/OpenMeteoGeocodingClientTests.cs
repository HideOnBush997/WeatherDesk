using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using WeatherDesk.Domain.Results;
using WeatherDesk.Infrastructure.Http;

namespace WeatherDesk.Tests.ServiceTests;

public sealed class OpenMeteoGeocodingClientTests
{
    [Fact]
    public async Task Search_maps_locations_and_encodes_the_query_parameters()
    {
        Uri? requestedUri = null;
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            requestedUri = request.RequestUri;
            return Task.FromResult(JsonResponse("""
                {
                  "results": [
                    {
                      "id": 1,
                      "name": "Shanghai",
                      "latitude": 31.22,
                      "longitude": 121.46,
                      "country": "China",
                      "country_code": "CN",
                      "admin1": "Shanghai",
                      "timezone": "Asia/Shanghai",
                      "utc_offset_seconds": 28800
                    },
                    { "id": 2, "latitude": 1.0, "longitude": 2.0 }
                  ]
                }
                """));
        });
        var client = CreateClient(handler);

        var result = await client.SearchAsync("São Paulo & 浦东", CancellationToken.None);

        Assert.True(result.IsSuccess);
        var location = Assert.Single(result.Value!);
        Assert.Equal("1", location.Id);
        Assert.Equal("Shanghai, China", location.DisplayName);
        Assert.Equal(31.22, location.Latitude);
        Assert.Equal(121.46, location.Longitude);
        Assert.Equal("CN", location.CountryCode);
        Assert.Equal("Asia/Shanghai", location.Timezone);
        Assert.Equal(28800, location.UtcOffsetSeconds);
        Assert.NotNull(requestedUri);
        Assert.Equal("geocoding-api.open-meteo.com", requestedUri!.Host);
        Assert.Equal("/v1/search", requestedUri.AbsolutePath);
        Assert.Equal("São Paulo & 浦东", GetQueryValue(requestedUri, "name"));
        Assert.Equal("100", GetQueryValue(requestedUri, "count"));
        Assert.Equal("zh", GetQueryValue(requestedUri, "language"));
        Assert.Equal("json", GetQueryValue(requestedUri, "format"));
    }

    [Fact]
    public async Task Search_uses_china_nominatim_fallback_when_open_meteo_has_no_match()
    {
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            if (request.RequestUri!.Host == "nominatim.openstreetmap.org")
            {
                return Task.FromResult(JsonResponse("""
                    [{"name":"海北藏族自治州","lat":"37.7191885","lon":"100.4565590","address":{"state":"青海省","country_code":"cn"}}]
                    """));
            }
            return Task.FromResult(JsonResponse("{}"));
        });
        var result = await CreateClient(handler).SearchAsync("海北藏族自治州", CancellationToken.None);

        var location = Assert.Single(result.Value!);
        Assert.Equal("海北藏族自治州", location.Name);
        Assert.Equal("青海省", location.Admin1);
        Assert.Equal(37.7191885, location.Latitude);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"results\":[]}")]
    [InlineData("{\"results\":[null]}")]
    public async Task Search_returns_empty_results_when_no_usable_locations_are_present(string json)
    {
        var client = CreateClient(new StubHttpMessageHandler((_, _) =>
            Task.FromResult(JsonResponse(json))));

        var result = await client.SearchAsync("Nowhere", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!);
    }

    [Fact]
    public async Task Search_maps_server_errors_to_retryable_upstream_failures()
    {
        var client = CreateClient(new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError))));

        var result = await client.SearchAsync("Shanghai", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(FailureKind.Upstream, result.Failure!.Kind);
        Assert.True(result.Failure.Retryable);
        Assert.DoesNotContain("Shanghai", result.Failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Search_maps_response_read_io_errors_to_network_failures()
    {
        var client = CreateClient(new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new FailingReadStreamContent()
            })));

        var result = await client.SearchAsync("Shanghai", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(FailureKind.Network, result.Failure!.Kind);
    }

    [Fact]
    public async Task Search_rejects_blank_keyword_without_sending_a_request()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            throw new InvalidOperationException("No request should be sent."));
        var client = CreateClient(handler);

        var result = await client.SearchAsync(" \t ", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(FailureKind.Input, result.Failure!.Kind);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task Search_distinguishes_client_timeout_from_caller_cancellation()
    {
        var timeoutHandler = new StubHttpMessageHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var timeoutClient = new HttpClient(timeoutHandler)
        {
            Timeout = TimeSpan.FromMilliseconds(30)
        };

        var timedOut = await new OpenMeteoGeocodingClient(
            timeoutClient,
            NullLogger<OpenMeteoGeocodingClient>.Instance)
            .SearchAsync("Shanghai", CancellationToken.None);

        Assert.Equal(FailureKind.Timeout, timedOut.Failure!.Kind);

        var cancelHandler = new StubHttpMessageHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var callerClient = new HttpClient(cancelHandler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));

        var canceled = await new OpenMeteoGeocodingClient(
            callerClient,
            NullLogger<OpenMeteoGeocodingClient>.Instance)
            .SearchAsync("Shanghai", cancellation.Token);

        Assert.Equal(FailureKind.Canceled, canceled.Failure!.Kind);
    }

    [Fact]
    public async Task Search_maps_malformed_json_to_data_format_failure()
    {
        var client = CreateClient(new StubHttpMessageHandler((_, _) =>
            Task.FromResult(JsonResponse("{ malformed"))));

        var result = await client.SearchAsync("Shanghai", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(FailureKind.DataFormat, result.Failure!.Kind);
    }

    [Fact]
    public async Task Search_skips_entries_with_missing_or_out_of_range_required_fields()
    {
        var client = CreateClient(new StubHttpMessageHandler((_, _) =>
            Task.FromResult(JsonResponse("""
                { "results": [
                  { "id": 1, "name": "Valid", "latitude": 1, "longitude": 2 },
                  { "id": 2, "name": "Missing latitude", "longitude": 2 },
                  { "id": 3, "name": "Invalid latitude", "latitude": 100, "longitude": 2 }
                ] }
                """))));

        var result = await client.SearchAsync("Valid", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Valid", Assert.Single(result.Value!).Name);
    }

    private static OpenMeteoGeocodingClient CreateClient(HttpMessageHandler handler) =>
        new(
            new HttpClient(handler),
            NullLogger<OpenMeteoGeocodingClient>.Instance);

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private static string? GetQueryValue(Uri uri, string name)
    {
        var pair = uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(value => value.Split('=', 2))
            .FirstOrDefault(value => Uri.UnescapeDataString(value[0]) == name);

        return pair is null || pair.Length < 2
            ? null
            : Uri.UnescapeDataString(pair[1].Replace('+', ' '));
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
        : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return responder(request, cancellationToken);
        }
    }

    private sealed class FailingReadStreamContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context) =>
            Task.FromException(new IOException("Simulated response stream failure."));

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
