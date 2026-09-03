# Security policy

## Reporting

Report a suspected vulnerability privately through GitHub's security-advisory flow. Do not open a public issue containing a credential, personal profile, recipient address, exploit payload, or other sensitive evidence.

Only the latest revision is supported.

## Data boundary

- Never commit `.env`, `briefing-profile.local.json`, API keys, recipient addresses, or a real personal profile.
- Store the production profile in the `DIGEST_PROFILE_B64` GitHub Actions secret.
- Keep the profile limited to ranking context. Credentials, mutable balances, private records, and raw personal-system files do not belong in the digest.
- `data/state.json` may contain public article titles and links. It must not contain secrets or private profile content.

## Untrusted inputs

RSS titles, summaries, links, and model output are untrusted.

- The AI instruction treats feed content as data and rejects embedded instructions.
- Structured output constrains the model response.
- Markdown rendering disables raw HTML.
- Email links accept only absolute HTTP or HTTPS URLs.
- Unsupported versions, metrics, prices, dates, availability, and causal claims are forbidden by the briefing contract.

## Dependencies and operations

Before merging a change:

```bash
dotnet restore
dotnet build --configuration Release
dotnet test --configuration Release
dotnet list package --vulnerable --include-transitive
```

The delivery workflow has a timeout and concurrency guard. A failed email does not mark candidates reviewed, and a failed state push must fail visibly.

If a credential is exposed, revoke it first, replace the repository secret, and then remove the leaked value from history if necessary.
