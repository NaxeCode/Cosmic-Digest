# AI Newsletter Digest

An automated AI-powered newsletter system that curates personalized news digests from RSS feeds, tracks prices, and delivers customized summaries via email.

## Features

- ?? **RSS Feed Ingestion**: Aggregates news from multiple RSS sources
- ?? **Personalized Relevance Scoring**: Filters news based on your topics, regions, and keywords
- ?? **Trend Detection**: Identifies developing stories by comparing recent vs. previous periods
- ?? **Price Tracking**: Monitors product prices and provides buy/hold recommendations
- ?? **AI-Powered Summaries**: Uses OpenAI to generate concise, relevant news digests
- ?? **Email Delivery**: Sends digests via Mailgun

## Tech Stack

- **.NET 10** (C# 14.0)
- **OpenAI API** for AI-powered summaries
- **Mailgun** for email delivery
- **CodeHollow.FeedReader** for RSS parsing
- **AngleSharp** for web scraping
- **DotNetEnv** for environment configuration

## Prerequisites

- .NET 10 SDK
- OpenAI API key
- Mailgun account (free sandbox tier works for testing)

## Setup

1. **Clone the repository**
   ```bash
   git clone https://github.com/yourusername/ai-newsletter.git
   cd ai-newsletter
   ```

2. **Install dependencies**
   ```bash
   dotnet restore
   ```

3. **Configure environment variables**
   
   Copy `.env.example` to `.env` and fill in your credentials:
   ```bash
   cp .env.example .env
   ```

   Edit `.env` with your values:
   - `OPENAI_API_KEY`: Your OpenAI API key
   - `MAILGUN_API_KEY`: Your Mailgun API key
   - `MAILGUN_DOMAIN`: Your Mailgun domain (sandbox or verified)
   - `MAIL_TO`: Recipient email address
   - `MAIL_FROM`: Sender email address (optional)

4. **Configure your preferences**
   
   Customize these in `.env`:
   - `PREF_TOPICS`: Topics you're interested in
   - `PREF_REGIONS`: Regions to focus on
   - `PREF_KEYWORDS`: Specific keywords to prioritize
   - `RSS_FEEDS`: Comma-separated list of RSS feed URLs
   - `PRICE_WATCH`: Products to track (format: `name|url|currency`)

## Usage

Run the newsletter generator:

```bash
dotnet run
```

The application will:
1. Load existing state from `/data/state.json`
2. Fetch fresh news from configured RSS feeds
3. Score and filter articles based on your preferences
4. Detect trending topics
5. Update tracked prices
6. Generate a personalized digest
7. Send it via email
8. Save updated state

## Configuration

### RSS Feeds
Add RSS feeds as a comma-separated list:
```
RSS_FEEDS=https://feeds.bbci.co.uk/news/rss.xml,https://www.reuters.com/rssFeed/worldNews
```

### Personalization
```
PREF_TOPICS=ai,hardware,gaming,geopolitics,macroeconomy
PREF_REGIONS=US,EU,Middle East
PREF_KEYWORDS=AMD,NVIDIA,SSD,GPU
```

### Price Tracking
Track products with semicolon-separated entries:
```
PRICE_WATCH=GPU RTX 4090|https://example.com/gpu|USD;SSD 4TB|https://example.com/ssd|USD
```

## Deployment

### Docker Support
A Dockerfile is included for containerized deployment.

```bash
docker build -t ai-newsletter .
docker run --env-file .env ai-newsletter
```

### Fly.io
This project is ready for deployment on Fly.io. Set your secrets:

```bash
fly secrets set OPENAI_API_KEY=your_key
fly secrets set MAILGUN_API_KEY=your_key
fly secrets set MAILGUN_DOMAIN=your_domain
fly secrets set MAIL_TO=your_email
# ... set other secrets
```

## Project Structure

```
ai-newsletter/
??? Program.cs           # Main application logic
??? NewsAi.cs           # OpenAI integration for summaries
??? Models.cs.cs        # Data models
??? StateStore.cs       # State persistence
??? RssIngestor.cs      # RSS feed parsing
??? Relevance.cs        # Scoring and trend detection
??? PriceTracker.cs     # Price monitoring
??? DigestComposer.cs   # Markdown digest generation
??? .env.example        # Environment template
```

## Security Notes

?? **Never commit your `.env` file to GitHub!** It contains sensitive API keys.

- The `.gitignore` is configured to exclude `.env`
- Use `.env.example` as a template
- For production, use environment variables or secure secret management

## Mailgun Free Tier Limitations

If using Mailgun's free sandbox domain:
- You must add authorized recipients in the Mailgun dashboard
- Recipients must confirm their email address
- Consider upgrading or using a verified domain for production

## License

MIT License - feel free to use and modify as needed.

## Contributing

Contributions are welcome! Please open an issue or submit a pull request.

## Roadmap

- [ ] Add more sophisticated AI summarization
- [ ] Support additional email providers (SendGrid, AWS SES)
- [ ] Improve price tracking with API integrations
- [ ] Add web dashboard for configuration
- [ ] Implement scheduling (cron/periodic runs)
- [ ] Add database support for larger state storage

## Author

Built with ?? for personalized news consumption
