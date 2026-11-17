import { useEffect, useState } from 'react';
import { errorText, listContractors, resendJob, type ContractorOption, type JobListItem } from './api';

/** The fallback suggested for an overdue job: another contractor for its category, else any other contractor. */
function suggestedFallback(job: JobListItem, contractors: ContractorOption[]): string {
  const others = contractors.filter((c) => c.id !== job.suggestedContractorId);
  return (others.find((c) => c.trade === job.category) ?? others[0])?.id ?? '';
}

/**
 * The alert at the top of the queue for emergencies not attended within 24 hours of their first message (RULE-8).
 * A person picks a fallback contractor and re-sends; the old link stops working (R21). Nothing is chosen automatically.
 */
export function OverdueAlert({
  jobs,
  actingAs,
  actingAsName,
  onResent,
}: {
  jobs: JobListItem[];
  actingAs: string | null;
  actingAsName: string | null;
  onResent: () => void;
}) {
  const [contractors, setContractors] = useState<ContractorOption[]>([]);
  const [loadError, setLoadError] = useState<string | null>(null);

  useEffect(() => {
    if (jobs.length === 0) {
      return;
    }
    listContractors()
      .then((list) => {
        setContractors(list);
        setLoadError(null);
      })
      .catch((e: unknown) => setLoadError(errorText(e)));
  }, [jobs.length]);

  if (jobs.length === 0) {
    return null;
  }

  return (
    <section className="panel overdue-alert" aria-labelledby="overdue-heading">
      <h2 id="overdue-heading">Overdue: not attended within 24 hours</h2>
      {loadError && (
        <p role="alert" className="error">
          {loadError}
        </p>
      )}
      {jobs.map((job) => (
        <OverdueRow
          key={`${job.id}-${job.suggestedContractorId ?? ''}`}
          job={job}
          contractors={contractors}
          actingAs={actingAs}
          actingAsName={actingAsName}
          onResent={onResent}
        />
      ))}
    </section>
  );
}

function OverdueRow({
  job,
  contractors,
  actingAs,
  actingAsName,
  onResent,
}: {
  job: JobListItem;
  contractors: ContractorOption[];
  actingAs: string | null;
  actingAsName: string | null;
  onResent: () => void;
}) {
  const [chosen, setChosen] = useState<string | null>(null);
  const [sending, setSending] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const selected = chosen ?? suggestedFallback(job, contractors);
  const fallbacks = contractors.filter((c) => c.id !== job.suggestedContractorId);

  async function onSend() {
    if (!actingAs || !selected) {
      return;
    }
    setSending(true);
    setError(null);
    try {
      await resendJob(job.id, actingAs, selected);
      onResent();
    } catch (e) {
      setError(`${job.reference}: ${errorText(e)}`);
    } finally {
      setSending(false);
    }
  }

  return (
    <div className="overdue-row">
      <p>
        <strong>{job.reference}</strong> {job.propertyName ?? 'address unresolved'} · {job.category} · sent to{' '}
        {job.suggestedContractorName ?? 'a contractor'}. <span className="overdue-label">Overdue: send fallback</span>
      </p>
      <div className="actions">
        <label htmlFor={`fallback-${job.id}`}>Fallback contractor</label>
        <select
          id={`fallback-${job.id}`}
          value={selected}
          onChange={(e) => setChosen(e.target.value)}
          disabled={fallbacks.length === 0}
        >
          {fallbacks.map((c) => (
            <option key={c.id} value={c.id}>
              {c.name} ({c.trade}, {c.phone})
            </option>
          ))}
        </select>
        <button type="button" disabled={sending || !actingAs || !selected} onClick={() => void onSend()}>
          {sending ? 'Sending…' : `Send to fallback as ${actingAsName ?? '…'}`}
        </button>
        {error && (
          <span role="alert" className="error">
            {error}
          </span>
        )}
      </div>
    </div>
  );
}
