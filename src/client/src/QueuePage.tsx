import { useCallback, useEffect, useState, type FormEvent } from 'react';
import {
  acknowledgeEscalation,
  errorText,
  listJobs,
  listNotifications,
  submitMessage,
  type ContractorRule,
  type EscalationState,
  type JobListItem,
  type StaffNotification,
  type SubmittedJob,
} from './api';
import { ActingAsSwitch, useActingAs } from './ActingAs';
import { JobDetailPanel } from './JobDetailPanel';
import { NotificationsPanel } from './NotificationsPanel';
import { OverdueAlert } from './OverdueAlert';
import { SentToTenant } from './SentToTenant';

// Stored times are UTC; the UI shows Europe/London (AD-9).
const londonTime = new Intl.DateTimeFormat('en-GB', {
  timeZone: 'Europe/London',
  dateStyle: 'medium',
  timeStyle: 'short',
});

function formatReceived(utc: string): string {
  return londonTime.format(new Date(utc));
}

const EXPLANATION_LENGTH = 140;

function shorten(value: string, max: number): string {
  const chars = Array.from(value);
  return chars.length <= max ? value : `${chars.slice(0, max - 1).join('').trimEnd()}…`;
}

function describeSubmitted(jobs: SubmittedJob[]): string {
  const created = jobs.filter((j) => j.outcome === 'created').map((j) => j.reference);
  const followUps = jobs.filter((j) => j.outcome === 'followUp').map((j) => j.reference);
  const parts: string[] = [];
  if (created.length > 0) {
    parts.push(`Added as ${created.join(', ')}`);
  }
  if (followUps.length > 0) {
    parts.push(`Attached as a follow-up to ${followUps.join(', ')}`);
  }
  return parts.join('; ');
}

const UNKNOWN_BUILDING = 'Building unknown';

const RULE_LABELS: Record<ContractorRule, { label: string; title: string }> = {
  Building: { label: 'usual for building', title: 'The usual contractor for this building and category' },
  Default: { label: 'company default', title: 'The company-wide default for this category' },
  Person: { label: 'chosen by a person', title: 'Chosen by a person in review' },
};

// The escalation worker ticks every minute; the queue and notifications refresh a little more often.
const REFRESH_MS = 20_000;

const ESCALATION_LABELS: Record<Exclude<EscalationState, 'None'>, string> = {
  NotifiedRachel: 'Rachel notified',
  NotifiedBackup: 'Backup notified',
  Acknowledged: 'Acknowledged',
  Failed: 'Escalation failed',
};

function EscalationBadge({ job, staffName }: { job: JobListItem; staffName: (id: string | null) => string }) {
  if (job.escalationState === 'None') {
    return job.urgency === 'Emergency' ? <span className="escalation-badge escalation-pending">Notifying…</span> : null;
  }
  const label =
    job.escalationState === 'Acknowledged'
      ? `Acknowledged by ${staffName(job.escalationAcknowledgedBy)}`
      : ESCALATION_LABELS[job.escalationState];
  return <span className={`escalation-badge escalation-${job.escalationState.toLowerCase()}`}>{label}</span>;
}

export function QueuePage() {
  const [jobs, setJobs] = useState<JobListItem[]>([]);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [sender, setSender] = useState('');
  const [text, setText] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [submitError, setSubmitError] = useState<string | null>(null);
  const [lastResult, setLastResult] = useState<string | null>(null);
  const [selectedJobId, setSelectedJobId] = useState<string | null>(null);
  const { staff, actingAs, setActingAs, error: staffError } = useActingAs();
  const actingAsName = staff.find((s) => s.id === actingAs)?.name ?? null;
  const staffName = (id: string | null) => staff.find((m) => m.id === id)?.name ?? id ?? 'someone';
  const [notifications, setNotifications] = useState<StaffNotification[]>([]);
  const [notificationsError, setNotificationsError] = useState<string | null>(null);
  const [acknowledging, setAcknowledging] = useState<string | null>(null);
  const [ackError, setAckError] = useState<string | null>(null);

  const refresh = useCallback(async () => {
    try {
      setJobs(await listJobs());
      setLoadError(null);
    } catch (error) {
      setLoadError(errorText(error));
    }
    try {
      setNotifications(await listNotifications());
      setNotificationsError(null);
    } catch (error) {
      setNotificationsError(errorText(error));
    }
  }, []);

  useEffect(() => {
    void refresh();
    const timer = window.setInterval(() => void refresh(), REFRESH_MS);
    return () => window.clearInterval(timer);
  }, [refresh]);

  async function onAcknowledge(job: JobListItem) {
    if (!actingAs) {
      return;
    }
    setAcknowledging(job.id);
    setAckError(null);
    try {
      await acknowledgeEscalation(job.id, actingAs);
    } catch (error) {
      setAckError(`${job.reference}: ${errorText(error)}`);
    } finally {
      setAcknowledging(null);
      await refresh();
    }
  }

  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setSubmitting(true);
    setSubmitError(null);
    setLastResult(null);
    try {
      const created = await submitMessage(sender, text);
      setLastResult(describeSubmitted(created.jobs));
      setSender('');
      setText('');
      await refresh();
    } catch (error) {
      setSubmitError(errorText(error));
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <main>
      <header className="page-header">
        <h1>Review queue</h1>
        <a href="/numbers">Numbers</a>
        <ActingAsSwitch staff={staff} actingAs={actingAs} onChange={setActingAs} error={staffError} />
      </header>

      {selectedJobId && (
        <JobDetailPanel
          jobId={selectedJobId}
          actingAs={actingAs}
          actingAsName={actingAsName}
          onChanged={() => void refresh()}
          onClose={() => setSelectedJobId(null)}
        />
      )}

      <section aria-labelledby="paste-heading" className="panel">
        <h2 id="paste-heading">Paste a message</h2>
        <form onSubmit={onSubmit}>
          <label htmlFor="sender">Sender</label>
          <input
            id="sender"
            name="sender"
            value={sender}
            onChange={(e) => setSender(e.target.value)}
            autoComplete="off"
          />
          <label htmlFor="text">Message</label>
          <textarea id="text" name="text" rows={6} value={text} onChange={(e) => setText(e.target.value)} />
          <div className="actions">
            <button type="submit" disabled={submitting}>
              {submitting ? 'Adding…' : 'Add to queue'}
            </button>
            {lastResult && <span role="status">{lastResult}</span>}
            {submitError && (
              <span role="alert" className="error">
                {submitError}
              </span>
            )}
          </div>
        </form>
      </section>

      <OverdueAlert
        jobs={jobs.filter((j) => j.attendanceOverdue)}
        actingAs={actingAs}
        actingAsName={actingAsName}
        onResent={() => void refresh()}
      />

      <section aria-labelledby="queue-heading">
        <h2 id="queue-heading">Jobs ({jobs.length})</h2>
        {ackError && (
          <p role="alert" className="error">
            {ackError}
          </p>
        )}
        {loadError && (
          <p role="alert" className="error">
            {loadError}
          </p>
        )}
        {jobs.length === 0 && !loadError ? (
          <p>No jobs yet.</p>
        ) : (
          <table>
            <thead>
              <tr>
                <th>Reference</th>
                <th>Received</th>
                <th>Status</th>
                <th>Urgency</th>
                <th>Category</th>
                <th>Property</th>
                <th>Contractor</th>
                <th>Summary</th>
                <th>Message</th>
              </tr>
            </thead>
            <tbody>
              {jobs.map((job) => (
                <tr
                  key={job.id}
                  className={[
                    job.id === selectedJobId ? 'selected' : '',
                    job.escalationState === 'Failed' ? 'escalation-failed-row' : '',
                    job.attendanceOverdue ? 'attendance-overdue-row' : '',
                  ].filter(Boolean).join(' ') || undefined}
                >
                  <td>
                    <button type="button" className="link" onClick={() => setSelectedJobId(job.id)}>
                      {job.reference}
                    </button>
                  </td>
                  <td>{formatReceived(job.receivedAtUtc)}</td>
                  <td>
                    {job.status}
                    {job.attendanceOverdue && <span className="overdue-label">Overdue: send fallback</span>}
                  </td>
                  <td>
                    <span className={`urgency-${job.urgency.toLowerCase()}`}>{job.urgency}</span>
                    {job.ruleIds.map((rule) => (
                      <span key={rule} className="rule-label" title="Set by a fixed emergency rule, not the AI">
                        rule: {rule}
                      </span>
                    ))}
                    <EscalationBadge job={job} staffName={staffName} />
                    {job.urgency === 'Emergency' && job.escalationState !== 'Acknowledged' && (
                      <button
                        type="button"
                        className="acknowledge"
                        disabled={acknowledging === job.id || !actingAs}
                        onClick={() => void onAcknowledge(job)}
                      >
                        {acknowledging === job.id ? 'Acknowledging…' : `Acknowledge as ${actingAsName ?? '…'}`}
                      </button>
                    )}
                  </td>
                  <td>{job.category}</td>
                  <td>
                    {job.propertyName ?? <span className="muted">unresolved</span>}
                    {job.followUpCount > 0 && (
                      <span className="follow-up-label">
                        {job.followUpCount} follow-up{job.followUpCount === 1 ? '' : 's'}
                      </span>
                    )}
                  </td>
                  <td>
                    {job.suggestedContractorName ? (
                      <>
                        {job.buildingName ?? UNKNOWN_BUILDING} · {job.suggestedContractorName} ({job.suggestedContractorPhone})
                        {job.suggestedContractorRule && (
                          <span className="muted contractor-rule" title={RULE_LABELS[job.suggestedContractorRule].title}>
                            {RULE_LABELS[job.suggestedContractorRule].label}
                          </span>
                        )}
                      </>
                    ) : (
                      <>
                        {job.buildingName ?? UNKNOWN_BUILDING} · <span className="needs-person">needs a person</span>
                      </>
                    )}
                  </td>
                  <td className="summary">
                    {job.summaryEn ? (
                      <>
                        <span className="ai-label">AI summary</span> {job.summaryEn}
                        {job.explanation && (
                          <p className="explanation" title={job.explanation}>
                            <span className="ai-label">Why</span> {shorten(job.explanation, EXPLANATION_LENGTH)}
                          </p>
                        )}
                      </>
                    ) : job.explanation ? (
                      <span className="explanation">{job.explanation}</span>
                    ) : (
                      <span className="muted">Not prepared</span>
                    )}
                  </td>
                  <td className="preview">
                    {job.preview}
                    <SentToTenant jobId={job.id} />
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </section>

      <NotificationsPanel notifications={notifications} error={notificationsError} formatTime={formatReceived} />
    </main>
  );
}
