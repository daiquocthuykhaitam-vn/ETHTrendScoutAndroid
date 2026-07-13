namespace TrendGovernor.Wpf;

public sealed record ExecutionAuditRecord(
    DateTimeOffset Time,
    string Symbol,
    string CorrelationId,
    string Stage,
    string Status,
    string PayloadJson);
