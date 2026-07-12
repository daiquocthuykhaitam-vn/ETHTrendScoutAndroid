using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TrendGovernor.Wpf;

public sealed class BinanceExecutionGatewayV14 : IAsyncDisposable
{
    private static readonly string[] TerminalStatuses = ["FILLED", "CANCELED", "EXPIRED", "REJECTED"];
    private readonly HttpClient _http =