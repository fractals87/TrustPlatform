# UI to API mapping (proposed)

**Status:** Design mapping with the initial health/UI skeleton, SIGN OAuth authentication layer, and the first business operation (Automatic + CAdES) implemented. The local PoC does not require TrustPlatform user authentication or multi-tenancy and must not be publicly exposed; production caller authentication and tenancy remain separate decisions. The source [API catalog](./api-catalog.md) describes examples from the Postman collection, not a normative provider schema. Only the fields and values below are enabled for the selected request.

## 1. Parameter classification

Each source parameter is assigned one of these categories:

1. **User-entered** — supplied as task data by a user; a TrustPlatform login is not part of the local PoC.
2. **Selected from predefined values** — a backend-approved option, not free-form provider JSON.
3. **Generated automatically** — created or derived by TrustPlatform after the format/encoding contract is confirmed.
4. **Read from backend configuration** — deployment, tenant, or approved service configuration; never returned to React.
5. **Obtained from a previous API response** — retained and consumed by the backend; never forwarded to React as a credential or opaque provider state.
6. **Unsupported** — excluded until its semantics, allowed values, security treatment, or encoding are confirmed. The reason is stated.

For secrets, **user-entered** means entered through a separate ASP.NET Core-hosted sensitive-input page using a direct native POST. It does not mean a React component, React state, or React JSON request may contain the value. If that entry surface is not approved, the dependent workflow is unsupported.

## 2. UI grouping and route-to-request map

**Implemented-flow exception:** the supported Automatic + CAdES PoC uses masked transient React inputs for PIN and Infocert-SAT, posts them to the same-origin BFF, and clears both after the request. The earlier sensitive-input rule applies to other, unsupported workflows.

| UI workflow | Postman request groups | Backend behavior |
|---|---|---|
| Sign a document | Approved Automatic + CAdES/PAdES workflow | Implemented for Automatic + CAdES and Automatic + PAdES. Upload one PDF, collect Certificate ID, separate X-signer-id, PIN, SAT, packaging, and `BASELINE-B`. React calls the corresponding CAdES or PAdES endpoint; the backend composes the provider request and returns validated signed bytes as a controlled download. Remote/XAdES/Hash are visible but disabled. |
| Validate a signature | `ValidationAPI / CAdES`, `PAdES`, `XAdES`, `JAdES`, `ASiC-S`, `ASiC-E`, `Bad Requests`, `Validation Errors`, `Templates` | Implemented as `POST /api/validation/signature` with one signed file from the current `.pdf`, `.p7m`, `.xml`, `.p7s` PoC allowlist. React uploads multipart/form-data only to TrustPlatform; the BFF sanitizes the basename, Base64-encodes bytes, and sends the confirmed JSON contract to configured `ValidationApi:BaseUrl` (the provider `/validation/signature/v3` endpoint). Validation uses its dedicated OAuth client with `client_credentials` and `scope=validation`. Detached originals and other profiles are not implemented. |
| Certificates list | SIGN API `GET /certificates` | On page entry, `GET /api/certificates/defaults` returns only configured identifiers. The UI displays editable X-signer-id and calls `POST /api/certificates` only after submit; the backend forwards it as the provider `X-signer-id` header. Certificate ID is not required by the list contract. |
| Certificate details | SIGN API `GET /certificates/:certificateId` | On page entry, `GET /api/certificates/defaults` returns the configured identifiers. The UI displays editable Certificate ID and X-signer-id and calls `POST /api/certificates/configured` only after submit; the backend puts Certificate ID in the provider path and X-signer-id in the provider header. |
| Timestamp a request | `Sign API - automatic Signature / 06. timestamp` | Upload a timestamp-query file to the BFF; proxy with configured content type; stream the response. |
| Authentication plumbing | `Authentication`; `ValidationAPI / Templates / 01_get oauth token`; `Templates / Remote signature / get oauth token` | SIGN and VALIDATION API use separate backend-only OAuth `client_credentials` services and token caches. The VALIDATION API request uses scope `validation`. Diagnostics are Development-only and return safe metadata, never tokens or credentials. Never expose token acquisition as a browser endpoint. |

Do not create a page for each of the 92 requests. Top-level folder/request names are not guaranteed product features. Keep certificate issuance (`emitCertificateRequest`) disabled pending provider and privacy approval.

## 3. Host and URL parameter mapping

The collection has no query parameters. URI scheme, static path segments, and HTTP methods are implementation constants selected by the backend adapter, not user inputs.

| Collection variable or path value | Classification | Mapping / limitation |
|---|---|---|
| `api.url`, `idp.url`, `validation.url`, `timestamp.url`, `base.url`, `baseUrl` | Read from backend configuration | Separate named API origins/configuration. `base.url` versus `baseUrl` and `api.url` versus `validation.url` usage are inconsistent; consolidate only after confirming environments. Never accept a URL from React. |
| `idp.url/token` | Read from backend configuration | For SIGN OAuth, configure the identity-provider origin server-side and append the collection's `/token` path in the backend token client. Do not accept an OAuth endpoint URL from React or copy the fixed staging URL into application code. |
| `automaticSign-domain_alias`, `eSeal-domain_alias`, `domain_alias` | Read from backend configuration | These values belong to the separate domain-alias Postman routes and are not used by the implemented certificate-path signing route. Never expose or accept arbitrary aliases. |
| `automaticSign-xSignerID`, `eSeal-xSignerID`, `xSignerID` | Read from backend configuration | For the local single-configuration PoC, resolve the signer ID from backend configuration and never accept it from React. In production, bind it to authenticated user/tenant context; do not trust a client-supplied signer ID. |
| Fixed staging hosts in collection | Read from backend configuration | Treat only as example environment origins. Configure per deployment; do not hardcode the staging origin in a browser bundle. |
| Fixed certificate ID in the staging path | USER INPUT (approved PoC contract) | The Postman `5a. cadesSign - auto` example embeds a fixed identifier. TrustPlatform does not reuse it; the user enters a Certificate ID which is allowlist-validated and URL-escaped. |
| `:authType` | Obtained from a previous API response | Resolve from the backend authenticator-list response and retain the selection on the server. Accepted literal values are not separately documented. |
| `:certificateId` | USER INPUT (approved Automatic + CAdES PoC only) | Certificate ID is entered by the user and validated before use as one path segment. Certificate-list-driven workflows may instead use an authorized previous API response. |
| `{certificate-id}` in the remote template paths | Unsupported | Not Postman `{{...}}` syntax; substitution/encoding is unspecified. Confirm or remove before enabling those examples. |
| Query parameters | None in supplied collection | Do not add a React or BFF query field for an upstream query parameter without a new provider contract. |

## 4. Authentication, headers, and secret classification

| Source parameter | Classification | Mapping |
|---|---|---|
| Basic-auth `username`, `password`; `client.username`, `client.password` | Read from backend configuration | For the primary `Authentication / 01. get oauth token` request, use backend-only client credentials with the collection's inherited Basic authentication. Load values from server configuration/secrets; never accept them from React or copy Postman values. Some other saved token templates do not define a credential source; do not infer those configurations from the SIGN flow. |
| OAuth `grant_type` | Generated automatically | Fixed to `client_credentials` for both implemented server-side OAuth clients. It is not a user-editable form field. |
| SIGN OAuth `scope` | Read from backend configuration | Configure the provider-approved SIGN API scope, currently `signature`. |
| VALIDATION API OAuth `scope` | Generated automatically | Fixed to `validation` by the confirmed contract. Do not add a React scope field or reuse the SIGN token/cache for validation. |
| OAuth `Authorization: Bearer <access_token>`; `access_token`, `apiToken`, `bearerToken` variables | Obtained from a previous API response | `SignApiTokenService` acquires and caches the access token server-side using the returned `expires_in`, and the SIGN API client attaches it as a Bearer token. Never return it to React or store it in browser storage, logs, or client-visible errors. Explicit authorization headers in `noauth` requests are still sensitive and must be constructed server-side. |
| `X-signer-id` | User-entered (Automatic + CAdES PoC only) | `xSignerId` is submitted separately from `certificateId` and forwarded only as a provider header. Production must authorize its mapping to the authenticated user/tenant. |
| `Infocert-SAT` (Automatic + CAdES PoC) | User-entered | Masked transient React input, sent as multipart `sat`, forwarded only as the `Infocert-SAT` provider header, and cleared after the request. It is not the OAuth Bearer token. Other workflows may obtain SAT from a prior provider response. |
| `Otp` header and `otp` body field | User-entered | Collect only through the separate backend-hosted sensitive-input page and forward directly from ASP.NET Core. No React field, local storage, URL, or JSON DTO. |
| `pin` body field (Automatic + CAdES PoC) | User-entered | Masked transient React input, sent as multipart `pin` and forwarded only in the provider JSON body. Cleared after the request and never written to browser storage. Certificate-issuance PIN remains unsupported. |
| `Transaction-Id` / `Transaction-id` header and body `transactionId` | Obtained from a previous API response | Challenge response state remains backend-side. Header-name casing differs in examples; HTTP field names are case-insensitive, but verify the provider's expected header. Never expose the ID to React. |
| `X-Request-ID` | Generated automatically | Generate a TrustPlatform correlation value and forward only if the provider contract confirms this header. Do not re-use it as an idempotency key without confirmation. |
| `Prefer` | Unsupported | Collection values are opaque example text; semantics and allowed values are undocumented. Do not forward or create a UI control until confirmed. |
| `Content-Type` | Generated automatically | Set from the selected, validated request body. For timestamp query, the collection explicitly uses `application/timestamp-query`. For other bodies, verify actual provider expectations; do not let React choose arbitrary values. |
| Challenge `scope` | Unsupported | The challenge body's accepted values and relationship to OAuth scope are not established. Keep distinct from OAuth `scope`; do not forward or expose a user control until confirmed. |

## 5. Body field mapping

Types and field paths reflect the example bodies. **Unsupported** means do not expose or proxy the field until the named contract gap is resolved.

### OAuth, challenge, authorization, and validation bodies

| Body field | Classification | UI/API mapping |
|---|---|---|
| OAuth `grant_type` | Generated automatically | Set to `client_credentials` on the backend for the SIGN OAuth token request. |
| OAuth `scope` | Read from backend configuration | The request example uses `signature`; configure the provider-approved value for the target environment. Do not infer the required scope from the token response or make it user-editable. |
| Challenge `scope` | Unsupported | Separate challenge field; its accepted values and semantics are undocumented. It is not the OAuth token request's `scope`. |
| `deviceId` | Unsupported | Source, ownership, and whether it is user/device registration data are undocumented. Do not fabricate or request it until confirmed. |
| `signaturesNumber` | Unsupported | Whether it is a user choice, count of selected signatures, or provider-specific limit is unknown. |
| `transactionId` | Obtained from a previous API response | Server-held challenge result; not a React form field. |
| `otp` | User-entered | Separate backend-hosted sensitive-input page only. |
| `pin` | User-entered | Separate backend-hosted sensitive-input page only; never part of a React-facing DTO. |
| `signedDocument.data` | Generated automatically | For the confirmed validation request, read the uploaded file at the BFF and serialize its bytes as Base64. Never send the multipart body to the provider or expose Base64 to React. |
| `signedDocument.name` | Generated automatically | Send only the sanitized original filename basename; never send a client filesystem path. |
| `originalDocuments[].data` | Unsupported | Source is each user-uploaded detached original; transfer encoding is not documented. Keep the files in the BFF path until confirmed. |
| `originalDocuments[].name` | Generated automatically | Derive a safe normalized name from each uploaded file after confirming provider association/name rules. |
| `originalDocumentsContentType` | Unsupported | The field's exact meaning, accepted values, and relationship to per-file media types are undocumented. Deriving it from file metadata requires contract confirmation. |
| `validationOptions.validationLevel` | Selected from predefined values | Fixed to `ARCHIVAL_DATA` for this TrustPlatform iteration; not user-editable. |
| `validationOptions.extractOriginals` | Generated automatically | Fixed JSON Boolean `false`; do not serialize as a string or a Postman placeholder. |
| `validationOptions.includeEtsiValidationReport` | Generated automatically | Fixed JSON Boolean `false`; do not serialize as a string or a Postman placeholder. |

### Automatic and remote signature bodies

| Body field | Classification | UI/API mapping |
|---|---|---|
| `applicationId` | Unsupported | Creation, uniqueness, idempotency, and lifecycle are undocumented. Do not let users enter it or generate a value until the provider contract confirms its purpose. |
| `pin` | User-entered | Server-hosted sensitive entry only. |
| `cadesSignatures[]`, `padesSignatures[]`, `xadesSignatures[]` | Generated automatically | ASP.NET Core composes the provider arrays from uploaded file(s), selected enabled format, and approved options. Do not accept arbitrary arrays/JSON from React. |
| `*.requestId` | Unsupported | Correlation/uniqueness rules are undocumented. Generate server-side only after confirming the accepted format and response association semantics. |
| `*.document.content` | Unsupported | Derived from a user upload, but provider encoding is undocumented. Do not invent a base64/raw-content transformation. |
| `*.document.contentType` | Unsupported | Likely media type by field name, but accepted values and consistency requirements are unspecified. Enable backend derivation only after confirmation. |
| `*.document.attachName` | Unsupported | Example only; provider naming rules and whether the field is required are undocumented. Do not accept an arbitrary path or send an unvalidated filename. |
| `*.packaging` | Unsupported | No supported enum is defined. When confirmed, map a format-specific UI selection to an allowlisted value. |
| `*.signatureLevel` | Unsupported | Request names suggest profiles such as B/T/LTA/BT, but exact API values and supported combinations are not documented. Enable only as predefined values after confirmation. |
| `padesSignatures[].isVisible` | Implemented | Automatic PAdES serializes the confirmed string values `"true"` or `"false"`. Invisible signatures omit `signatureFields`. |
| `padesSignatures[].signatureFields[]` | Implemented (one field) | Visible PAdES sends exactly one field with `isPdfa` and a numeric position. Multiple fields remain unsupported. |
| `padesSignatures[].signatureFields[].position.llx`, `lly`, `urx`, `ury` | Implemented with ambiguity documented | Numeric position values use the collection defaults. Coordinate units/origin remain provider-contract ambiguities; TrustPlatform does not rewrite values. |
| `hashSignatures[].hash` | Unsupported | Hash algorithm, input bytes, encoding, and prefix format are unknown. Never accept a raw user hash or calculate one by assumption. |
| Entire `03. hashSign` request body | Unsupported | Its saved raw-JSON example is not parseable as JSON. Exclude until a valid provider schema is obtained. |
| `emitCertificateRequest` | Unsupported | Certificate-issuance contract, consent, privacy basis, required fields and process are not established. Keep the one-shot issuance workflow disabled. |

### Certificate-issuance fields (`emitCertificateRequest`)

These are all present in the collection's example but are not an approved UI contract. Personal/identity fields require a purpose, data-minimization review, access controls and retention decision. Do not enable this workflow just because a field appears in the sample.

| Body field | Classification | UI/API mapping |
|---|---|---|
| `assertions[].type_proof` | Unsupported | Accepted proof-type vocabulary and proof requirement are undocumented. |
| `assertions[].id` | Unsupported | Assertion identifier source and permitted values are undocumented. |
| `assertions[].value` | Unsupported | Meaning, sensitivity, format, and allowed values are undocumented. |
| `holder.identification.type` | Selected from predefined values | Identity type must come from a provider-approved list; list/meaning is not supplied, so keep issuance disabled. |
| `holder.identification.country` | Selected from predefined values | Country code/list format is not documented; require an approved code list. |
| `holder.identification.document.type.value` | Selected from predefined values | Document type vocabulary is missing; keep issuance disabled until supplied. |
| `holder.identification.document.number.value` | User-entered | Identity/document number; collect only in an approved issuance form with privacy and retention controls. |
| `holder.identification.document.emitter.value` | User-entered | Issuing authority; required format/validation is undocumented. |
| `holder.identification.document.emissionDate.value` | User-entered | Date input; exact expected format/timezone is undocumented. |
| `holder.identification.document.endOfValidityDate.value` | User-entered | Date input; exact expected format/timezone is undocumented. |
| `holder.identification.document.country.value` | Selected from predefined values | Country code/list format is not documented; require an approved code list. |
| `holder.identification.requester` | Unsupported | Whether this is a user, organization, or provider identifier is unclear. |
| `holder.contact.email` | User-entered | Personal contact data; validate format and purpose after issuance scope is approved. |
| `holder.contact.telephone` | User-entered | Personal contact data; country/number format is undocumented. |
| `holder.contact.address` | User-entered | Personal address; field structure/length is undocumented. |
| `holder.contact.postalCode` | User-entered | Personal data; country-specific format is undocumented. |
| `holder.contact.municipality` | User-entered | Personal data; accepted format is undocumented. |
| `holder.contact.province` | User-entered | Personal data; accepted format is undocumented. |
| `holder.contact.country` | Selected from predefined values | Country list and code format need provider confirmation. |
| `holder.personalDetails.firstName` | User-entered | Personal data; validation and necessity require approval. |
| `holder.personalDetails.lastName` | User-entered | Personal data; validation and necessity require approval. |
| `holder.personalDetails.countryCode` | Selected from predefined values | Country-code standard is not specified. |
| `holder.personalDetails.serialNumber` | User-entered | Potentially sensitive identifier; purpose, format and retention need approval. |
| `holder.personalDetails.dateOfBirth` | User-entered | Sensitive personal data; format and necessity need approval. |
| `holder.personalDetails.gender` | Selected from predefined values | Vocabulary and whether this field is necessary are unresolved. |
| `holder.personalDetails.nationality` | Selected from predefined values | Country/nationality code list and format need confirmation. |
| `holder.personalDetails.birthPlace` | User-entered | Personal data; format and necessity need approval. |
| `holder.organization.name` | User-entered | Organization information; validation and issuance purpose need approval. |
| `holder.organization.id` | User-entered | Organization identifier; type/format are undocumented. |
| `holder.organization.country` | Selected from predefined values | Country list and code format need confirmation. |
| `emitCertificateRequest.pin` | User-entered | Secret; separate backend-hosted sensitive-entry page only. |

### Timestamp request body

| Input | Classification | UI/API mapping |
|---|---|---|
| Timestamp-query file | User-entered | User uploads a file; BFF sends the file body with the collection's explicit `application/timestamp-query` content type. The exact accepted file/response contract and size limits need confirmation. |

## 6. Response mapping

| Provider response field/data | Classification | Frontend exposure |
|---|---|---|
| OAuth `access_token` | Obtained from a previous API response | `SignApiTokenService` holds the access token only in backend runtime state. Attach it as a Bearer token only to configured SIGN API requests. Never return it to React. |
| `transactionId` | Obtained from a previous API response | Backend challenge state only. |
| `sat` | Obtained from a previous API response | Backend authorization state only; never return it as a signing result. |
| `applicationId` in a signing result | Unsupported | Semantics and sensitivity are undocumented. Do not display or use as an idempotency key without confirmation. |
| `signatureResult` | Unsupported | Shape varies/has no schema in the collection. Treat as backend provider data; expose only confirmed safe metadata and downloads. |
| Signed document bytes, ZIP, multipart attachments, timestamp response | Obtained from a previous API response | Stream through an authorized BFF download response. MIME type, filename, and response framing require contract confirmation. |
| Validation `valid` | Obtained from a previous API response | Return as a normalized Boolean only if provider type is confirmed. |
| `validationReport`, `originalDocuments` | Obtained from a previous API response | Return only a sanitized, contract-backed subset; do not assume nested schema or expose unreviewed content. |
| Certificate `certificate`, `ids`, `subject`, `issuer`, `status`, `expirationDate` | Obtained from a previous API response | Return only fields needed by the UI and authorized for the current tenant. Exact field schemas are not supplied. |
| `auxiliaryData` | Unsupported | QR/challenge payload format and rendering behavior are undocumented. Do not render or relay until confirmed. |
| `title`, `status`, `detail`, `cause`, `stacktrace` error fields | Unsupported | Normalize safe error code/message. Never return stack trace or raw `cause`; retain only redacted diagnostics server-side. |
| Array response values (`authenticators`, certificates, chains) | Obtained from a previous API response | Array element schemas are not defined by the collection; do not bind UI to guessed item properties. |
| Token response `expires_in` | Obtained from a previous API response | Determine the cached access token's expiry from the value returned by the token endpoint; do not hardcode the saved example duration. Acquire a new token after expiry. Reject an absent/invalid lifetime rather than silently treating the token as unexpired. |
| Token response `token_type` | Obtained from a previous API response | The saved example is `Bearer`; the backend uses Bearer authorization for SIGN calls and must not send a token with an incompatible token type. |
| Token response `refresh_expires_in`, `not-before-policy`, `scope` | Obtained from a previous API response | Backend-only response fields. The collection does not document refresh-token use, claim validation, or a required scope mapping from the response; do not implement refresh behavior or return these fields to React based on the examples. |
| Nested signing/validation response fields `isOk`, `certificateChain`, `claimedSigningTime`, `documentFileName`, `issuerDN`, `messageDigest`, `messages`, `notAfter`, `notBefore`, `productionTime`, `section`, `signatureAlgorithm`, `signatureError`, `signatureFormat`, `signatureId`, `signatureQualification`, `signaturesCount`, `signedAttributes`, `signerName`, `signingCertificateId`, `signingTime`, `statusDetails`, `subjectDN`, `timestampId`, `timestampIds`, `timestamps`, `validationPolicyDescription`, `validationPolicyName`, `validationTime`, `validSignaturesCount` | Obtained from a previous API response | These names occur inside saved response examples, but their nested schema and user-safe subset are not specified. Keep raw values server-side and unsupported for display until reviewed against a provider schema. |

## 7. Endpoint families and dependencies

| Family | Upstream request patterns | BFF/client mapping |
|---|---|---|
| OAuth for SIGN API | `POST {{idp.url}}/token` | ASP.NET Core submits the configured client credentials using the collection's inherited Basic authentication, with form fields `grant_type=client_credentials` and the configured provider-approved scope. `SignApiTokenService` caches `access_token` for returned `expires_in`; the SIGN API client attaches it as Bearer. No token endpoint or provider token exists in the React API. |
| Automatic signing | `POST {{api.url}}/{{...domain_alias}}/sign` | Upload + approved profile; alias, signer ID, OAuth Bearer token, workflow SAT/auth and provider JSON are resolved server-side. |
| Remote challenge | `POST .../authenticators/{authType}/challenge` | Authenticator comes from a prior backend lookup; challenge ID is retained on server. |
| Remote authorization | `POST .../{certificateId}/{authType}/authorize` | Certificate/authenticator selection is bound to the operation; PIN/OTP input follows the server-hosted sensitive-input rule; returned SAT is server-only. |
| Remote signing | `POST .../certificates/{certificateId}/sign` or `/oneshot/sign` | Use prior challenge/authorization state. The collection's no-auth plus explicit Authorization pattern is handled as backend auth, not copied to the UI. |
| Certificate discovery | `GET .../certificates`, detail and chain paths | User selects from a sanitized, authorized backend list. Never accept a path ID as proof of authorization. |
| Validation | `POST /document` | Upload, select confirmed format/profile, add detached sources where applicable; no provider token in the request from React. |
| Timestamp | `POST /apply` | Upload timestamp query and stream result. |

Collection scripts write `access_token`, `apiToken`, `bearerToken`, and `transactionId`. In TrustPlatform, OAuth token values and provider transaction state are backend state derived from provider responses, not browser variables. OAuth client credentials authenticate TrustPlatform to the provider. PIN, OTP, Infocert-SAT, `X-signer-id`, and transaction IDs belong to individual workflows and are not interchangeable with OAuth credentials or access tokens. The collection does not wire signing output into validation input; that may be offered as a separate user action after the signing result is safely downloaded or retained under an approved policy.

## 8. Reusable UI and API components

| Component | Responsibility |
|---|---|
| `FileDropzone` and `FileList` | File selection and display of safe file metadata; no persistent file content in browser storage. |
| `CertificateSelect` | Display safe certificate choices returned by BFF; submit an opaque selection. |
| `FormatProfileSelect` | Render only backend-approved enums. Hidden/disabled for unconfirmed values. |
| `SigningForm` | Compose user-approved signing intent and file references; no provider body, secret, token, or header. |
| `ValidationForm` | Compose file(s), format and confirmed validation options. |
| `WorkflowStepper` | Present local progress without transaction IDs, SAT, or provider secrets. |
| `SensitiveInputPage` | Separate ASP.NET Core-hosted native form for PIN/OTP if explicitly approved; not a React component. |
| `DownloadAction` | Request an authorized operation download; never navigate to provider-supplied URLs. |
| Backend `ISignApiClient`, `IValidationApiClient`, `IIdentityProviderClient`, `ITimestampApiClient` | Typed server-side adapters; use named `HttpClient`, allowlisted routes and redacted telemetry. |
| Implemented `SignApiTokenService` | Uses server-configured client credentials to POST the collection's form-encoded `client_credentials` grant and configured scope to the identity-provider `/token` endpoint; caches the token using returned `expires_in`. Token values remain backend-only. |
| Implemented `SignApiClient` | Resolves relative paths against configured `SignApi:BaseUrl` and attaches the cached Bearer token. It is infrastructure only; no signing business operation is enabled. |
| `POST /api/diagnostics/sign-auth` | Development-only PoC diagnostic; returns only authentication status, `Bearer` token type, returned lifetime, or a sanitized error. Never exposes credentials or access token. |
| Backend operation/result DTOs | Return safe operation status, normalized report metadata, and internal download IDs only. |

## 9. Error, file, and logging rules

- Reject unknown fields and options. Do not pass through arbitrary JSON, URLs, headers, enums, or file metadata.
- Enforce upload and download authorization, approved limits, and safe filenames on the server. The collection does not specify file limits, content encoding, or retention.
- Stream or use encrypted bounded temporary storage with expiry and cleanup. Persistent document retention requires a separate approved requirement.
- Treat 202 and 207 as distinct statuses. The collection provides no status/poll endpoint or retry/idempotency contract; no automatic polling or non-idempotent retry is inferred.
- Normalize errors to a safe code, message, correlation ID, and optional field issue. Never return `stacktrace`, raw `cause`, tokens, SAT, transaction ID, document bodies, or unreviewed validation reports.
- Logs may include operation category, correlation ID, safe route template, duration, status and byte count. Never log credentials, secret entry, document/signature bytes, personal fields, transaction IDs, or raw provider request/response data.

## 10. Unresolved contract and product questions

1. What identity provider, session mechanism, user authorization, tenant mapping, and certificate ownership model protects the BFF?
2. Is an ASP.NET Core-hosted native PIN/OTP entry page acceptable under the constraint that React never receives secrets? If not, which out-of-band flow should replace it?
3. Which provider-approved OAuth scope, target token endpoint, and deployment credential provisioning should be configured? The SIGN client-credentials grant, Bearer token use, and `expires_in`-based cache lifetime are defined for the backend. The collection does not define a universal scope, production secret-store product, or refresh-token flow. OAuth details for the separate validation/template examples must be checked before sharing the SIGN token service with those APIs.
4. Which CAdES/PAdES/XAdES/JAdES/ASiC profiles and authenticators are approved, and what exact `signatureLevel`, `packaging`, `validationLevel`, and scope values do they map to?
5. What are the required encodings, MIME rules, filenames and size limits for `data`/`content` and detached originals?
6. What are the semantics and constraints for `applicationId`, per-signature `requestId`, `hash`, `isVisible`, `Prefer`, `deviceId`, `signaturesNumber`, and `emitCertificateRequest`?
7. How do challenge, 202, and 207 results continue, and which operations are idempotent/retry-safe?
8. What output formats, download metadata, retention and audit policy are required?
9. Is certificate issuance in product scope, and what privacy/legal approval and data minimization apply to the example identity fields?

## 11. Implemented Automatic + CAdES PoC field mapping

| UI/API field | Source classification | Mapping |
|---|---|---|
| Certificate type | Selected from predefined values | `AUTOMATIC` is enabled. `REMOTE` is visible but disabled and cannot submit. |
| Signature type | Selected from predefined values | `CAdES` is enabled. `PAdES`, `XAdES`, and `HASH` are visible but disabled and cannot submit. |
| Document | Uploaded by the user | Exactly one `.pdf`; client checks extension, browser MIME metadata, and the configured size. The backend enforces size, extension, and PDF magic bytes; it sets provider `document.contentType` to `application/pdf`. |
| `certificateId` | USER INPUT; optional Development SERVER CONFIGURATION default | Required multipart value used only as the validated, escaped path segment in `/certificates/{certificateId}/sign`. Allowlist: ASCII letters, digits, `+`, `_`, `-`; maximum 128 characters. |
| `xSignerId` | USER INPUT; optional Development SERVER CONFIGURATION default | Required multipart value sent only as provider header `X-signer-id`; independent of Certificate ID. |
| `pin` | Entered by the user | Password-masked transient form value; sent as multipart `pin` and mapped to the CAdES JSON `pin`. Cleared on completion, never persisted. |
| `sat` | Entered by the user | Password-masked transient form value; sent as multipart `sat` and mapped to provider header `Infocert-SAT`. Cleared on completion; distinct from OAuth Bearer token. |
| `packaging` | Selected from predefined values; Development SERVER CONFIGURATION default | Exact values: `ENVELOPED`, `ENVELOPING`. |
| `signatureLevel` | Selected from predefined values | `BASELINE-B` is selected by default and is the only enabled/accepted value. `BASELINE-T`, `BASELINE-LT`, and `BASELINE-LTV` remain visible but disabled; the backend rejects them and unknown values. |
| `applicationId` | Read from SignApi configuration | `SignApi:Defaults:ApplicationId`; never sent by React. |
| Provider BaseUrl | SERVER CONFIGURATION | `SignApi:BaseUrl`; backend appends the fixed `/certificates/` prefix, validated and escaped Certificate ID, and fixed `/sign` suffix. No provider URL or path is accepted from React. |
| `requestId` | Generated by the backend | Unique GUID string generated for each provider request; not sent by React. |
| `document.content` | BACKEND GENERATED | Base64 encoded by ASP.NET Core from the validated uploaded PDF bytes; never generated by React. |
| `document.contentType` | BACKEND GENERATED | Set to `application/pdf` only after backend validation of file length, extension, size, and `%PDF-` signature. |
| OAuth access token | Obtained from a previous API response | `SignApiTokenService` caches it; `SignApiClient` adds `Authorization: Bearer` server-side. It is not PIN or SAT. |

The browser sends only `file`, `certificateId`, `xSignerId`, `pin`, `sat`, `packaging`, and `signatureLevel` to `POST /api/signing/automatic/cades`. The provider body contains only `applicationId`, `pin`, and `cadesSignatures` with `requestId`, `document.content`, `document.contentType`, `packaging`, and `signatureLevel`; it has no PAdES `isVisible` or `signatureFields`.

When `AUTOMATIC` is selected, the Development build calls `GET /api/signing/automatic/cades/defaults` to initialize untouched form fields from `SignApi:Defaults:AutomaticSigning`. Its response contains only `certificateId`, `xSignerId`, `pin`, `infocertSat`, `packaging`, and `signatureLevel`, is marked `Cache-Control: no-store`, and is not written to browser storage. Values edited by the user are not overwritten. PIN/SAT defaults are a local-development PoC convenience only; remove the endpoint and configured defaults before shared, staging, or production deployment. Use User Secrets or process environment variables for local credential values, not committed configuration.

The provider path shape is shown by `Sign API - remote Signature / 5a. cadesSign - auto`, which uses a fixed certificate identifier in the collection and has no saved response. The Automatic folder's `01. cadesSign` request instead uses `{{api.url}}/{{automaticSign-domain_alias}}/sign` and has a saved response example. The product owner explicitly approved the certificate-path structure for this Automatic + CAdES implementation and separately approved the Automatic request's saved response shape. The provider call is `POST {SignApi:BaseUrl}/certificates/{certificateId}/sign`, JSON, OAuth Bearer, `X-signer-id`, and `Infocert-SAT`. TrustPlatform converts uploaded PDF bytes to Base64. The saved response example contains `applicationId` and `signatureResult` with `requestId`, `isOk`, and `signedDocument` (`content`, `contentType`, `attachName`). Signed content is Base64 with `application/pkcs7-mime` in the example; TrustPlatform validates and returns the bytes as a generated-name download, without exposing raw provider JSON or trusting `attachName`.

The default 10 MiB limit is a configurable TrustPlatform PoC restriction, not an InfoCert limit. The file is not persisted in browser storage or application storage. Remaining deployment work: configure the provider API base URL and OAuth secrets. The Postman certificate-path request has no saved response; the response structure supplied for this implementation is therefore checked against the saved Automatic CAdES response example. Remote, PAdES, XAdES, Hash, and the disabled baseline levels remain unsupported.
