# TrustPlatform - Copilot Instructions

TrustPlatform is a C# ASP.NET Core web project.

## Principles
- Keep the solution simple and maintainable.
- Follow standard C# and .NET conventions.
- Apply SOLID principles where they provide real value.
- Avoid unnecessary abstractions and dependencies.
- Prefer asynchronous APIs for I/O operations.

## Development workflow
For non-trivial changes:
1. Read the requirements.
2. Inspect the existing codebase.
3. Read the current architecture.
4. Create an implementation plan.
5. Implement the smallest useful change.
6. Add or update tests.
7. Run tests.
8. Report changed files and test results.

## Architecture
- Architectural decisions must be documented in docs/decisions.md.
- Do not change architecture as part of an unrelated feature.
- Keep business logic independent from infrastructure where practical.

## Testing
- New functionality should have automated tests.
- Tests must be deterministic.
- Unit tests must not require external systems.

## Security
- Never hardcode secrets, passwords, tokens, or credentials.
- Never commit .env files containing secrets.
- Validate external input.
- Follow secure coding practices.

## Documentation
Keep these files updated when relevant:
- README.md
- docs/requirements.md
- docs/architecture.md
- docs/decisions.md
