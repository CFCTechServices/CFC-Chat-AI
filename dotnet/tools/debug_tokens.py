from transformers import AutoTokenizer

t = AutoTokenizer.from_pretrained("sentence-transformers/all-MiniLM-L6-v2")
s = "Set the mixer to 72°F before adding the premix."
ids = t(s)["input_ids"]
print("Py ids:   ", " ".join(map(str, ids)))
print("Py tokens:", " ".join(t.convert_ids_to_tokens(ids)))