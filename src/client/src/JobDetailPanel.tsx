import { useCallback, useEffect, useState, type FormEvent } from 'react';
import {
  approveJob,
  CATEGORIES,
  editJob,
  errorText,
  getJob,
  listContractors,
  listProperties,
  rejectJob,
  URGENCIES,
  type Category,
  type ContractorOption,
  type ContractorRule,
  type JobDetail,
  type JobEdit,
  type PropertyOption,
  type Urgency,
} from './api';

// Stored times are UTC; the UI shows Europe/London (AD-9).
const londonTime = new Intl.DateTimeFormat('en-GB', {
  timeZone: 'Europe/London',
  dateStyle: 'medium',
  timeStyle: 'short',
});

const RULE_LABELS: Record<ContractorRule, string> = {
  Building: 'usual for building',
  Default: 'company default',
  Person: 'chosen by a person',
};

const MISSING_LABELS: Record<string, string> = {
  category: 'set a category',
  contractor: 'choose a contractor',
};

const URGENCY_ORDER: Record<Urgency, number> = { Routine: 0, Urgent: 1, Emergency: 2 };

interface Props {
  jobId: string;
  actingAs: string | null;
  actingAsName: string | null;
  onChanged: () => void;
  onClose: () => void;
}

/** One job with its explanation, messages, rules, contractor and history, and the review actions (R13–R15). */
export function JobDetailPanel({ jobId, actingAs, actingAsName, onChanged, onClose }: Props) {
  const [job, setJob] = useState<JobDetail | null>(null);
  const [contractors, setContractors] = useState<ContractorOption[]>([]);
  const [properties, setProperties] = useState<PropertyOption[]>([]);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const [category, setCategory] = useState<Category>('Unsure');
  const [urgency, setUrgency] = useState<Urgency>('Routine');
  const [urgencyReason, setUrgencyReason] = useState('');
  const [contractorId, setContractorId] = useState('');
  const [propertyId, setPropertyId] = useState('');
  const [rejectReason, setRejectReason] = useState('');

  const show = useCallback((detail: JobDetail) => {
    setJob(detail);
    setCategory(detail.category);
    setUrgency(detail.urgency);
    setUrgencyReason('');
    setContractorId(detail.contractor?.id ?? '');
    setPropertyId(detail.propertyId ?? '');
  }, []);

  useEffect(() => {
    let cancelled = false;
    setJob(null);
    setActionError(null);
    setRejectReason('');
    Promise.all([getJob(jobId), listContractors(), listProperties()])
      .then(([detail, contractorList, propertyList]) => {
        if (cancelled) {
          return;
        }
        show(detail);
        setContractors(contractorList);
        setProperties(propertyList);
        setLoadError(null);
      })
      .catch((e: unknown) => {
        if (!cancelled) {
          setLoadError(errorText(e));
        }
      });
    return () => {
      cancelled = true;
    };
  }, [jobId, show]);

  async function run(action: (actor: string) => Promise<JobDetail>) {
    if (!actingAs) {
      setActionError('Choose who you are acting as.');
      return;
    }
    setBusy(true);
    setActionError(null);
    try {
      show(await action(actingAs));
      onChanged();
    } catch (e) {
      setActionError(errorText(e));
    } finally {
      setBusy(false);
    }
  }

  if (loadError) {
    return (
      <section className="panel detail" aria-label="Job detail">
        <p role="alert" className="error">
          {loadError}
        </p>
        <button type="button" onClick={onClose}>
          Close
        </button>
      </section>
    );
  }

  if (!job) {
    return (
      <section className="panel detail" aria-label="Job detail">
        <p className="muted">Loading…</p>
      </section>
    );
  }

  const lowering = URGENCY_ORDER[urgency] < URGENCY_ORDER[job.urgency];

  function onSave(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!job) {
      return;
    }
    const edit: JobEdit = {};
    if (category !== job.category) {
      edit.category = category;
    }
    if (urgency !== job.urgency) {
      edit.urgency = urgency;
      if (urgencyReason.trim()) {
        edit.urgencyReason = urgencyReason.trim();
      }
    }
    // Leaving the contractor as it was lets a category change re-suggest the usual one.
    if (contractorId && contractorId !== (job.contractor?.id ?? '')) {
      edit.contractorId = contractorId;
    }
    if (propertyId && propertyId !== (job.propertyId ?? '')) {
      edit.propertyId = propertyId;
    }
    if (Object.keys(edit).length === 0) {
      setActionError('Nothing to change.');
      return;
    }
    void run((actor) => editJob(job.id, actor, edit));
  }

  function onReject(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!job) {
      return;
    }
    void run((actor) => rejectJob(job.id, actor, rejectReason));
  }

  const missing = job.missingForApproval.map((m) => MISSING_LABELS[m] ?? m);

  return (
    <section className="panel detail" aria-labelledby="detail-heading">
      <div className="detail-header">
        <h2 id="detail-heading">
          {job.reference} <span className={`status status-${job.status.toLowerCase()}`}>{job.status}</span>
        </h2>
        <button type="button" onClick={onClose}>
          Close
        </button>
      </div>

      <dl className="facts">
        <dt>Urgency</dt>
        <dd>
          <span className={`urgency-${job.urgency.toLowerCase()}`}>{job.urgency}</span>
        </dd>
        <dt>Category</dt>
        <dd>{job.category === 'Unsure' ? <span className="needs-person">Unsure, needs a person</span> : job.category}</dd>
        <dt>Property</dt>
        <dd>
          {job.propertyName ?? <span className="muted">unresolved</span>}
          {job.buildingName && <span className="muted"> ({job.buildingName})</span>}
        </dd>
        <dt>Contractor</dt>
        <dd>
          {job.contractor ? (
            <>
              {job.contractor.name} ({job.contractor.phone})
              {job.contractor.rule && <span className="muted"> · {RULE_LABELS[job.contractor.rule]}</span>}
            </>
          ) : (
            <span className="needs-person">needs a person</span>
          )}
        </dd>
        <dt>Rules</dt>
        <dd>
          {job.rules.length === 0 ? (
            <span className="muted">No emergency rule fired</span>
          ) : (
            job.rules.map((r) => (
              <span key={r.id} className="rule-label inline" title="Set by a fixed emergency rule, not the AI">
                rule: {r.id} (“{r.matchedPhrase}”)
              </span>
            ))
          )}
        </dd>
      </dl>

      <h3>AI preparation</h3>
      {job.summaryEn && (
        <p>
          <span className="ai-label">AI summary</span> {job.summaryEn}
        </p>
      )}
      {job.explanation ? (
        <p className="explanation">
          <span className="ai-label">Why</span> {job.explanation}
        </p>
      ) : (
        <p className="muted">Not prepared by the AI.</p>
      )}

      <h3>Messages ({job.messages.length})</h3>
      {job.messages.map((m) => (
        <div key={m.id} className="detail-message">
          <p className="muted">
            {m.sender} · {londonTime.format(new Date(m.receivedAtUtc))}
          </p>
          <blockquote>{m.text}</blockquote>
        </div>
      ))}

      {job.canReview ? (
        <>
          <h3>Review</h3>
          <div className="actions">
            <button
              type="button"
              disabled={busy || !actingAs || missing.length > 0}
              onClick={() => void run((actor) => approveJob(job.id, actor))}
            >
              Approve as {actingAsName ?? '…'}
            </button>
            {missing.length > 0 && <span className="muted">Before approving: {missing.join(' and ')}.</span>}
          </div>

          <form className="edit-form" onSubmit={onSave}>
            <label htmlFor="edit-category">Category</label>
            <select id="edit-category" value={category} onChange={(e) => setCategory(e.target.value as Category)}>
              {CATEGORIES.map((c) => (
                <option key={c} value={c}>
                  {c}
                </option>
              ))}
            </select>

            <label htmlFor="edit-urgency">Urgency</label>
            <select id="edit-urgency" value={urgency} onChange={(e) => setUrgency(e.target.value as Urgency)}>
              {URGENCIES.map((u) => (
                <option key={u} value={u}>
                  {u}
                </option>
              ))}
            </select>
            {urgency !== job.urgency && (
              <>
                <label htmlFor="edit-urgency-reason">
                  Reason for the urgency change{lowering ? ' (required to lower it)' : ' (optional)'}
                </label>
                <input
                  id="edit-urgency-reason"
                  value={urgencyReason}
                  onChange={(e) => setUrgencyReason(e.target.value)}
                  required={lowering}
                />
              </>
            )}

            <label htmlFor="edit-contractor">Contractor</label>
            <select id="edit-contractor" value={contractorId} onChange={(e) => setContractorId(e.target.value)}>
              {!job.contractor && <option value="">Needs a person: choose one</option>}
              {contractors.map((c) => (
                <option key={c.id} value={c.id}>
                  {c.name} ({c.trade})
                </option>
              ))}
            </select>

            <label htmlFor="edit-property">Property</label>
            <select id="edit-property" value={propertyId} onChange={(e) => setPropertyId(e.target.value)}>
              {!job.propertyId && <option value="">Unresolved: choose one</option>}
              {properties.map((p) => (
                <option key={p.id} value={p.id}>
                  {p.name}
                </option>
              ))}
            </select>

            <div className="actions">
              <button type="submit" disabled={busy || !actingAs}>
                Save changes
              </button>
            </div>
          </form>

          <form className="reject-form" onSubmit={onReject}>
            <label htmlFor="reject-reason">Reason for rejecting</label>
            <input id="reject-reason" value={rejectReason} onChange={(e) => setRejectReason(e.target.value)} required />
            <div className="actions">
              <button type="submit" className="danger" disabled={busy || !actingAs || !rejectReason.trim()}>
                Reject
              </button>
            </div>
          </form>
        </>
      ) : (
        <p className="muted">This job is {job.status} and can no longer be reviewed.</p>
      )}

      {actionError && (
        <p role="alert" className="error">
          {actionError}
        </p>
      )}

      <h3>History</h3>
      <ol className="history">
        {job.events.map((e, i) => (
          <li key={`${e.occurredAtUtc}-${i}`}>
            <span className="muted">{londonTime.format(new Date(e.occurredAtUtc))}</span> · <strong>{e.actor}</strong> ·{' '}
            {e.summary}
          </li>
        ))}
      </ol>
    </section>
  );
}
