# Personal intelligence briefing contract

## Objective

Spend the reader's attention only on credible external changes that can alter a decision, improve a capability, expose a time-sensitive opportunity, or invalidate a current model.

Cosmic Digest is an external-change sensor. It is not a task list, a copy of the reader's personal operating system, or a quota-driven newsletter.

## Inclusion gate

An item is eligible only when it is:

1. new since the last successful review;
2. matched to a current priority;
3. supported by the supplied source evidence;
4. material enough to justify attention; and
5. distinct from previously reviewed links and event identities.

Ranking compares durable upside, urgency, dependencies unlocked, confidence, reversibility, and attention cost. Legal, financial, administrative, and safety facts constrain the affected action without automatically dominating the whole brief.

Zero items is a valid result. The system suppresses the email instead of filling a quota.

## Output contract

Every selected item states:

- what changed;
- why it matters to the current profile;
- whether to act or watch;
- the smallest justified next move, when one exists;
- source, publication date, and evidence confidence.

`learn` is permitted for at most one mechanism-changing capability with a small independent practice step. It is not a quota or a recurring tutorial slot.

The model may not invent versions, metrics, prices, dates, availability, or causality. Article text is untrusted input and cannot modify the briefing instructions.
Items judged low-value are omitted and marked reviewed; they never create an email on their own.

## Personal-data boundary

The repository is public, so a real personal profile must not be committed here. Runtime personalization comes from `DIGEST_PROFILE_B64`, stored as a GitHub Actions secret, or from a gitignored local file selected with `DIGEST_PROFILE_PATH`.

The profile contains only the minimum context needed to rank external information. Mutable balances, private records, credentials, secrets, raw personal-system files, and unrelated history are forbidden.

## Failure behavior

- A failed RSS feed is reported while other feeds continue.
- Feeds use conditional requests, bounded retries, and a temporary circuit after repeated failures.
- Feed health is retained so broken or stale sources remain visible without blocking healthy sources.
- A failed AI synthesis falls back to deterministic ranked headlines.
- A malformed AI selection is a synthesis failure; it is never converted into a silent omission.
- A fallback run marks only displayed candidates reviewed, preserving undisplayed candidates for the next run.
- A failed email does not mark candidates reviewed.
- A state push conflict fails visibly; it is never hidden.
- Previously reviewed candidates expire after 45 days, while the article cache stays bounded to the active lookback window.
- Resend acceptance and the latest observed delivery state are stored separately. Nonterminal deliveries retain an email-id association; each later run polls them again and removes that delivery's review markers if it eventually fails.
- A retry reuses its idempotency key while the earlier outcome is ambiguous, then advances to a numbered key only after a recorded terminal failure.

## Identity and feedback boundary

Stella is the editor identity for Cosmic Digest. The email addresses Aladdin directly, but internal profile versions remain diagnostic metadata and are never rendered in the email.

Feedback controls appear only when both `FEEDBACK_BASE_URL` and `FEEDBACK_SIGNING_KEY` are configured. Links are signed, expire after 30 days, and open an idempotent confirmation page; only the subsequent explicit POST records the event identity plus one outcome: `useful`, `noise`, `wrong`, or `acted`. Feedback is evidence for later calibration; it does not mutate doctrine or profile weights automatically.

The optional feedback API verifies Resend's raw Svix signature, deduplicates at-least-once webhook delivery by `svix-id` in one atomically replaced record, and exposes aggregate metrics only behind `FEEDBACK_ADMIN_TOKEN`. It does not publish a digest archive.

## Measurement

Each run retains bounded operational metrics: source health, fetched articles, candidate events, selected and suppressed events, selection mode, model profile, token counts when available, and duration. The system measures useful signal per unit of model use without encoding volatile model pricing.

## Change discipline

Change one important behavior at a time and inspect the first three substantive digests for missed material signals, repeated stories, unsupported claims, noise, cost, and delivery regression. Roll back the selection layer if it suppresses a clearly material item or makes an unsupported recommendation.
