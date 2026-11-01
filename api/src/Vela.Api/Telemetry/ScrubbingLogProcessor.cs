using OpenTelemetry;
using OpenTelemetry.Logs;

namespace Vela.Api.Telemetry;

/// <summary>
/// Scrubs log records before export. The exception object is never exported: exception messages can echo user input or
/// database row values, so only its type name and scrubbed message are kept, in the body.
/// </summary>
public sealed class ScrubbingLogProcessor : BaseProcessor<LogRecord>
{
    public override void OnEnd(LogRecord record)
    {
        var exception = record.Exception is { } e
            ? $" [{e.GetType().Name}: {PiiScrubber.Scrub(e.Message)}]"
            : "";
        record.Exception = null;
        record.FormattedMessage = PiiScrubber.Scrub(record.FormattedMessage) is { } m ? m + exception : exception.Trim();
        record.Body = PiiScrubber.Scrub(record.Body) is { } b ? b + exception : record.Body;
    }
}
