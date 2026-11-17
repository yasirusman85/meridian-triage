import { useEffect, useState } from 'react';
import {
  errorText,
  getMetrics,
  type DurationMeasure,
  type MetricCount,
  type Metrics,
  type MetricsWindow,
  type PromiseMeasure,
} from './api';

const NO_DATA = 'no data yet';

// Labels for the enum names the numbers come with; anything unlisted shows its name.
const LABELS: Record<string, string> = {
  LocksAndDoors: 'Locks and doors',
  SentToContractor: 'Sent to contractor',
};

function label(name: string): string {
  return LABELS[name] ?? name;
}

/** Minutes as hours and minutes, e.g. "2 h 5 min" or "45 min". */
function formatMinutes(minutes: number): string {
  const total = Math.round(minutes);
  const hours = Math.floor(total / 60);
  const mins = total % 60;
  return hours > 0 ? `${hours} h ${mins} min` : `${mins} min`;
}

function durationText(measure: DurationMeasure): string {
  if (measure.jobs === 0 || measure.medianMinutes === null || measure.percentile90Minutes === null) {
    return NO_DATA;
  }
  return `median ${formatMinutes(measure.medianMinutes)}, 90th percentile ${formatMinutes(measure.percentile90Minutes)}`;
}

function promiseText(measure: PromiseMeasure): string {
  if (measure.eligible === 0) {
    return NO_DATA;
  }
  const percent = Math.round((measure.met / measure.eligible) * 100);
  return `${measure.met} of ${measure.eligible} met (${percent}%)`;
}

function CountTable({ rows, jobCount, caption }: { rows: MetricCount[]; jobCount: number; caption: string }) {
  if (jobCount === 0) {
    return <p className="no-data">{NO_DATA}</p>;
  }
  return (
    <table>
      <caption className="visually-hidden">{caption}</caption>
      <tbody>
        {rows.map((row) => (
          <tr key={row.name}>
            <td>{label(row.name)}</td>
            <td className="numeric">{row.count}</td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}

function MeasureRow({ name, value, empty }: { name: string; value: string; empty: boolean }) {
  return (
    <tr>
      <th scope="row">{name}</th>
      <td className={empty ? 'no-data' : undefined}>{value}</td>
    </tr>
  );
}

function WindowColumn({ title, window }: { title: string; window: MetricsWindow }) {
  return (
    <section className="panel numbers-column" aria-label={title}>
      <h2>{title}</h2>
      <p>{window.jobCount === 1 ? '1 job' : `${window.jobCount} jobs`}</p>

      <h3>Jobs by category</h3>
      <CountTable rows={window.byCategory} jobCount={window.jobCount} caption="Jobs by category" />

      <h3>Jobs by urgency</h3>
      <CountTable rows={window.byUrgency} jobCount={window.jobCount} caption="Jobs by urgency" />

      <h3>Open jobs by status</h3>
      <CountTable rows={window.openByStatus} jobCount={window.jobCount} caption="Open jobs by status" />

      <h3>Time</h3>
      <table>
        <tbody>
          <MeasureRow
            name="To first human look"
            value={durationText(window.firstHumanLook)}
            empty={window.firstHumanLook.jobs === 0}
          />
          <MeasureRow
            name="To approval"
            value={durationText(window.timeToApproval)}
            empty={window.timeToApproval.jobs === 0}
          />
          <MeasureRow
            name="To attendance"
            value={durationText(window.timeToAttendance)}
            empty={window.timeToAttendance.jobs === 0}
          />
        </tbody>
      </table>

      <h3>Promises</h3>
      <table>
        <tbody>
          <MeasureRow
            name="Emergency attended within 24 hours"
            value={promiseText(window.emergency24Hours)}
            empty={window.emergency24Hours.eligible === 0}
          />
          <MeasureRow
            name="Other jobs attended within 5 working days"
            value={promiseText(window.nonEmergency5WorkingDays)}
            empty={window.nonEmergency5WorkingDays.eligible === 0}
          />
        </tbody>
      </table>
    </section>
  );
}

export function NumbersPage() {
  const [metrics, setMetrics] = useState<Metrics | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    getMetrics()
      .then((result) => {
        if (!cancelled) {
          setMetrics(result);
        }
      })
      .catch((err: unknown) => {
        if (!cancelled) {
          setError(errorText(err));
        }
      });
    return () => {
      cancelled = true;
    };
  }, []);

  return (
    <main>
      <header className="page-header">
        <h1>Numbers</h1>
        <a href="/">Review queue</a>
      </header>

      {error && (
        <p role="alert" className="error">
          {error}
        </p>
      )}
      {!metrics && !error && <p>Loading…</p>}

      {metrics && (
        <div className="numbers-columns">
          <WindowColumn title="Last 30 days" window={metrics.last30Days} />
          <WindowColumn title="All time" window={metrics.allTime} />
        </div>
      )}
    </main>
  );
}
