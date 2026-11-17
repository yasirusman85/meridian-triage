import { useEffect, useState } from 'react';
import { CONTRACTOR_TEMPLATE_IDS, errorText, listOutbound, type JobStatus, type OutboundMessage } from './api';

/** The single-job link inside a contractor message, e.g. "/c/abc_DEF-123". */
const LINK = /\/c\/[A-Za-z0-9_-]+/;

/**
 * The contractor's link for staff (R20): the latest message sent to the contractor and its link. Once the job is
 * attended the link no longer works, and the page says so.
 */
export function ContractorMessage({ jobId, status }: { jobId: string; status: JobStatus }) {
  const [message, setMessage] = useState<OutboundMessage | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loaded, setLoaded] = useState(false);

  useEffect(() => {
    let cancelled = false;
    setLoaded(false);
    listOutbound(jobId)
      .then((messages) => {
        if (!cancelled) {
          setMessage([...messages].reverse().find((m) => CONTRACTOR_TEMPLATE_IDS.includes(m.templateId)) ?? null);
          setError(null);
        }
      })
      .catch((e: unknown) => {
        if (!cancelled) {
          setError(errorText(e));
        }
      })
      .finally(() => {
        if (!cancelled) {
          setLoaded(true);
        }
      });
    return () => {
      cancelled = true;
    };
  }, [jobId, status]);

  if (!loaded && !error) {
    return null;
  }
  if (error) {
    return (
      <p role="alert" className="error">
        {error}
      </p>
    );
  }
  if (!message) {
    return null;
  }

  const link = message.body.match(LINK)?.[0] ?? null;
  return (
    <>
      <h3>Sent to contractor</h3>
      <p className="contractor-link">
        {status === 'Attended' ? (
          <span className="muted">Marked attended. The contractor's link no longer works.</span>
        ) : link ? (
          <>
            Contractor's link: <a href={link}>{link}</a>
          </>
        ) : null}
      </p>
      <blockquote>{message.body}</blockquote>
    </>
  );
}
