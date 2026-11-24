# Local briefing-model evaluation

The [frozen synthetic packets](packets.json) and [predeclared scoring protocol](PROTOCOL.md) precede inference in commit `86494f41819498ab173edbdd73c119c51cb4eb57`. The [recorded run](run.json) contains each untouched response, prompt digest, wall time, token counts and finish reason. The two orderings are paired observations of the same twelve packets.

From the repository root, rerun scoring or export the browser lab with .NET 10. Neither operation invokes a model.

```sh
dotnet run --project tools/BriefingLab -- score-model tools/BriefingLab/model-eval/run.json /tmp/digest-model-score.json
dotnet run --project tools/BriefingLab -- /tmp/digest-briefing-lab
```

For fresh inference on an Apple silicon Mac, install [uv](https://docs.astral.sh/uv/) and use a new working directory. The initial dependency and public weight downloads require a network connection. Inference then loads the local files with Hugging Face offline mode enabled.

```sh
RUN_DIR="$(mktemp -d)"
uv venv --python 3.12.15 "$RUN_DIR/venv"
uv pip install --python "$RUN_DIR/venv/bin/python" -r tools/BriefingLab/model-eval/requirements-macos-arm64.txt
HF_HUB_DISABLE_IMPLICIT_TOKEN=1 "$RUN_DIR/venv/bin/hf" download mlx-community/Qwen2.5-1.5B-Instruct-4bit --revision 8b403126fc14f14cfc99bb4cfa72ecbc129ea677 --local-dir "$RUN_DIR/model"
dotnet run --project tools/BriefingLab -- prepare-model "$RUN_DIR/prompts.json"
"$RUN_DIR/venv/bin/python" tools/BriefingLab/model-eval/generate.py "$RUN_DIR/model" "$RUN_DIR/prompts.json" "$RUN_DIR/output.json"
dotnet run --project tools/BriefingLab -- score-model "$RUN_DIR/output.json" "$RUN_DIR/score.json"
```

The generator verifies the 868,628,559-byte checkpoint's SHA-256 before loading it. The C# scorer recreates the ordered prompts and checks their hashes, including candidate indices. All reports pass through the real article selector. Reversal happens after ranking. The local runner presents the production schema as text, while the hosted product requests a strict schema from its provider.

The rubric checks source identity and explicit facts in `bottom_line` and `what_changed`. It does not score the full recommendations, every paraphrase or all possible contradictions. The scope patterns were frozen before the run. An additional diagnostic counts exact individual excerpts even in a briefing rejected for returning too many items. That item-level count was added after inspecting the first completion and does not replace the predeclared whole-document results. Agreement compares selected source IDs without regard to their sequence and excludes parse errors or out-of-bounds references.


## Observed result

The 24 completions emitted 3,886 tokens in 146.471 seconds including 1.558 seconds of model load. Of the 24 trials, 23 parsed, 17 passed structural checks and 13 passed the production quote gate. All 13 nonempty accepted outputs contained verified quotes. Six of 24 passed both the guard and frozen editorial rubric. Seven accepted outputs missed that rubric. The model saw the briefing prompt and candidate reports. Packet questions were withheld, so these scores assess editorial selection and scope.

Only one of 12 pairs selected the same source IDs regardless of presentation order. Both outputs in that pair exceeded the one-item cap. A retrospective gate requiring agreement plus two guard passes would admit 0/12 and refer 12/12. There were zero admitted pairs meeting the rubric both ways. Precision for that gate is undefined. Keep this small model and prompt in human review or use the deterministic fallback for these correction-sensitive packets.
