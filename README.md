# Watch Sentence

작은 창에 현재 시각과 날짜를 보여주고, 그 시각(분 단위)이 등장하는 영어 문학 작품의 한 구절을 출처와 함께 보여주는 Windows 데스크톱 시계입니다.

A small Windows desktop clock that shows the time, the date, and a line from English-language literature that mentions the current minute.

```
┌──────────────────────────────────────────────────┐
│ 16:30:42                                         │
│ 2026-10-06 화요일                                 │
│                                                  │
│  "Gentlemen," said d'Artagnan, "it is half past  │
│      four, and we have scarcely time to be on    │
│            the road of Chaillot by six."         │
│           — The Three Musketeers, Alexandre Dumas │
└──────────────────────────────────────────────────┘
```

## 사용법

- [Releases](../../releases)에서 `WatchSentence.exe`를 받아 실행합니다. 설치가 필요 없습니다 (Windows 10/11 x64).
- 창 아무 곳이나 끌어서 옮기고, 가장자리를 끌어서 크기를 바꿉니다.
- **오른쪽 클릭** 메뉴
  - 항상 위에 고정
  - 24시간제 / 12시간제
  - 요일 한글/영어 표시
  - 테마: Paper, Kindle, Sepia, Slate, Ink (e-ink 느낌의 저대비 색), 배경색·글자색 직접 선택
  - 투명도
  - 다른 문구 보기, 문구 복사
  - Windows 시작 시 실행
- **더블클릭**하면 같은 시각의 다른 문구를 보여줍니다.
- 설정과 창 위치는 `%APPDATA%\WatchSentence\settings.json`에 저장됩니다.

## 문구 데이터

`src/WatchSentence/quotes.tsv`의 문구는 모두 Project Gutenberg에 있는 퍼블릭 도메인 작품(코난 도일, 디킨스, 오스틴, 쥘 베른, 콜린스, 트롤럽 등 160여 권)에서 `tools/build_quotes.py`로 자동 추출했습니다. 시각 표현("half past four", "a quarter to seven", "5.30 p.m.", "midnight" 등)이 들어 있는 30단어 안팎의 문장만 남겼습니다.

정확히 그 분을 언급하는 문구가 없는 시각에는 가장 가까운 이전 시각의 문구를 보여줍니다 (예: 4:32에는 "half past four").

다시 생성하려면:

```
python3 tools/build_quotes.py <gutenberg-txt-폴더> src/WatchSentence/quotes.tsv
```

## 빌드

```
dotnet publish src/WatchSentence/WatchSentence.csproj -c Release -r win-x64 -o out
```

`v*` 태그를 push하거나, Actions 탭에서 "Build and release" 워크플로를 `release_version`(예: 1.0.1)과 함께 수동 실행하면 빌드 → 서명 → Release 게시가 진행됩니다.

## 코드 서명

Release 워크플로는 아래 둘 중 하나가 설정되어 있으면 실행 파일에 서명합니다. 둘 다 없으면 서명 없이 빌드합니다.

**A. 코드 서명 인증서 (.pfx)** — Repository → Settings → Secrets and variables → Actions → *Secrets*
- `WINDOWS_CERTIFICATE`: .pfx 파일을 base64로 인코딩한 값 (PowerShell: `[Convert]::ToBase64String([IO.File]::ReadAllBytes("cert.pfx"))`)
- `WINDOWS_CERTIFICATE_PASSWORD`: .pfx 비밀번호

**B. Azure Trusted Signing**
- Secrets: `AZURE_TENANT_ID`, `AZURE_CLIENT_ID`, `AZURE_CLIENT_SECRET`
- Variables: `TRUSTED_SIGNING_ENDPOINT` (예: `https://eus.codesigning.azure.net/`), `TRUSTED_SIGNING_ACCOUNT`, `TRUSTED_SIGNING_PROFILE`

설정 후 Actions 탭에서 워크플로를 다시 실행하거나 새 태그를 push하면 서명된 빌드가 릴리즈됩니다.
