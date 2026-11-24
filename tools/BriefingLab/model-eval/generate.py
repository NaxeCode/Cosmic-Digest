"""Run the frozen prompts once with local MLX weights. Never calls a model endpoint."""
import argparse
import hashlib
import json
import os
import platform
import time
from datetime import datetime, timezone
from importlib.metadata import version
from pathlib import Path

os.environ["HF_HUB_OFFLINE"] = "1"
os.environ["HF_HUB_DISABLE_IMPLICIT_TOKEN"] = "1"

import mlx.core as mx
from mlx_lm import load, stream_generate
from mlx_lm.sample_utils import make_sampler

MODEL = "mlx-community/Qwen2.5-1.5B-Instruct-4bit"
REVISION = "8b403126fc14f14cfc99bb4cfa72ecbc129ea677"
WEIGHTS = "0979f33d1bc58afcf696d13f57977644e7b11a6f0eec3e631d8e9463d18c0717"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("model_dir", type=Path)
    parser.add_argument("prompts", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    model_dir = args.model_dir.resolve(strict=True)
    with (model_dir / "model.safetensors").open("rb") as weights:
        if hashlib.file_digest(weights, "sha256").hexdigest() != WEIGHTS:
            raise ValueError("Weight digest differs from the pinned release.")
    if args.output.exists():
        raise FileExistsError("Choose a fresh output path to preserve the recorded run.")
    prompts = json.loads(args.prompts.read_text())
    start = time.monotonic()
    stamp = datetime.now(timezone.utc).isoformat()
    mx.random.seed(0)
    model, tokenizer = load(str(model_dir))
    load_seconds = time.monotonic() - start
    sampler = make_sampler(temp=0)
    metadata = {
        "model": MODEL, "modelRevision": REVISION, "weightSha256": WEIGHTS,
        "freezeCommit": "86494f41819498ab173edbdd73c119c51cb4eb57",
        "startUtc": stamp, "device": mx.device_info()["device_name"], "macOS": platform.mac_ver()[0],
        "python": platform.python_version(),
        "libraries": {name: version(name) for name in ["mlx", "mlx-lm", "tokenizers", "transformers"]},
        "temperature": 0, "seed": 0, "maxNewTokens": 420, "loadSeconds": round(load_seconds, 3),
    }
    outputs = []
    for trial in prompts:
        messages = [{"role": role, "content": trial[role]} for role in ("system", "user")]
        prompt = tokenizer.apply_chat_template(messages, tokenize=False, add_generation_prompt=True)
        started = time.monotonic()
        pieces = []
        for token in stream_generate(model, tokenizer, prompt, max_tokens=420, sampler=sampler):
            pieces.append(token.text)
        outputs.append({
            "packet": trial["packet"], "order": trial["order"], "promptHash": trial["promptHash"],
            "raw": "".join(pieces), "promptTokens": token.prompt_tokens, "generatedTokens": token.generation_tokens,
            "finishReason": token.finish_reason, "seconds": round(time.monotonic() - started, 3),
        })
        print(f"{len(outputs)}/{len(prompts)} {trial['packet']} {trial['order']} {token.generation_tokens} tokens", flush=True)
        # Keep interrupted evidence. The C# scorer rejects missing prompts and duplicate keys.
        metadata["wallSeconds"] = round(time.monotonic() - start, 3)
        partial = args.output.with_suffix(".partial")
        partial.write_text(json.dumps({"metadata": metadata, "outputs": outputs}, ensure_ascii=False, indent=2) + "\n")
        partial.replace(args.output)


if __name__ == "__main__":
    main()
