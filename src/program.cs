using CodeHollow.FeedReader;
using Microsoft.Extensions.Configuration;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;

// Hub2Cord Builder
var builder = WebApplication.CreateBuilder(args);

// appsettings.json
builder.Services.AddHttpClient("DiscordBot", client =>
{
    var token = builder.Configuration["Discord:BotToken"];
    var asm = Assembly.GetExecutingAssembly();
    var ver = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
           ?? asm.GetName().Version?.ToString();

    if (!string.IsNullOrWhiteSpace(token))
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bot", token);
    client.DefaultRequestHeaders.UserAgent.ParseAdd($"Hub2Cord/{ver}");
    client.Timeout = TimeSpan.FromSeconds(15);
});

// Service Builder
builder.Services.AddHostedService<RssWorker>();

var app = builder.Build();

// Endpoint For Health Check
app.MapGet("/", () => "Hub2Cord running");

// Hub2Cord Start
var asm = Assembly.GetExecutingAssembly();
var ver = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
       ?? asm.GetName().Version?.ToString();
Console.WriteLine($"⏳ Hub2Cord v{ver} 시작 중...");

await app.RunAsync();


// Core
public class RssWorker : BackgroundService
{
    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _httpFactory;
    private readonly string _stateFilePath;
    private Dictionary<string, string?> _lastIds = new();

    public RssWorker(IConfiguration config, IHttpClientFactory httpFactory)
    {
        _config = config;
        _httpFactory = httpFactory;
        
        string tempPath = _config["StateFilePath"] ?? "";

        if (string.IsNullOrWhiteSpace(tempPath))
        {
            _stateFilePath = Path.Combine(AppContext.BaseDirectory, "hub2cord_state.json");
        }
        else
        {
            _stateFilePath = tempPath;
        }
    }

protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _lastIds = LoadLastIds(_stateFilePath);

        var runEveryHours = _config.GetValue<int>("RunEveryHours", 3);
        var startHour = _config.GetValue<int>("StartHour", 9);
        var smartPrime = _config.GetValue<bool>("SmartPrime", true);
        var tz = ResolveTz(_config["TimeZoneId"]);
        var channelId = _config["Discord:ChannelId"];

        var rssUrls = _config.GetSection("RssUrls").Get<string[]>() 
             ?? new[] { _config["RssUrl"] ?? "https://github.com/Midori-D/Hub2Cord/releases.atom" };
        
        if (rssUrls == null || rssUrls.Length == 0) rssUrls = Array.Empty<string>();

        if (string.IsNullOrWhiteSpace(channelId) || string.IsNullOrWhiteSpace(_config["Discord:BotToken"]))
        {
            Console.Error.WriteLine("❌ 설정 오류: Discord 토큰 또는 채널 ID가 없습니다.");
            return;
        }

        var newRepos = new List<string>();
        foreach (var url in rssUrls)
        {
            if (!_lastIds.ContainsKey(url))
            {
                newRepos.Add(url);
            }
        }

        if (smartPrime && newRepos.Count > 0)
        {
            Console.WriteLine($"🔎 새로운 리포지토리 {newRepos.Count}개 발견! 즉시 정보를 가져옵니다...");
            
            foreach (var url in newRepos)
            {
                if (stoppingToken.IsCancellationRequested) break;
                await CheckOneAsync(url, channelId, stoppingToken);
                await Task.Delay(1000, stoppingToken);
            }
        }
        else
        {
            Console.WriteLine("✅ 모든 리포지토리가 최신 상태입니다. 스케줄 대기 모드로 진입합니다.");
        }

        // Main Loop
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = DelayUntilNextAlignedHour(runEveryHours, startHour, tz);
            Console.WriteLine($"💤 다음 검사까지 대기: {delay.TotalMinutes:F1}분 ({DateTime.Now.Add(delay):MM-dd HH:mm})");
            
            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (TaskCanceledException) { break; }

            await CheckAllAsync(rssUrls, channelId, stoppingToken);
        }
    }

    private async Task CheckAllAsync(string[] rssUrls, string channelId, CancellationToken ct)
    {
        foreach (var url in rssUrls)
        {
            if (ct.IsCancellationRequested) break;
            await CheckOneAsync(url, channelId, ct);
            
            await Task.Delay(1000, ct); 
        }
    }

    private async Task CheckOneAsync(string feedUrl, string channelId, CancellationToken ct)
    {
        try
        {
            var feed = await FeedReader.ReadAsync(feedUrl, ct);
            var latest = feed.Items.FirstOrDefault();
            if (latest is null) return;

            var currentId = latest.Id ?? $"{latest.Link}|{latest.Title}";
            
            if (_lastIds.TryGetValue(feedUrl, out var savedId) && savedId == currentId) 
                return;

            var repoName = GetRepoName(latest.Link);
            var stamp = GetKstStamp(latest);

            var content =
                $"📢 [{repoName}] 새로운 버전이 나왔어요!💌\n" +
                $"🔗 <{latest.Link}>\n" +
                $"📝 {latest.Title}\n" +
                $"📅 {stamp} KST";

            var payload = new { content };

            using var client = _httpFactory.CreateClient("DiscordBot");
            var json = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var res = await client.PostAsync($"https://discord.com/api/v10/channels/{channelId}/messages", json, ct);

            res.EnsureSuccessStatusCode();

            UpdateAndSave(feedUrl, currentId);
            Console.WriteLine($"✅ 전송 완료: [{repoName}] {latest.Title}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"❌ 전송 실패({GetRepoName(feedUrl)}): {ex.Message}");
        }
    }

    // hub2cord_state.json
    private void UpdateAndSave(string feedUrl, string newId)
    {
        _lastIds[feedUrl] = newId;
        try
        {
            var dir = Path.GetDirectoryName(_stateFilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            
            var tmp = _stateFilePath + ".tmp";
            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(tmp, JsonSerializer.Serialize(_lastIds, options));
            File.Move(tmp, _stateFilePath, true);
        }
        catch (Exception ex) 
        { 
            Console.Error.WriteLine($"⚠️ 상태 저장 실패: {ex.Message}"); 
        }
    }

    private Dictionary<string, string?> LoadLastIds(string path)
    {
        try
        {
            if (!File.Exists(path)) return new();
            return JsonSerializer.Deserialize<Dictionary<string, string?>>(File.ReadAllText(path)) ?? new();
        }
        catch { return new(); }
    }

    static string GetRepoName(string link)
    {
        try
        {
            var uri = new Uri(link);
            var segs = uri.Segments;
            if (segs.Length >= 3) return Uri.UnescapeDataString(segs[2].Trim('/'));
        }
        catch { }
        return "Repository";
    }

    // TimeZone
    static TimeZoneInfo ResolveTz(string? timeZoneId)
    {

        if (!string.IsNullOrWhiteSpace(timeZoneId))
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId); } catch { }
        }
        
        try { return TimeZoneInfo.FindSystemTimeZoneById("Korea Standard Time"); } catch { }
        try { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Seoul"); } catch { }
        
        return TimeZoneInfo.Local;
    }

    static string GetKstStamp(FeedItem latest)
    {
        DateTimeOffset publishedUtc = DateTimeOffset.UtcNow;

        if (latest.PublishingDate is DateTime dt)
            publishedUtc = (dt.Kind == DateTimeKind.Unspecified) ? new DateTimeOffset(dt, TimeSpan.Zero) : new DateTimeOffset(dt);
        else if (!string.IsNullOrWhiteSpace(latest.PublishingDateString) && DateTimeOffset.TryParse(latest.PublishingDateString, out var dto))
            publishedUtc = dto;
        
        return publishedUtc.ToUniversalTime().ToOffset(TimeSpan.FromHours(9)).ToString("yyyy-MM-dd HH:mm");
    }

    static int Mod(int a, int m) => ((a % m) + m) % m;

    static TimeSpan DelayUntilNextAlignedHour(int everyHours, int startHour, TimeZoneInfo tz)
    {
        if (everyHours <= 0) everyHours = 1;
        startHour = Mod(startHour, 24);
        var anchor = Mod(startHour, everyHours);

        var nowUtc = DateTimeOffset.UtcNow;
        var nowLoc = TimeZoneInfo.ConvertTime(nowUtc, tz);

        var next = new DateTimeOffset(nowLoc.Year, nowLoc.Month, nowLoc.Day, nowLoc.Hour, 0, 0, tz.GetUtcOffset(nowLoc)).AddHours(1);

        while (Mod(next.Hour - anchor, everyHours) != 0)
            next = next.AddHours(1);

        var delay = next.ToUniversalTime() - nowUtc;
        return delay <= TimeSpan.Zero ? TimeSpan.FromSeconds(1) : delay;
    }
}
