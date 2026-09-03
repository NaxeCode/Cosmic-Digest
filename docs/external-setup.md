# External setup gates

The repository ships safely without these integrations. Complete them only when the corresponding account is available.

## 1. Activate the private briefing profile

The repository is public. Keep the real profile in the existing gitignored `config/briefing-profile.local.json` and publish only its base64 encoding as an Actions secret:

```bash
gh secret set DIGEST_PROFILE_B64 --body "$(base64 -w0 config/briefing-profile.local.json)"
```

Confirm the next run logs the expected profile version instead of `legacy-env`. The version remains in diagnostics but is intentionally absent from the email.

## 2. Use the existing domain before claiming another

`naxe.dev` already exists, so the smallest durable route is a sending subdomain such as `digest.naxe.dev`. Add that subdomain in Resend and copy the exact SPF and DKIM records Resend supplies into the DNS provider. After verification, set:

```text
MAIL_FROM=Stella · Cosmic Digest <stella@digest.naxe.dev>
```

The GitHub Student Developer Pack currently offers additional domain benefits, but a second domain is useful only if a deliberately separate public brand becomes valuable. Do not create a new renewal obligation merely to consume a credit.

- Student Pack: https://education.github.com/pack
- Resend domain setup: https://resend.com/docs/dashboard/domains/introduction

## 3. Set the Gmail sender avatar

Create or use a Google Account whose email exactly matches the verified sender address, then upload `assets/brand/stella-avatar-256.png` as its profile picture. This is the pragmatic Gmail-specific route for the inbox avatar.

Do not buy BIMI certification for this personal sender. Reconsider BIMI only after the sender becomes a public brand with a trademark and cross-provider volume.

- Gmail avatar behavior: https://resend.com/docs/knowledge-base/how-do-i-send-with-an-avatar
- BIMI requirements: https://resend.com/docs/dashboard/domains/bimi

## 4. Deploy the optional feedback API

The Student Pack's Azure credit is appropriate for this small reversible service, not for replacing the working GitHub Actions scheduler.

1. Build `feedback/CosmicDigest.Feedback.Api/Dockerfile` from the repository root.
2. Deploy it to a small managed container service with persistent storage mounted at `FEEDBACK_DATA_DIR`.
3. Configure independent random values for `FEEDBACK_SIGNING_KEY` and `FEEDBACK_ADMIN_TOKEN`.
4. Set `FEEDBACK_BASE_URL` in the digest workflow to the public HTTPS `/feedback` route.
5. Register the HTTPS `/webhooks/resend` route in Resend for delivered, bounced, complained, failed, delayed, and suppressed email events.
6. Copy the returned signing secret into `RESEND_WEBHOOK_SECRET` on the feedback service.

The endpoint verifies the raw Svix signature and deduplicates `svix-id`. Feedback GET requests only show a confirmation page, which prevents email-link scanners from recording false signals; the user-initiated POST performs the write. Do not place the API behind middleware that rewrites the webhook request body before verification.

- Webhook verification: https://resend.com/docs/webhooks/verify-webhooks-requests
- Webhook delivery semantics: https://resend.com/docs/webhooks/introduction

## 5. Enable the Testmail contract workflow

Claim the Testmail Student Pack benefit, then add these repository secrets:

- `TESTMAIL_APIKEY`
- `TESTMAIL_NAMESPACE`

Set repository variable `TESTMAIL_ENABLED=true` and manually dispatch **Email Contract** once. The workflow then runs weekly and verifies real delivery, the subject, Stella identity, HTML/plain-text parity, and the absence of private profile metadata.

The workflow uses a unique Testmail tag per run and a `timestamp_from` boundary so parallel or retained messages cannot create a false pass.

- Testmail API: https://testmail.app/docs/

## 6. Acceptance gate

After the private profile and domain are active, inspect three substantive briefs. Keep the release only if:

- no clearly material signal is missed;
- no event repeats under a different link;
- recommendations contain no unsupported claim;
- source failures remain visible without blocking healthy feeds;
- delivery reaches `delivered` or is confirmed in the inbox; and
- reading burden remains lower than the value of the selected signals.

Rollback the last behavior change, not the entire intelligence brief, if one of these conditions regresses.
