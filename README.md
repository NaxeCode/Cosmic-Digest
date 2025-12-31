# Cosmic Digest

A personalized AI-powered tech newsletter that delivers curated news, relevant articles, and daily coding challenges straight to your inbox. Built for developers who want signal over noise.

**Key Features:**
- 🤖 **AI-Powered Summaries** - GPT-5.1 generates concise, technical summaries with specific version numbers and citations
- 🎯 **Smart Relevance Scoring** - Filters news by your interests (topics, keywords, regions) with recency boost
- 📧 **Beautiful HTML Emails** - Styled, mobile-friendly digests via Resend
- 💪 **Daily Coding Challenges** - Keep your skills sharp with algorithm problems, system design, and real-world scenarios
- 📊 **Price Tracking** - Monitor product prices with buy/hold recommendations (optional)
- ⚡ **Automated Delivery** - GitHub Actions runs daily at 8 AM UTC (free forever)

## How It Works

1. **Ingest** - Fetches RSS feeds from Hacker News, TechCrunch, The Verge, and more
2. **Score** - Ranks articles by relevance to your interests (AI, frontend, backend, open-source, etc.)
3. **Summarize** - GPT-5.1 generates a technical summary with specific details and clickable citations
4. **Challenge** - Generates a unique daily coding challenge to keep skills sharp
5. **Email** - Sends a beautiful HTML digest via Resend
6. **Persist** - Saves state to prevent duplicate articles

## Sample Output

Your daily email includes:

### AI Summary
- **Next.js 15.0**: Released with React Server Components as default, new App Router caching strategy, and 40% faster builds via Turbopack. Breaking change: Pages Router now requires manual opt-in. Critical for teams migrating from Pages to App Router. [[1]](https://...)
- **Claude 3.7 Sonnet**: Anthropic's latest model with 200K context window (2x previous), 88% on SWE-bench coding benchmark. Available via API today - ideal for code generation. [[3]](https://...)

### Worldwide but relevant to you
- [TypeScript 5.6 Beta Announcement](https://...) — Microsoft (2025-12-30)
- [The State of WebAssembly 2025](https://...) — Hacker News (2025-12-29)

### Challenge of the Day
**Challenge of the Day: Implement LRU Cache**

Design and implement a data structure for a Least Recently Used (LRU) cache with O(1) get and put operations.

**Difficulty:** Medium
**Skills:** Hash Tables, Doubly Linked List, Design

**Example:**
```
cache = LRUCache(2)
cache.put(1, 1)
cache.put(2, 2)
cache.get(1)       // returns 1
cache.put(3, 3)    // evicts key 2
cache.get(2)       // returns -1 (not found)
```

## Tech Stack

- **Runtime:** .NET 10 (net10.0), C#
- **Email Service:** Resend (modern, developer-friendly email API)
- **AI:** OpenAI GPT-5.1 (technical summaries + daily challenges)
- **RSS:** CodeHollow.FeedReader
- **HTML Parsing:** AngleSharp (for price scraping)
- **Markdown:** Markdig (converts markdown to styled HTML)
- **Config:** DotNetEnv (`.env` for local dev)
- **Automation:** GitHub Actions (free daily runs)

## Quickstart (Local)

**Prerequisites:** .NET 10 SDK, Resend API key, OpenAI API key

1. **Clone the repo:**
```bash
git clone https://github.com/yourusername/Cosmic-Digest.git
cd Cosmic-Digest
```

2. **Install .NET 10 SDK:**
```bash
# On WSL/Ubuntu
wget https://dot.net/v1/dotnet-install.sh
chmod +x dotnet-install.sh
./dotnet-install.sh --channel 10.0
```

3. **Create `.env` file:**
```bash
cp .env.example .env
# Edit .env with your API keys and preferences
```

4. **Restore dependencies:**
```bash
dotnet restore
```

5. **Run locally:**
```bash
dotnet run
```

The digest will be sent to your email immediately.

## Configuration

Edit `.env` to customize your digest:

| Variable | Description | Example |
|----------|-------------|---------|
| `RESEND_API_KEY` | Resend API key ([get one free](https://resend.com)) | `re_xxxxx` |
| `MAIL_TO` | Your email address | `you@example.com` |
| `MAIL_FROM` | Sender address | `digest@resend.dev` |
| `TIMEZONE` | Your timezone (IANA format) | `America/New_York` |
| `OPENAI_API_KEY` | OpenAI API key | `sk-proj-xxxxx` |
| `ENABLE_AI_SUMMARY` | Enable AI summaries | `true` or `false` |
| `PREF_TOPICS` | Comma-separated topics | `ai,frontend,backend,open source` |
| `PREF_REGIONS` | Comma-separated regions | `US,Silicon Valley,Canada` |
| `PREF_KEYWORDS` | Comma-separated keywords | `React,TypeScript,Python,Docker` |
| `RSS_FEEDS` | Comma-separated RSS feed URLs | `https://hnrss.org/frontpage,...` |
| `PRICE_WATCH` | (Optional) Price tracking | `Item\|url\|USD;...` |

### Customization Tips

**For full-stack engineers:**
- Topics: `ai, frontend, backend, web development, devops, open source, libraries, frameworks`
- Keywords: `React, Next.js, TypeScript, Node.js, Python, Docker, Kubernetes, PostgreSQL, testing, CI/CD`

**For AI/ML engineers:**
- Topics: `ai, machine learning, deep learning, NLP, computer vision`
- Keywords: `LLM, GPT, Claude, PyTorch, TensorFlow, transformers, fine-tuning, RAG`

## GitHub Actions Setup (Free Daily Emails)

**Never pay for hosting - run daily digests for free with GitHub Actions!**

1. **Add secrets to your GitHub repo:**
   - Go to Settings → Secrets and variables → Actions
   - Add these secrets:
     - `RESEND_API_KEY`
     - `OPENAI_API_KEY`
     - `MAIL_TO`
     - `TIMEZONE`
     - `PREF_TOPICS`
     - `PREF_KEYWORDS`
     - `PREF_REGIONS`
     - `RSS_FEEDS`

2. **Enable GitHub Actions:**
   - The workflow is already configured in `.github/workflows/daily-digest.yml`
   - Runs daily at 8 AM UTC (customize the cron schedule)
   - Also supports manual trigger via "Run workflow" button

3. **Push your changes:**
```bash
git add .
git commit -m "chore: configure daily digest"
git push
```

4. **Test the workflow:**
   - Go to Actions tab in GitHub
   - Click "Daily AI Digest"
   - Click "Run workflow" to test immediately

See [GITHUB_ACTIONS_SETUP.md](GITHUB_ACTIONS_SETUP.md) for detailed instructions.

## Features Deep Dive

### 🤖 AI Summaries

- Uses **GPT-5.1** (latest model, 50% cheaper than GPT-4o)
- Generates **6-8 concise bullet points** with specific technical details
- Includes **version numbers**, **metrics**, and **why it matters**
- **Clickable citations** link directly to source articles
- Costs ~$0.02 per digest

### 💪 Daily Coding Challenges

- Rotates between **6 challenge types**:
  - Algorithm problems (sorting, searching, graphs, trees, DP)
  - Data structure implementations (LRU cache, trie, heap)
  - System design (URL shortener, rate limiter, etc.)
  - Code optimization (improve time/space complexity)
  - Real-world scenarios (parse CSV, debounce, etc.)
  - Web fundamentals (Promise.all, deep clone, etc.)
- **Deterministic** - same challenge all day, rotates daily
- Designed to be completable in **15-30 minutes**

### 🎯 Smart Relevance Scoring

Articles are scored by:
- **Keywords match** (+1.0 per keyword)
- **Topics match** (+0.6 per topic)
- **Regions match** (+0.4 per region)
- **Recency boost** (0-0.5 based on age, decays over 3 days)

Only articles scoring **>0.75** make it to your digest.

### 📊 Price Tracking (Optional)

Monitor product prices and get buy/hold recommendations:
```bash
PRICE_WATCH=M2 MacBook Air|https://example.com/m2-air|USD;RTX 4090|https://example.com/rtx-4090|USD
```

Simple HTML scraping - designed to be swapped with real APIs.

## Project Structure

```
Cosmic-Digest/
├── Program.cs              # Main pipeline orchestration
├── NewsAi.cs              # GPT-5.1 summarization
├── ChallengeGenerator.cs  # Daily coding challenges
├── DigestComposer.cs      # Markdown/HTML email builder
├── RssIngestor.cs         # RSS feed fetcher
├── Relevance.cs           # Scoring + trend detection
├── PriceTracker.cs        # Price monitoring
├── StateStore.cs          # JSON persistence
├── Models.cs              # Data models
├── .env                   # Local config (gitignored)
├── .github/
│   └── workflows/
│       └── daily-digest.yml  # GitHub Actions automation
└── data/
    └── state.json         # Persistent state (tracked in git)
```

## Cost Analysis

**Completely free to run with GitHub Actions!**

- **GitHub Actions:** Free (2,000 minutes/month)
- **Resend:** Free tier (100 emails/day)
- **OpenAI GPT-5.1:** ~$0.02 per digest (~$0.60/month for daily emails)

**Total monthly cost:** ~$0.60 💰

## Portfolio Highlights

- **Clean pipeline architecture** - Each stage is a testable, swappable unit
- **Smart relevance scoring** - Multi-factor ranking with recency decay
- **AI integration** - Deterministic output with strict formatting rules
- **State management** - Rolling cache with deduplication
- **GitHub Actions automation** - Free, reliable daily execution
- **Modern .NET 10** - Latest C# features and performance

## Roadmap

- [x] AI-powered summaries with GPT-5.1
- [x] Daily coding challenges
- [x] GitHub Actions automation
- [x] HTML email styling
- [x] Clickable citations
- [ ] Web dashboard for configuration
- [ ] Article sentiment analysis
- [ ] Replace naive price scraping with real APIs
- [ ] Database support for larger archives
- [ ] Browser extension for reading list

## Security

- **Never commit `.env`** - Contains API keys
- Store secrets in **GitHub Actions Secrets** for automation
- API keys visible in `.env` should be **revoked immediately**
- Resend and OpenAI API keys are **bearer tokens** - treat like passwords

## License

MIT - Build whatever you want with it!

---

Built with ☕ and curiosity. Questions? Open an issue or PR!
