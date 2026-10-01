import sys
from transformers import AutoTokenizer

s = sys.argv[1] if len(sys.argv) > 1 else "Set the mixer to 72°F before adding the premix."

t = AutoTokenizer.from_pretrained("sentence-transformers/all-MiniLM-L6-v2")
ids = t(s)["input_ids"]
print("Py ids:   ", " ".join(map(str, ids)))
print("Py tokens:", " ".join(t.convert_ids_to_tokens(ids)))