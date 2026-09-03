# Cosmic Digest

Cosmic Digest turns RSS updates into a sparse personal intelligence brief. It decides whether a development deserves attention before it writes or sends anything.

The goal is not to fill a newsletter. The goal is to surface credible changes that can alter a decision, improve a capability, expose a time-sensitive opportunity, or invalidate a current model.

## What it does

- pulls candidate stories from configured RSS feeds;
- ranks them against a versioned personal briefing profile;
- rejects previously reviewed, stale, irrelevant, and low-value items;
- asks an OpenAI model for a structured `act` or `watch` decision and omits low-value items;
- states what changed, why it matters, the smallest justified next move, and evidence confidence;
- sends a compact email through Resend;
- suppresses the email when nothing clears the materiality gate; and
- persists a bounded review history through GitHub Actions.

The full behavior is defined in [the briefing contract](docs/briefing-contract.md).

## Pipeline

```text
RSS feeds
  -> bounded article cache
  -> deterministic priority, freshness, trust, and novelty score
  -> structured AI decision gate
  -> compact evidence-linked brief
  -> Resend
  -> reviewed-link state
```

The deterministic layer keeps the AI input small and auditable. The AI layer performs the context-sensitive judgment that literal keyword scoring cannot: whether a matched item actually changes anything for the reader.

## Requirements

- .NET 10 SDK
- Resend API key
- OpenAI API key when `ENABLE_AI_SUMMARY=true`

```bash
git clone https://github.com/NaxeCode/Cosmic-Digest.git
cd Cosmic-Digest
cp .env.example .env
dotnet restore
dotnet run
```

## Personalization

The preferred input is a JSON profile. Start from [the example profile](config/briefing-profile.example.json):

```bash
cp config/briefing-profile.example.json briefing-profile.local.json
```

Then set this in `.env`:

```dotenv
DIGEST_PROFILE_PATH=briefing-profile.local.json
```

The local profile is gitignored. The repository is public, so do not commit real personal context.

For GitHub Actions, store the base64-encoded profile as one repository secret:

```bash
gh secret set DIGEST_PROFILE_B64 --body "$(base64 -w0 briefing-profile.local.json)"
```

If no JSON profile is supplied, Cosmic Digest remains backward compatible with `PREF_TOPICS`, `PREF_KEYWORDS`, `PREF_REGIONS`, and `RSS_FEEDS`.

### Profile fields

| Field | Purpose |
| --- | --- |
| `version` | Identifies the context revision used in the email |
| `objective` | Defines what the brief is optimizing |
| `priorities` | Weighted domains, matching signals, and why each matters |
| `trustedDomains` | Adds a bounded source-quality boost |
| `exclusions` | Names recurring classes of noise |
| `feeds` | RSS inputs for this profile |
| `lookbackHours` | Bounds freshness and cache retention |
| `candidateLimit` | Caps AI input size |
| `maxItems` | Caps the brief, never creates a quota |
| `minimumScore` | Deterministic admission threshold |

Keep the profile minimal. It should contain only context needed to rank external developments, never credentials, mutable balances, private records, or raw personal-system files.

## Configuration

```dotenv
# Required delivery settings
RESEND_API_KEY=re_xxxxx
MAIL_TO=you@example.com
MAIL_FROM=digest@yourdomain.com
TIMEZONE=America/New_York

# AI decision layer
OPENAI_API_KEY=sk-proj-xxxxx
ENABLE_AI_SUMMARY=true
OPENAI_MODEL=gpt-5.6-terra
OPENAI_REASONING_EFFORT=medium

# Preferred profile input
DIGEST_PROFILE_PATH=briefing-profile.local.json

# Legacy fallback inputs
PREF_TOPICS=ai,backend,developer tooling
PREF_KEYWORDS=OpenAI,.NET,C#,PostgreSQL
PREF_REGIONS=United States
RSS_FEEDS=https://openai.com/news/rss.xml,https://github.blog/changelog/feed/
```

The model and reasoning effort are explicit runtime settings. `gpt-5.6-terra` with medium effort is the default because this is routine multi-source synthesis with moderate ambiguity. Lower it only after the first three substantive briefs show no material misses; raise it only when observed quality justifies the cost.

## GitHub Actions

The daily workflow runs at 8:17 AM in `America/New_York`. The off-hour minute reduces top-of-hour queue pressure, while the IANA timezone preserves the local time across daylight-saving changes.

Configure these repository secrets:

- `RESEND_API_KEY`
- `MAIL_TO`
- `MAIL_FROM`
- `OPENAI_API_KEY`
- `ENABLE_AI_SUMMARY`
- `DIGEST_PROFILE_B64`

`OPENAI_MODEL` and `OPENAI_REASONING_EFFORT` may be set as repository variables. The workflow has a concurrency guard, runs the test suite before delivery, and fails visibly if reviewed-state persistence cannot be pushed.

Scheduled GitHub Actions may still be delayed under platform load. The workflow preserves correct local scheduling, but GitHub does not provide a real-time delivery SLA.

## State and failure semantics

`data/state.json` stores a short article cache and 45 days of reviewed-link history.

- Upgrades use the prior `LastDigestUtc` as a migration boundary and persist it until it ages outside the active lookback window.
- URL tracking parameters are removed before deduplication.
- AI-rejected candidates are marked reviewed so they do not consume tokens every day.
- If AI synthesis fails, the email falls back to deterministic ranked headlines.
- If delivery fails, candidates are not marked reviewed.
- If the state commit conflicts, the workflow fails instead of silently losing state.

This remains intentionally small. A database is not justified for one daily personal workflow with one writer.

## Development

```bash
dotnet restore
dotnet build --configuration Release
dotnet test --configuration Release
```

Project layout:

```text
Program.cs                         pipeline and delivery
BriefingProfile.cs                 private-profile loading and validation
Relevance.cs                       deterministic selection and URL identity
NewsAi.cs                          structured AI decision gate
DigestComposer.cs                  plain-text and HTML-safe rendering
RssIngestor.cs                     feed ingestion
StateStore.cs                      bounded cache and review history
tests/CosmicDigest.Tests/          regression tests
docs/briefing-contract.md          product and failure contract
```

## Security

- Real profiles and `.env` files are gitignored.
- GitHub Actions receives the profile through an encrypted secret.
- Article titles and summaries are treated as untrusted model input.
- Raw HTML from the model or feeds is disabled during Markdown rendering.
- The prompt forbids unsupported versions, metrics, prices, dates, and causal claims.

If a key is exposed, revoke it immediately and replace the corresponding repository secret.

## License

MIT
