import { useCallback, useEffect, useState, type FormEvent } from 'react';
import { ApiError, listJobs, submitMessage, type JobListItem } from './api';

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
  const [lastReference, setLastReference] = useState<string | null>(null);

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
    setLastReference(null);
    try {
      const created = await submitMessage(sender, text);
      setLastReference(created.reference);
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
            {lastReference && <span role="status">Added as {lastReference}</span>}
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
                  <td className={`urgency-${job.urgency.toLowerCase()}`}>{job.urgency}</td>
                  <td>{job.category}</td>
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
                    ) : (
                      <span className="muted">Not prepared</span>
                    )}
                  </td>
                  <td className="preview">{job.preview}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </section>
    </main>
  );
}
