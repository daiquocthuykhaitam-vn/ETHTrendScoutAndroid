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

    public Task<string> ExportPack14Async(
        string outputDirectory,
        Pack14Runtime runtime,
        TradingConfig config,
        IReadOnlyCollection<FrozenTradePlan> plans,
        IReadOnlyCollection<PositionRow> positions,
        IReadOnlyCollection<OrderRow> orders,
        CancellationToken ct)
        => ExportCoreAsync(
            outputDirectory,
            runtime.SessionId,
            config,
            runtime.Candidates,
            plans,
            positions,
            orders,
            runtime.Grants,
            runtime.Intents,
            runtime.Audit,
            runtime.MissedOpportunities,
            Path.Combine(runtime.Journal.SessionDirectory, "events.jsonl"),
            ct);

    public Task<string> ExportAsync(
        string outputDirectory,
        string sessionId,
        TradingConfig config,
        IReadOnlyCollection<CandidateRecord> candidates,
        IReadOnlyCollection<FrozenTradePlan> plans,
        IReadOnlyCollection<PositionRow> positions,
        IReadOnlyCollection<OrderRow> orders,
        string eventJournalPath,
        CancellationToken ct)
        => ExportCoreAsync(
            outputDirectory,
            sessionId,
            config,
            candidates,
            plans,
            positions,
            orders,
            Array.Empty<VerifyGrant>(),
            Array.Empty<OrderIntent>(),
            Array.Empty<ExecutionAuditRecord>(),
            Array.Empty<MissedOpportunityRecord>(),
            eventJournalPath,
            ct);

    private static async Task<string> ExportCoreAsync(
        string outputDirectory,
        string sessionId,
        TradingConfig config,
        IReadOnlyCollection<CandidateRecord> candidates,
        IReadOnlyCollection<FrozenTradePlan> plans,
        IReadOnlyCollection<PositionRow> positions,
        IReadOnlyCollection<OrderRow> orders,
        IReadOnlyCollection<VerifyGrant> grants,
        IReadOnlyCollection<OrderIntent> intents,
        IReadOnlyCollection<ExecutionAuditRecord> audit,
        IReadOnlyCollection<MissedOpportunityRecord> missed,
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
            verify_grant_count = grants.Count,
            order_intent_count = intents.Count,
            position_count = positions.Count,
            order_count = orders.Count,
            execution_audit_count = audit.Count,
            missed_opportunity_count = missed.Count,
            bot_position_count = positions.Count(x => x.Owner == "BOT"),
            manual_or_existing_position_count = positions.Count(x => x.Owner != "BOT"),
            unprotected_bot_position_count = positions.Count(x => x.Owner == "BOT" && !x.StopLossConfirmed),
            verify_pass_count = grants.Count(x => x.Passed),
            verify_block_count = grants.Count(x => !x.Passed),
            config
        };

        await WriteJsonAsync(Path.Combine(work, "session_summary.json"), summary, ct);
        await WriteJsonAsync(Path.Combine(work, "config_and_version.json"), new
        {
            session_id = sessionId,
            schema_version = 2,
            build_pack = "PACK_14_RADAR_CONTROL_REPLAY",
            generated_utc = DateTimeOffset.UtcNow,
            config
        }, ct);

        await WriteCandidatesAsync(Path.Combine(work, "radar_candidates.csv"), candidates, ct);
        await WritePlansAsync(Path.Combine(work, "plans.csv"), plans, ct);
        await WriteVerifyAsync(Path.Combine(work, "verify_matrix.csv"), grants, ct);
        await WriteIntentsAsync(Path.Combine(work, "order_intents.csv"), intents, ct);
        await WriteOrdersAsync(Path.Combine(work, "exchange_orders.csv"), orders, ct);
        await WriteAuditAsync(Path.Combine(work, "fills.csv"), audit.Where(x => x.Stage == "ORDER"), ct);
        await WriteAuditAsync(Path.Combine(work, "protection_actions.csv"), audit.Where(x => x.Stage == "PROTECTION"), ct);
        await WritePositionsAsync(Path.Combine(work, "position_timeline.csv"), positions, ct);
        await WriteMissedAsync(Path.Combine(work, "missed_opportunities.csv"), missed, ct);

        if (File.Exists(eventJournalPath))
            File.Copy(eventJournalPath, Path.Combine(work, "events.jsonl"), true);
        else
            await File.WriteAllTextAsync(Path.Combine(work, "events.jsonl"), string.Empty, Encoding.UTF8, ct);

        var zipPath = work + ".zip";
        if (File.Exists(zipPath)) File.Delete(zipPath);
        ZipFile.CreateFromDirectory(work, zipPath, CompressionLevel.Optimal, false);
        return zipPath;
    }

    private static Task WriteJsonAsync(string path, object value, CancellationToken ct)
        => File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, JsonOptions), Encoding.UTF8, ct);

    private static Task WriteCandidatesAsync(string path, IEnumerable<CandidateRecord> rows, CancellationToken ct)
    {
        var sb = new StringBuilder("candidate_id,symbol,first_seen,last_seen,stable_cycles,current_stage,stage_reason,expires_at,decision_version,source_snapshot_id\n");
        foreach (var x in rows)
            sb.AppendLine(Csv(x.CandidateId, x.Symbol, x.FirstSeen, x.LastSeen, x.StableCycles, x.Stage, x.StageReason, x.ExpiresAt, x.DecisionVersion, x.SourceSnapshotId));
        return File.WriteAllTextAsync(path, sb.ToString(), Encoding.UTF8, ct);
    }

    private static Task WritePlansAsync(string path, IEnumerable<FrozenTradePlan> rows, CancellationToken ct)
    {
        var sb = new StringBuilder("plan_id,symbol,direction,entry_low,entry_high,stop_loss,take_profit,rr,score,stable_cycles,created_utc,expires_utc\n");
        foreach (var x in rows)
            sb.AppendLine(Csv(x.PlanId, x.Symbol, x.Direction, x.EntryLow, x.EntryHigh, x.StopLoss, x.TakeProfit, x.RiskReward, x.Score, x.StableCycles, x.CreatedUtc, x.ExpiresUtc));
        return File.WriteAllTextAsync(path, sb.ToString(), Encoding.UTF8, ct);
    }

    private static Task WriteVerifyAsync(string path, IEnumerable<VerifyGrant> grants, CancellationToken ct)
    {
        var sb = new StringBuilder("grant_id,plan_id,candidate_id,symbol,issued_at,expires_at,used_at,passed,gate_code,gate_passed,reason\n");
        foreach (var grant in grants)
            foreach (var gate in grant.Gates)
                sb.AppendLine(Csv(grant.GrantId, grant.PlanId, grant.CandidateId, grant.Symbol, grant.IssuedAt, grant.ExpiresAt, grant.UsedAt, grant.Passed, gate.GateCode, gate.Passed, gate.Reason));
        return File.WriteAllTextAsync(path, sb.ToString(), Encoding.UTF8, ct);
    }

    private static Task WriteIntentsAsync(string path, IEnumerable<OrderIntent> rows, CancellationToken ct)
    {
        var sb = new StringBuilder("order_intent_id,grant_id,plan_id,candidate_id,symbol,side,type,quantity,price,reduce_only,created_at\n");
        foreach (var x in rows)
            sb.AppendLine(Csv(x.OrderIntentId, x.GrantId, x.PlanId, x.CandidateId, x.Symbol, x.Side, x.Type, x.Quantity, x.Price, x.ReduceOnly, x.CreatedAt));
        return File.WriteAllTextAsync(path, sb.ToString(), Encoding.UTF8, ct);
    }

    private static Task WritePositionsAsync(string path, IEnumerable<PositionRow> rows, CancellationToken ct)
    {
        var sb = new StringBuilder("symbol,owner,side,quantity,entry_price,mark_price,unrealized_pnl,realized_funding,net_pnl,peak_net_pnl,giveback_percent,sl_confirmed,tp_confirmed,protection,health,recommendation\n");
        foreach (var x in rows)
            sb.AppendLine(Csv(x.Symbol, x.Owner, x.Side, x.Quantity, x.EntryPrice, x.MarkPrice, x.UnrealizedPnl, x.RealizedFunding, x.NetPnlAfterFunding, x.PeakNetPnl, x.GivebackPercent, x.StopLossConfirmed, x.TakeProfitConfirmed, x.Protection, x.Health, x.Recommendation));
        return File.WriteAllTextAsync(path, sb.ToString(), Encoding.UTF8, ct);
    }

    private static Task WriteOrdersAsync(string path, IEnumerable<OrderRow> rows, CancellationToken ct)
    {
        var sb = new StringBuilder("order_id,client_order_id,symbol,source,type,side,price,stop_price,quantity,status,reduce_only\n");
        foreach (var x in rows)
            sb.AppendLine(Csv(x.OrderId, x.ClientOrderId, x.Symbol, x.Source, x.Type, x.Side, x.Price, x.StopPrice, x.Quantity, x.Status, x.ReduceOnly));
        return File.WriteAllTextAsync(path, sb.ToString(), Encoding.UTF8, ct);
    }

    private static Task WriteAuditAsync(string path, IEnumerable<ExecutionAuditRecord> rows, CancellationToken ct)
    {
        var sb = new StringBuilder("time,symbol,correlation_id,stage,status,payload_json\n");
        foreach (var x in rows)
            sb.AppendLine(Csv(x.Time, x.Symbol, x.CorrelationId, x.Stage, x.Status, x.PayloadJson));
        return File.WriteAllTextAsync(path, sb.ToString(), Encoding.UTF8, ct);
    }

    private static Task WriteMissedAsync(string path, IEnumerable<MissedOpportunityRecord> rows, CancellationToken ct)
    {
        var sb = new StringBuilder("time,symbol,candidate_id,stage,reason_code,detail\n");
        foreach (var x in rows)
            sb.AppendLine(Csv(x.Time, x.Symbol, x.CandidateId, x.Stage, x.ReasonCode, x.Detail));
        return File.WriteAllTextAsync(path, sb.ToString(), Encoding.UTF8, ct);
    }

    private static string Csv(params object?[] values)
        => string.Join(',', values.Select(value => Quote(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty)));

    private static string Quote(string value) => '"' + value.Replace("\"", "\"\"") + '"';

    private static string SafeName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
    }
}
