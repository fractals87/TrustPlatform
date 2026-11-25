# TrustPlatform Architecture (proposed)

**Status:** Design proposal with the health/UI skeleton, separate backend-only SIGN and VALIDATION API OAuth diagnostics, Automatic + CAdES/PAdES signing, and one confirmed signature-validation operation implemented. The broader design is based on the 92-request collection catalogued in [api-catalog.md](./api-catalog.md); examples are not authoritative contracts outside fields confirmed for each operation.

## 1. Goals and boundaries

- Provide a React/TypeScript web experience for document signing, validation, certificate discovery, and timestamping.
- Put all external SIGN API calls behind an ASP.NET Core backend-for-frontend (BFF). The browser must not call the external APIs.
- Keep OAuth client credentials and access tokens out of React state, browser storage, logs, and responses. For the supported PoC only, masked PIN and Infocert-SAT values are transient React form state and multipart request fields; never persist or log them, and clear them after completion.
- Use backend configuration and a managed secret store for service credentials and API hosts.
- Development-only signing-form defaults may be returned by a local backend endpoint for the PoC. Such defaults can include PIN and SAT, must remain in React component memory only, and must never be enabled in shared, staging, or production environments.
- Support upload and download without requiring the UI to understand undocumented provider response encodings.
- Group the examples into task-oriented workflows rather than creating one page for each Postman request.
- Exclude unsupported payload variants until their provider contract and security requirements are confirmed.

For the current local PoC, TrustPlatform user authentication and multi-tenancy are not required, and the application is not intended for public exposure. These are PoC constraints only; they do not decide the production user-authentication, authorization, tenant-isolation, consent, data-retention, or browser-safe API design.

## 2. Solution and project structure

Proposed minimal solution:

```text
TrustPlatform.sln
src/
  TrustPlatform.Api/                ASP.NET Core API host and future BFF
    Features/
      Signing/
      Validation/
      Certificates/
      Timestamping/
      Operations/
    ExternalApis/
      SignApi/
      IdentityProvider/
      ValidationApi/
      TimestampApi/
    Security/
    Files/
    Configuration/
  TrustPlatform.Web/                React + TypeScript SPA, built with Vite
    src/
      app/                           routing, shell, navigation
      features/
        signing/
        validation/
        certificates/
        timestamping/
        operations/
      components/
      api/                           same-origin BFF client; no provider client
      validation/
tests/
  TrustPlatform.Api.Tests/           deterministic backend unit/integration tests
```

The initial solution uses the explicit project names `TrustPlatform.Api`, `TrustPlatform.Web`, and `TrustPlatform.Api.Tests`. Frontend component tests live alongside the Vite app under `src/TrustPlatform.Web/src` and run with Vitest/React Testing Library. Do not introduce a separate domain project or persistence layer until a concrete need for them is established.

The current implementation includes `GET /api/health`, the React application shell, server-side OAuth client-credentials token services and Development-only authentication diagnostics for SIGN API and VALIDATION API, Automatic + CAdES/PAdES signing, a single-document validation workflow, and certificate queries (`POST /api/certificates` and `POST /api/certificates/configured`) that run only after explicit user submission. Certificate pages load only the configured identifiers from `GET /api/certificates/defaults`; opening a page does not call SIGN API. Remote signing, XAdES, Hash signing, timestamping, TrustPlatform user authentication, and database storage remain unimplemented.

Deploy the SPA and BFF behind one public origin where possible. ASP.NET Core serves the built SPA or a same-origin reverse proxy routes `/api/*` to the BFF. This avoids browser-to-provider CORS and keeps provider URLs private. Development may use a dev-server proxy, but the client must still call only same-origin BFF paths.

## 3. Functional groups, pages, and navigation

The collection's folders are examples, not page boundaries. Build these navigable workflows:

| Navigation item | Main page and actions | Collection groups covered |
|---|---|---|
| Sign a document | Choose a method and type; Automatic + CAdES or PAdES accepts one PDF, Certificate ID, separate X-signer-id, PIN, SAT, packaging, and signature level, then offers the signed result | Implemented as Automatic + CAdES/PAdES using the `/certificates/{certificateId}/sign` path. Remote, XAdES, and Hash are visible but disabled/not implemented |
| Validate a signature | Upload one signed PDF, P7M, XML, or P7S file; submit and inspect the sanitized report | Implemented through the configured VALIDATION API `/validation/signature/v3` endpoint using the confirmed `ARCHIVAL_DATA` request contract. The extension list is the current TrustPlatform PoC allowlist, not the complete provider-supported set. Detached originals and format/profile selection remain unsupported. |
| Certificates list | Edit X-signer-id and explicitly retrieve safe certificate metadata and expiration state | `GET /api/certificates/defaults` supplies the configured X-signer-id; `POST /api/certificates` is called only on submit. The backend sends X-signer-id as the SIGN API header. |
| Certificate details | Edit X-signer-id and Certificate ID, then explicitly retrieve the certificate summary and availability state | `GET /api/certificates/defaults` supplies both identifiers; `POST /api/certificates/configured` is called only on submit. The backend sends X-signer-id as the header and Certificate ID as the path segment. |
| Timestamp a request | Upload a timestamp-query file, submit, download timestamp response | Automatic-sign timestamp example |
| Operation result | Show safe operation status, validation summary, signing metadata, and available downloads | Shared result workflow; not a new provider endpoint |

Use one format/profile-driven signing form and one validation form; change sections only where their input shape differs. Keep authenticator discovery and challenge as steps in remote signing, not standalone user pages. OAuth/token requests are backend infrastructure and never appear in navigation.

The certificate-issuance request shape (`emitCertificateRequest`) carries extensive personal and identity data. It is not a phase-one form. Add it only after confirming the product use case, provider schema, privacy/legal basis, retention, and a secure server-side data-entry flow.

## 4. UI forms and reusable components

### Signing

**Implemented-flow exception:** for Automatic + CAdES only, PIN and Infocert-SAT are password-masked transient React inputs sent to the same-origin BFF and cleared after completion. The following proposed sensitive-input guidance is for workflows that have not been implemented.

- **Signing method/type:** keep a method/type model extensible, but enable only Automatic + CAdES in this PoC. Remote, PAdES, XAdES, and Hash remain visible and disabled.
- **Certificate ID and X-signer-id:** separate required user inputs. Certificate ID is validated against an ASCII alphanumeric/`+`/`_`/`-` allowlist and used only as one escaped provider path segment. X-signer-id is validated and sent only in the provider header. Production must authorize both against authenticated user and tenant context.
- **Development defaults:** `GET /api/signing/automatic/cades/defaults` is mapped only in the Development environment. It returns configured form defaults, including PIN and SAT, with `Cache-Control: no-store`; React keeps values only in component memory. This local convenience must be removed before any shared/staging/production deployment and is not a production secret-management pattern.
- **Document picker:** accepts one PDF for the implemented operation. Client checks extension, declared MIME where available, and configured size; the backend independently checks size, extension, and PDF signature bytes. Do not persist the file in browser storage.
- **CAdES parameters:** packaging selects `ENVELOPED` or `ENVELOPING`. The implemented PoC enables only `BASELINE-B`; `BASELINE-T`, `BASELINE-LT`, and `BASELINE-LTV` are visible but disabled. The backend independently accepts only `BASELINE-B`.
- **PIN/OTP entry:** never rendered or managed by React. If the provider requires either, a separate ASP.NET Core-hosted, one-time sensitive-entry page posts directly to the backend and returns only a non-sensitive continuation/result. Do not send PIN or OTP in a React API call. If this separate surface is not acceptable, those workflows are unsupported.
- **Remote signing steps:** select an authenticator from returned options; request challenge; complete the hosted sensitive-entry step; submit signature; display only safe progress and result data. Do not show the transaction ID or SAT.
- **Visible PAdES settings:** the collection only shows an `isVisible` example represented as a string. Do not invent placement, page, or coordinate controls.

### Validation

- Accept exactly one non-empty signed document up to `ValidationApi:Upload:MaxFileSizeBytes`; the current PoC extension allowlist is `.pdf`, `.p7m`, `.xml`, and `.p7s`. This is not a complete provider-supported format list.
- Keep the upload as multipart/form-data only between React and TrustPlatform. The BFF sanitizes the filename, reads the file into bounded memory, Base64-encodes its bytes, and sends JSON to the configured `ValidationApi:BaseUrl`.
- The provider request is `signedDocument: { data, name }` and `validationOptions: { validationLevel: "ARCHIVAL_DATA", extractOriginals: false, includeEtsiValidationReport: false }`. The Boolean fields are JSON Booleans. React cannot select or override these values.
- Use the dedicated `ValidationApiTokenService` and its `client_credentials` grant with `scope=validation`; never share the SIGN API token cache.
- Display `valid` and a sanitized report, excluding certificate data blobs. Numeric epoch date values and string date values are both accepted. In Development, provider errors may show only allowlisted, sanitized status/code/message/detail metadata and a TrustPlatform correlation ID. If a successful provider response cannot be parsed, show status, reason phrase, Content-Type, response shape, safe parser message, and a sanitized JSON excerpt limited to 5,000 characters; remove `certificates[].data` before constructing the excerpt. Production responses omit provider body details. Never expose tokens, credentials, document content, paths, or stack traces.

### Shared components

- `FileDropzone` / `FileList`: client-side file selection only; no file content persisted in local/session storage.
- `CertificateSelect`: consumes BFF-sanitized certificate metadata.
- `FormatProfileSelect`: receives server-approved options; does not encode undocumented API values.
- `WorkflowStepper`: shows local steps and safe operation state; never displays provider tokens or challenge identifiers.
- `ProgressPanel`, `ValidationSummary`, `ErrorNotice`, `DownloadAction`: display normalized backend models.
- `SensitiveInputPage`: separately served by ASP.NET Core, not a React component. Use for PIN/OTP only if the product approves this server-hosted browser workflow.
- Reuse accessible form labels, validation summaries, loading state, and confirmation patterns.

## 5. Backend boundaries and external API proxy

React calls application-specific endpoints only. ASP.NET Core validates user authorization, files and allowed options, constructs provider requests, attaches backend-held authorization, invokes the appropriate external API, translates the result, and streams any file output.

Proposed BFF surface (internal route design; not provider routes):

| BFF endpoint | Purpose |
|---|---|
| `GET /api/ui-capabilities` | Return enabled workflows and provider-approved format/profile options. Do not return secrets or raw provider configuration. |
| `GET /api/certificates/defaults` | Return only the configured certificate ID and X-signer-id for editable certificate-query fields; never call the provider. |
| `POST /api/certificates` | List certificates using the request's X-signer-id as the provider header. |
| `POST /api/certificates/configured` | Read sanitized details using request Certificate ID in the provider path and X-signer-id as the provider header. |
| `GET/POST /api/certificates/{certificateId}/chain` | Retrieve the complete chain metadata array. POST accepts Certificate ID path data and request X-signer-id; the GET fallback reads `SignApi:Defaults:AutomaticSigning:XSignerId`. |
| `GET /api/signing/authenticators` | List supported authenticators for the selected certificate/identity. |
| `POST /api/signing/challenges` | Begin a remote challenge; retain transaction ID server-side and return a non-secret continuation reference. |
| `POST /api/signing/automatic` | Accept multipart file(s) and approved signing options; proxy the matching configured automatic-sign variant. |
| `POST /api/signing/remote` | Continue a remote-sign workflow using server-held challenge/auth state and uploaded documents. Secret entry is excluded from this React endpoint. |
| `GET /api/operations/{operationId}` | Return application-owned operation state and safe result metadata. This is not a claim that the provider supplies a polling endpoint. |
| `GET /api/operations/{operationId}/download/{fileId}` | Stream an authorized result file; do not expose provider URLs or file-store paths. |
| `POST /api/validation/signature` | Accept exactly one allowed signed file as multipart/form-data; construct the confirmed JSON request and proxy to configured `ValidationApi:BaseUrl`. |
| `POST /api/timestamp` | Accept the timestamp-query file and proxy the configured timestamp operation. |

The sensitive-entry page uses separate server-rendered GET/POST routes and antiforgery protection. Its one-time continuation is bound to the operation and expiry, and in production also to the authenticated user and tenant. It must not put PIN/OTP values in URLs, cookies, React state, or operation-status responses. Whether the browser session/authentication mechanism can support this safely remains unresolved. The local PoC does not add TrustPlatform user authentication; it must remain non-publicly exposed.

Do not expose a generic `POST /proxy` endpoint, arbitrary upstream URL, arbitrary header, provider auth scheme, or raw provider body. Each BFF endpoint accepts a typed allowlisted model.

## 6. Request/response models and mapping

Use application-owned request/response DTOs; do not bind provider JSON directly to controller inputs or serialize provider response objects to React.

- Upload requests use `multipart/form-data` at the BFF boundary. The signing endpoint validates its PDF contract. Validation accepts one file from the current `.pdf`, `.p7m`, `.xml`, and `.p7s` PoC allowlist and sends Base64 bytes in the confirmed `signedDocument.data` JSON property with a sanitized basename in `signedDocument.name`.
- The Automatic + CAdES PoC DTO accepts the masked PIN and Infocert-SAT from the same-origin React form, forwards each only to its documented provider destination, and never returns either. Other signing DTOs must follow their own approved secret-handling contract.
- Validation DTOs define the confirmed provider JSON request with fixed `ARCHIVAL_DATA` level and Boolean `false` values for `extractOriginals` and `includeEtsiValidationReport`. Detached originals and other profiles are not accepted in this iteration.
- Remote challenge state (transaction ID, SAT, access token) is backend-held and associated with an operation/session; frontend sees only opaque operation references and safe status.
- Result DTOs expose status, safe validation fields, and backend download identifiers. Preserve binary/ZIP/multipart output through a backend streaming response when possible; exact provider response negotiation and download framing need confirmation.
- Normalize errors to a stable application error code, safe message, correlation ID, and optional field errors. Do not return raw provider headers, tokens, stack traces, or credential-bearing payloads.

Full field classification and request-to-DTO mapping are maintained in [ui-api-mapping.md](./ui-api-mapping.md).

## 7. Configuration, credentials, and authentication

- **Provider OAuth for SIGN API calls:** React calls only the TrustPlatform BFF. ASP.NET Core obtains a provider access token from the configured identity-provider token endpoint using OAuth 2.0 `client_credentials`, then calls the configured SIGN API with `Authorization: Bearer <access_token>`. No OAuth token endpoint is exposed through the BFF.
- The collection's Authentication request is `POST {{idp.url}}/token` with `application/x-www-form-urlencoded` fields `grant_type=client_credentials` and an example `scope=signature`. Its saved response includes `access_token`, `token_type=Bearer`, and `expires_in`; the example's `expires_in` is sample data, not a constant. Configure the endpoint origin and client credentials on the server. Keep the requested scope configurable and use only a provider-approved value; do not infer a universal scope from the example or the response's `scope` field.
- **VALIDATION API OAuth:** use a separate `ValidationApi` configuration, `ValidationApiTokenService`, and token cache. Request `client_credentials` with scope `validation`; never reuse the SIGN API token. Configure `ValidationApi:BaseUrl` as the provider validation endpoint (`/validation/signature/v3`) per environment. No provider URL or token is exposed to React.
- The backend `SignApiTokenService` acquires and validates the token response, retains the token only server-side, and caches it until a short safety window before the returned `expires_in` lifetime elapses. It derives expiry from acquisition time and returned lifetime; it does not hardcode the example duration. On expiry, it acquires a new token before a SIGN API request. It does not assume a refresh-token flow: the collection does not document one. If required token fields are absent, invalid, or unusable, it fails explicitly rather than calling SIGN without a valid bearer token.
- The collection examples use Basic client authentication for token acquisition (the primary Authentication request inherits collection-level auth). Client ID/secret values must come from backend-only configuration or secrets; never copy collection values or expose them to React. Which secret store is used in production remains a deployment decision.
- This OAuth client-credentials layer is distinct from workflow-specific PIN, OTP, Infocert-SAT, `X-signer-id`, and transaction state. Those values follow their individual workflow rules and must not be treated as OAuth client credentials, bearer tokens, or substitutes for the OAuth token.
- Configure provider base URLs, timeouts, certificate/domain mappings, enabled capabilities, and non-secret defaults by deployment environment.
- Store client credentials and other provider secrets in an approved secret manager in production. Environment variables or .NET user-secrets may be used for local development secrets only if excluded from source control and logs. Access tokens are runtime state and must never be written to source-controlled configuration, browser storage, logs, or responses.
- Bind configuration in the backend only and never embed it in the SPA build. The certificate-query defaults endpoint is a narrow exception that returns only the non-credential Certificate ID and X-signer-id needed to populate those editable forms; it never returns provider credentials, OAuth secrets, or tokens.
- Use named `HttpClient` instances for the identity provider, signing API, validation API, and timestamp API; configure TLS validation and bounded timeouts. The SIGN API client obtains its bearer token from `SignApiTokenService` and attaches it server-side. Avoid logging request/response bodies, authorization headers, client credentials, and token values.
- The current local PoC does not require TrustPlatform user authentication or multi-tenancy and is not intended for public exposure. Production must separately define caller authentication/authorization and tenant boundaries before exposure; client-credentials authentication authenticates TrustPlatform to the provider, not a TrustPlatform user.
- Bind signer identity, certificate access, and any service SAT to the authenticated user's authorized tenant. Do not trust a client-supplied signer ID or certificate ID without backend ownership checks.

## 8. File handling and downloads

- Browser uploads go only to TrustPlatform over HTTPS. The backend enforces configured request limits, allowed file types, safe temporary names, and per-user authorization. Validation's current local PoC limit is `ValidationApi:Upload:MaxFileSizeBytes` (10 MiB by default) with `.pdf`, `.p7m`, `.xml`, and `.p7s` extensions; this extension list is not the complete provider-supported set.
- Avoid buffering large files in React or ASP.NET memory. Stream to bounded temporary storage or through the provider where supported; encrypt temporary storage, scope access, and delete on completion/expiry.
- Do not persist source files, signed outputs, validation reports, PINs, or OTPs by default. A retention/audit requirement would require a separately approved design.
- For the confirmed validation request, read the single upload into memory only within the configured maximum, convert bytes to Base64, and send the sanitized basename. Do not log the Base64 or return certificate data blobs. Other provider document encodings, detached-original association, output filename, MIME type, and JSON/ZIP/multipart response behavior remain unresolved until verified.
- Serve downloads through an authenticated BFF route with safe `Content-Disposition`; never return raw file-system paths or provider URLs.

## 9. Multi-step operations and dependencies

1. **Token acquisition:** `SignApiTokenService` posts the configured client-credentials request to the configured identity-provider token endpoint, caches the returned access token for its returned lifetime, and supplies it to the SIGN API client. Postman variables `access_token`, `apiToken`, and `bearerToken` are replaced by backend token state. The OAuth token is not exposed to React.
2. **Certificate discovery:** list certificates, select an authorized certificate, then optionally inspect details/chain or authenticator options.
3. **Remote signing:** request challenge; hold returned transaction ID server-side; collect OTP/PIN only through the separate backend-hosted input; perform any required authorization exchange; hold SAT server-side; invoke sign; stream the result. The collection includes 202 challenge-not-verified examples and no polling endpoint. Do not invent a poll/retry loop.
4. **Automatic signing:** select configured signer/domain, upload, compose the format-specific request, sign, and expose result/download. The collection does not define idempotency or `applicationId` semantics.
5. **Validation:** upload one signed document to `POST /api/validation/signature`; submit the fixed `ARCHIVAL_DATA` request to configured `ValidationApi:BaseUrl` using the dedicated validation token; return the sanitized report. Detached originals and other profiles are unsupported in this iteration.
6. **Timestamp:** upload a timestamp-query file and receive a response. The collection does not describe whether the response is an artifact, its filename, or how it should be associated with an input.

No long-running operation model or provider status endpoint is specified. Implement only synchronous workflows until a verified provider contract establishes how 202/207 responses continue.

## 10. Error handling

- Validate file size/type, selected option allowlists, authorization, operation ownership, and request shape before proxying.
- Translate known provider outcomes to stable UI-safe errors while preserving the original status in protected server telemetry. In Development only, allowlisted provider error text and status metadata may be returned after redaction; all other environments return only a generic title, HTTP status, and correlation ID. Handle 202 and 207 as distinct statuses; their business semantics remain ambiguous.
- Use a correlation ID generated by the backend and include it in safe client errors and logs.
- Return actionable user messages for validation failures, rejected signatures, expired sessions, and service availability without exposing provider stack traces, `cause`, tokens, or request bodies.
- Do not silently retry non-idempotent signing operations. Retry policy requires provider idempotency guarantees.

## 11. Logging and privacy

Log operation category, internal correlation ID, authenticated tenant/user reference in a privacy-approved pseudonymous form, provider route template, duration, status, and byte counts. Apply access controls and retention to logs.

Never log request/response bodies, authorization headers, Basic credentials, bearer tokens, SAT, PIN, OTP, transaction IDs, uploaded document content, signed content, raw validation reports, personal identity fields, or unredacted filenames. Sanitize provider errors before logging and before returning them. Add automated tests that assert secret-bearing fields are absent from logs and client models.

## 12. Testing strategy

- **Backend unit tests:** mapping and allowlist validation, route-to-provider selection, normalized error mapping, operation ownership, `expires_in`-based token-cache expiry, invalid token-response handling, and configuration validation.
- **Backend integration tests:** use fake `HttpMessageHandler`/test server for the OAuth token endpoint, signing, validation and timestamp APIs. Assert the configured client authentication and form body, Bearer attachment to SIGN calls, expiry/reacquisition behavior, and prove sentinel credentials/tokens never appear in BFF responses or logs.
- **File tests:** bounded upload, invalid type/size, stream behavior, safe download disposition, cleanup/expiry, detached-file mapping, and binary/ZIP/multipart pass-through after contract confirmation.
- **Frontend tests:** accessible forms, option gating, file selection, workflow transitions, sanitized errors and downloads. Assert that PIN, OTP, SAT, bearer token, and client credential fields are absent from React routes, API DTOs, storage, and network calls.
- **End-to-end tests:** run against a mock provider only; cover certificate selection, validation, automatic signing, and remote challenge continuation without real credentials or external systems.
- Keep tests deterministic and independent. Do not call real external services from unit tests. Introduce live sandbox contract tests only as an explicitly isolated, opt-in suite after secrets and data are approved.

## 13. Incremental implementation plan

1. **Requirements and risk decisions:** for production, approve user authentication/tenant mapping, role model, supported API variants, secret-entry constraints, file limits/retention, and certificate-issuance scope. The local PoC does not require user authentication or multi-tenancy and must not be publicly exposed. Resolve the questions in [ui-api-mapping.md](./ui-api-mapping.md).
2. **Provider contract confirmation:** configure and verify the target token endpoint, client credential delivery, approved OAuth scope, and SIGN API origins; obtain authoritative schemas for string encodings, option enums, `applicationId`/`requestId`/hash semantics, challenge/authorization, 202/207 continuation, and result downloads. The collection identifies the client-credentials grant and an example token lifetime field, but examples do not establish deployment-specific values. Update the mapping document.
3. **Foundation:** solution/API and client projects, typed SIGN configuration, local secret handling, `SignApiTokenService` with response-lifetime token caching, a server-side `SignApiClient`, a Development-only auth diagnostic, and fake-provider tests are implemented. Production secret-store integration, user authorization, safe error/correlation infrastructure, and broader provider adapters remain follow-up work. Provider endpoint, credentials, and approved scope are explicit server configuration; no guessed credential or scope values are defaults.
4. **Read-only workflows:** implement certificate list/detail/chain and UI capabilities with authorization and safe response DTOs.
5. **Validation:** implement upload, detached originals, confirmed options, report display, download if supported; add mapping and file tests.
6. **Automatic signing and timestamping:** implement only confirmed profiles and configuration; add server-side sensitive input if PIN is needed and approved; protect output streaming.
7. **Remote signing:** implement challenge/authenticator/sign sequence with server-held transaction/token/SAT state and backend-hosted PIN/OTP entry; do not expose secrets to React.
8. **Operational hardening:** add retention cleanup, rate limits, audit policy, observability redaction tests, accessibility and end-to-end tests; then consider certificate issuance or additional profiles separately.

Provider workflow implementation must wait until steps 1-2 establish enough contract to avoid inventing provider behavior. The SIGN OAuth mechanism itself is defined for implementation as a backend service, with deployment-specific endpoint, client credentials, and provider-approved scope supplied by configuration; no sample values become defaults.

## 14. Unresolved design questions

1. For production (not the local PoC), which TrustPlatform identity provider, login/session model, tenant boundary, and authorization policy should protect the BFF?
2. Does “React must never receive secrets” permit a separately served ASP.NET Core sensitive-entry page with native POST for user-entered PIN/OTP, or must remote/PIN signing use an out-of-band provider flow and remain unavailable otherwise?
3. Which provider-approved OAuth scope, deployment-specific token endpoint, client credential provisioning, and production secret manager should be configured? The collection shows `client_credentials`, an example request scope, and an `expires_in` response field; it does not define a universal deployment scope or secret-store product. The backend uses the returned token lifetime and does not assume refresh-token behavior.
4. Which signing formats/profiles, authenticator types, validation options, and certificate issuance workflows are in product scope?
5. What are the authoritative MIME/encoding/file-size constraints and original-document association rules?
6. What do `applicationId`, per-signature `requestId`, `hash`, `Prefer`, `emitCertificateRequest`, and `isVisible` mean to the provider?
7. How do provider 202/207 results continue, and are any signing operations idempotent or safely retryable?
8. What response negotiation, download names, retention, audit trail, and report disclosure policy are required?

## 15. Implemented first signing workflows

The enabled combinations are **AUTOMATIC + CAdES** and **AUTOMATIC + PAdES**. REMOTE, XAdES, and HASH remain visible but disabled and cannot submit.

- Provider request: `POST {SignApi:BaseUrl}/certificates/{certificateId}/sign`, JSON (`application/json`), OAuth Bearer, `X-signer-id`, and `Infocert-SAT`. Certificate ID is validated and URL-escaped by the backend as one path segment.
- Postman contract note: the Automatic folder's `01. cadesSign` uses `{{automaticSign-domain_alias}}/sign` and has no Certificate ID. The `/certificates/{...}/sign` structure is shown by `Sign API - remote Signature / 5a. cadesSign - auto` with a fixed staging certificate identifier and no saved response. The product owner explicitly approved using that certificate-path structure for this Automatic + CAdES PoC. The saved response model is taken from the Automatic `01. cadesSign` response example.
- Required request JSON: configured `applicationId`, user-entered `pin`, and one `cadesSignatures` entry with backend-generated `requestId`, `document.content` (Base64), `document.contentType` (`application/pdf`), `packaging`, and `signatureLevel`. No PAdES-only fields are sent.
- Supported packaging values: `ENVELOPED` and `ENVELOPING`. Only `BASELINE-B` is enabled and selected by default; `BASELINE-T`, `BASELINE-LT`, and `BASELINE-LTV` are visible but disabled and rejected by the backend. The Postman CAdES body example uses `BASELINE-T`.
- Frontend multipart fields are `file`, `certificateId`, `xSignerId`, `pin`, `sat`, `packaging`, and `signatureLevel`. Certificate ID and X-signer-id are independent values; Certificate ID is used only in the provider path and X-signer-id only in the header. OAuth token, `applicationId`, request ID, provider URL, and Authorization header are not accepted from React.
- Development only: the backend maps `GET /api/signing/automatic/cades/defaults` and returns `certificateId`, `xSignerId`, `pin`, `infocertSat`, `packaging`, and `signatureLevel` from strongly typed options. Unsupported configured signature levels fall back to `BASELINE-B`; unsupported packaging falls back to the first supported packaging value. The endpoint is not mapped outside Development.
- Upload policy: one PDF, default 10 MiB. This limit is configurable by `SignApi:Upload:MaxFileSizeBytes` and is a TrustPlatform PoC limit, not a provider limit. The server validates extension, size, and `%PDF-` signature bytes, then uses `application/pdf`; it does not trust browser MIME metadata.

### Automatic + PAdES

- Provider request: `POST {SignApi:BaseUrl}/certificates/{certificateId}/sign` with OAuth Bearer, `X-signer-id`, `Infocert-SAT`, and JSON.
- Required PAdES JSON: configured `applicationId`, user-entered `pin`, and one `padesSignatures` entry containing a backend-generated `requestId`, Base64 `document.content`, `document.contentType: application/pdf`, `packaging`, `signatureLevel`, and string `isVisible`.
- Visible signatures include one `signatureFields` entry: string `isPdfa` and numeric `position.page`, `llx`, `lly`, `urx`, and `ury`. Invisible signatures omit `signatureFields`.
- The collection confirms string values for `isVisible` (`"true"`/`"false"`). Coordinate semantics remain ambiguous; this PoC preserves the requested defaults and does not rewrite coordinates.
- PAdES defaults are `isVisible=true`, `isPdfa=true`, `page=1`, `llx=100`, `lly=100`, `urx=20`, `ury=20`. They are under `SignApi:Defaults:AutomaticSigning:Pades`.
- The provider success shape is `applicationId` plus `signatureResult[]` with matching `requestId`, Boolean `isOk`, and `signedDocument.content/contentType/attachName`. TrustPlatform returns validated PDF bytes with a generated filename and does not trust `attachName`.
- The saved provider response is JSON with `applicationId` and a `signatureResult` item containing `requestId`, `isOk`, and `signedDocument.content`, `contentType`, and `attachName`. TrustPlatform validates `requestId`, success, Base64 content, and `application/pkcs7-mime`, ignores provider filename/path data, and returns a controlled download named with a generated ID.
- The client displays only safe success/error state, original filename, generated download filename, and generated operation ID. It does not display raw provider response content or errors.

Other signing methods, types, and profiles remain unsupported pending their own verified contracts.
