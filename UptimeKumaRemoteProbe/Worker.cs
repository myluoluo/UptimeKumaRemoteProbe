namespace UptimeKumaRemoteProbe;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly PingService _pingService;
    private readonly HttpService _httpService;
    private readonly TcpService _tcpService;
    private readonly CertificateService _certificateService;
    private readonly DbService _dbService;
    private readonly MonitorsService _monitorsService;
    private readonly Configurations _configurations;

    public Worker(ILogger<Worker> logger, IConfiguration configuration, PingService pingService, HttpService httpService,
        TcpService tcpService, CertificateService certificateService, DbService dbService, MonitorsService monitorsService)
    {
        _logger = logger;
        _configurations = configuration.GetSection(nameof(Configurations)).Get<Configurations>();
        _pingService = pingService;
        _httpService = httpService;
        _tcpService = tcpService;
        _certificateService = certificateService;
        _dbService = dbService;
        _monitorsService = monitorsService;
    }

    protected async override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("App version: {version}", Assembly.GetExecutingAssembly().GetName().Version.ToString());

        if (_configurations.UpDependency == "")
        {
            _logger.LogWarning("Up Dependency is not set.");
            Environment.Exit(0);
        }

        Ping ping = new();
        PingReply pingReply = null;

        while (!stoppingToken.IsCancellationRequested)
        {
            if (_configurations.UpDependency != "")
            {
                try
                {
                    pingReply = ping.Send(_configurations.UpDependency, _configurations.Timeout);
                }
                catch (Exception ex)
                {
                    _logger.LogError("Network is unreachable. {ex}", ex.Message);
                }
            }

            if (pingReply?.Status == IPStatus.Success)
            {
                var monitors = await _monitorsService.GetMonitorsAsync();
                if (monitors is not null)
                {
                    var endpoints = ParseEndpoints(monitors);
                    await LoopAsync(endpoints);
                }
            }
            else
            {
                _logger.LogError("Up Dependency is unreachable.");
            }
            await Task.Delay(_configurations.Delay, stoppingToken);
        }
    }

    private List<Endpoint> ParseEndpoints(List<Monitors> monitors)
    {
        var endpoints = new List<Endpoint>();

        foreach (var monitor in monitors)
        {
            var probe = monitor.Tags.Where(w => w.Name == "Probe").Select(s => s.Value).FirstOrDefault() == _configurations.ProbeName;
            if (monitor.Active && monitor.Type == "push" && probe)
            {
                var endpoint = new Endpoint
                {
                    Type = monitor.Tags.Where(w => w.Name == "Type").Select(s => s.Value).First(),
                    Destination = monitor.Tags.Where(w => w.Name == "Address").Select(s => s.Value).First(),
                    Timeout = 1000,
                    PushUri = new Uri($"{_configurations.BasePushUri}{monitor.PushToken}?status=up&msg=OK&ping="),
                    Keyword = monitor.Tags.Where(w => w.Name == "Keyword").Select(s => s.Value).FirstOrDefault() ?? string.Empty
                };
                endpoints.Add(endpoint);
            }
        }
        return endpoints;
    }

    private async Task LoopAsync(List<Endpoint> endpoints)
    {
        foreach (var item in endpoints)
        {
            switch (item.Type)
            {
                case "Ping":
                    await _pingService.CheckPingAsync(item);
                    break;
                case "Http":
                    await _httpService.CheckHttpAsync(item);
                    break;
                case "Tcp":
                    await _tcpService.CheckTcpAsync(item);
                    break;
                case "Certificate":
                    await _certificateService.CheckCertificateAsync(item);
                    break;
                case "DataBase":
                    await _dbService.CheckDbAsync(item);
                    break;
                default:
                    break;
            }
        }
    }
}
