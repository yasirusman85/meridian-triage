import type { StaffNotification } from './api';

const TEMPLATE_LABELS: Record<string, string> = {
  'escalation-primary': 'Emergency alert',
  'escalation-backup': 'Backup alert',
};

/** Staff notifications (simulated SMS), newest first. The parent loads and refreshes them. */
export function NotificationsPanel({
  notifications,
  error,
  formatTime,
}: {
  notifications: StaffNotification[];
  error: string | null;
  formatTime: (utc: string) => string;
}) {
  return (
    <section aria-labelledby="notifications-heading" className="panel notifications">
      <h2 id="notifications-heading">Staff notifications ({notifications.length})</h2>
      <p className="muted">Simulated text messages: nothing is really sent.</p>
      {error && (
        <p role="alert" className="error">
          {error}
        </p>
      )}
      {notifications.length === 0 && !error ? (
        <p className="muted">No notifications yet.</p>
      ) : (
        <ul className="notification-list">
          {notifications.map((n) => (
            <li key={n.id} className="notification">
              <p className="outbound-meta">
                <span className="template-label">{TEMPLATE_LABELS[n.templateId] ?? n.templateId}</span>
                {n.jobReference && <> for {n.jobReference}</>} to {n.recipient} by {n.channel}{' '}
                <span className="muted">· {formatTime(n.createdAtUtc)}</span>
              </p>
              <blockquote>{n.body}</blockquote>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
