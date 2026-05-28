using B2A.DbTula.Core.Enums;
using B2A.DbTula.Core.Models;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Text.Json;

namespace B2A.DbTula.Cli.Services;

public static class EmailService
{
    public record EmailConfig(
        string Host, int Port, string User, string Password,
        string From, string[] To, bool UseSsl = true);

    /// <summary>
    /// Reads SMTP config: env vars first, then appsettings.local.json in working/exe dir.
    /// Returns null if neither source has SMTP host + recipients configured.
    /// Env vars: DBTULA_SMTP_HOST, DBTULA_SMTP_TO (required), DBTULA_SMTP_PORT,
    ///           DBTULA_SMTP_USER, DBTULA_SMTP_PASS, DBTULA_SMTP_FROM, DBTULA_SMTP_USE_SSL
    /// </summary>
    public static EmailConfig? ReadFromEnvironment()
    {
        var host = Environment.GetEnvironmentVariable("DBTULA_SMTP_HOST");
        var to   = Environment.GetEnvironmentVariable("DBTULA_SMTP_TO");

        if (!string.IsNullOrWhiteSpace(host) && !string.IsNullOrWhiteSpace(to))
        {
            int.TryParse(Environment.GetEnvironmentVariable("DBTULA_SMTP_PORT"), out var port);
            if (port == 0) port = 587;
            bool.TryParse(Environment.GetEnvironmentVariable("DBTULA_SMTP_USE_SSL") ?? "true", out var useSsl);
            var user = Environment.GetEnvironmentVariable("DBTULA_SMTP_USER") ?? "";
            var from = Environment.GetEnvironmentVariable("DBTULA_SMTP_FROM") ?? user;
            return new EmailConfig(
                Host: host, Port: port, User: user,
                Password: Environment.GetEnvironmentVariable("DBTULA_SMTP_PASS") ?? "",
                From: from,
                To:   to.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                UseSsl: useSsl);
        }

        return ReadFromLocalFile();
    }

    private static EmailConfig? ReadFromLocalFile()
    {
        var candidates = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), "appsettings.local.json"),
            Path.Combine(AppContext.BaseDirectory,        "appsettings.local.json"),
        };

        var path = candidates.FirstOrDefault(File.Exists);
        if (path is null) return null;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("smtp", out var smtp)) return null;

            string Get(string key) =>
                smtp.TryGetProperty(key, out var el) ? el.GetString() ?? "" : "";

            var smtpHost = Get("host");
            var smtpTo   = Get("to");
            if (string.IsNullOrWhiteSpace(smtpHost) || string.IsNullOrWhiteSpace(smtpTo))
                return null;

            int.TryParse(smtp.TryGetProperty("port", out var portEl) ? portEl.GetRawText() : "587", out var port);
            if (port == 0) port = 587;
            bool useSsl = !smtp.TryGetProperty("useSsl", out var sslEl) || sslEl.GetBoolean();
            var user = Get("user");
            var from = Get("from") is { Length: > 0 } f ? f : user;

            return new EmailConfig(
                Host: smtpHost, Port: port, User: user,
                Password: Get("pass"), From: from,
                To:   smtpTo.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                UseSsl: useSsl);
        }
        catch { return null; }
    }

    public static async Task SendDriftReportAsync(
        EmailConfig config,
        string subject,
        string bodyHtml,
        string reportFilePath)
    {
#pragma warning disable SYSLIB0006 // SmtpClient is functional for basic SMTP — MailKit not yet a dependency
        using var client = new SmtpClient(config.Host, config.Port)
        {
            EnableSsl   = config.UseSsl,
            Credentials = string.IsNullOrEmpty(config.User)
                ? null
                : new NetworkCredential(config.User, config.Password)
        };

        using var message = new MailMessage
        {
            From       = new MailAddress(config.From),
            Subject    = subject,
            Body       = bodyHtml,
            IsBodyHtml = true
        };

        foreach (var recipient in config.To)
            message.To.Add(recipient);

        if (File.Exists(reportFilePath))
            message.Attachments.Add(new Attachment(reportFilePath, "text/html"));

        await client.SendMailAsync(message);
#pragma warning restore SYSLIB0006
    }

    public static string BuildDriftEmailBody(
        string title, string sourceLabel, string targetLabel,
        IList<ComparisonResult> results,
        int matchCount, int mismatchCount, int missingInTargetCount, int missingInSourceCount)
    {
        var sb = new StringBuilder();
        sb.Append("<html><body style='font-family:Arial,sans-serif'>");
        sb.Append($"<h2 style='color:#c0392b'>⚠️ Schema Drift Detected</h2>");
        sb.Append($"<p><strong>{WebUtility.HtmlEncode(title)}</strong><br/>");
        sb.Append($"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC</p>");

        sb.Append("<table border='1' cellpadding='8' cellspacing='0' style='border-collapse:collapse;margin-bottom:16px'>");
        sb.Append("<tr style='background:#f5f5f5'><th align='left'>Metric</th><th align='right'>Count</th></tr>");
        sb.Append($"<tr><td>✅ Match</td><td align='right'>{matchCount}</td></tr>");
        sb.Append($"<tr style='background:#fff3cd'><td>⚠️ Mismatch</td><td align='right'>{mismatchCount}</td></tr>");
        sb.Append($"<tr style='background:#f8d7da'><td>❌ Missing in {WebUtility.HtmlEncode(targetLabel)}</td><td align='right'>{missingInTargetCount}</td></tr>");
        sb.Append($"<tr style='background:#f8d7da'><td>❌ Missing in {WebUtility.HtmlEncode(sourceLabel)}</td><td align='right'>{missingInSourceCount}</td></tr>");
        sb.Append("</table>");

        var driftItems = results.Where(r => r.Status != ComparisonStatus.Match).ToList();
        if (driftItems.Count > 0)
        {
            sb.Append("<h3>Drift Items</h3>");
            sb.Append("<table border='1' cellpadding='6' cellspacing='0' style='border-collapse:collapse'>");
            sb.Append("<tr style='background:#f5f5f5'><th>Type</th><th>Name</th><th>Status</th></tr>");
            foreach (var item in driftItems)
            {
                var bg = item.Status == ComparisonStatus.Mismatch ? "#fff3cd" : "#f8d7da";
                sb.Append($"<tr style='background:{bg}'>");
                sb.Append($"<td>{item.ObjectType}</td>");
                sb.Append($"<td>{WebUtility.HtmlEncode(item.Name)}</td>");
                sb.Append($"<td>{WebUtility.HtmlEncode(item.DisplayStatus)}</td>");
                sb.Append("</tr>");
            }
            sb.Append("</table>");
        }

        sb.Append("<br/><p style='color:#666'>See attached HTML report for full diff details.</p>");
        sb.Append("</body></html>");
        return sb.ToString();
    }

    public static async Task SendBatchDriftReportAsync(
        EmailConfig    config,
        string         subject,
        string         bodyHtml,
        IList<string>  reportPaths)
    {
#pragma warning disable SYSLIB0006
        using var client = new SmtpClient(config.Host, config.Port)
        {
            EnableSsl   = config.UseSsl,
            Credentials = string.IsNullOrEmpty(config.User)
                ? null
                : new NetworkCredential(config.User, config.Password)
        };

        using var message = new MailMessage
        {
            From       = new MailAddress(config.From),
            Subject    = subject,
            Body       = bodyHtml,
            IsBodyHtml = true
        };

        foreach (var recipient in config.To)
            message.To.Add(recipient);

        foreach (var path in reportPaths.Where(File.Exists))
            message.Attachments.Add(new Attachment(path, "text/html"));

        await client.SendMailAsync(message);
#pragma warning restore SYSLIB0006
    }

    public static string BuildBatchDriftEmailBody(string title, IList<BatchJobResult> results)
    {
        var sb = new StringBuilder();
        sb.Append("<html><body style='font-family:Arial,sans-serif'>");
        sb.Append("<h2 style='color:#c0392b'>⚠️ Schema Drift Detected</h2>");
        sb.Append($"<p><strong>{WebUtility.HtmlEncode(title)}</strong><br/>");
        sb.Append($"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC</p>");

        sb.Append("<table border='1' cellpadding='8' cellspacing='0' style='border-collapse:collapse;margin-bottom:20px;min-width:500px'>");
        sb.Append("<tr style='background:#f5f5f5'>");
        sb.Append("<th align='left'>Service</th><th align='right'>Match</th><th align='right'>Mismatch</th>");
        sb.Append("<th align='right'>Missing in Target</th><th align='right'>Missing in Source</th><th align='right'>Status</th>");
        sb.Append("</tr>");

        foreach (var r in results)
        {
            if (r.Skipped)
            {
                sb.Append("<tr style='color:#999'>");
                sb.Append($"<td>{WebUtility.HtmlEncode(r.Name)}</td><td colspan='4' align='center'>skipped</td><td>⏭</td>");
                sb.Append("</tr>");
                continue;
            }
            if (r.Failed)
            {
                sb.Append("<tr style='background:#f8d7da'>");
                sb.Append($"<td>{WebUtility.HtmlEncode(r.Name)}</td><td colspan='4' align='center'>{WebUtility.HtmlEncode(r.FailReason ?? "error")}</td><td>❌</td>");
                sb.Append("</tr>");
                continue;
            }
            var bg     = r.HasDrift ? (r.MissingInTargetCount > 0 ? "#f8d7da" : "#fff3cd") : "#f0fff4";
            var status = r.HasDrift ? (r.MissingInTargetCount > 0 ? "🚨 Missing" : "⚠️ Drift") : "✅ Clean";
            sb.Append($"<tr style='background:{bg}'>");
            sb.Append($"<td><strong>{WebUtility.HtmlEncode(r.Name)}</strong></td>");
            sb.Append($"<td align='right'>{r.MatchCount}</td>");
            sb.Append($"<td align='right'>{r.MismatchCount}</td>");
            sb.Append($"<td align='right'>{r.MissingInTargetCount}</td>");
            sb.Append($"<td align='right'>{r.MissingInSourceCount}</td>");
            sb.Append($"<td align='center'>{status}</td>");
            sb.Append("</tr>");
        }
        sb.Append("</table>");
        sb.Append("<p style='color:#666'>Individual HTML reports are attached — open each for full side-by-side diff.</p>");
        sb.Append("</body></html>");
        return sb.ToString();
    }
}
