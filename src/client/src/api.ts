export type JobStatus = 'Prepared' | 'Approved' | 'SentToContractor' | 'Attended' | 'Rejected';
export type Urgency = 'Routine' | 'Urgent' | 'Emergency';
export type Category =
  | 'Unsure'
  | 'Plumbing'
  | 'Electrical'
  | 'Heating'
  | 'Appliances'
  | 'LocksAndDoors'
  | 'Pest'
  | 'General';

export interface JobListItem {
  id: string;
  reference: string;
  receivedAtUtc: string;
  status: JobStatus;
  urgency: Urgency;
  category: Category;
  sender: string;
  preview: string;
  /** Why the AI prepared the job this way; null when it was not prepared. */
  explanation: string | null;
  /** English summary from the AI; null when it was not prepared. */
  summaryEn: string | null;
  /** Ids of the emergency rules that fired on the job's messages, e.g. "gas"; empty when none fired. */
  ruleIds: string[];
  /** The resolved property; null when the address was missing, unknown or ambiguous. */
  propertyId: string | null;
  /** How the resolved property is shown, e.g. "Flat 4, 22 Pemberton Road"; null when unresolved. */
  propertyName: string | null;
  /** Messages attached after the first one. */
  followUpCount: number;
  /** The building of the resolved property, e.g. "Alder Court"; null when unresolved. */
  buildingName: string | null;
  /** The contractor fixed code suggests; null means a person must choose ("needs a person"). */
  suggestedContractorId: string | null;
  suggestedContractorName: string | null;
  suggestedContractorPhone: string | null;
  /** "Building": the building's usual contractor; "Default": the company-wide default for the category. */
  suggestedContractorRule: 'Building' | 'Default' | null;
}

export interface SubmittedJob {
  jobId: string;
  reference: string;
  /** "created" for a new job, "followUp" when the message was attached to an existing open job. */
  outcome: 'created' | 'followUp';
}

/** A message written to the outbox for a job; sending is simulated in the first version. */
export interface OutboundMessage {
  id: string;
  jobId: string;
  channel: string;
  recipient: string;
  /** The fixed template the body came from, e.g. "acknowledgement" or "gas-safety". */
  templateId: string;
  body: string;
  status: 'Simulated';
  createdAtUtc: string;
}

export interface SubmitMessageResponse {
  messageId: string;
  /** Every job the message made or was attached to. */
  jobs: SubmittedJob[];
}

export interface ProblemDetails {
  title?: string;
  detail?: string;
  status?: number;
  errors?: Record<string, string[]>;
}

export class ApiError extends Error {
  readonly problem: ProblemDetails;

  constructor(problem: ProblemDetails) {
    super(problem.title ?? 'Request failed');
    this.problem = problem;
  }
}

async function readProblem(response: Response): Promise<ProblemDetails> {
  try {
    return (await response.json()) as ProblemDetails;
  } catch {
    return { title: `Request failed (${response.status})`, status: response.status };
  }
}

export async function listJobs(): Promise<JobListItem[]> {
  const response = await fetch('/api/jobs');
  if (!response.ok) {
    throw new ApiError(await readProblem(response));
  }
  return (await response.json()) as JobListItem[];
}

export async function listOutbound(jobId: string): Promise<OutboundMessage[]> {
  const response = await fetch(`/api/jobs/${encodeURIComponent(jobId)}/outbound`);
  if (!response.ok) {
    throw new ApiError(await readProblem(response));
  }
  return (await response.json()) as OutboundMessage[];
}

export async function submitMessage(sender: string, text: string): Promise<SubmitMessageResponse> {
  const response = await fetch('/api/messages', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ sender, text }),
  });
  if (!response.ok) {
    throw new ApiError(await readProblem(response));
  }
  return (await response.json()) as SubmitMessageResponse;
}
