# Hub2Cord
깃허브 릴리즈 (.Atom 피드) → Discord 채널 알림 봇<br>
여러 플러그인(여러 RSS)도 각각 독립적으로 감지하여, 같은 채널로 공지할 수 있어요.

# Features
- 깃허브 .atom 릴리즈 피드를 주기적으로 체크
- **KST(한국시간)**으로 발행 시각 표시
- 콜드 스타트(옵션): 실행 직후엔 조용, 새 릴리즈부터 알림

# Requirements
- .NET 8 SDK
- Discord 봇 토큰(봇이 채널에 글쓰기 권한 필요)

# How to Use
1. appsettings.example.json을 appsettings.json로 수정 후 빈칸 채우기
2. 실행하기

# Example
```
📢 [CounterStrikeSharp] 새로운 버전이 나왔어요!💌
🔗 https://github.com/roflmuffin/CounterStrikeSharp/releases/tag/v1.0.xxx
📝 v1.0.xxx
📅 20xx-0x-0x 12:00
```

# Version
- 1.0 배포
- 1.1 Config 단순화, 코드 최적화
