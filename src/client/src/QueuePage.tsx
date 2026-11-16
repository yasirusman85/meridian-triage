import { useCallback, useEffect, useState, type FormEvent } from 'react';
import { ApiError, listJobs, submitMessage, type JobListItem, type SubmittedJob } from './api';
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

function errorText(error: unknown): string {
  if (error instanceof ApiError) {
    const fieldErrors = Object.values(error.problem.errors ?? {}).flat();
    return fieldErrors.length > 0 ? fieldErrors.join(' ') : (error.problem.detail ?? error.message);
  }
  return error instanceof Error ? error.message : 'Something went wrong.';
}

export function QueuePage() {
  const [jobs, setJobs] = useState<JobListItem[]>([]);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [sender, setSender] = useState('');
  const [text, setText] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [submitError, setSubmitError] = useState<string | null>(null);
  const [lastResult, setLastResult] = useState<string | null>(null);

  const refresh = useCallback(async () => {
    try {
      setJobs(await listJobs());
      setLoadError(null);
    } catch (error) {
      setLoadError(errorText(error));
    }
  }, []);

  useEffect(() => {
    void refresh();
  }, [refresh]);

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
      <h1>Review queue</h1>

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

      <section aria-labelledby="queue-heading">
        <h2 id="queue-heading">Jobs ({jobs.length})</h2>
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
                <th>Summary</th>
                <th>Message</th>
              </tr>
            </thead>
            <tbody>
              {jobs.map((job) => (
                <tr key={job.id}>
                  <td>{job.reference}</td>
                  <td>{formatReceived(job.receivedAtUtc)}</td>
                  <td>{job.status}</td>
                  <td>
                    <span className={`urgency-${job.urgency.toLowerCase()}`}>{job.urgency}</span>
                    {job.ruleIds.map((rule) => (
                      <span key={rule} className="rule-label" title="Set by a fixed emergency rule, not the AI">
                        rule: {rule}
                      </span>
                    ))}
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
    </main>
  );
}
