# Cosmic Digest

A personal news aggregator that sends you a daily email with tech news you actually care about. Uses GPT to summarize the day's headlines and throws in a coding challenge to keep you sharp.

I built this because most tech newsletters are either too broad or too niche. This one learns your interests and filters accordingly.

## What it does

- Pulls from RSS feeds (Hacker News, TechCrunch, etc.)
- Scores articles based on your topics/keywords/regions
- GPT-5.1 writes a summary with the important bits
- Adds a daily coding problem (algorithm, system design, or practical stuff)
- Emails you a clean digest every morning
- Runs for free on GitHub Actions

## Example digest

```
Daily Digest — Tuesday, December 31, 2024 at 8:00 AM EST

## AI Summary
- Next.js 15.0: React Server Components are now the default, new App Router caching, 40% faster builds with Turbopack. Pages Router needs manual opt-in now. [1]
- Claude 3.7 Sonnet: 200K context (doubled), 88% on SWE-bench. Available via API. [3]

## Worldwide but relevant to you
- TypeScript 5.6 Beta Announcement — Microsoft (2024-12-30)
- The State of WebAssembly 2025 — Hacker News (2024-12-29)

## Challenge of the Day
Implement an LRU cache with O(1) get and put operations.
Difficulty: Medium | Skills: Hash Tables, Doubly Linked List
```

## Tech

- .NET 10
- Resend for emails (free tier: 100/day)
- OpenAI GPT-5.1 for summaries (~$0.02 per digest)
- Markdig for markdown → HTML
- GitHub Actions for scheduling (free)

## Setup

You need .NET 10 SDK, a Resend API key, and an OpenAI API key.

```bash
git clone <your-repo>
cd Cosmic-Digest

# Install .NET 10 if needed (WSL/Ubuntu)
wget https://dot.net/v1/dotnet-install.sh
chmod +x dotnet-install.sh
./dotnet-install.sh --channel 10.0

# Config
cp .env.example .env
# edit .env with your keys

dotnet restore
dotnet run
```

You'll get an email right away.

## Configuration

Edit `.env`:

```bash
# Required
RESEND_API_KEY=re_xxxxx
OPENAI_API_KEY=sk-proj-xxxxx
MAIL_TO=you@example.com
TIMEZONE=America/New_York

# Personalization
PREF_TOPICS=ai,frontend,backend,open source,devops
PREF_KEYWORDS=React,TypeScript,Python,Docker,Kubernetes,Next.js
PREF_REGIONS=US,Silicon Valley,Canada

# Sources
RSS_FEEDS=https://hnrss.org/frontpage,https://techcrunch.com/feed/,...

# Optional
ENABLE_AI_SUMMARY=true
PRICE_WATCH=M2 MacBook Air|https://example.com|USD
```

**For full-stack devs:**
Topics: `ai, frontend, backend, web development, devops, open source`
Keywords: `React, Next.js, TypeScript, Node.js, Docker, PostgreSQL, CI/CD`

**For ML engineers:**
Topics: `ai, machine learning, deep learning, NLP`
Keywords: `LLM, GPT, PyTorch, transformers, fine-tuning, RAG`

## GitHub Actions (automated daily emails)

The workflow is already set up in `.github/workflows/daily-digest.yml`.

1. Add your secrets to GitHub repo settings (Settings → Secrets → Actions):
   - `RESEND_API_KEY`
   - `OPENAI_API_KEY`
   - `MAIL_TO`
   - `TIMEZONE`
   - `PREF_TOPICS`
   - `PREF_KEYWORDS`
   - `PREF_REGIONS`
   - `RSS_FEEDS`

2. Push to GitHub

3. It runs daily at 8 AM UTC. You can change the cron schedule or trigger it manually from the Actions tab.

See [GITHUB_ACTIONS_SETUP.md](GITHUB_ACTIONS_SETUP.md) for details.

## How it works

**Relevance scoring:**
Each article gets points for matching your preferences:
- Keywords: +1.0 each
- Topics: +0.6 each
- Regions: +0.4 each
- Recency boost: 0-0.5 (decays over 3 days)

Only articles scoring >0.75 make it to your digest.

**AI summaries:**
GPT-5.1 reads the top 15 articles and writes 6-8 bullets. I tuned the prompt to force specific version numbers, metrics, and citations. The citation numbers are clickable links to the source articles.

**Daily challenges:**
Rotates between algorithms, data structures, system design, and practical problems. Same challenge all day (deterministic based on date), new one tomorrow. Should take 15-30 minutes.

**Price tracking:**
Naive HTML scraping for now. Checks if price is above/below recent average. You can plug in real APIs later.

## Project structure

```
Cosmic-Digest/
├── Program.cs              # Main pipeline
├── NewsAi.cs              # GPT summarization
├── ChallengeGenerator.cs  # Daily challenges
├── DigestComposer.cs      # Email builder
├── RssIngestor.cs         # RSS fetcher
├── Relevance.cs           # Scoring algorithm
├── PriceTracker.cs        # Price monitoring
├── StateStore.cs          # JSON persistence
├── Models.cs              # Data models
├── .env                   # Your config (gitignored)
└── data/state.json        # Article cache (tracked in git for Actions)
```

## Cost

- GitHub Actions: Free (2,000 min/month)
- Resend: Free (100 emails/day)
- GPT-5.1: ~$0.60/month for daily digests

Total: less than a dollar a month.

## Why I built this

Most news feeds are noise. I wanted something that:
- Learns what I care about
- Doesn't spam me with 50 headlines
- Actually helps me stay current in tech
- Keeps my coding skills fresh

The relevance scoring works surprisingly well. The AI summaries save time. The daily challenges are a nice habit.

## Tradeoffs

**JSON file vs Database**

I'm using a JSON file (`data/state.json`) for persistence instead of a database. This works because:
- The dataset is small (few hundred articles max)
- No concurrent writes (single daily job)
- GitHub Actions can commit state changes back to the repo
- Zero hosting costs

If you need to scale to thousands of users or run more frequently, switch to PostgreSQL or SQLite. The `StateStore.cs` abstraction makes this swap easy.

**GitHub Actions vs Cloud hosting**

Running on GitHub Actions instead of a dedicated server or cloud function. The tradeoff:
- ✅ Completely free (2,000 min/month)
- ✅ No infrastructure to manage
- ✅ State persists via git commits
- ❌ Cold starts on every run
- ❌ Must commit state to repo (pollutes git history)
- ❌ 2,000 minute monthly limit

For more frequent digests (hourly, real-time), use Vercel Cron or AWS Lambda with a real database. For personal daily use, GitHub Actions is perfect.

**GPT API vs Local LLM**

I'm using OpenAI's GPT-5.1 API instead of running a local model. Here's why:
- GPT-5.1 quality is significantly better for technical summaries
- Costs ~$0.60/month (negligible for personal use)
- No GPU or hosting infrastructure needed
- API is reliable and fast

If privacy is critical or you're processing thousands of digests, run Llama 3 or Mistral locally. You'll need a GPU and the quality won't be as good, but it's free and private.

## Known issues

- Price scraping is brittle (HTML parsing always is)
- No web UI yet (everything is env vars)
- Citations sometimes break in certain email clients
- State file grows unbounded (should add cleanup)

## Roadmap

- [ ] Web dashboard for config
- [ ] Replace price scraping with actual APIs
- [ ] Article sentiment analysis
- [ ] Database instead of JSON
- [ ] Browser extension for reading list

Pull requests welcome.

## Security

Don't commit your `.env` file. Use GitHub Secrets for automation. If you accidentally expose API keys (like I did), revoke them immediately.

## License

MIT
