- source_plan: `_bmad-output/initiative-meridian-triage/tickets/epic-triage-core/tickets/story-the-ai-prepares-a-message-through-the-model-port-plan.md`
  summary: Give JobEvent a per-job sequence number so history order is fixed when events share a timestamp; take up in story 1.7.
  evidence: Events written in one save get the same UTC timestamp, and ordering by timestamp alone leaves ModelPrepared and UrgencyRaised unordered.
  resolved: story 1.7 added JobEvent.Sequence (per job, set on append) with a backfill migration; history reads order by it.
