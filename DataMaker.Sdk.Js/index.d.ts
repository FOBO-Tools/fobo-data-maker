// Type definitions for @fobo-tools/datamaker

export type FieldKind =
  | 'text' | 'long-text' | 'rich-text'
  | 'number' | 'decimal' | 'money'
  | 'date' | 'datetime' | 'boolean'
  | 'choice' | 'multi-choice' | 'list'
  | 'email' | 'phone' | 'url' | 'geo'
  | 'image' | 'attachment' | 'relation'
  | (string & {});

export interface ChoiceOption {
  value: string;
  label: string;
}

export interface FieldDescriptor {
  id: string;
  /** Storage key (the field's `name`). What the receiver indexes on. */
  key: string;
  name: string;
  label: string;
  kind: FieldKind;
  required: boolean;
  description?: string;
  placeholder?: string;
  choices?: ChoiceOption[];
  allowCustom?: boolean;
}

export interface SignerInfo {
  publicKey: string;
  identity: { email?: string; name?: string; company?: string } | null;
}

export interface FormDescriptor {
  formId: string;
  name: string;
  description: string | null;
  schemaVersion: number;
  submitPolicy: string | null;
  recipientUserId: string | null;
  recipientPublicKey: string | null;
  signer: SignerInfo | null;
  envelopeVersion: number;
  fields: FieldDescriptor[];
  verified: boolean;
}

export interface SubmissionEnvelope {
  submissionId: string;
  formId: string;
  recipientUserId: string;
  submitterId: string | null;
  ciphertext: string;
  editToken?: string | null;
  receivedAt?: string | null;
}

export interface SubmissionPayload {
  values: Record<string, unknown>;
  submittedAt: string;
  formVersion: number;
  formSchema: string;
  mode: 'Create' | 'Update';
  editToken?: string | null;
}

export interface ValidationIssue {
  field: string;
  kind: FieldKind | null;
  message: string;
}

export type FetchLike = (
  url: string,
  init: { method: string; headers: Record<string, string>; body: string }
) => Promise<{ ok: boolean; status: number; text(): Promise<string> }>;

export interface ReadFormOptions {
  /** Verify Ed25519 signature + form.json hash (default true). */
  verify?: boolean;
}

export interface BuildSubmissionOptions {
  validate?: boolean;
  allowUnknown?: boolean;
  submitterId?: string | null;
  submissionId?: string;
  submittedAt?: string;
}

export interface SubmitOptions extends BuildSubmissionOptions {
  form?: FormDescriptor;
  dmf?: Uint8Array | Buffer;
  values: Record<string, unknown>;
  apiBaseUrl?: string;
  verify?: boolean;
  fetch?: FetchLike;
}

export interface BuiltSubmission {
  submissionId: string;
  envelope: SubmissionEnvelope;
  payload: SubmissionPayload;
  values: Record<string, unknown>;
}

export interface SubmitResult {
  submissionId: string;
  editToken: string;
  formId: string;
}

// --- .dmf reading ---------------------------------------------------------
export function readForm(dmf: Uint8Array | Buffer, opts?: ReadFormOptions): Promise<FormDescriptor>;
export const parseDmf: typeof readForm;
export function extractFields(form: unknown): FieldDescriptor[];

// --- submission -----------------------------------------------------------
export function submit(opts: SubmitOptions): Promise<SubmitResult>;
export function buildSubmission(
  form: FormDescriptor | Partial<FormDescriptor>,
  values: Record<string, unknown>,
  opts?: BuildSubmissionOptions
): Promise<BuiltSubmission>;
export function postSubmission(
  envelope: SubmissionEnvelope,
  opts?: { apiBaseUrl?: string; fetch?: FetchLike }
): Promise<{ submissionId: string; editToken: string }>;
export function newSubmissionId(): string;
export const DEFAULT_API_BASE_URL: string;

// --- web-renderer glue ----------------------------------------------------
export interface RenderSubmitPayload {
  form: { id: string; schemaVersion?: number; fields?: unknown[]; [k: string]: unknown };
  col?: unknown;
  values: Record<string, unknown>;
}

export interface SubmitHandlerConfig {
  recipientPublicKey: string;
  recipientUserId: string;
  apiBaseUrl?: string;
  submitterId?: string | null;
  validate?: boolean;
  applyFieldErrors?: (errors: Record<string, string>) => void;
  onSuccess?: (result: { submissionId: string; editToken: string }) => void;
  onError?: (error: Error) => void;
  fetch?: FetchLike;
  /** false → renderer skips the .dmf's author design (palette + per-element
   *  CSS) and renders structure-only. Browser-only; no-op under Node. */
  applyFormStyle?: boolean;
  /** Light/dark for the embedded form. 'auto' (default) follows the visitor's
   *  prefers-color-scheme and re-themes live; 'light'/'dark' force one. */
  theme?: 'auto' | 'light' | 'dark';
}

export interface RenderOptions {
  /** false → render the form structure-only, dropping the author's .dmf design. */
  applyFormStyle?: boolean;
}

/** Push render options onto the web renderer before mount (browser-only). */
export function setRenderOptions(opts: RenderOptions): void;

export interface SubmitHandlerResult {
  ok: boolean;
  submissionId?: string;
  editToken?: string;
  issues?: ValidationIssue[];
  error?: Error;
}

/** Build an `onSubmit(payload)` for the web renderer's `hooks.onSubmit`. */
export function createSubmitHandler(
  config: SubmitHandlerConfig
): (payload: RenderSubmitPayload) => Promise<SubmitHandlerResult>;

// --- Wasm renderer embed --------------------------------------------------
export interface MountWasmConfig {
  /** base64 X25519 recipient key from the .dmf manifest. */
  recipientPublicKey: string;
  recipientUserId?: string | null;
  /** Submissions endpoint; defaults to the public one. */
  apiBaseUrl?: string;
  /** Shown in place of the form after a successful submit. */
  afterSubmitText?: string;
  /** Base URL of the Wasm bundle host. Defaults to {@link DEFAULT_WASM_HOST}. */
  wasmHost?: string;
  /** Light/dark for the form. 'auto' (default) follows the visitor's
   *  prefers-color-scheme and re-themes live; 'light'/'dark' force one. */
  theme?: 'auto' | 'light' | 'dark';
}

/** Public host of the static Wasm renderer bundle. */
export const DEFAULT_WASM_HOST: string;

/** Mount the full Uno-in-WebAssembly renderer (pixel-parity with the desktop
 *  designer) into an iframe on `opts.root` (or `#form-root`), reading the form
 *  from `opts.form` or a `#form-bundle` script. Submission still seals
 *  client-side. Returns an unmount function. Browser-only. */
export function mountWasm(
  config: MountWasmConfig,
  opts?: { root?: Element; form?: unknown; bundle?: Element }
): () => void;

// --- validation -----------------------------------------------------------
export function validateValues(
  fields: FieldDescriptor[],
  input: Record<string, unknown>,
  opts?: { allowUnknown?: boolean }
): { values: Record<string, unknown>; issues: ValidationIssue[] };

// --- field-kind helpers ---------------------------------------------------
export const KINDS: Record<string, FieldKind>;
export const ALL_KINDS: Set<string>;
export const NON_INPUT_KINDS: Set<string>;
export const PASSTHROUGH_KINDS: Set<string>;
export function normalizeKind(kind: string): FieldKind;
export function isInputKind(kind: string): boolean;
export function coerceValue(
  kind: string,
  raw: unknown,
  field: FieldDescriptor
): { value?: unknown; error?: string };

// --- crypto primitives ----------------------------------------------------
export function initSodium(): Promise<unknown>;
export function sealPayload(payload: unknown, recipientPubkeyBase64: string): Promise<string>;
export function sha256Hex(bytes: Uint8Array | Buffer): Promise<string>;

// --- errors ---------------------------------------------------------------
export class DataMakerError extends Error {
  code: string;
}
export class DmfError extends DataMakerError {}
export class ValidationError extends DataMakerError {
  issues: ValidationIssue[];
}
export class SubmissionError extends DataMakerError {
  status: number;
  body: string;
}
