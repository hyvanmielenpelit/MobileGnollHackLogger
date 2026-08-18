using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text;
using System.Net.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Overseer.Controllers
{
    [ApiController]
    [Route("api/sentry/log")]
    [IgnoreAntiforgeryToken] // Allowed: Endpoint is protected by [Authorize] and Rate Limiter
    [Authorize]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("TunnelRateLimit")]
    public class SentryTunnelController : ControllerBase
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<SentryTunnelController> _logger;
        private const int MaxPayloadSize = 512 * 1024; // 512 KB

        public SentryTunnelController(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<SentryTunnelController> logger)
        {
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _logger = logger;
        }

        [HttpPost]
        public async Task<IActionResult> Tunnel()
        {
            var dsnString = _configuration["SentryDSN"];
            if (string.IsNullOrEmpty(dsnString))
            {
                // Graceful degradation: Sentry not configured
                return StatusCode(503, "Sentry is not configured.");
            }

            if (!Uri.TryCreate(dsnString, UriKind.Absolute, out var dsnUri))
            {
                return StatusCode(500, "Invalid Sentry DSN configuration.");
            }

            // Extract real Project ID from the backend DSN path (e.g., /123456)
            var projectId = dsnUri.AbsolutePath.Trim('/');
            if (string.IsNullOrEmpty(projectId))
            {
                return StatusCode(500, "Sentry DSN is missing Project ID.");
            }

            // Read envelope completely into memory, enforcing size limit
            using var ms = new MemoryStream();
            await Request.Body.CopyToAsync(ms, HttpContext.RequestAborted);
            if (ms.Length > MaxPayloadSize)
            {
                return StatusCode(413, "Payload Too Large");
            }

            var envelopeBytes = ms.ToArray();
            if (envelopeBytes.Length == 0)
            {
                return BadRequest("Empty payload.");
            }

            // Sentry envelope format: header\n item1_header\n item1_payload\n ...
            // We must parse the envelope header byte-by-byte to avoid corrupting binary payloads (like Session Replays)
            int newlineIndex = Array.IndexOf(envelopeBytes, (byte)'\n');
            if (newlineIndex == -1)
            {
                return BadRequest("Invalid envelope format.");
            }

            var headerJson = Encoding.UTF8.GetString(envelopeBytes, 0, newlineIndex);
            
            try
            {
                var headerObj = JsonNode.Parse(headerJson) as JsonObject;
                if (headerObj == null)
                {
                    return BadRequest("Invalid envelope header JSON.");
                }
                
                // SSRF Mitigation: Construct the upstream URL purely from the server's securely stored DSN.
                var upstreamUrl = $"{dsnUri.Scheme}://{dsnUri.Host}/api/{projectId}/envelope/";

                // Rewrite the envelope header with the backend's real DSN and public key
                headerObj["dsn"] = dsnString;
                var publicKey = dsnUri.UserInfo;
                if (!string.IsNullOrEmpty(publicKey))
                {
                    headerObj["public_key"] = publicKey;
                }

                var newHeaderBytes = Encoding.UTF8.GetBytes(headerObj.ToJsonString());
                
                // Reconstruct envelope: new header + \n + remaining bytes
                var remainingEnvelopeSpan = envelopeBytes.AsSpan(newlineIndex + 1);
                var newPayload = new byte[newHeaderBytes.Length + 1 + remainingEnvelopeSpan.Length];
                Buffer.BlockCopy(newHeaderBytes, 0, newPayload, 0, newHeaderBytes.Length);
                newPayload[newHeaderBytes.Length] = (byte)'\n';
                remainingEnvelopeSpan.CopyTo(newPayload.AsSpan(newHeaderBytes.Length + 1));

                var client = _httpClientFactory.CreateClient("SentryTunnel");
                var request = new HttpRequestMessage(HttpMethod.Post, upstreamUrl);
                
                // Forward the client IP for accurate user location parsing by Sentry
                if (HttpContext.Connection.RemoteIpAddress != null)
                {
                    request.Headers.TryAddWithoutValidation("X-Forwarded-For", HttpContext.Connection.RemoteIpAddress.ToString());
                }

                // Append Sentry authentication header
                if (!string.IsNullOrEmpty(publicKey))
                {
                    request.Headers.TryAddWithoutValidation("X-Sentry-Auth", $"Sentry sentry_version=7, sentry_client=sentry.dotnet.tunnel/1.0, sentry_key={publicKey}");
                }

                var requestContent = new ByteArrayContent(newPayload);
                requestContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-sentry-envelope");
                request.Content = requestContent;

                var response = await client.SendAsync(request);
                
                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning("Upstream Sentry returned {StatusCode}: {ErrorBody}", response.StatusCode, errorBody);
                }

                return StatusCode((int)response.StatusCode);
            }
            catch (JsonException)
            {
                return BadRequest("Invalid envelope header JSON.");
            }
        }
    }
}
