## Add Swagger/OpenAPI for testing Azure Functions

This document describes how we will enable Swagger/OpenAPI in the .NET 8 isolated Azure Functions app and add a small helper HTTP function to make the Swagger UI easy to find and control via configuration.

### Goals
- Expose OpenAPI docs and a Swagger UI locally to explore and test HTTP-triggered functions.
- Add a simple `/swagger` endpoint that redirects to the Swagger UI when enabled.
- Keep Swagger disabled or locked down in production by default.

### Deliverables
1) OpenAPI/Swagger endpoints provided by the Functions OpenAPI extension.
2) New HTTP function: `SwaggerUiRedirectFunction` (route: `GET /swagger`).
3) Config flag to enable/disable Swagger UI (local on, prod off).
4) Example OpenAPI annotations on one existing HTTP function for discoverability.

---

## Plan

1) Dependencies
	 - Add NuGet package to the Functions project:
		 - `Microsoft.Azure.Functions.Worker.Extensions.OpenApi` (for .NET isolated worker)

2) Configuration
	 - Add an app setting flag to control visibility of the UI:
		 - `ENABLE_SWAGGER=true` in `local.settings.json` (local only)
		 - Omit or set `ENABLE_SWAGGER=false` in production
	 - Optional hardening (recommended for non-dev):
		 - Set OpenAPI UI/document auth level via app settings when needed:
			 - `OpenApi__AuthLevel__UI=Function` or `Admin`
			 - `OpenApi__AuthLevel__Document=Function` or `Admin`

3) New helper function
	 - Create `SwaggerUiRedirectFunction` with:
		 - Trigger: HTTP GET, `AuthorizationLevel.Anonymous`
		 - Route: `/swagger`
		 - Behavior:
			 - If `ENABLE_SWAGGER=true`: 302 redirect to `/api/swagger/ui`
			 - Else: 404 (or 403) with a short message
		 - Notes:
			 - Redirect target `/api/swagger/ui` is provided by the OpenAPI extension.
			 - Log decisions for visibility (redirect vs disabled).

4) Annotate existing HTTP functions
	 - Add OpenAPI attributes to make endpoints appear clearly in Swagger:
		 - `[OpenApiOperation]`, `[OpenApiParameter]`, `[OpenApiResponseWithBody]`, etc.
	 - Start with one example (e.g., `HttpTriggerFunction`) and expand later.

5) Documentation
	 - Update `README.md` with a short "How to use Swagger locally" note.
	 - Link to this file for details.

6) Validation
	 - Build and run locally.
	 - Browse `http://localhost:7071/api/swagger` → confirm redirect to `/api/swagger/ui`.
	 - Confirm all annotated functions appear and are testable.
	 - Flip `ENABLE_SWAGGER=false` → `GET /swagger` returns 404/403.

---

## Function specification: SwaggerUiRedirectFunction

- Purpose: Provide a friendly local entry point to the Swagger UI and allow simple environment-based enable/disable.
- Trigger: HTTP GET
- Auth level: Anonymous
- Route: `/swagger`
- Inputs: None (no body); optional future query params ignored
- Outputs:
	- 302 redirect to `/api/swagger/ui` when enabled
	- 404 (or 403) with `text/plain` when disabled
- Config:
	- `ENABLE_SWAGGER` (string: `true`/`false`), default true in local.settings.json
	- Optional: `OpenApi__AuthLevel__UI` and `OpenApi__AuthLevel__Document` (e.g., `Function`/`Admin`)
- Observability:
	- Log redirect action and disabled state
- Error handling:
	- If the OpenAPI extension endpoints are not available, return 500 with hint to install/restore the OpenAPI extension

---

## Implementation steps (to apply next)

1) Add package reference
	 - Add `Microsoft.Azure.Functions.Worker.Extensions.OpenApi` to `RagSearch.csproj`.

2) Create `SwaggerUiRedirectFunction.cs`
	 - New file in project root (or `Functions/` folder if preferred).
	 - Minimal implementation:
		 - Read `ENABLE_SWAGGER` from configuration or environment.
		 - If true, return 302 to `/api/swagger/ui`; else return 404/403.

3) Add OpenAPI attributes to one HTTP function (example)
	 - On `HttpTriggerFunction` add:
		 - `[OpenApiOperation]` with operationId and tags
		 - `[OpenApiParameter]` for query/path params
		 - `[OpenApiResponseWithBody]` describing the response

4) Update configuration files
	 - `local.settings.json`: add `"ENABLE_SWAGGER": "true"`
	 - (Optional) host/app settings for auth level:
		 - `OpenApi__AuthLevel__UI` and `OpenApi__AuthLevel__Document`

5) Smoke test locally
	 - Run the functions (via existing scripts or VS Code task).
	 - Open `http://localhost:7071/swagger` (should redirect to `/api/swagger/ui`).
	 - Validate documented endpoints appear and can be invoked.

---

## Acceptance criteria

- [ ] GET `/swagger` returns 302 to `/api/swagger/ui` when `ENABLE_SWAGGER=true` locally.
- [ ] GET `/swagger` returns 404/403 when `ENABLE_SWAGGER=false`.
- [ ] Swagger UI lists at least one annotated function with parameters and response types.
- [ ] No changes required to timer-triggered functions; they are not listed in the UI.
- [ ] Optional: OpenAPI UI/document endpoints require at least Function or Admin key in non-dev.

---

## Notes and caveats

- The OpenAPI extension provides the `/api/swagger/ui` and document endpoints; the redirect function is just a convenience and a place to gate access by environment.
- Ensure you commit only safe configuration to source control. Keep production settings (e.g., `OpenApi__AuthLevel__*`) in deployment environment variables.
- If you do not want Swagger in production at all, consider removing the OpenAPI extension package from the production build or using a separate build profile.

