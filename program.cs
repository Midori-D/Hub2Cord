using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CodeHollow.FeedReader;

var builder = WebApplication.CreateBuilder(args);

// 설정 파일 로드
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
var config = builder.Configuration;

// 설정 → 환경변수 순으로 폴백
var token = config["Discord:BotToken"]
             ?? Environment.GetEnvironmentVariable("discode_bot_token")
             ?? Environment.GetEnvironmentVariable("DISCORD_BOT_TOKEN");
var channelId = config["Discord:ChannelId"]
             ?? Environment.GetEnvironmentVariable("DISCORD_CHANNEL_ID");

// 다중 RSS: 배열 우선, 없으면 CS#
var rssUrls = config.GetSection("RssUrls").Get<string[]>()
           ?? new[] { config["RssUrl"] ?? "https://github.com/roflmuffin/CounterStrikeSharp/releases.atom" };

// 체크 반복 시간 설정
var intervalMinutes = config.GetValue("CheckIntervalMinutes", 180);

// 디버깅
if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(channelId))
{
    Console.Error.WriteLine("❌ 설정 부족: Discord:BotToken / Discord:ChannelId (또는 환경변수) 를 확인하세요.");
    return;
}

// HTTP 클라이언트 설정
var http = new HttpClient();
http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bot", token);
http.DefaultRequestHeaders.UserAgent.ParseAdd("Hub2Cord/1.0 (+feed -> discord)");
http.Timeout = TimeSpan.FromSeconds(15);
    
// 피드별 lastId 저장
var lastIds = new Dictionary<string, string?>();

// 날짜 설정(KST)
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

// 레포 이름 추출
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

// 한 개 피드 처리
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
        Console.WriteLine($"✅ Sent: [{repoName}] {latest.Title} @ {stamp} KST");
    }
    catch (HttpRequestException ex)
    {
        Console.Error.WriteLine($"❌ Discord 전송 실패({feedUrl}): {ex.Message}");
    }
}

// 콜드 스타트 기능
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
                Console.WriteLine($"🧊 primed: {url} -> {id}");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"⚠️ prime failed ({url}): {ex.Message}");
        }
    }
}

// 모든 피드 순회
async Task CheckAllAsync()
{
    foreach (var url in rssUrls)
    {
        try { await CheckOneAsync(url); }
        catch (Exception ex) { Console.Error.WriteLine($"[{url}] 에러: {ex.Message}"); }
    }
}

// 최초 1회 + 주기 실행
_ = Task.Run(async () =>
{
    if (suppressOnStartup)
    {
        await PrimeLastIdsAsync(); // ← 처음엔 “현재 최신”만 기억 후 슬립
    }

    await CheckAllAsync(); // 즉시 한 번 체크 (프라임되어 있으면 스킵됨)
    var timer = new PeriodicTimer(TimeSpan.FromMinutes(intervalMinutes));
    while (await timer.WaitForNextTickAsync())
    {
        try { await CheckAllAsync(); }
        catch (Exception ex) { Console.Error.WriteLine(ex); }
    }
});

// 헬스체크 엔드포인트 (Cloud Run 호환)
var app = builder.Build();
app.MapGet("/", () => "Hub2Cord running");
await app.RunAsync();
