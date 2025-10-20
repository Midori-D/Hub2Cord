# 🎀 Hub2Cord
GitHub Releases (Atom) → Discord channel notification bot.
- **Flexible scheduling**: run exactly at the hour you want (e.g., every day at 09:00 KST) or every N hours aligned to the top of the hour.
- **Cold start suppression**: remembers the last release per feed to avoid reposts.

## ✨ Features
- Monitor one or more GitHub Releases Atom feeds.
- “Cold start suppression” to prevent spam on the first launch.

## ⚙️ Config (appsettings.json)

```json
{
  "Discord": {
    "BotToken": "Bot_Token",
    "ChannelId": "Channel_ID"
  },
  "SuppressOnStartup": true,
  "RunEveryHours": 3,
  "StartHour": 9,
  "TimeZoneId": "Asia/Seoul",
  "RssUrls": [
    "https://github.com/Midori-D/Hub2Cord/releases.atom",
    "https://github.com/Midori-D/CS2_ForceNames/releases.atom"
  ]
}
```

## 🧪 Build
- .NET 8 SDK

## How to Use
1. Rename appsettings.example.json to appsettings.json, then fill in the required fields.
2. Run the app.

## Example
```
📢 [Hub2Cord] A new version is out! 💌
🔗 https://github.com/Midori-D/Hub2Cord/releases/tag/v1.0.xxx
📝 v1.0.xxx
📅 20xx-0x-0x 12:00
```

## 📝 Changelog
- 1.0 Initial release
- 1.1 Simplified config, optimized code
- 1.2 Added On-Time Loop
- 1.3 Added Flexible scheduling

## 🙏 Credits
- Midori server ops team

## 📄 License
- MIT
