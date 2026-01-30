# 🎀 Hub2Cord [GitHub Releases (Atom) -> Discord]

A tiny bot that watches **GitHub Releases Atom feeds** and posts new release notifications to a Discord channel.
<br />
<br /> ![Hub2Cord](/Hub2Cord.png)

## ✨ Features
* Monitor one or more GitHub Releases Atom feeds (`.../releases.atom`)
* Post release messages to a Discord channel
* Remembers the last release per feed across restarts via a local state file (SmartPrime)

## ⚙️ Config (appsettings.json)

```json
{
  "Discord": {
    "BotToken": "Bot_Token",
    "ChannelId": "Channel_ID"
  },
  "SmartPrime": true,
  "RunEveryHours": 3,
  "StartHour": 9,
  "TimeZoneId": "Asia/Seoul",
  "StateFilePath": "hub2cord_state.json",
  "RssUrls": [
    "https://github.com/Midori-D/Hub2Cord/releases.atom",
    "https://github.com/Midori-D/CS2_ForceNames/releases.atom"
  ]
}
```
* **SmartPrime**: If `true`, Enables restart-safe deduplication. Hub2Cord stores the last processed release ID per feed in "StateFilePath" and restores it on startup, preventing duplicate/spam notifications after the server goes down and comes back up.
* **RunEveryHours**: Run interval in hours (aligned to the top of the hour).
* **StartHour**: Alignment anchor hour (0–23).
* Example: 'StartHour=9', 'RunEveryHours=24' ⇒ runs at 09:00 every day.
* **StateFilePath**: Path to the state JSON file (optional). Default: 'hub2cord_state.json' next to the app.

## 🧪 Build
- .NET 8 SDK

## 🚀 How to Use
1. Rename appsettings.example.json to appsettings.json, then fill in the required fields.
2. Run the app.

## 🖥️ Example
```
📢 [Hub2Cord] A new version is out! 💌
🔗 https://github.com/Midori-D/Hub2Cord/releases/tag/v1.0.xxx
📝 v1.0.xxx
📅 20xx-0x-0x 12:00
```

## 📝 Changelog

```
## [1.4] - 2026.01.31
- Added SmartPrime
- Optimized code

## [1.3]
- Added Flexible scheduling

## [1.2]
- Added On-Time Loop

## [1.1]
- Simplified config, optimized code

## [1.0]
- Initial release
```

## 🙏 Credits
- Midori server ops team

## 📄 License
- MIT
