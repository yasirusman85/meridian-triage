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
}

export interface SubmitMessageResponse {
  jobId: string;
  reference: string;
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
