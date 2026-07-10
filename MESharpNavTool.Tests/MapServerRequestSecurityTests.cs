using MESharp.Services;
using System.Net;
using System.Net.Http;
using Xunit;

namespace MESharpNavTool.Tests;

public sealed class MapServerRequestSecurityTests
{
    [Fact]
    public void CreateToken_IsNonEmptyAndDoesNotRepeat()
    {
        var first = MapServerRequestSecurity.CreateToken();
        var second = MapServerRequestSecurity.CreateToken();

        Assert.Equal(64, first.Length); // 32 random bytes as upper-case hexadecimal
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void IsValidToken_OnlyAcceptsTheExactCapability()
    {
        const string expected = "D4A6FF0CEAF1B0D75B02C6F1F6AC1A729B92D5D15C53B2D0824A899B97A332D1";

        Assert.True(MapServerRequestSecurity.IsValidToken(expected, expected));
        Assert.False(MapServerRequestSecurity.IsValidToken(null, expected));
        Assert.False(MapServerRequestSecurity.IsValidToken(string.Empty, expected));
        Assert.False(MapServerRequestSecurity.IsValidToken(expected[..^1] + "0", expected));
        Assert.False(MapServerRequestSecurity.IsValidToken(expected, null));
    }
}

public sealed class MapTravelStatusTests
{
    [Fact]
    public void FromResult_ReportsFalseResultAsFailure()
    {
        var status = MapTravelStatus.FromResult("Travel to 3200,3200,0", succeeded: false, cancellationRequested: false);

        Assert.Equal("Travel to 3200,3200,0 failed: destination was not reached.", status);
    }

    [Fact]
    public void FromResult_DistinguishesSuccessAndCancellation()
    {
        Assert.Equal("Walk finished.", MapTravelStatus.FromResult("Walk", succeeded: true, cancellationRequested: false));
        Assert.Equal("Walk cancelled.", MapTravelStatus.FromResult("Walk", succeeded: false, cancellationRequested: true));
    }
}

public sealed class CoverageMapServerAuthorizationTests
{
    [Fact]
    public async Task MutationApi_RejectsMissingCapabilityAndAcceptsFragmentCapability()
    {
        var mapUrl = new Uri(CoverageMapServer.Start());
        var endpoint = new Uri(mapUrl.GetLeftPart(UriPartial.Path) + "api/not_a_real_endpoint");
        var token = new UriQuery(mapUrl.Fragment).Get("mapToken");

        try
        {
            using var client = new HttpClient();
            using var missingToken = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
            var rejected = await client.PostAsync(endpoint, missingToken);
            Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);

            using var authorized = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json")
            };
            authorized.Headers.Add(MapServerRequestSecurity.TokenHeaderName, token);
            var accepted = await client.SendAsync(authorized);
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        }
        finally
        {
            CoverageMapServer.Stop();
        }
    }

    private sealed class UriQuery
    {
        private readonly System.Collections.Generic.Dictionary<string, string> _values;

        public UriQuery(string fragment)
        {
            _values = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in fragment.TrimStart('#').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var separator = pair.IndexOf('=');
                if (separator > 0)
                    _values[Uri.UnescapeDataString(pair[..separator])] = Uri.UnescapeDataString(pair[(separator + 1)..]);
            }
        }

        public string Get(string key) => Assert.IsType<string>(_values.GetValueOrDefault(key));
    }
}
