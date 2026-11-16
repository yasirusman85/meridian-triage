# Recorded responses for the stub model

These files are **hand-written**, not captured from a real model. Each one is the answer we expect a model
to give under contract `triage.v1` for one of the six messages Dawn forwarded (`docs/input/client-input.md`,
section 2.2).

- `text` is the message exactly as a tenant sent it. `StubTriageModel` matches a request by the SHA-256 of
  the whitespace-trimmed text, so it must stay byte-for-byte identical to the message.
- `response` is returned to the pipeline unvalidated and goes through the same `TriageValidator` as a real
  model's output would. A string `response` is returned as is, for recording malformed output.

Any other message gets the failure `no-recorded-response`, and its job stays Unsure.
