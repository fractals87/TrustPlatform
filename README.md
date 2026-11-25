# TrustPlatform

TrustPlatform is an ASP.NET Core Web API with a React/TypeScript frontend built by Vite.

The initial TrustPlatform PoC includes:

- `GET /api/health`
- A React shell with Home, Signing, Validation, Certificates list, Certificate details, and Diagnostics navigation
- A same-origin frontend API client and Vite development proxy
- xUnit backend tests and Vitest/React Testing Library frontend tests
- Backend-only SIGN API and VALIDATION API OAuth client-credentials token services
- Development-only SIGN API and VALIDATION API authentication diagnostics
- Automatic + CAdES signing of one PDF document through `POST /api/signing/automatic/cades`
- Automatic + PAdES signing of one PDF document through `POST /api/signing/automatic/pades`
- Signature validation through `POST /api/validation/signature` for one uploaded signed document

Certificate pages load only the configured identifiers from `GET /api/certificates/defaults`; opening either page does not call SIGN API. Explicit user submission invokes `POST /api/certificates` for the list (X-signer-id header only) or `POST /api/certificates/configured` for details (Certificate ID path segment and X-signer-id header). Edited values apply only to that request. Remote signing, XAdES, Hash signing, and timestamping remain not implemented. TrustPlatform user authentication and database storage are also not implemented. Do not add credentials or secrets to frontend configuration.

## Prerequisites

- .NET 10 LTS SDK
- Node.js LTS and npm

## Run locally

Restore and start the backend in one terminal:

```powershell
dotnet restore TrustPlatform.sln
dotnet run --project src\TrustPlatform.Api --launch-profile http
```

The API listens at `http://localhost:5028`. Check its health endpoint at `http://localhost:5028/api/health`.

In a second terminal, install frontend dependencies and start Vite:

```powershell
Set-Location src\TrustPlatform.Web
npm install
npm run dev
```

Open the local URL printed by Vite. Requests to `/api/*` are proxied to the ASP.NET Core backend at `http://localhost:5028`; the React app does not call external APIs.

## Configure the local SIGN API authentication diagnostic

The backend reads non-sensitive defaults from `src\TrustPlatform.Api\appsettings.json`. Set the provider identity-provider URL and SIGN API base URL for the environment, and supply client credentials through .NET User Secrets (never commit them):

```powershell
dotnet user-secrets init --project .\src\TrustPlatform.Api
dotnet user-secrets set "SignApi:Authentication:ClientId" "<client-id>" --project .\src\TrustPlatform.Api
dotnet user-secrets set "SignApi:Authentication:ClientSecret" "<client-secret>" --project .\src\TrustPlatform.Api
dotnet user-secrets set "SignApi:Defaults:AutomaticSigning:Pin" "<local-pin>" --project .\src\TrustPlatform.Api
dotnet user-secrets set "SignApi:Defaults:AutomaticSigning:InfocertSat" "<local-sat>" --project .\src\TrustPlatform.Api
```

Set the environment URLs in the backend process before starting it. `IdentityProviderUrl` is the identity-provider base URL; the backend appends the collection's `/token` path.

```powershell
$env:SignApi__IdentityProviderUrl = "https://<provider-identity-host>/<realm>"
$env:SignApi__BaseUrl = "https://<sign-api-host>/signature/v1"
```

The configured grant and scope default to the collection's `client_credentials` and `signature`. Change them only to values confirmed for the target provider environment. Alternatively provide any setting through the corresponding `SignApi__...` environment variable, including `SignApi__Authentication__ClientId` and `SignApi__Authentication__ClientSecret`.

## Configure the local VALIDATION API authentication diagnostic

The backend also reads a separate `ValidationApi` configuration block. Keep the validation OAuth client separate from the SIGN API configuration and supply the password using User Secrets or an environment variable such as:

```powershell
dotnet user-secrets set "ValidationApi:Authentication:Password" "<validation-password>" --project .\src\TrustPlatform.Api
$env:ValidationApi__Authentication__Password = "<validation-password>"
```

Use the same pattern for `ValidationApi__IdentityProviderUrl`, `ValidationApi__BaseUrl`, and `ValidationApi__Authentication__Username` as needed. The validation OAuth grant and scope are fixed by the confirmed contract to `client_credentials` and `validation`. Do not commit real validation credentials.

Set `ValidationApi:BaseUrl` to the confirmed provider endpoint for the target environment (for example, `https://<validation-host>/validation/signature/v3`). The backend sends `client_credentials` with scope `validation` using the dedicated Validation API token service; do not share the SIGN API token or credentials.

The Development configuration includes blank `CertificateId`, `XSignerId`, `Pin`, and `InfocertSat` defaults; Packaging is `ENVELOPING` and SignatureLevel is `BASELINE-B`. For local form defaults, supply Certificate ID, X-signer-id, PIN, and SAT using User Secrets or process environment variables; do not commit real credentials.

In the Development environment only, `GET /api/signing/automatic/cades/defaults` returns these six form defaults and sets `Cache-Control: no-store`. The React page loads them into component memory and allows editing; it does not persist them in browser storage. Returning PIN and SAT as form defaults is a local-development convenience only, not an acceptable production secret-management strategy. This endpoint must be removed before any shared, staging, or production deployment.

Automatic CAdES signing requires `SignApi:Defaults:ApplicationId`. The user supplies a Certificate ID for the provider path and a separate X-signer-id header value, along with PIN and Infocert-SAT. PIN and SAT are masked, held only in transient React state, cleared after completion, and sent only to the same-origin ASP.NET Core endpoint. The backend uses Certificate ID only in `/certificates/{certificateId}/sign`, and sets `X-signer-id` and `Infocert-SAT` headers server-side.

The configured `SignApi:BaseUrl` must end at the provider API base (for example, the `/signature/v1` base for this path); the backend appends `/certificates/{certificateId}/sign`. Certificate ID accepts only ASCII letters, digits, `+`, `_`, and `-` and is URL-escaped as one path segment.

The Signing page accepts one PDF (`application/pdf`) per request. The default maximum is 10 MiB (`SignApi:Upload:MaxFileSizeBytes`); this is a local TrustPlatform PoC limit, not an InfoCert provider limit. The backend verifies the PDF signature bytes and does not rely on the browser-provided media type. For CAdES, the provider JSON embeds the PDF bytes as Base64. On success, the provider's Base64 `signedDocument.content` is validated and returned as a controlled `application/pkcs7-mime` download with a backend-generated filename.

The enabled packaging values are `ENVELOPED` and `ENVELOPING`. `BASELINE-B` is the only enabled signature level; `BASELINE-T`, `BASELINE-LT`, and `BASELINE-LTV` are visible but disabled. The Automatic folder's `01. cadesSign` request uses a domain-alias URL and has a saved response, while the certificate-path request is `Sign API - remote Signature / 5a. cadesSign - auto`, which contains a fixed certificate identifier and no saved response. Per the approved PoC contract, TrustPlatform uses the certificate path for Automatic + CAdES with the identifier supplied by the user; this distinction is recorded in [docs/decisions.md](docs/decisions.md). Provider response handling follows the saved CAdES response example.

Automatic PAdES is available through `POST /api/signing/automatic/pades`. It also requires one PDF, Certificate ID, X-signer-id, PIN, SAT, packaging, and `BASELINE-B`. The Postman PAdES examples use string `"true"`/`"false"` values for `isVisible`; visible signatures include one field with string `isPdfa` and numeric `page`, `llx`, `lly`, `urx`, and `ury`. The configured defaults are under `SignApi:Defaults:AutomaticSigning:Pades`; invisible signatures omit `signatureFields`. On success, TrustPlatform validates the provider `signatureResult` and returns `signedDocument.content` as a controlled PDF download.

In the Diagnostics page, **Test SIGN API authentication** calls `POST /api/diagnostics/sign-auth`, and **Test VALIDATION API authentication** calls `POST /api/diagnostics/validation-auth`. Both endpoints are mapped only in the Development environment. They return authentication status and safe token metadata, never the token or client credentials. The local PoC is not intended for public exposure.

The validation page uploads exactly one non-empty `.pdf`, `.p7m`, `.xml`, or `.p7s` file to `POST /api/validation/signature`. This is the current TrustPlatform PoC allowlist, not the complete provider-supported format list. ASP.NET Core enforces the configured upload limit, sanitizes the basename, reads the file within bounded memory, Base64-encodes the bytes, and sends JSON to the configured `ValidationApi:BaseUrl` with `validationLevel: "ARCHIVAL_DATA"`, `extractOriginals: false`, and `includeEtsiValidationReport: false`. The response is sanitized into a business result with validity, policy, signature, certificate metadata, timestamps, and grouped messages; certificate blobs are omitted. Provider error details are redacted and shown only in Development, with a correlation ID; production returns a generic error.

## Build and test

From the repository root:

```powershell
dotnet build TrustPlatform.sln
dotnet test TrustPlatform.sln
```

For the frontend:

```powershell
Set-Location src\TrustPlatform.Web
npm run build
npm test
npm run lint
```
