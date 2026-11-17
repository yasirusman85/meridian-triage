import { useEffect, useState } from 'react';
import { errorText, getContractorJob, markContractorJobAttended, type ContractorJob } from './api';

/** The token from a contractor link path such as "/c/abc123", or null when the path is not a contractor link. */
export function contractorTokenFromPath(pathname: string): string | null {
  if (!pathname.startsWith('/c/')) {
    return null;
  }
  const token = pathname.slice('/c/'.length).replace(/\/+$/, '');
  return token.length > 0 ? decodeURIComponent(token) : null;
}

const INVALID_LINK = 'This link is not valid. It may already have been used, or replaced by a newer one.';

/**
 * The contractor's page for one job (AD-11, R20): the job's reference, address, category, summary and the contractor's
 * name, and "Mark attended". It shows nothing about any other job and needs no sign-in.
 */
export function ContractorPage({ token }: { token: string }) {
  const [job, setJob] = useState<ContractorJob | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [attended, setAttended] = useState<string | null>(null);
  const [marking, setMarking] = useState(false);
  const [markError, setMarkError] = useState<string | null>(null);

  useEffect(() => {
    getContractorJob(token)
      .then(setJob)
      .catch(() => setLoadError(INVALID_LINK));
  }, [token]);

  async function onMarkAttended() {
    setMarking(true);
    setMarkError(null);
    try {
      const result = await markContractorJobAttended(token);
      setAttended(result.reference);
    } catch (e) {
      setMarkError(errorText(e) === 'Job not found' ? INVALID_LINK : errorText(e));
    } finally {
      setMarking(false);
    }
  }

  return (
    <main>
      <header className="page-header">
        <h1>Meridian job</h1>
      </header>

      {loadError && (
        <section className="panel" aria-live="polite">
          <p role="alert" className="error">
            {loadError}
          </p>
        </section>
      )}

      {job && (
        <section className="panel contractor-page" aria-labelledby="job-heading">
          <h2 id="job-heading">{job.reference}</h2>
          <dl className="facts">
            <dt>Address</dt>
            <dd>{job.address ?? 'Not resolved'}</dd>
            <dt>Work</dt>
            <dd>{job.category}</dd>
            <dt>Summary</dt>
            <dd>{job.summary ?? 'No summary recorded.'}</dd>
            <dt>Contractor</dt>
            <dd>{job.contractorName}</dd>
          </dl>

          {attended ? (
            <p role="status">Thank you. {attended} is marked attended.</p>
          ) : (
            <div className="actions">
              <button type="button" className="mark-attended" disabled={marking} onClick={() => void onMarkAttended()}>
                {marking ? 'Marking…' : 'Mark attended'}
              </button>
              {markError && (
                <span role="alert" className="error">
                  {markError}
                </span>
              )}
            </div>
          )}
        </section>
      )}
    </main>
  );
}
