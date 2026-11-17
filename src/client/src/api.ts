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

export type ContractorRule = 'Building' | 'Default' | 'Person';

export const CATEGORIES: Category[] = [
  'Unsure',
  'Plumbing',
  'Electrical',
  'Heating',
  'Appliances',
  'LocksAndDoors',
  'Pest',
  'General',
];

export const URGENCIES: Urgency[] = ['Routine', 'Urgent', 'Emergency'];

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
  /**
   * "Building": the building's usual contractor; "Default": the company-wide default for the category;
   * "Person": chosen by a person in review.
   */
  suggestedContractorRule: ContractorRule | null;
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
  /** On a 409 from approve: what the job still needs ("category", "contractor"). */
  missing?: string[];
}

/** The text to show for a failed request: field errors, else the problem's detail, else its title. */
export function errorText(error: unknown): string {
  if (error instanceof ApiError) {
    const fieldErrors = Object.values(error.problem.errors ?? {}).flat();
    return fieldErrors.length > 0 ? fieldErrors.join(' ') : (error.problem.detail ?? error.message);
  }
  return error instanceof Error ? error.message : 'Something went wrong.';
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

/** A person the "acting as" switch offers; there is no login in the first version. */
export interface StaffMember {
  id: string;
  name: string;
}

export interface ContractorOption {
  id: string;
  name: string;
  trade: Category;
  phone: string;
}

export interface PropertyOption {
  id: string;
  name: string;
  buildingId: string;
  buildingName: string | null;
}

export interface JobDetail {
  id: string;
  reference: string;
  status: JobStatus;
  urgency: Urgency;
  category: Category;
  receivedAtUtc: string;
  explanation: string | null;
  summaryEn: string | null;
  language: string | null;
  propertyId: string | null;
  propertyName: string | null;
  buildingName: string | null;
  /** The job's contractor; null means a person must choose. */
  contractor: { id: string; name: string; phone: string; trade: Category; rule: ContractorRule | null } | null;
  /** Emergency rules that fired, with the phrase each matched. */
  rules: { id: string; matchedPhrase: string }[];
  messages: { id: string; sender: string; text: string; receivedAtUtc: string }[];
  /** The job's history in sequence order (1, 2, 3… per job). */
  events: JobDetailEvent[];
  /** True while the job is Prepared and can be approved, edited or rejected. */
  canReview: boolean;
  /** What approval still needs: "category", "contractor". */
  missingForApproval: string[];
}

/** The model run behind an AI event: tokens are summed over attempts; rawOutput is the last attempt's. */
export interface ModelRun {
  modelId: string | null;
  promptVersion: string | null;
  latencyMs: number | null;
  inputTokens: number | null;
  outputTokens: number | null;
  rawOutput: string | null;
  /** Why the model gave no usable answer; null when it did. */
  reason: string | null;
  attempts: number;
}

/** One history row. payloadJson is the event's stored payload; model is set for ModelPrepared and ModelPreparationFailed. */
export interface JobDetailEvent {
  type: string;
  occurredAtUtc: string;
  actor: string;
  summary: string;
  sequence: number;
  payloadJson: string;
  model: ModelRun | null;
}

/** A change to a Prepared job; omitted fields stay as they are. A reason is required to lower urgency. */
export interface JobEdit {
  category?: Category;
  urgency?: Urgency;
  urgencyReason?: string;
  contractorId?: string;
  propertyId?: string;
}

const ACTING_AS_HEADER = 'X-Acting-As';

async function getJson<T>(url: string): Promise<T> {
  const response = await fetch(url);
  if (!response.ok) {
    throw new ApiError(await readProblem(response));
  }
  return (await response.json()) as T;
}

async function reviewAction(method: 'POST' | 'PATCH', url: string, actingAs: string, body?: unknown): Promise<JobDetail> {
  const headers: Record<string, string> = { [ACTING_AS_HEADER]: actingAs };
  if (body !== undefined) {
    headers['Content-Type'] = 'application/json';
  }
  const response = await fetch(url, {
    method,
    headers,
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  if (!response.ok) {
    throw new ApiError(await readProblem(response));
  }
  return (await response.json()) as JobDetail;
}

export const listStaff = () => getJson<StaffMember[]>('/api/staff');

export const listContractors = () => getJson<ContractorOption[]>('/api/contractors');

export const listProperties = () => getJson<PropertyOption[]>('/api/properties');

export const getJob = (jobId: string) => getJson<JobDetail>(`/api/jobs/${encodeURIComponent(jobId)}`);

export const approveJob = (jobId: string, actingAs: string) =>
  reviewAction('POST', `/api/jobs/${encodeURIComponent(jobId)}/approve`, actingAs);

export const rejectJob = (jobId: string, actingAs: string, reason: string) =>
  reviewAction('POST', `/api/jobs/${encodeURIComponent(jobId)}/reject`, actingAs, { reason });

export const editJob = (jobId: string, actingAs: string, edit: JobEdit) =>
  reviewAction('PATCH', `/api/jobs/${encodeURIComponent(jobId)}`, actingAs, edit);
