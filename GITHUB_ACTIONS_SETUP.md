# GitHub Actions Setup Guide

This guide will help you set up automated daily digests using GitHub Actions.

## Prerequisites

1. A Resend API key (see below)
2. Your repository pushed to GitHub
3. 5 minutes of setup time

---

## Step 1: Get Your Resend API Key

1. Go to **[resend.com](https://resend.com)** and sign up (free, no credit card)
2. Verify your email
3. Navigate to **API Keys** in the dashboard
4. Click **Create API Key**
5. Give it a name (e.g., "Cosmic Digest")
6. Copy the key (starts with `re_...`)

**Note:** For testing, you can use `digest@resend.dev` as your `MAIL_FROM` address without domain verification.

---

## Step 2: Configure GitHub Secrets

Go to your GitHub repository:

**Settings → Secrets and variables → Actions → New repository secret**

Add the following secrets:

### Required Secrets

| Secret Name | Value | Example |
|-------------|-------|---------|
| `RESEND_API_KEY` | Your Resend API key | `re_123abc...` |
| `MAIL_TO` | Your email address | `you@gmail.com` |
| `MAIL_FROM` | Sender email | `digest@resend.dev` |
| `RSS_FEEDS` | Comma-separated RSS feed URLs | See below |
| `PREF_TOPICS` | Your topics of interest | `ai,hardware,gaming` |
| `PREF_REGIONS` | Regions you care about | `US,EU,Asia` |
| `PREF_KEYWORDS` | Keywords to track | `AMD,NVIDIA,Intel` |

### Optional Secrets

| Secret Name | Value | Example |
|-------------|-------|---------|
| `ENABLE_AI_SUMMARY` | Enable AI summaries | `true` or `false` |
| `OPENAI_API_KEY` | OpenAI API key (if AI enabled) | `sk-...` |
| `PRICE_WATCH` | Price watchlist (optional) | Leave empty to disable |

### Example RSS Feeds

```
https://feeds.bbci.co.uk/news/rss.xml,https://www.reuters.com/rssFeed/worldNews,https://www.theverge.com/rss/index.xml,https://techcrunch.com/feed/,https://news.ycombinator.com/rss
```

---

## Step 3: Set Schedule (Optional)

The workflow runs daily at **8 AM UTC** by default.

To change this, edit `.github/workflows/daily-digest.yml`:

```yaml
schedule:
  # Examples:
  - cron: '0 8 * * *'   # 8 AM UTC daily
  - cron: '0 12 * * *'  # 12 PM UTC daily
  - cron: '0 0 * * 1'   # Monday at midnight UTC (weekly)
```

**Cron format:** `minute hour day month weekday`

**Time zone converter:**
- 8 AM UTC = 3 AM EST / 12 AM PST
- Use [crontab.guru](https://crontab.guru) to build your schedule

---

## Step 4: Test It

### Manual Test (Recommended First)

1. Go to **Actions** tab in GitHub
2. Click **Daily Digest** workflow
3. Click **Run workflow** → **Run workflow**
4. Watch the logs to verify it works
5. Check your email!

### Automatic Test

Just wait until the next scheduled time and check your inbox.

---

## How It Works

1. **Scheduled Trigger:** GitHub Actions runs the workflow at the specified time
2. **Fetch News:** Pulls from your RSS feeds
3. **Score & Filter:** Ranks articles by your interests
4. **AI Summary:** (Optional) Generates personalized summary
5. **Price Check:** (Optional) Checks your watchlist
6. **Send Email:** Delivers digest via Resend
7. **Save State:** Commits `data/state.json` back to repo to track history

---

## Troubleshooting

### ❌ "Missing RESEND_API_KEY or MAIL_TO"
- Check that you added the secrets with the exact names shown above
- Secrets are case-sensitive

### ❌ Workflow doesn't run on schedule
- GitHub Actions can delay scheduled workflows by up to 10 minutes
- Workflows on inactive repos may be paused (just run manually once to resume)

### ❌ Email not received
- Check spam/junk folder
- Verify `MAIL_FROM` is a valid Resend sender
- Check workflow logs for errors

### ✅ How to disable
- Go to **Actions** → **Daily Digest** → **⋯** → **Disable workflow**

---

## Cost Estimate

| Service | Free Tier | Your Usage | Cost |
|---------|-----------|------------|------|
| **GitHub Actions** | 2,000 min/month | ~5 min/month | $0 |
| **Resend** | 3,000 emails/month | 30 emails/month | $0 |
| **OpenAI** (optional) | Pay-as-you-go | ~$0.005/month | ~$0.01/month |

**Total: FREE** (or ~$0.01/month with AI enabled)

---

## Tips

- **Start simple:** Test with `ENABLE_AI_SUMMARY=false` first
- **Adjust relevance:** Change `PREF_TOPICS` to fine-tune results
- **Check logs:** View workflow runs under the Actions tab
- **State tracking:** The `data/state.json` file prevents duplicate articles

---

## Need Help?

- Check workflow logs in GitHub Actions
- Review [Resend docs](https://resend.com/docs)
- Open an issue in this repo
