# Cosmic Digest

A personal "signal over noise" digest that pulls stories from RSS feeds, scores them against your interests, tracks a small price watchlist, and emails a compact 3-day brief via Mailgun.

- Built for automation: run on a schedule, keep lightweight state, and get the digest in your inbox.
- Built for personalization: relevance scoring via topics/regions/keywords + recency boost.
- Built for extensibility: a clean pipeline you can swap pieces in/out (better scoring, LLM summaries, real price APIs).

## Screenshots
Add your images to `docs/screenshots/` and replace these placeholders.

![Email digest preview](docs/screenshots/email-digest.png)
![Developing stories section](docs/screenshots/developing-stories.png)
![Price watchlist section](docs/screenshots/price-watchlist.png)
![Configuration example](docs/screenshots/config.png)

## How It Works
1. Ingest RSS feeds into a rolling cache (`/data/state.json`).
2. Score items for relevance (topics/regions/keywords + recency) and select the top stories.
3. Detect "developing stories" by comparing title tokens in the last N days vs the previous window.
4. Fetch prices for a watchlist (currently a naive HTML scan; designed to be replaced with APIs).
5. Compose a Markdown digest and send it via Mailgun.

## Tech Stack
- Runtime: .NET 10 (`net10.0`), C#
- Integrations: Mailgun HTTP API
- Content ingestion: `CodeHollow.FeedReader` (RSS)
- HTML parsing/scraping: `AngleSharp` (price fetcher)
- Config: `DotNetEnv` (`.env` for local dev)
- Containerization: Docker
- CI: GitHub Actions (.NET build workflow)
- Optional/experimental: OpenAI .NET SDK (see `NewsAi.cs`)

## Sample Output
```md
## Developing stories (what changed)
- **ai chips** - now: 9, prev: 2, delta: +7

## Worldwide but relevant to you
- [Headline...](https://...)

## Price trends (watchlist)
- **Example Product** - HOLD. Price above recent lows/avg. Latest: 123.45 USD on 2025-01-01
```

## Quickstart (Local)
**Prereqs:** .NET 10 SDK, Mailgun API key + domain.

```bash
cd Cosmic-Digest
dotnet restore
```

Create your `.env`:
- PowerShell: `Copy-Item .env.example .env`
- macOS/Linux: `cp .env.example .env`

Run:
```bash
dotnet run --project ai-newsletter.csproj
```

Note: state persists to `/data/state.json`. On Windows, this maps to `C:\\data\\state.json` (root of the current drive). For a cleaner setup, prefer Docker (below) or change the path in `StateStore.cs`.

## Configuration
Set these in `.env` (see `.env.example`):

| Variable | What it does |
| --- | --- |
| `MAILGUN_API_KEY` | Mailgun API key |
| `MAILGUN_DOMAIN` | Mailgun domain (sandbox or verified) |
| `MAIL_TO` | Recipient email |
| `MAIL_FROM` | Sender email (defaults to `bot@{MAILGUN_DOMAIN}`) |
| `RSS_FEEDS` | Comma-separated RSS feed URLs |
| `PREF_TOPICS` | CSV topics you care about |
| `PREF_REGIONS` | CSV regions you care about |
| `PREF_KEYWORDS` | CSV high-signal keywords |
| `PRICE_WATCH` | `name|url|currency` entries separated by `;` |
| `OPENAI_API_KEY` | Only needed if you wire in LLM summaries |

## Quickstart (Docker)
```bash
cd Cosmic-Digest
docker build -t cosmic-digest .
docker run --rm --env-file .env -v cosmic-digest-data:/data cosmic-digest
```

## Project Highlights (Portfolio Notes)
- Pipeline-style design: each stage is a small, testable unit (ingestion -> scoring -> trends -> prices -> compose -> send).
- State management: rolling cache for news + time-series price history in a single JSON file.
- Trend detection: windowed comparison to surface "what changed" instead of just "what happened".

## Roadmap
- [ ] Wire in LLM summaries for the selected headlines (optional mode)
- [ ] Add scheduling (cron / hosted job)
- [ ] Replace naive price scraping with API-backed integrations
- [ ] Add web dashboard for configuration and history
- [ ] Swap JSON file state for a database when needed

## Security
- Never commit `.env` (API keys). Use `.env.example` as a template.
- For production, prefer platform secrets (GitHub Actions/Fly/your host).

## License
MIT
