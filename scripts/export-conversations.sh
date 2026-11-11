#!/usr/bin/env bash
# Exports every Claude Code session transcript for this project into /conversations.
# - <id>.jsonl : the raw transcript, unedited except for redacting personal email addresses
# - <id>.md    : a readable rendering of the same transcript
# Run near the end of each working session (the cloud container is ephemeral).
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$ROOT/conversations/claude-code"
mkdir -p "$OUT"

# Personal addresses to redact; add more with REDACT="a@b.com c@d.com".
REDACT="${REDACT:-uyasir85@gmail.com}"

redact() {
  local f="$1"
  for e in $REDACT; do sed -i "s/${e//./\\.}/[redacted-email]/g" "$f"; done
}

for f in "$HOME"/.claude/projects/*/*.jsonl; do
  [ -e "$f" ] || continue
  name="$(basename "$f" .jsonl)"
  cp "$f" "$OUT/$name.jsonl"
  redact "$OUT/$name.jsonl"
  jq -r '
    select(.type=="user" or .type=="assistant") |
    "\n### " + (.type|ascii_upcase) + "  (" + (.timestamp // "") + ")\n" +
    ( .message.content |
      if type=="string" then .
      else map(
        if .type=="text" then .text
        elif .type=="tool_use" then "```tool_use " + .name + "\n" + (.input|tostring) + "\n```"
        elif .type=="tool_result" then "```tool_result\n" + ((.content|tostring)[0:4000]) + "\n```"
        else "" end) | join("\n")
      end )
  ' "$OUT/$name.jsonl" > "$OUT/$name.md"
  echo "exported $name ($(wc -c < "$OUT/$name.jsonl") bytes)"
done
