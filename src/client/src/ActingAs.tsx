import { useEffect, useState } from 'react';
import { errorText, listStaff, type StaffMember } from './api';

const STORAGE_KEY = 'meridian.actingAs';

function readStored(): string | null {
  try {
    return window.localStorage.getItem(STORAGE_KEY);
  } catch {
    return null;
  }
}

function writeStored(id: string) {
  try {
    window.localStorage.setItem(STORAGE_KEY, id);
  } catch {
    // Storage blocked (private window): the choice lasts for this page only.
  }
}

/**
 * The person review actions are recorded as (no login in the first version). Remembered per viewer in this
 * browser; defaults to the first person on the staff list.
 */
export function useActingAs(): { staff: StaffMember[]; actingAs: string | null; setActingAs: (id: string) => void; error: string | null } {
  const [staff, setStaff] = useState<StaffMember[]>([]);
  const [actingAs, setActingAsState] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    listStaff()
      .then((members) => {
        if (cancelled) {
          return;
        }
        setStaff(members);
        const stored = readStored();
        setActingAsState(members.some((m) => m.id === stored) ? stored : (members[0]?.id ?? null));
      })
      .catch((e: unknown) => {
        if (!cancelled) {
          setError(errorText(e));
        }
      });
    return () => {
      cancelled = true;
    };
  }, []);

  function setActingAs(id: string) {
    setActingAsState(id);
    writeStored(id);
  }

  return { staff, actingAs, setActingAs, error };
}

export function ActingAsSwitch({
  staff,
  actingAs,
  onChange,
  error,
}: {
  staff: StaffMember[];
  actingAs: string | null;
  onChange: (id: string) => void;
  error: string | null;
}) {
  return (
    <div className="acting-as">
      <label htmlFor="acting-as">Acting as</label>
      <select id="acting-as" value={actingAs ?? ''} onChange={(e) => onChange(e.target.value)} disabled={staff.length === 0}>
        {staff.map((s) => (
          <option key={s.id} value={s.id}>
            {s.name}
          </option>
        ))}
      </select>
      {error && (
        <span role="alert" className="error">
          {error}
        </span>
      )}
    </div>
  );
}
