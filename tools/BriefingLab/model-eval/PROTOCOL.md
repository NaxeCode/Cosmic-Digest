# Frozen local model protocol

Run one public four-bit conversion of Qwen2.5-1.5B-Instruct at revision `8b403126fc14f14cfc99bb4cfa72ecbc129ea677` with MLX-LM 0.32.2 on Apple silicon. There are twelve independently written synthetic packets with two reports per packet. Each is evaluated in the production selector's ranked order and in reversed presentation order. The validator receives exactly the candidate sequence presented on that trial. Both presentations contain identical source text. Packet questions, source IDs and gold labels are for readers and scoring and are withheld from inference.

Use the product's briefing system prompt and candidate serialization with a one-item limit. Supply the product JSON schema as text because this local runner has no hosted strict-schema decoder. Request one JSON object without Markdown. Use greedy decoding (temperature 0), seed 0 and a maximum of 420 newly generated tokens. No retries, repair, fence stripping or second-chance parsing. Save every raw response before scoring. Include token counts, finish reason and elapsed wall time. Hardware and framework versions belong to the observed run.

**Predeclared measures** (denominator 24 output trials unless stated otherwise):

1. Parse the untouched model response and run the production structural checks. Count successful trials.
2. Run the production quote check, including the same 12–240-character excerpt rule. Empty selections may pass the guard without a quote. Count empty and nonempty separately.
3. Apply the gold rubric independently. Require exactly the expected representative source ID (or zero items for abstention). For nonempty outputs require every case-insensitive `requires` pattern in the combined bottom line and selected `what_changed`, and no `forbids` pattern there. The copied evidence quote cannot satisfy a prose criterion by itself. Count guard-plus-rubric success and report guard passes that miss the rubric. This lexical scope check covers only the listed facts and wording. It is not a general entailment judge.
4. Compare selected source IDs between the two orderings of each packet (12 pairs). Two parse failures do not count as agreement. Retain failures, differences and inconclusive cases.

No production endpoint, feed or delivery is part of the experiment. This small adversarial corpus was written for this evaluation and is not a random or held-out production sample.
