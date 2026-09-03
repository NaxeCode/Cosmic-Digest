# Cosmic Digest

![Stella](assets/brand/stella-avatar-128.png)

Cosmic Digest turns RSS updates into a sparse personal intelligence brief. It decides whether a development deserves attention before it writes or sends anything.

The goal is not to fill a newsletter. The goal is to surface credible changes that can alter a decision, improve a capability, expose a time-sensitive opportunity, or invalidate a current model.

## What it does

- pulls candidate stories from configured RSS feeds;
- records per-source health, conditional-cache metadata, retries, and circuit state;
- clusters corroborating links into one external event instead of repeating the same story;
- ranks them against a versioned personal briefing profile;
- rejects previously reviewed, stale, irrelevant, and low-value items;
- asks an OpenAI model for a structured `act`, `watch`, or tightly capped `learn` decision and omits low-value items;
- states what changed, why it matters, the smallest justified next move, and evidence confidence;
- sends a compact, accessible Stella-branded email through Resend with an idempotency key;
- distinguishes API acceptance from the latest observed delivery state;
- optionally captures signed usefulness feedback and verified Resend webhooks;
- suppresses the email when nothing clears the materiality gate; and
- persists a bounded review history through GitHub Actions.

The full behavior is defined in [the briefing contract](docs/briefing-contract.md).

## Pipeline

```text
source registry
  -> bounded article cache
  -> cross-source event identity and corroboration
  -> deterministic priority, freshness, trust, and novelty score
  -> structured AI decision gate
  -> Stella-branded evidence-linked brief
  -> Resend
  -> reviewed-event, delivery, source-health, and run state
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

Generate a deterministic presentation preview without network calls or secrets:

```bash
dotnet run -- --preview
```

The resulting `artifacts/email-preview.html` is intentionally gitignored.

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
| `sources` | Named RSS inputs with official/trust metadata and tags |
| `feeds` | Backward-compatible list converted into source entries |
| `lookbackHours` | Bounds freshness and cache retention |
| `candidateLimit` | Caps AI input size |
| `maxItems` | Caps the brief, never creates a quota |
| `minimumScore` | Deterministic admission threshold |
| `eventSimilarityThreshold` | Bounds cross-source title clustering |
| `feedCircuitFailureThreshold` | Opens a temporary feed circuit after repeated failures |
| `feedCircuitHours` | Duration of the temporary feed pause |

Keep the profile minimal. It should contain only context needed to rank external developments, never credentials, mutable balances, private records, or raw personal-system files.

## Configuration

```dotenv
# Required delivery settings
RESEND_API_KEY=re_xxxxx
MAIL_TO=you@example.com
MAIL_FROM=Stella · Cosmic Digest <stella@digest.yourdomain.com>
TIMEZONE=America/New_York
RESEND_VERIFY_DELIVERY=true

# Sender identity
BRAND_NAME=Stella · Cosmic Digest
BRAND_AVATAR_URL=https://raw.githubusercontent.com/NaxeCode/Cosmic-Digest/main/assets/brand/stella-avatar-128.png

# AI decision layer
OPENAI_API_KEY=sk-proj-xxxxx
ENABLE_AI_SUMMARY=true
OPENAI_MODEL=gpt-5.6-terra
OPENAI_REASONING_EFFORT=medium

# Preferred profile input
DIGEST_PROFILE_PATH=briefing-profile.local.json

# Optional outcome loop; both values are required before links appear
FEEDBACK_BASE_URL=https://feedback.yourdomain.com/feedback
FEEDBACK_SIGNING_KEY=replace-with-a-long-random-secret

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

Optional capabilities use `BRAND_AVATAR_URL`, `FEEDBACK_BASE_URL`, `FEEDBACK_SIGNING_KEY`, and `RESEND_VERIFY_DELIVERY`.

`OPENAI_MODEL` and `OPENAI_REASONING_EFFORT` may be set as repository variables. The workflow has a concurrency guard, runs the test suite before delivery, and fails visibly if reviewed-state persistence cannot be pushed.

Scheduled GitHub Actions may still be delayed under platform load. The workflow preserves correct local scheduling, but GitHub does not provide a real-time delivery SLA.

## State and failure semantics

`data/state.json` stores a short article cache plus bounded reviewed-event, source-health, delivery, and run-metric history.

- Upgrades use the prior `LastDigestUtc` as a migration boundary and persist it until it ages outside the active lookback window.
- URL tracking parameters are removed before deduplication.
- Similar titles from independent sources are clustered into one event and receive a bounded corroboration boost.
- New links are also compared with retained reviewed titles, so a corroborating retitle that arrives on a later run is suppressed without collapsing conflicting version numbers.
- AI-rejected candidates are marked reviewed so they do not consume tokens every day.
- If AI synthesis fails, the email falls back to deterministic ranked headlines.
- If delivery fails, candidates are not marked reviewed. A nonterminal delivery keeps an email-id association, and the next run restores its included events if Resend later reports a terminal failure.
- Resend is polled briefly for `last_event`; pending delivery ids are reconciled again before every later selection run.
- Content-derived idempotency keys stay stable across clock and date boundaries while a send outcome is ambiguous, then advance only after a recorded retryable terminal failure.
- A prepared-send outbox is saved before the Resend call, pinning the exact ambiguity key even if later corroboration changes cluster membership.
- Recipient complaints remain terminal and reviewed; they are never treated as retryable delivery failures.
- If the state commit conflicts, the workflow fails instead of silently losing state.
- The workflow commits a state file produced by the digest even when delivery exits nonzero, while preserving the failed job result.

This remains intentionally small. JSON is still the correct store for one daily writer. The optional feedback service uses an append-only journal and can be moved to managed storage only when observed volume or multiple writers justify it.

## Feedback and delivery service

`feedback/CosmicDigest.Feedback.Api` is an optional minimal ASP.NET service with:

- `GET /feedback` for a scanner-safe confirmation page and `POST /feedback` for the signed, expiring `Useful`, `Noise`, `Wrong`, and `I acted` response;
- `POST /webhooks/resend` for raw-body Svix-verified delivery events with `svix-id` deduplication;
- `GET /metrics` for aggregate outcomes behind a bearer token; and
- `GET /health` for hosting checks.

Feedback and webhook entries are deduplicated from the same atomically replaced journal record, so an interrupted write cannot separate an event from its uniqueness marker.
Unsigned webhook bodies are rejected above 256 KB before the service constructs the payload string.

It is deliberately dormant until its URL and secrets are configured. Build its container from the repository root:

```bash
docker build -f feedback/CosmicDigest.Feedback.Api/Dockerfile -t cosmic-digest-feedback .
```

Use persistent storage for `FEEDBACK_DATA_DIR`. The service stores outcomes, not a public digest archive, and it never adjusts profile weights automatically.

See [external setup](docs/external-setup.md) for the custom domain, Gmail avatar, Resend webhook, Student Pack, and Testmail gates.

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
EventIdentity.cs                   event clustering and stable identity
FeedbackSecurity.cs                signed feedback and webhook verification
ResendEmailClient.cs               idempotent send and delivery-state lookup
StateStore.cs                      bounded operational memory
feedback/CosmicDigest.Feedback.Api optional outcome and webhook service
assets/brand/                      Stella sender identity assets
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
