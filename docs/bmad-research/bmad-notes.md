# BMAD research notes (11 Nov 2026)

Sources read:
- Masterclass video transcript: https://www.youtube.com/watch?v=LorEJPrALcg ("The Official BMad-Method Masterclass")
- Docs home: https://docs.bmad-method.org/
- Install page: https://docs.bmad-method.org/start/install-bmad/
- Choose a Planning Path: https://docs.bmad-method.org/plan/choose-a-planning-path/
- GitHub README: https://github.com/bmad-code-org/BMAD-METHOD (main branch, last commit 8 Nov 2026)

---

## 1. What the video teaches (v4.33.1)

### The flow shown
1. **Install:** `npx bmad-method install` (v4.33.1). Pick "BMAD core", say yes to sharding the PRD and architecture, pick the IDE (Claude Code). Web bundles are optional.
2. **Analyst (Mary):** `/analyst`, then `*help`. Brainstorming with 20 techniques (Six Thinking Hats, Five Whys, role-play, SCAMPER and others) produces a brainstorming doc.
3. **Analyst, new chat:** create a **Project Brief** from the brainstorming output.
4. **PM (John):** create a **PRD** from the brief. It covers FRs/NFRs, MVP vs post-MVP scope, and **epics with stories**. Greenfield and brownfield are separate templates.
5. **Architect (Winston):** full-stack, backend, frontend or brownfield architecture. Produces a tech-stack table with pinned versions, data models, coding standards and a source tree.
6. **Shard:** `shard-doc` (md-tree) splits the PRD and architecture into small files by H2 section.
7. **PO (Sarah):** `run checklist` checks that stories and architecture line up. Optional for small apps.
8. **Scrum Master (Bob):** `*draft 1.1` turns an epic story into a detailed, self-contained dev story (Draft → Approved). Also has `correct-course` for mid-project changes.
9. **Dev (James):** `*develop-story` implements it task by task and sets the story to "Ready for Review". It always loads `devLoadAlwaysFiles` (coding standards, tech stack, source tree) via `core-config.yaml`.
10. **QA (Quinn):** reviews the story in a fresh context, ideally with the strongest model.

### Lessons that still apply (worth using in the Solvefy task)
1. **Start a fresh chat for every output document.** Hand over context through the saved document, not chat history. Don't rely on compaction because it "randomly forgets things".
2. **Agents are coaches, not oracles.** "Don't check your brain at the door." The analyst draws ideas out of *you*.
3. **Advanced elicitation:** after each section, push the LLM past its "average" answer. Examples: critique and refine, identify risks, red team vs blue team, "hindsight is 20/20 / if only…", stakeholder round table. "Hindsight" in particular is a good way to cut MVP scope.
4. **The PRD exists mainly to control MVP scope.** Ideas you cut still get recorded as post-MVP epics. This maps straight onto the "what you deliberately left out" section in the README.
5. **Story sequencing:** no story may depend on a later one, and story 1 is project setup. Ask the agent to "deep think" about the ordering.
6. **Pin the tech stack and versions in the architecture.** Otherwise the dev agent quietly installs a new test framework or package when it gets stuck. Coding standards and the source tree keep it on track as well.
7. **Ask the agents "why"** (Five Whys on their choices). That's how you end up owning the design, which matters because the interview will test it.
8. **Keep stale documents out of context.** Move brainstorming notes and the brief aside once the PRD and architecture exist.
9. **Use the strongest model for architecture and QA review.**
10. **Use correct-course when requirements change** instead of patching by hand.
11. **Read the stories for human-only steps**, such as getting API keys or creating accounts.

---

## 2. What is out of date (video vs. current docs)

| Topic | Video (v4.33.1) | Current (Nov 2026) |
|---|---|---|
| Release line | v4 | Two lines: **stable v6.12** (frozen apart from bug fixes) and **V7 preview alpha**, release ETA "mid-November 2026". docs.bmad-method.org now documents **V7**. |
| Install | `npx bmad-method install` with an interactive menu | v6.12: still `npx bmad-method install`. V7: `npx skills add bmad-code-org/BMAD-METHOD --skill '*'`, then ask the `bmad` skill to run `bmad setup` (needs Node 20.12+, Python 3.10+, **uv**). Claude Code plugin marketplace is also available: `/plugin marketplace add bmad-code-org/bmad-plugins`. |
| How agents are invoked | Slash commands per agent, plus `*help` / `*draft` star commands | **Skills**: `bmad-agent-analyst`, `bmad-agent-pm`, `bmad-agent-po`, `bmad-architecture`, `bmad-ux`, `bmad-build`, `bmad-retrospective`. A `bmad` hub handles help, status and setup. |
| Dev loop | SM drafts story → Dev implements → QA reviews (3 agents, 3 chats) | **`bmad-build`** plans, builds and reviews one unit per session. `bmad-build-auto` runs unattended. The Planning page doesn't list a separate Scrum Master or QA agent. |
| Stories | SM writes a story `.md` per story | **Sofia, the PO** (`bmad-agent-po`) writes the requirements and slices the epic into stories in build order in **`tickets.toml`**. A story file is optional (`refine = true`). |
| Process size | Full flow presented as the default path | **"Right-sized"**: a PRD is only needed "when more than one person must agree… or more than one epic must not diverge". A short decisions list is often enough. Trivial, one-session, epic-sized and project-sized paths all share one loop. |
| New planning tools | Brainstorming, market research | `bmad-brainstorming`, **`bmad-forge-idea`** (pressure-test an idea), **`bmad-deep-recon`** (cited research), Analyst can now write a **PRFAQ** or **BRD**. |
| Output location | `docs/` with sharded `prd/` and `architecture/` folders | `_bmad-output/<initiative>/<type>-<slug>/`, with `.memlog.md` files beside the documents. |
| Sharding | md-tree `shard-doc` was a core step | Not part of the V7 planning flow. Sofia reads input "in one pass" up to "roughly a 40-page document", so you **condense** instead. |
| Close-out | QA review | `bmad-retrospective` per epic judges the combined result against the epic requirements. |
| Web bundles / Gemini Gems | Mentioned as an alternative to the IDE | Not mentioned in the V7 docs. |

### Which source to trust (to put in DECISIONS.md)
- The task says to follow the **current version**. docs.bmad-method.org documents V7, so the docs site plus the README on `main` are the primary sources. The video counts only for principles, not commands.
- **Risk:** V7 is still alpha ("expect breaking changes between updates"). Mitigation: record the exact version from `bmad status` in the README and don't update mid-task. If V7 gets in the way, fall back to **v6.12 stable**.
- An interesting point for the interview: the docs say a **BRD** "fits only when the people who want the system and the people who build it are separate parties, such as a client and a vendor". Meridian/Solvefy is exactly that, so a brief or BRD from Mary is a defensible choice.

---

## 3. Mapping the assessment's required artifacts to V7

The task asks for: **brief, PRD, epics, stories, architecture** in `/docs`.

| Required | V7 skill | Output |
|---|---|---|
| Brief | `bmad-agent-analyst` (Mary) | `brief-<slug>.md` (optionally a BRD) |
| PRD | `bmad-agent-pm` (John) | `prd-<slug>.md` |
| Architecture | `bmad-architecture` | `architecture-<slug>.md` |
| Epics + stories | `bmad-agent-po` (Sofia) | Epic requirements + `tickets.toml` |
| Build | `bmad-build`, one session per story | code + build plan |
| Close | `bmad-retrospective` | retro notes |

Note: V7 says a PRD is optional for small work, but the assessment requires one. Do it anyway and mention the tension. That's a good DECISIONS.md entry.

At the end, copy everything from `_bmad-output/` into `/docs`.
