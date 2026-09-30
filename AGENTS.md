# AGENTS.md

Cosmic Digest is a .NET 10 console job (`Program.cs` plus top-level `*.cs` files) that turns RSS feeds into a relevance-gated daily email sent through Resend. It runs daily in GitHub Actions (`.github/workflows/daily-digest.yml`) and commits its state to `data/state.json` in this public repo. The optional feedback API lives in `feedback/CosmicDigest.Feedback.Api/`.

Build and test: `dotnet restore && dotnet build --configuration Release && dotnet test --configuration Release` (xUnit in `tests/CosmicDigest.Tests/`). `dotnet run -- --preview` renders an email with no network access or secrets. The behavior contract is `docs/briefing-contract.md`.

## Code Review Rules

Focus on delivery correctness, durability of the committed state, and secret or PII exposure in a public repo. Formatting and analyzer warnings are left to CI.

### Always flag (P0/P1)

- **Duplicate or lost email.** The outbox has to be committed before anything is sent: `--prepare-only` commits it, then `--deliver-pending` sends. Flag any change that sends before `PendingDigestSends` is persisted, or that removes an outbox entry before a terminal provider result is recorded.
- **Idempotency key drift.** `DigestIdempotency.BuildKey` must be content-derived and stable across clock or date changes and replays. It may advance to a numbered key only after a recorded terminal failure. Flag keys built from timestamps, GUIDs, or run ids. Flag removal of the 24h `AutomaticReplayWindow` guard in `ResumeOldest`, because Resend's idempotency window is 24h.
- **Reviewed-marker bugs.** A failed or ambiguous send must not mark candidates reviewed. A fallback run marks only the candidates it displayed. Delivery that fails after acceptance must remove that delivery's review markers. Flag changes in `ReviewPolicy.cs` and `StateStore.cs` that break these rules, or that turn a malformed AI selection into a silent omission instead of a synthesis failure.
- **Plaintext in public state.** Titles, links, validators, and outbox payloads in `data/state.json` must stay AES-GCM encrypted (`DurableSecretProtection.cs`). Feed URLs must stay replaced by non-reversible identities (`SourceIdentity.cs`). Flag new persisted fields holding article, profile, or recipient data that bypass protection. Flag nonce reuse, a changed key derivation, or a bumped `ProtectionVersion` without a migration path for older state.
- **Committed personal data or secrets.** Flag a real briefing profile, `MAIL_TO`, API keys, `OUTBOX_ENCRYPTION_KEY`, or signing keys committed under `config/` or anywhere else. Only `*.example.json` and test-mail fixtures belong in the repo. The real profile comes from `DIGEST_PROFILE_B64` or a gitignored `DIGEST_PROFILE_PATH`.
- **Weakened feedback or webhook auth** (`FeedbackSecurity.cs`, `feedback/.../Program.cs`). Flag any of these:
  - signature or token comparisons that aren't `CryptographicOperations.FixedTimeEquals`
  - skipping the Svix timestamp or signature check, or parsing the body before `BoundedBodyReader` caps it
  - dropping link expiry
  - letting a GET record feedback (only the explicit POST may)
  - exposing `/metrics` without `FEEDBACK_ADMIN_TOKEN`
- **Torn writes.** State must be written to a temp file and then atomically moved (`StateStore.Save`), and the feedback journal must keep its lock file (`JsonLineJournal.cs`). Flag direct overwrites or unlocked appends.

### Flag when relevant

- `RssIngestor.cs`: a missing per-attempt timeout, unbounded retries, lost `ETag`/`Last-Modified` handling, or a circuit breaker that stops reopening. One failing feed must not abort the run.
- `NewsAi.cs`: article text must stay untrusted data, never instructions. Flag prompt changes that let the model invent versions, prices, dates, or metrics, and flag removal of the ranked-headline fallback when the model call fails.
- Workflow changes: the `concurrency` group must stay. `validate_only` dispatches must never send or commit. A state push conflict must fail visibly, never be swallowed. The deliver retry loop must stop once the outbox is empty.
- Materiality gate changes that force an email when nothing qualifies. Zero items suppresses the send by design.

### Don't flag

- Using a JSON file instead of a database for state. It is intentional for a single daily writer.
- The feedback API being single-replica.
- Profile version metadata that is kept internal and never rendered in the email.
- Formatting, naming, or nullable-annotation nits.
