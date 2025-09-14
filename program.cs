using CodeHollow.FeedReader;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Reflection;

// Load appsettings.json
var builder = WebApplication.CreateBuilder(args);

// Hub2Cord Start
var asm = Assembly.GetExecutingAssembly();
var ver = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
       ?? asm.GetName().Version?.ToString();
Console.WriteLine($"⏳ Hub2Cord v{ver} 실행...");       

var config = builder.Configuration;

// BotToken, ChannelId, intervalMinutes
var token = config["Discord:BotToken"]
             ?? Environment.GetEnvironmentVariable("discord_bot_token")
             ?? Environment.GetEnvironmentVariable("DISCORD_BOT_TOKEN");
var channelId = config["Discord:ChannelId"]
             ?? Environment.GetEnvironmentVariable("DISCORD_CHANNEL_ID");
var intervalMinutes = config.GetValue("CheckIntervalMinutes", 180);

var rssUrls = config.GetSection("RssUrls").Get<string[]>()
           ?? new[] { config["RssUrl"] ?? "https://github.com/roflmuffin/CounterStrikeSharp/releases.atom" };

// Error Code
if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(channelId))
{
    Console.Error.WriteLine("❌ 설정 부족: Discord:BotToken / Discord:ChannelId (또는 환경변수) 를 확인하세요.");
    return;
}

// HTTP Settings
var http = new HttpClient();
http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bot", token);
http.DefaultRequestHeaders.UserAgent.ParseAdd("Hub2Cord/1.0 (+feed -> discord)");
http.Timeout = TimeSpan.FromSeconds(15);

// LastId Memory cache
var lastIds = new Dictionary<string, string?>();

// Date Setting(KST)
static string GetKstStamp(CodeHollow.FeedReader.FeedItem latest)
{
    DateTimeOffset? published = null;

    if (latest.PublishingDate is DateTime dt1)
    {
        var dto = (dt1.Kind == DateTimeKind.Unspecified)
            ? new DateTimeOffset(dt1, TimeSpan.Zero)
            : new DateTimeOffset(dt1);
        published = dto.ToUniversalTime();
    }
    else if (!string.IsNullOrWhiteSpace(latest.PublishingDateString)
          && DateTimeOffset.TryParse(latest.PublishingDateString, out var dto2))
    {
        published = dto2.ToUniversalTime();
    }

    if (published is null)
    {
        try
        {
            var atom = System.Xml.Linq.XNamespace.Get("http://www.w3.org/2005/Atom");
            var xe = latest.SpecificItem?.Element;
            var publishedStr = xe?.Element(atom + "published")?.Value
                            ?? xe?.Element(atom + "updated")?.Value;
            if (!string.IsNullOrWhiteSpace(publishedStr)
             && DateTimeOffset.TryParse(publishedStr, out var dto3))
            {
                published = dto3.ToUniversalTime();
            }
        }
        catch { /* 무시 */ }
    }

    var publishedUtc = published ?? DateTimeOffset.UtcNow;
    var publishedKst = publishedUtc.ToOffset(TimeSpan.FromHours(9));
    return publishedKst.ToString("yyyy-MM-dd HH:mm");
}

// Extract Repo Name
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

async Task CheckOneAsync(string feedUrl)
{
    var feed = await FeedReader.ReadAsync(feedUrl);
    var latest = feed.Items.FirstOrDefault();
    if (latest is null) return;

    var currentId = latest.Id ?? $"{latest.Link}|{latest.Title}";
    if (lastIds.TryGetValue(feedUrl, out var saved) && saved == currentId) return;
    lastIds[feedUrl] = currentId;

    var repoName = GetRepoName(latest.Link);
    var stamp = GetKstStamp(latest);

    var content =
        $"📢 [{repoName}] 새로운 버전이 나왔어요!💌\n" +
        $"🔗 <{latest.Link}>\n" +
        $"📝 {latest.Title}\n" +
        $"📅 {stamp} KST";

    var payload = new { content };

    try
    {
        var json = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var res = await http.PostAsync($"https://discord.com/api/channels/{channelId}/messages", json);
        res.EnsureSuccessStatusCode();
        Console.WriteLine($"✅ Hub2Cord 성공!: [{repoName}] {latest.Title} @ {stamp} KST");
    }
    catch (HttpRequestException ex)
    {
        Console.Error.WriteLine($"❌ Discord 전송 실패({feedUrl}): {ex.Message}");
    }
}

// Cold Start
var suppressOnStartup = config.GetValue("SuppressOnStartup", true);
async Task PrimeLastIdsAsync()
{
    foreach (var url in rssUrls)
    {
        try
        {
            var feed = await FeedReader.ReadAsync(url);
            var latest = feed.Items.FirstOrDefault();
            if (latest != null)
            {
                var id = latest.Id ?? $"{latest.Link}|{latest.Title}";
                lastIds[url] = id;
                Console.WriteLine($"🧊 프라임(콜드 스타트)됨: {url} -> {id}");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"⚠️ 프라임(콜드 스타트) 실패 ({url}): {ex.Message}");
        }
    }
}

// Feed Processing
async Task CheckAllAsync()
{
    foreach (var url in rssUrls)
    {
        try { await CheckOneAsync(url); }
        catch (Exception ex) { Console.Error.WriteLine($"❌ [{url}] 에러: {ex.Message}"); }
    }
}

static TimeSpan DelayUntilNextTopOfHour()
{
    var now = DateTimeOffset.Now; // Server Local Time
    var next = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, now.Offset).AddHours(1);
    var delay = next - now;
    return delay <= TimeSpan.Zero ? TimeSpan.FromSeconds(1) : delay;
}

// Task Run
_ = Task.Run(async () =>
{
    if (suppressOnStartup)
    {
        await PrimeLastIdsAsync();
    }

    // First Run CSheck
    await CheckAllAsync();

    // On-Time Loop
    while (true)
    {
        try
        {
            await Task.Delay(DelayUntilNextTopOfHour());
            await CheckAllAsync();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
        }
    }
});

// Endpoint For Health Check
var app = builder.Build();
app.MapGet("/", () => "Hub2Cord running");
await app.RunAsync();
