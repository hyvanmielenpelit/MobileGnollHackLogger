using System.Threading.Channels;

namespace Overseer.Services.ApiKeyAlerts;

/// <summary>
/// The queue between a failing provider call and <see cref="ApiKeyAlertDispatcher"/>. Reporting
/// never waits and never throws, so the user's error reaches the chat without delay.
/// </summary>
public sealed class ApiKeyAlertService
{
    public const int Capacity = 256;

    private readonly Channel<ApiKeyFailureReport> _channel;
    private readonly ILogger<ApiKeyAlertService> _logger;

    public ApiKeyAlertService(ApiKeyAlertOptions options, ILogger<ApiKeyAlertService> logger)
    {
        Options = options;
        _logger = logger;
        // Wait mode with TryWrite only: a full queue refuses the report at once, which unlike
        // DropWrite lets the refusal be seen and logged.
        _channel = Channel.CreateBounded<ApiKeyFailureReport>(new BoundedChannelOptions(Capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });
    }

    public ApiKeyAlertOptions Options { get; }

    public ChannelReader<ApiKeyFailureReport> Reader => _channel.Reader;

    /// <summary>Queues a report. False when alerts are disabled or the queue is full.</summary>
    public bool TryReport(ApiKeyFailureReport report)
    {
        try
        {
            if (!Options.Enabled) return false;

            if (_channel.Writer.TryWrite(report)) return true;

            _logger.LogWarning(
                "API key failure alert queue is full ({Capacity}); dropped the {Kind} report for {Provider} System AI Config {ConfigId}.",
                Capacity, report.Kind, report.Provider, report.SystemAiApiConfigurationId);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to queue an API key failure alert.");
            return false;
        }
    }
}
