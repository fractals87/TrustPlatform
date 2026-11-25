# TrustPlatform Requirements

## Signature validation (current PoC)

- React uploads exactly one non-empty `.pdf`, `.p7m`, `.xml`, or `.p7s` document to `POST /api/validation/signature` using multipart/form-data. This is the TrustPlatform PoC allowlist, not the complete provider-supported format list.
- ASP.NET Core enforces `ValidationApi:Upload:MaxFileSizeBytes`, sanitizes the filename to a basename, reads the file within a bounded memory limit, and sends JSON to the configured `ValidationApi:BaseUrl`.
- The provider request contains Base64 `signedDocument.data`, sanitized `signedDocument.name`, and fixed validation options: `validationLevel=ARCHIVAL_DATA`, `extractOriginals=false`, and `includeEtsiValidationReport=false` with the Boolean values serialized as JSON Booleans.
- Validation uses the dedicated VALIDATION API OAuth client with `grant_type=client_credentials` and `scope=validation`; it does not share the SIGN API token cache.
- Successful responses return a sanitized human-readable validation report. `valid=false` is a completed business result, not a technical failure. Certificate data blobs are never returned or rendered.
- The completed result includes a collapsed-by-default “Raw validation response” section after all human-readable report sections. It formats only TrustPlatform's safe validation response DTO as indented JSON; certificate `data` blobs and request/provider secrets are excluded.
- Development errors may expose only sanitized provider status, textual code/message/detail, response content type, reason phrase, and TrustPlatform correlation ID. If a successful provider response cannot be parsed, Development diagnostics may additionally include its shape, a safe parsing error, and a sanitized JSON excerpt limited to 5,000 characters, with every `certificates[].data` removed. Production errors contain only a generic title, HTTP status, and correlation ID. Credentials, tokens, authorization headers, document content, filesystem paths, raw provider bodies, and stack traces are never exposed.
- Upload controls show one accessible custom file-selection button backed by a visually hidden native input.

## Certificate queries

- Opening Certificates List or Certificate Details loads only the configured certificate-query identifiers from TrustPlatform; it must not call the SIGN API.
- The list operation requires only X-signer-id, sent by the backend as the SIGN API `X-signer-id` header.
- The detail operation requires X-signer-id and Certificate ID. The backend sends X-signer-id as a header and uses Certificate ID as the certificate path segment.
- Both operations run only after an explicit user submission. User-edited identifiers apply only to that request and are not persisted.
- The defaults response contains only the configured certificate ID and X-signer-id and is marked `Cache-Control: no-store`; OAuth credentials and tokens remain server-side.
- Certificate Chain retrieves `GET /api/certificates/{certificateId}/chain` only after explicit submission. The backend validates Certificate ID, obtains the configured SignApi SignerId, adds `X-signer-id`, and never exposes OAuth credentials or tokens. The response is a direct array of chain items and the UI renders every item plus collapsible certificate content and indented raw JSON.
