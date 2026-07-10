using System;
using System.Security.Cryptography;
using System.Text;

namespace MESharp.Services
{
    /// <summary>
    /// Per-server capability token used by the map page for requests that can alter
    /// navigation data or control the character. The token is carried in the URL
    /// fragment, so it is never sent as part of the initial HTTP request.
    /// </summary>
    internal static class MapServerRequestSecurity
    {
        internal const string TokenHeaderName = "X-MESharp-Map-Token";

        internal static string CreateToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

        internal static bool IsValidToken(string? candidate, string? expected)
        {
            if (string.IsNullOrWhiteSpace(candidate) || string.IsNullOrWhiteSpace(expected))
                return false;

            var candidateBytes = Encoding.UTF8.GetBytes(candidate);
            var expectedBytes = Encoding.UTF8.GetBytes(expected);
            return candidateBytes.Length == expectedBytes.Length &&
                   CryptographicOperations.FixedTimeEquals(candidateBytes, expectedBytes);
        }
    }
}
