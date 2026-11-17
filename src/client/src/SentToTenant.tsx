import { useState, type SyntheticEvent } from 'react';
import { listOutbound, type OutboundMessage } from './api';

const TEMPLATE_LABELS: Record<string, string> = {
  acknowledgement: 'Acknowledgement',
  'gas-safety': 'Gas safety message',
};

/** "Sent to tenant" expander for one job: loads the job's outbound messages the first time it is opened. */
export function SentToTenant({ jobId }: { jobId: string }) {
  const [messages, setMessages] = useState<OutboundMessage[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  async function onToggle(event: SyntheticEvent<HTMLDetailsElement>) {
    if (!event.currentTarget.open || messages !== null) {
      return;
    }
    try {
      // Staff notifications and contractor messages are filed under the job too; only the tenant's are listed here.
      setMessages((await listOutbound(jobId)).filter((m) => m.audience === 'tenant'));
      setError(null);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not load messages.');
    }
  }

  return (
    <details className="outbound" onToggle={onToggle}>
      <summary>Sent to tenant</summary>
      {error && (
        <p role="alert" className="error">
          {error}
        </p>
      )}
      {messages === null && !error && <p className="muted">Loading…</p>}
      {messages !== null && messages.length === 0 && <p className="muted">Nothing sent.</p>}
      {messages?.map((m) => (
        <div key={m.id} className="outbound-message">
          <p className="outbound-meta">
            <span className="template-label">{TEMPLATE_LABELS[m.templateId] ?? m.templateId}</span> to {m.recipient} by{' '}
            {m.channel} <span className="muted">({m.status.toLowerCase()}, not really sent)</span>
          </p>
          <blockquote>{m.body}</blockquote>
        </div>
      ))}
    </details>
  );
}
