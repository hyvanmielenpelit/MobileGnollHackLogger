using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using MobileGnollHackLogger.Data;

namespace Overseer.Services.ApiKeyAlerts;

/// <summary>A configuration whose key column holds the same key as the failing one.</summary>
public sealed record ApiKeyAlertSharingConfig(
    long Id, string DisplayName, string ModelId, bool IsEnabled, bool UseDefaultApiKey, bool KeyUnreadable);

/// <summary>Everything one alert email shows. Holds no key.</summary>
public sealed record ApiKeyAlertEmailModel
{
    public required ApiKeyFailureReport Report { get; init; }

    /// <summary>Null when the configuration has been deleted.</summary>
    public SystemAiApiConfiguration? Config { get; init; }

    /// <summary>The provider's default key row, when the configuration uses it.</summary>
    public SystemDefaultApiKey? DefaultKey { get; init; }

    /// <summary>
    /// Configurations of the same provider holding the same key, the failing one included.
    /// Null when they could not be checked.
    /// </summary>
    public IReadOnlyList<ApiKeyAlertSharingConfig>? SharingConfigs { get; init; }

    public bool? SessionIsConfidential { get; init; }
    public bool? SessionIsGnollHack { get; init; }

    /// <summary>Occurrences since the last email, the one that triggered this email included.</summary>
    public int OccurrencesSinceLastEmail { get; init; } = 1;
    public DateTime FirstOccurredUtc { get; init; }
    public DateTime? PreviousEmailSentUtc { get; init; }

    public string EnvironmentName { get; init; } = string.Empty;
    public string MachineName { get; init; } = string.Empty;
    public string OverseerVersion { get; init; } = string.Empty;
    public TimeSpan ThrottleWindow { get; init; }
    public DateTime GeneratedUtc { get; init; }
}

/// <summary>
/// Builds the subject and HTML body of an API key failure alert. Every interpolated value is
/// HTML-encoded, because a provider body can contain markup. Never prints message content.
/// </summary>
public static class ApiKeyAlertEmailBuilder
{
    private const string Cell = "padding:4px 10px;border:1px solid #ccc;vertical-align:top;text-align:left;";
    private const string HeadCell = Cell + "background:#f2f2f2;font-weight:bold;white-space:nowrap;";

    public static (string Subject, string Html) Build(ApiKeyAlertEmailModel model)
    {
        var r = model.Report;
        var config = model.Config;
        bool balance = r.Kind == ApiKeyFailureKind.InsufficientBalance;
        string what = balance ? "out of balance" : "rejected (invalid or expired)";
        string keySource = KeySourceShort(config);
        string displayName = config?.DisplayName ?? "(deleted configuration)";

        string subject = $"[Overseer {model.EnvironmentName} @ {model.MachineName}] {r.Provider} API key {what} — "
            + $"\"{displayName}\" (System AI Config #{r.SystemAiApiConfigurationId}, {keySource} key)";

        var sb = new StringBuilder();
        sb.Append("<div style=\"font-family:Segoe UI,Arial,sans-serif;font-size:14px;color:#222;\">");
        sb.Append("<h2 style=\"margin:0 0 8px 0;\">").Append(E(balance
            ? $"{r.Provider} API key is out of balance"
            : $"{r.Provider} API key was rejected (invalid, expired, revoked or not permitted)")).Append("</h2>");

        // Summary
        Table(sb, "Summary", new (string, string?)[]
        {
            ("Failure", balance
                ? "The provider reports that the account behind this key has no credit or quota left."
                : "The provider rejected the key: it is invalid, expired, revoked, or not permitted for this request."),
            ("Provider", r.Provider),
            ("Key source", KeySourceLong(config, r.Provider)),
            ("Key hint", r.KeyHint != null ? "••••" + r.KeyHint : "(key too short to show a hint)"),
            ("Used for", UsageLabel(r.Context))
        });

        // What to do
        sb.Append(Heading("What to do"));
        sb.Append("<ul style=\"margin:4px 0 12px 20px;padding:0;\">");
        if (balance)
        {
            sb.Append("<li>").Append(E($"Top up the {r.Provider} account behind this key ({BillingPageName(r.Provider)}).")).Append("</li>");
        }
        else if (config?.UseDefaultApiKey == true)
        {
            sb.Append("<li>").Append(E($"Create a new key at {r.Provider}, then save it under Admin → Default API keys. Every configuration below that uses the default key picks it up.")).Append("</li>");
        }
        else
        {
            sb.Append("<li>").Append(E($"Create a new key at {r.Provider}, then save it in this System AI Config.")).Append("</li>");
        }
        sb.Append("<li>").Append(E("The configurations sharing this key are listed below; all of them are affected.")).Append("</li>");
        sb.Append("</ul>");

        // Configs sharing the key
        sb.Append(Heading("Configurations sharing this key"));
        if (model.SharingConfigs == null)
        {
            sb.Append("<p>").Append(E("Could not be checked.")).Append("</p>");
        }
        else if (model.SharingConfigs.Count == 0)
        {
            sb.Append("<p>").Append(E("None found (the configuration may have been deleted or its key changed since).")).Append("</p>");
        }
        else
        {
            sb.Append("<table style=\"border-collapse:collapse;margin-bottom:12px;\"><tr>");
            foreach (var h in new[] { "Id", "Display name", "Model id", "Enabled", "Key" })
                sb.Append("<th style=\"").Append(HeadCell).Append("\">").Append(E(h)).Append("</th>");
            sb.Append("</tr>");
            foreach (var s in model.SharingConfigs)
            {
                sb.Append("<tr>");
                AppendCell(sb, "#" + s.Id.ToString(CultureInfo.InvariantCulture));
                AppendCell(sb, s.DisplayName);
                AppendCell(sb, s.ModelId);
                AppendCell(sb, YesNo(s.IsEnabled));
                AppendCell(sb, s.KeyUnreadable ? "key unreadable" : s.UseDefaultApiKey ? "default key" : "custom key");
                sb.Append("</tr>");
            }
            sb.Append("</table>");
        }

        // Error
        var errorRows = new List<(string, string?)>
        {
            ("Occurred (UTC)", r.OccurredUtc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)),
            ("Occurred (Helsinki)", HelsinkiTime(r.OccurredUtc)),
            ("HTTP status", r.HttpStatus.HasValue
                ? (r.HttpStatus.Value.ToString(CultureInfo.InvariantCulture) + (string.IsNullOrWhiteSpace(r.HttpReason) ? "" : " " + r.HttpReason))
                : "(stream error event)"),
            ("Provider message", r.ProviderErrorMessage),
            ("Request", r.RequestTarget),
            ("Model id (request)", r.ModelId),
            ("Service tier (request)", r.ServiceTier),
            ("Elapsed", r.ElapsedMs.HasValue ? r.ElapsedMs.Value.ToString("0", CultureInfo.InvariantCulture) + " ms" : null),
            ("Attempt", r.Attempt > 0 ? r.Attempt.ToString(CultureInfo.InvariantCulture) : null)
        };
        foreach (var h in r.ResponseHeaders)
            errorRows.Add(("Header " + h.Key, h.Value));
        Table(sb, "Error", errorRows);

        if (!string.IsNullOrEmpty(r.ResponseBodyExcerpt))
        {
            sb.Append("<p style=\"margin:4px 0;\"><b>").Append(E("Response body (key redacted)")).Append("</b></p>");
            sb.Append("<pre style=\"white-space:pre-wrap;word-break:break-all;background:#f7f7f7;border:1px solid #ccc;padding:8px;font-size:12px;\">")
              .Append(E(r.ResponseBodyExcerpt)).Append("</pre>");
        }

        // Occurrences
        int further = Math.Max(0, model.OccurrencesSinceLastEmail - 1);
        string furtherText = further == 1 ? "1 further occurrence" : $"{further} further occurrences";
        Table(sb, "Occurrences", new (string, string?)[]
        {
            ("Since the previous email", model.PreviousEmailSentUtc.HasValue
                ? $"{furtherText} since the previous email ({Utc(model.PreviousEmailSentUtc.Value)})"
                : $"{furtherText}; this is the first email for this key"),
            ("First occurrence of this series", Utc(model.FirstOccurredUtc))
        });

        // System AI Config
        if (config == null)
        {
            sb.Append(Heading("System AI Config"));
            sb.Append("<p>").Append(E($"System AI Config #{r.SystemAiApiConfigurationId} has been deleted since the failure.")).Append("</p>");
        }
        else
        {
            Table(sb, "System AI Config", ConfigRows(config));
        }

        // Default key
        if (config?.UseDefaultApiKey == true)
        {
            var dk = model.DefaultKey;
            if (dk == null)
            {
                sb.Append(Heading("Default key"));
                sb.Append("<p>").Append(E($"No default {r.Provider} key is stored any more.")).Append("</p>");
            }
            else
            {
                Table(sb, "Default key", new (string, string?)[]
                {
                    ("Key hint", dk.KeyHint != null ? "••••" + dk.KeyHint : null),
                    ("Last updated (UTC)", Utc(dk.UpdatedAtUtc)),
                    ("Verification", dk.ApiKeyVerification?.ToString() ?? "never checked"),
                    ("Checked (UTC)", dk.ApiKeyVerificationCheckedAtUtc.HasValue ? Utc(dk.ApiKeyVerificationCheckedAtUtc.Value) : null),
                    ("Verification message", dk.ApiKeyVerificationMessage)
                });
            }
        }

        // Chat
        var session = r.Context.Session;
        Table(sb, "Chat", new (string, string?)[]
        {
            ("Session", session.IsPersistent ? "#" + session.PersistentId.ToString(CultureInfo.InvariantCulture) : "ephemeral (not stored)"),
            ("Confidential", session.IsPersistent ? (model.SessionIsConfidential.HasValue ? YesNo(model.SessionIsConfidential.Value) : "unknown") : "n/a"),
            ("GnollHack session", session.IsPersistent ? (model.SessionIsGnollHack.HasValue ? YesNo(model.SessionIsGnollHack.Value) : "unknown") : "n/a"),
            ("User id", r.Context.UserId),
            ("User name", r.Context.UserName)
        });
        sb.Append("<p style=\"color:#555;\">").Append(E("No message content, session title or reply text is included in this email.")).Append("</p>");

        // Diagnostics
        Table(sb, "Diagnostics", new (string, string?)[]
        {
            ("Environment", model.EnvironmentName),
            ("Machine", model.MachineName),
            ("Overseer version", model.OverseerVersion),
            ("Throttle window", "one email per key per " + FormatWindow(model.ThrottleWindow)),
            ("Email generated (UTC)", Utc(model.GeneratedUtc))
        });

        sb.Append("</div>");
        return (subject, sb.ToString());
    }

    public static string UsageLabel(ApiKeyAlertContext context) => context.Usage switch
    {
        ApiKeyUsage.TitleGeneration => "Title generation",
        ApiKeyUsage.SubAgent => "Chat — sub-agent " + (context.AgentName ?? "(unnamed)"),
        _ => "Chat"
    };

    private static IEnumerable<(string, string?)> ConfigRows(SystemAiApiConfiguration c)
    {
        string inv(decimal? d) => d.HasValue ? d.Value.ToString("0.####", CultureInfo.InvariantCulture) : "—";
        string n(long? v) => v.HasValue ? v.Value.ToString(CultureInfo.InvariantCulture) : "no limit";

        return new (string, string?)[]
        {
            ("Id", "#" + c.Id.ToString(CultureInfo.InvariantCulture)),
            ("Display name", c.DisplayName),
            ("Display-name mode", c.DisplayNameMode ?? "(legacy)"),
            ("Provider", c.Provider),
            ("Model id", c.ModelId),
            ("Enabled", YesNo(c.IsEnabled)),
            ("System-wide", YesNo(c.IsSystemWide)),
            ("Roles", Roles(c.ModelRole)),
            ("Thinking level", c.ThinkingLevel),
            ("Reasoning mode", c.ReasoningMode),
            ("Reasoning summary", c.ReasoningSummary),
            ("Service tier", c.ServiceTier),
            ("Max input tokens", c.MaxInputTokens?.ToString(CultureInfo.InvariantCulture)),
            ("Max output tokens", c.MaxOutputTokens?.ToString(CultureInfo.InvariantCulture)),
            ("Pricing mode", c.PricingMode ?? "default"),
            ("Prices per million (input / output / cached input)", $"{inv(c.InputPricePerMillion)} / {inv(c.OutputPricePerMillion)} / {inv(c.CachedInputPricePerMillion)}"),
            ("Parallel execution", c.ParallelExecutionMode.ToString()),
            ("Key source", c.UseDefaultApiKey ? "default key of the provider" : "custom key of this config"),
            ("Endpoint", string.IsNullOrWhiteSpace(c.BaseUrl) ? "Official" : (ApiKeyFailureReport.StripUrl(c.BaseUrl) ?? "(unparseable custom URL)")),
            ("API version", c.ApiVersion),
            ("Custom header names", HeaderNames(c.CustomHeadersJson)),
            ("Confidentiality posture", c.ConfidentialityPosture ?? "Unknown"),
            ("Posture verified (UTC)", c.PostureVerifiedUtc.HasValue ? Utc(c.PostureVerifiedUtc.Value) : null),
            ("Data region", c.DataRegion),
            ("Created (UTC)", Utc(c.CreatedAtUtc)),
            ("Note", c.Note),
            ("Chat requests (day / month / total)", $"{c.DailyChatRequestsCount} / {c.MonthlyChatRequestsCount} / {c.TotalChatRequestsCount}"),
            ("Chat request limits", $"{n(c.MaxDailyChatRequests)} / {n(c.MaxMonthlyChatRequests)} / {n(c.MaxTotalChatRequests)}"),
            ("Chat tokens (day / month / total)", $"{c.DailyChatTokensCount} / {c.MonthlyChatTokensCount} / {c.TotalChatTokensCount}"),
            ("Chat token limits", $"{n(c.MaxDailyChatTokens)} / {n(c.MaxMonthlyChatTokens)} / {n(c.MaxTotalChatTokens)}"),
            ("Title requests (day / month / total)", $"{c.DailyTitleRequestsCount} / {c.MonthlyTitleRequestsCount} / {c.TotalTitleRequestsCount}"),
            ("Title request limits", $"{n(c.MaxDailyTitleRequests)} / {n(c.MaxMonthlyTitleRequests)} / {n(c.MaxTotalTitleRequests)}"),
            ("Title tokens (day / month / total)", $"{c.DailyTitleTokensCount} / {c.MonthlyTitleTokensCount} / {c.TotalTitleTokensCount}"),
            ("Title token limits", $"{n(c.MaxDailyTitleTokens)} / {n(c.MaxMonthlyTitleTokens)} / {n(c.MaxTotalTitleTokens)}")
        };
    }

    private static string Roles(int modelRole)
    {
        var roles = new List<string>();
        if ((modelRole & 1) != 0) roles.Add("Chat");
        if ((modelRole & 2) != 0) roles.Add("Title generation");
        if ((modelRole & 4) != 0) roles.Add("Benchmark");
        return roles.Count == 0 ? "none" : string.Join(", ", roles);
    }

    /// <summary>The header names of a flat JSON object; values are never read.</summary>
    private static string? HeaderNames(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return "(unreadable)";
            var names = doc.RootElement.EnumerateObject().Select(p => p.Name).ToList();
            return names.Count == 0 ? null : string.Join(", ", names);
        }
        catch (JsonException)
        {
            return "(unreadable)";
        }
    }

    private static string KeySourceShort(SystemAiApiConfiguration? config) =>
        config == null ? "unknown" : config.UseDefaultApiKey ? "default" : "custom";

    private static string KeySourceLong(SystemAiApiConfiguration? config, string provider) =>
        config == null ? "Unknown (the configuration has been deleted)"
        : config.UseDefaultApiKey ? $"Default key of {provider}"
        : "Custom key of this config";

    private static string BillingPageName(string provider) => provider switch
    {
        "Anthropic" => "Anthropic Console → Settings → Billing",
        "OpenAI" => "OpenAI Platform → Settings → Billing",
        "Google" => "Google AI Studio → Billing, or the linked Google Cloud billing account",
        _ => "the provider's billing page"
    };

    private static string HelsinkiTime(DateTime utc)
    {
        foreach (var id in new[] { "Europe/Helsinki", "FLE Standard Time" })
        {
            try
            {
                var zone = TimeZoneInfo.FindSystemTimeZoneById(id);
                var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone);
                var offset = zone.GetUtcOffset(local);
                string sign = offset < TimeSpan.Zero ? "-" : "+";
                return local.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                    + $" (UTC{sign}{offset:hh\\:mm})";
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
            }
        }
        return "(time zone unavailable)";
    }

    private static string FormatWindow(TimeSpan window) =>
        window.TotalHours.ToString("0.##", CultureInfo.InvariantCulture) + " h";

    private static string Utc(DateTime value) =>
        value.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static string YesNo(bool value) => value ? "yes" : "no";

    private static string Heading(string text) =>
        "<h3 style=\"margin:16px 0 4px 0;\">" + E(text) + "</h3>";

    private static void Table(StringBuilder sb, string heading, IEnumerable<(string Label, string? Value)> rows)
    {
        sb.Append(Heading(heading));
        sb.Append("<table style=\"border-collapse:collapse;margin-bottom:12px;\">");
        foreach (var (label, value) in rows)
        {
            sb.Append("<tr><th style=\"").Append(HeadCell).Append("\">").Append(E(label)).Append("</th>");
            AppendCell(sb, string.IsNullOrWhiteSpace(value) ? "—" : value);
            sb.Append("</tr>");
        }
        sb.Append("</table>");
    }

    private static void AppendCell(StringBuilder sb, string value) =>
        sb.Append("<td style=\"").Append(Cell).Append("\">").Append(E(value)).Append("</td>");

    private static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
