# Correction review experiment

Freeze this policy, eight new synthetic packets, the expected actions and the original model configuration before generating any responses. Use both presentation orders, the same one-item production prompt, quote guard, lexical rubric and pinned local model as the first study. Do not tune on the new outputs.

The challenger refers a guard-accepted answer for review when an item cites an older report and a strictly later candidate mentions the same bulletin identifier or version number along with a cancellation, hold, suspension, withdrawal, recall or postponement. Empty selections follow the existing guard. The challenger has no access to packet IDs, source IDs, expected decisions or rubric patterns. It deliberately does not understand negation, resolve implicit entities or identify all types of corrections.

For each of the shipping quote guard, the challenger and unconditional referral, count accepted outputs that fail the rubric, accepted outputs that pass, and rubric-passing outputs referred. Report baseline rejections separately from additional challenger referrals. A rubric miss is a proxy for a bad recommendation; inspect the actual response before interpreting it as harmful. Orders share a packet, and the fixtures have a single author. Capture actual local generation durations, raw token counts and hardware. No provider billing occurs. Keep the challenger in the lab pending evidence of broad value.

## Observed run

Six of the sixteen raw outputs met the frozen rubric. The existing guard accepted five of those six plus seven misses. The challenger accepted the same five passes plus three misses. Referring every response would retain zero passes and send all six passing responses to review. Looking at individual referrals matters. Three removed stale recommendations. The fourth, “no hold,” affirmed an existing approval and failed only the requirement to select the newer source. A stale suspension in the reopening packet still got through. These results support retaining the check as a research comparator.

Regenerate both recorded experiments' reports offline with the ordinary lab export. To repeat inference for these prompts, use the model and environment in the adjacent README and a fresh output filename.

```sh
dotnet run --project tools/BriefingLab -- prepare-corrections prompts.json
python tools/BriefingLab/model-eval/generate.py /path/to/pinned/model prompts.json new-run.json --freeze-commit 857c7fe28db5af81cbaf76b73cef7df820e656e5
dotnet run --project tools/BriefingLab -- score-corrections new-run.json scored.json
```
