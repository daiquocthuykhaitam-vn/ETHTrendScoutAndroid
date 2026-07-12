using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace TrendGovernor.Wpf;

public sealed class AnalysisBundleExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task<string> ExportAsync(
        string outputDirectory,
        string sessionId,
        TradingConfig config,
        IReadOnlyCollection<CandidateRecord> candidates,
        IReadOnlyCollection<FrozenTradePlan> plans,
        IReadOnlyCollection<PositionRow> positions,
        IReadOnlyCollection<OrderRow> orders,
        string eventJournalPath,
        CancellationToken ct)
    {
        Directory.CreateDirectory(outputDirectory);
        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var work = Path.Combine(outputDirectory, $"ANALYSIS_BUNDLE_{SafeName(sessionId)}_{stamp}");
        Directory.CreateDirectory(work);

        var summary = new
        {
            session_id = sessionId,
            generated_utc = DateTimeOffset.UtcNow,
            candidate_count = candidates.Count,
            plan_count = plans.Count,
            position_count = positions.Count,
            order_count = orders.Count,
            bot_position_count = positions.Count(x => x.Owner == "BOT"),
            manual_or_existing_position_count = positions.Count(x => x.Owner != "BOT"),
            unprotected_bot_position_count = positions.Count(x => x.Owner == "BOT" && !x.StopLossConfirmed),
            config
        };

        await WriteJsonAsync(Path.Combine(work, "session_summary.json"), summary, ct);
        await WriteJsonAsync(Path.Combine(work, "config_and_version.json"), new
        {
            session_id = sessionId,
            schema_version = 1,
            build_pack = "PACK_14_CONTROL_FOUNDATION",
            generated_utc = DateTimeOffset.UtcNow,
            config
        }, ct);

        await WriteCandidatesAsync(Path.Combine(work, "radar_candidates.csv"), candidates, ct);
        await WritePlansAsync(Path.Combine(work, "plans.csv"), plans, ct);
        await WritePositionsAsync(Path.Combine(work, "position_timeline.csv"), positions, ct);
        await WriteOrdersAsync(Path.Combine(work, "exchange_orders.csv"), orders, ct);

        if (File.Exists(eventJournalPath))
            File.Copy(eventJournalPath, Path.Combine(work, "events.jsonl"), overwrite: true);

        var zipPath = work + ".zip";
        if (File.Exists(zipPath)) File.Delete(zipPath);
        ZipFile.CreateFromDirectory(work, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);
        return zipPath;
    }

    private static async Task WriteJsonAsync(string path, object value, CancellationToken ct)
        => await File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, JsonOptions), Encoding.UTF8, ct);

    private static async Task WriteCandidatesAsync(string path, IEnumerable<CandidateRecord> rows, CancellationToken ct)
    {
        var sb = new StringBuilder("candidate_id,symbol,first_seen,last_seen,stable_cycles,current_stage,stage_reason,expires_at,decision_version,source_snapshot_id\n");
        foreach (var x in rows)
            sb.AppendLine(Csv(x.CandidateId, x.Symbol, x.FirstSeen, x.LastSeen, x.StableCycles, x.Stage, x.StageReason, x.ExpiresAt, x.DecisionVersion, x.SourceSnapshotId));
        await File.WriteAllTextAsync(path, sb.ToString(), Encoding.UTF8, ct);
    }

    private static async Task WritePlansAsync(string path, IEnumerable<FrozenTradePlan> rows, CancellationToken ct)
    {
        var sb = new StringBuilder("plan_id,symbol,direction,entry_low,entry_high,stop_loss,take_profit,rr,score,stable_cycles,created_utc,expires_utc\n");
        foreach (var x in rows)
            sb.AppendLine(Csv(x.PlanId, x.Symbol, x.Direction, x.EntryLow, x.EntryHigh, x.StopLoss, x.TakeProfit, x.RiskReward, x.Score, x.StableCycles, x.CreatedUtc, x.ExpiresUtc));
        await File.WriteAllTextAsync(path, sb.ToString(), Encoding.UTF8, ct);
    }

    private static async Task WritePositionsAsync(string path, IEnumerable<PositionRow> rows, CancellationToken ct)
    {
        var sb = new StringBuilder("symbol,owner,side,quantity,entry_price,mark_price,unrealized_pnl,realized_funding,net_pnl,peak_net_pnl,giveback_percent,sl_confirmed,tp_confirmed,protection,health,recommendation\n");
        foreach (var x in rows)
            sb.AppendLine(Csv(x.Symbol, x.Owner, x.Side, x.Quantity, x.EntryPrice, x.MarkPrice, x.UnrealizedPnl, x.RealizedFunding, x.NetPnlAfterFunding, x.PeakNetPnl, x.GivebackPercent, x.StopLossConfirmed, x.TakeProfitConfirmed, x.Protection, x.Health, x.Recommendation));
        await File.WriteAllTextAsync(path, sb.ToString(), Encoding.UTF8, ct);
    }

    private static async Task WriteOrdersAsync(string path, IEnumerable<OrderRow> rows, CancellationToken ct)
    {
        var sb = new StringBuilder("order_id,client_order_id,symbol,source,type,side,price,stop_price,quantity,status,reduce_only\n");
        foreach (var x in rows)
            sb.AppendLine(Csv(x.OrderId, x.ClientOrderId, x.Symbol, x.Source, x.Type, x.Side, x.Price, x.StopPrice, x.Quantity, x.Status, x.ReduceOnly));
        await File.WriteAllTextAsync(path, sb.ToString(), Encoding.UTF8, ct);
    }

    private static string Csv(params object?[] values)
        => string.Join(',', values.Select(value => Quote(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty)));

    private static string Quote(string value)
        => '"' + value.Replace("\"", "\"\"") + '"';

    private static string SafeName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
    }
}
