# Architecture Decision Records

These decisions record the initial skeleton and the implemented Automatic + CAdES/PAdES PoC. Future signing variants and other workflows remain subject to the unresolved questions in [architecture.md](./architecture.md) and [ui-api-mapping.md](./ui-api-mapping.md).

## ADR-001: Proxy external SIGN APIs through ASP.NET Core

- **Status:** Proposed
- **Context:** The Postman collection uses OAuth client credentials, bearer authorization, SAT, PIN, OTP, signer identity and sensitive documents. A browser-facing integration would expose or mishandle provider credentials and require undocumented CORS behavior.
- **Decision:** React calls only same-origin TrustPlatform BFF endpoints. ASP.NET Core obtains and holds provider OAuth tokens, constructs allowlisted provider requests, attaches the bearer token server-side, and normalizes/streams responses. TrustPlatform caller authentication is a separate production concern; the local PoC does not require it and must not be publicly exposed.
- **Alternatives considered:** Call the provider directly from React; expose a generic configurable proxy. Both expose secrets or allow unsafe upstream/header control and are rejected.
- **Consequences:** Provider adapters, safe DTOs, server-side secret management and fake-provider tests are required. Provider routes are not a public frontend contract.

## ADR-002: Use one ASP.NET Core host and one React/TypeScript client initially

- **Status:** Accepted for the initial skeleton
- **Context:** The repository is a small ASP.NET Core project with intentionally empty `src/TrustPlatform` and `tests/TrustPlatform.Tests` directories.
- **Decision:** Keep the API host in `src/TrustPlatform.Api`; use `src/TrustPlatform.Web` for the React/TypeScript SPA; keep backend tests in `tests/TrustPlatform.Api.Tests`. Deploy behind a same-origin route or serve the built SPA from the host.
- **Alternatives considered:** Add multiple backend/domain projects up front; embed frontend logic in the API project. Neither is justified by current requirements.
- **Consequences:** Feature folders and explicit interfaces can preserve separation without premature project proliferation. Revisit project boundaries if approved scale or ownership requirements warrant it.

## ADR-009: Use requested project names and deliver a health-only first slice

- **Status:** Accepted for the initial skeleton
- **Context:** The earlier architecture proposal used provisional project names (`TrustPlatform` and `TrustPlatform.Client`) and described future workflows. The implementation request specifies `TrustPlatform.Api`, `TrustPlatform.Web`, and `TrustPlatform.Api.Tests`, and limits this iteration to a health endpoint and UI placeholders.
- **Decision:** Use the explicitly requested project names and implement only `GET /api/health`, the Vite React shell, same-origin development proxy configuration, and basic backend/frontend tests. Keep all external SIGN operations out of this slice.
- **Alternatives considered:** Preserve the provisional project names; implement initial SIGN workflows while scaffolding. Rejected because the requested names and health-only scope are explicit.
- **Consequences:** Update the structure in `docs/architecture.md`. Signing, validation, file handling, and provider business operations remain unimplemented; OAuth token acquisition is implemented separately as documented in ADR-010. This does not change the intended BFF boundary.

## ADR-003: Keep provider credentials and authentication state backend-only

- **Status:** Proposed
- **Context:** The collection's credential values are environment-dependent. Its OAuth token request specifies the `client_credentials` grant, while TrustPlatform user authentication and production secret-store choices are separate concerns.
- **Decision:** Store provider client credentials in server-side configuration or secrets. Acquire and retain OAuth access tokens, SAT, transaction IDs and provider auth state only on the server. Never include these in React bundles, browser storage, React DTOs, logs or error responses. Cache the OAuth access token according to the token endpoint's returned `expires_in`; do not hardcode the sample lifetime or assume a refresh-token flow.
- **Sensitive user input:** PIN and OTP are secrets. The proposed design uses a separately served, one-time ASP.NET Core input page and native POST, not React state or a React JSON API. If this interpretation does not meet the requirement, those workflows must use an approved out-of-band flow or remain unsupported.
- **Alternatives considered:** Store tokens in browser storage, pass them through React, or accept secrets in the React API payload. Rejected.
- **Consequences:** A production TrustPlatform identity/session model, CSRF/antiforgery, tenant-bound operation state, production secret-store choice and secure sensitive-input UX remain separate decisions. For the local PoC, user authentication and multi-tenancy are not required, and the app must not be publicly exposed.

## ADR-010: Use OAuth client credentials for backend SIGN API calls

- **Status:** Accepted and implemented for backend SIGN API authentication
- **Context:** The Postman collection's Authentication request posts `grant_type=client_credentials` to the configured identity-provider `/token` endpoint, inherits collection-level Basic authentication, and includes a sample `scope` field. The saved token response contains an access token, `token_type=Bearer`, and `expires_in`. These values establish the authentication mechanism and response fields, but not deployment-specific endpoint, credential, or scope values.
- **Decision:** The ASP.NET Core backend obtains the OAuth access token using server-side client credentials with the collection's Basic client authentication, caches it only for the lifetime reported in `expires_in`, and attaches it as a Bearer token to SIGN API requests. React has no provider token or credential role and receives neither. Endpoint origin, credentials, and provider-approved scope are supplied through backend-only configuration/secrets. Do not copy Postman credential/token values, hardcode sample lifetime, or infer a refresh-token flow.
- **Alternatives considered:** Obtain the provider token in React; pass credentials or the token through a BFF request; use workflow PIN, OTP, SAT, or signer ID as OAuth authentication. All are rejected because they expose secrets or conflate distinct authentication/workflow data.
- **Consequences:** `SignApiTokenService`, server-side `SignApiClient`, the Development-only `POST /api/diagnostics/sign-auth` endpoint, and fake-token-endpoint tests implement this decision. The local PoC needs no TrustPlatform user authentication or multi-tenancy and must not be publicly exposed; this does not settle production caller authentication or authorization.

## ADR-004: Organize UI around user workflows, not Postman request count

- **Status:** Proposed
- **Context:** The 92 requests contain token helpers, templates, duplicated examples and format variants, not 92 separate end-user features.
- **Decision:** Use workflow areas for signing, validation, certificates/authenticators, timestamping and operation results. Use one reusable form per workflow with provider-approved choices and conditional format sections.
- **Alternatives considered:** Generate a page per collection request; expose raw provider JSON. Rejected because both leak example-specific behavior and burden users with API details.
- **Consequences:** Product owners must approve supported formats/profiles. Certificate issuance (`emitCertificateRequest`) remains out of initial scope until its contract, privacy basis and secure data-entry process are approved.

## ADR-005: Use typed BFF contracts and reject undocumented provider behavior

- **Status:** Proposed
- **Context:** The collection is an example set, not an OpenAPI contract. It has missing response examples, inconsistent base variables, untyped-looking string fields, malformed hash-sign example content and ambiguous continuation behavior.
- **Decision:** Define application-owned DTOs and explicit adapters. Classify each catalog parameter as user input, approved predefined selection, automatic value, backend configuration, previous server response, or unsupported. Reject unsupported fields/options rather than forwarding arbitrary input.
- **Alternatives considered:** Treat every example value as a valid enum/default; forward raw JSON to provider. Rejected.
- **Consequences:** Provider documentation or approved contract tests are prerequisites for enabling ambiguous variants. Mapping updates must precede implementation of those variants.

## ADR-011: Separate VALIDATION API configuration and token handling from SIGN API

- **Status:** Accepted and implemented
- **Context:** Validation is a distinct business operation with its own OAuth client credentials, bearer token lifecycle, provider base URL, and diagnostics flow. Reusing the SIGN API client or cache would conflate two different provider contracts and credentials.
- **Decision:** TrustPlatform defines a dedicated `ValidationApi` configuration block, a separate `ValidationApiTokenService`, and a dedicated `ValidationApiClient` and endpoint set. Validation authentication is isolated behind `POST /api/diagnostics/validation-auth`; the upload endpoint `POST /api/validation/signature` uses its own request pipeline and sanitizes provider metadata before returning it to the React UI. The configured `ValidationApi:BaseUrl` is the provider endpoint; no provider route is hardcoded into the validation operation.
- **Alternatives considered:** Reuse the `SignApi` OAuth token cache; expose raw provider validation JSON directly to the frontend; collapse validation and signing into a single provider adapter. Rejected because it would mix provider credentials and leak response details that are unsafe for the browser.
- **Consequences:** The app supports separate validation credentials and safe response shaping, while the SIGN API path remains unchanged. Real provider secrets must remain in server-side configuration such as User Secrets or environment variables, and the UI displays only business-safe validation summaries rather than raw certificate blobs or full provider payloads.

## ADR-012: Use the confirmed single-document validation JSON contract

- **Status:** Accepted and implemented
- **Context:** The validation collection examples identify a JSON body containing `signedDocument` and `validationOptions`, but use unresolved Postman placeholders and string-like Boolean values. The provider has confirmed the validation endpoint, fixed option values, and Base64 file encoding for this TrustPlatform iteration.
- **Decision:** `POST /api/validation/signature` accepts exactly one non-empty `.pdf`, `.p7m`, `.xml`, or `.p7s` upload (the current TrustPlatform PoC allowlist, not the complete provider list). ASP.NET Core enforces the configured size limit, sanitizes the basename, reads within a bounded memory limit, and sends JSON to configured `ValidationApi:BaseUrl`: `signedDocument: { data: <Base64>, name: <sanitized filename> }` and `validationOptions: { validationLevel: "ARCHIVAL_DATA", extractOriginals: false, includeEtsiValidationReport: false }`. React sends multipart/form-data only to TrustPlatform. The dedicated validation token service supplies the bearer token; validation does not reuse SIGN API credentials or cache.
- **Error handling:** In Development only, return allowlisted textual provider error fields with status, content type, and a TrustPlatform correlation ID after redacting credentials, tokens, file content, paths, and stack traces. For other environments, return only a generic title, HTTP status, and correlation ID. Never return raw provider bodies. A business response with `valid: false` remains a successful validation result.
- **Consequences:** Detached original files and other validation profiles/options remain unsupported until separately confirmed. Provider-response DTOs omit certificate data blobs. Upload controls use one custom accessible button with a visually hidden native input.

## ADR-006: Stream uploads and downloads with bounded temporary handling

- **Status:** Proposed
- **Context:** Signing and validation operate on documents; timestamping uses a file body. Provider document encoding, limits and output formats are not consistently described.
- **Decision:** Accept authenticated uploads at the BFF, apply approved limits, avoid persistent storage by default, and stream provider/file results through an authorized download endpoint. Use bounded encrypted temporary storage only when streaming is not supported, with cleanup and expiry.
- **Alternatives considered:** Store all documents indefinitely; buffer every request/response in application memory; return provider URLs. Rejected.
- **Consequences:** File size/type policy, output format negotiation, retention and audit requirements must be resolved. Do not select provider encoding or ZIP/multipart behavior without confirmation.

## ADR-007: No invented polling, retry, or idempotency semantics

- **Status:** Proposed
- **Context:** Saved examples contain 202 and 207 statuses, but the collection documents no provider polling/status route, continuation contract or idempotency key semantics.
- **Decision:** Model only confirmed synchronous steps. Do not automatically poll or retry signing. Treat 202/207 as distinct, safe-to-display states until the provider contract defines next actions.
- **Alternatives considered:** Guess a polling endpoint or retry policy from status codes and `Prefer`. Rejected.
- **Consequences:** Remote signing may remain incomplete until the provider specifies continuation behavior. Retry/idempotency tests depend on an authoritative contract.

## ADR-008: Do not log request bodies or sensitive identifiers

- **Status:** Proposed
- **Context:** Documents, signatures, personal data and authentication material appear in request bodies and headers.
- **Decision:** Log only operation category, correlation ID, approved pseudonymous user/tenant reference, route template, duration, status and byte counts. Redact provider errors; add automated secret-absence checks.
- **Alternatives considered:** Capture raw request/response bodies for diagnostics. Rejected by default.
- **Consequences:** Diagnostics rely on correlation and structured safe metadata. Any additional audit detail requires privacy/security approval.

## ADR-011: Enable only Automatic + CAdES for the first signing workflow

- **Status:** Accepted and implemented for the local PoC
- **Context:** The Postman collection documents `Sign API - automatic Signature / 01. cadesSign` as `POST {{api.url}}/{{automaticSign-domain_alias}}/sign`, using a CAdES JSON body, OAuth Bearer authentication, and the `X-signer-id` and `Infocert-SAT` headers. Its CAdES request example uses `BASELINE-T`, `ENVELOPED`, and a PDF with `application/pdf`. The saved success response contains Base64 `signedDocument.content`, `contentType`, and `attachName`.
- **Decision:** Implement only Automatic + CAdES with `BASELINE-B`. Require one PDF, a separate Certificate ID and X-signer-id, PIN, SAT, and packaging. Accept `ENVELOPED` and `ENVELOPING`; display all four requested baseline levels with only `BASELINE-B` enabled. Validate Certificate ID against a conservative identifier allowlist and encode it as one URL path segment. Configure `applicationId` and provider BaseUrl on the backend, generate a unique request ID, and use the existing `SignApiClient` and `SignApiTokenService`. Return validated signed bytes as a controlled download with a backend-generated filename.
- **Alternatives considered:** Enable remote or other signature types; offer baseline levels inferred from other operations; return provider response JSON or trust provider filenames. Rejected because those behaviors are not established for the selected CAdES request.
- **Consequences:** Remote, PAdES, XAdES, and Hash remain visible but disabled. Only `BASELINE-B` is enabled; `BASELINE-T`, `BASELINE-LT`, and `BASELINE-LTV` remain visible but disabled and are rejected by the backend. The certificate route is not the URL of the Automatic folder's `01. cadesSign`; its use here is an explicit product-owner-approved contract based on the separate `5a. cadesSign - auto` request. The latter has no saved response, so response mapping is validated against the saved Automatic CAdES example. PDF-only and the default 10 MiB cap are TrustPlatform PoC restrictions, not claimed InfoCert limits. PIN and SAT are transient masked form values and are never persisted or returned.

## ADR-012: Limit signing-form secret defaults to local Development

- **Status:** Accepted for local PoC only
- **Context:** Local development benefits from pre-populating the Automatic + CAdES form, but PIN and Infocert-SAT are credentials and must not be exposed through shared or production surfaces.
- **Decision:** Add strongly typed `SignApi:Defaults:AutomaticSigning` options and map `GET /api/signing/automatic/cades/defaults` only when ASP.NET Core runs in Development. Return only Certificate ID, X-signer-id, PIN, Infocert-SAT, Packaging, and Signature Level, with `Cache-Control: no-store`. React requests defaults only in a development build and retains them only in component memory. Keep credential values out of committed configuration; use local User Secrets or process environment variables.
- **Alternatives considered:** Return defaults from an always-available endpoint; put defaults in the frontend bundle or browser storage; configure PIN/SAT for shared or production environments. Rejected because they would expose sensitive values beyond local development.

## ADR-013: Implement one visible or invisible Automatic PAdES field

- **Status:** Accepted for the local PoC
- **Context:** The Postman collection confirms PAdES requests use `padesSignatures`, Base64 PDF `document.content`, `application/pdf`, string `isVisible`, and a saved success shape containing `signatureResult[].signedDocument`. Visible examples include one `signatureFields` entry with numeric position values.
- **Decision:** Enable Automatic + PAdES alongside CAdES. Accept only `BASELINE-B`, use `ENVELOPED` or `ENVELOPING`, serialize `isVisible` and `isPdfa` as strings `"true"`/`"false"`, and serialize position values as JSON numbers. Omit `signatureFields` when invisible. Preserve the configured defaults `isVisible=true`, `isPdfa=true`, `page=1`, `llx=100`, `lly=100`, `urx=20`, `ury=20` under `SignApi:Defaults:AutomaticSigning:Pades`.
- **Alternatives considered:** Boolean serialization for visibility/PDF-A, additional signature fields, and coordinate rewriting. Rejected or deferred because the collection confirms strings and provides only one-field examples; the coordinate semantics remain ambiguous, so this PoC allows submission without silently rewriting values.
- **Consequences:** PAdES is enabled with exactly one optional visible signature field. Remote signing, XAdES, Hash, and multiple signature fields remain unsupported. Success PDF bytes are downloaded with a backend-generated filename.
- **Consequences:** This endpoint and any configured PIN/SAT defaults are local-development conveniences, not a production secret-management strategy. Remove the endpoint and defaults before shared, staging, or production deployment. Unsupported signature-level configuration falls back to `BASELINE-B`.

## ADR-013: Require explicit certificate-query submission

- **Status:** Accepted
- **Context:** Opening the Certificates List and Certificate Details pages previously invoked SIGN API operations immediately with backend-configured identifiers. Users need to inspect and optionally edit the request identifiers before making a provider request.
- **Decision:** Page entry may call `GET /api/certificates/defaults`, which returns only the configured Certificate ID and X-signer-id with `Cache-Control: no-store`. The list operation is `POST /api/certificates` and accepts only X-signer-id; the detail operation is `POST /api/certificates/configured` and accepts Certificate ID and X-signer-id. Both provider calls require explicit form submission. The backend maps X-signer-id to the provider header and Certificate ID to the detail path segment; neither edited value is persisted.
- **Alternatives considered:** Continue querying on page entry; use one configured-ID provider request for both pages; pass Certificate ID as X-signer-id. Rejected because they either cause an implicit provider operation or do not match the provider contract.
- **Consequences:** Opening either page makes only a same-origin settings request, not a SIGN API call. The browser never receives OAuth credentials or tokens. The list form intentionally omits Certificate ID because the list operation does not require it.

## ADR-014: Add an explicit Certificate Chain workflow

- **Status:** Accepted
- **Context:** The SIGN API provides a certificate-chain operation that requires Certificate ID and `X-signer-id`, while React must not receive provider credentials or headers for user configuration.
- **Decision:** Add `GET/POST /api/certificates/{certificateId}/chain` with a validated, escaped Certificate ID path segment. The POST request accepts X-signer-id and is used by the edited UI form; the GET fallback reads `SignApi:Defaults:AutomaticSigning:XSignerId`. The provider OAuth bearer token remains server-side through `SignApiClient` and `SignApiTokenService`. Return the provider array directly as strongly typed application chain items, normalize unknown status values to `unknown`, and preserve valid expiration date strings.
- **Consequences:** The React page accepts only Certificate ID, displays every chain item, collapses certificate content, and includes the received response as indented raw JSON. Missing SignerId or unsafe configuration prevents provider calls and returns a safe service error.
