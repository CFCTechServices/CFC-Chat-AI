import json
from pathlib import Path
from sentence_transformers import SentenceTransformer

texts = [
    "How do I add a new feed ingredient?",
    "The ration report shows the wrong dry matter percentage.",
    "Where can I change the default nutrient requirements for a formula?",
    "Reset password",
    "Can I export a batch sheet to Excel?",
    # Edge cases: accents, symbols, punctuation, whitespace, empty, too long
    "Set the mixer to 72°F before adding the premix.",
    "Café-style résumé for naïve users",
    "Formula ID FX-2031/B (v2.4) — updated 2024–05–01",
    "What's the difference between the “batch” and ‘lot’ fields?",
    "   Leading and trailing whitespace   ",
    "",
    "ration " * 400,
    "Mix 5 kg ± 0.5 µg², then log it 😀 飼料配合",
]

model = SentenceTransformer("sentence-transformers/all-MiniLM-L6-v2")
embeddings = model.encode(texts)

out_path = Path(__file__).parent.parent / "tests" / "CFC.ChatAI.Rag.Tests" / "TestData" / "reference_embeddings.json"
out_path.parent.mkdir(parents=True, exist_ok=True)
out_path.write_text(json.dumps(
    [{"text": t, "embedding": e.tolist()} for t, e in zip(texts, embeddings)],
    indent=1,
))

print(f"Wrote {len(texts)} embeddings ({len(embeddings[0])} dims) to {out_path}")
print(texts[0], [round(float(x), 6) for x in embeddings[0][:5]])