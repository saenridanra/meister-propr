# Tasks: Admin Management UI

**Input**: Design documents from `/specs/004-admin-ui/`
**Branch**: `004-admin-ui`
**Stack**: Vue 3.5 (Composition API, `<script setup>`), Vite 6, TypeScript 5.6, Vue Router 4, Vitest 3, `@vue/test-utils` 2
**TDD**: Test tasks precede their implementation counterpart in every phase — confirm tests fail before writing implementation.

## Format: `[ID] [P?] [Story?] Description with file path`

- **[P]**: Can run in parallel (operates on different files, no dependency on incomplete sibling tasks)
- **[US#]**: Which user story this task belongs to (maps to spec.md)
- Test tasks appear **before** their implementation counterpart — must be confirmed failing first

---

## Phase 1: Setup (Project Scaffolding)

**Purpose**: Initialise the `admin-ui/` Vue project and supporting infrastructure config.
No business logic — purely scaffolding. All [P] tasks can run after T001.

- [X] T001 Initialise npm project in `admin-ui/` — create `package.json` with vue@^3.5, vue-router@^4.4, openapi-fetch@^0.13, vite@^6.0, @vitejs/plugin-vue@^5.2, typescript@^5.6, vue-tsc@^2.1, openapi-typescript@^7 (dev), vitest@^3.0, @vue/test-utils@^2.4, @vitest/coverage-v8@^3.0, jsdom@^25.0; add npm script `"generate:api": "openapi-typescript ../openapi.json -o src/services/generated/openapi.ts"`; run `npm install`
- [X] T002 Configure `admin-ui/vite.config.ts` — register `@vitejs/plugin-vue`, set `base: '/admin/'`, add dev-server proxy for `/clients`, `/reviews`, `/identities`, `/healthz` → `http://localhost:8080`, configure build output to `dist/`
- [X] T003 [P] Configure `admin-ui/tsconfig.json` and `admin-ui/tsconfig.node.json` — strict mode, `paths: { "@/*": ["./src/*"] }`, target `esnext`, moduleResolution `bundler`
- [X] T004 [P] Configure Vitest in `admin-ui/vite.config.ts` (inline `test` block) — environment `jsdom`, globals `true`, setupFiles `['tests/setup.ts']`, coverage provider `v8`; create `admin-ui/tests/setup.ts` with global fetch mock (`vi.fn()`), sessionStorage mock, and `@vue/test-utils` global router mock
- [X] T005 [P] Create `admin-ui/src/` directory structure: `components/`, `composables/`, `services/`, `views/`, `router/`, `types/`, `assets/styles/`; create `admin-ui/tests/` with `components/`, `composables/`, `views/`, `services/` subdirectories; add `admin-ui/public/favicon.ico` placeholder
- [X] T006 [P] Create `admin-ui/Dockerfile` — stage 1: `node:22-alpine` runs `npm ci && npm run build`; stage 2: `nginx:alpine` copies `dist/` to `/usr/share/nginx/html`, adds `admin-ui/nginx.conf` with `try_files $uri $uri/ /index.html` SPA fallback and `/healthz` returning 200, exposes port 80, adds HEALTHCHECK
- [X] T007 [P] Update `nginx/nginx.conf` — add `location /admin/ { proxy_pass http://admin-ui:80/; proxy_set_header Host $host; }`; update `docker-compose.yml` — add `admin-ui` service with `build: { context: ./admin-ui }`, `depends_on: [nginx]` removed (nginx depends on admin-ui), healthcheck via `wget -qO- http://localhost/healthz`; update `nginx` service `depends_on` to include `admin-ui`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Shared types, the fetch service, the session composable, router shell, and the app entry point.
All user stories depend on this phase being complete.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T008 Run `npm run generate:api` to generate `admin-ui/src/services/generated/openapi.ts` from `../openapi.json`; verify the generated file contains the `paths` schema object and component schemas (`Client`, `CreateClientRequest`, `UpdateClientRequest`, `AdoCredentialsRequest`); commit the generated file — it is the source of truth for all API types and must not be edited manually
- [X] T009 [P] Write failing Vitest tests for `useSession` composable in `admin-ui/tests/composables/useSession.spec.ts` — test: `setAdminKey` stores in `sessionStorage['meisterpropr_admin_key']`; `getAdminKey` returns stored value; `clearAdminKey` removes the key; `isAuthenticated` returns false when empty
- [X] T010 [P] Write failing Vitest tests for `api.ts` in `admin-ui/tests/services/api.spec.ts` — test: `createAdminClient()` returns an `openapi-fetch` client with `X-Admin-Key` header injected from sessionStorage via a middleware; `createAdminClient({ overrideKey })` injects the provided key instead (used by LoginView before key is stored); middleware intercepts 401 responses, calls `clearAdminKey()`, and re-throws as `UnauthorizedError`
- [X] T011 Implement `admin-ui/src/composables/useSession.ts` — `setAdminKey(key: string)`, `getAdminKey(): string | null`, `clearAdminKey()`, `isAuthenticated: ComputedRef<boolean>`; all backed by `sessionStorage['meisterpropr_admin_key']` (confirm T009 tests pass)
- [X] T012 Implement `admin-ui/src/services/api.ts` — imports generated types from `./generated/openapi.ts`; exports `createAdminClient(opts?: { overrideKey?: string })` which creates a typed `openapi-fetch` client (`createClient<paths>`) pointing at `VITE_API_BASE_URL`; attaches a request middleware that sets `X-Admin-Key` to `opts.overrideKey ?? getAdminKey()`; attaches a response middleware that on 401 calls `clearAdminKey()` and throws `UnauthorizedError`; the returned client is typed against the generated `paths` object so all callers get full path + method type safety (confirm T010 tests pass)
- [X] T013 Create `admin-ui/src/router/index.ts` — define routes: `{ path: '/login', name: 'login', component: () => import('@/views/LoginView.vue') }`, `{ path: '/', name: 'clients', component: () => import('@/views/ClientsView.vue'), meta: { requiresAuth: true } }`, `{ path: '/:id', name: 'client-detail', component: () => import('@/views/ClientDetailView.vue'), meta: { requiresAuth: true } }`; use `createWebHistory('/admin/')`; navigation guard added in Phase 3
- [X] T014 Create `admin-ui/src/App.vue` (root component with `<RouterView />` and `<AppHeader />` placeholder) and `admin-ui/src/main.ts` (mount app with router, `app.mount('#app')`); create `admin-ui/index.html` with `<div id="app"></div>` and script module entry

**Checkpoint**: `npm run build` succeeds; `npm test` runs (tests for T009/T010 failing is expected — implementation not yet done)

---

## Phase 3: User Story 1 — Log In (Priority: P1) 🎯 MVP

**Goal**: Administrator can enter an admin key, have it verified against the backend, be redirected to the client list on success, see an error on failure, and log out.

**Independent Test**: Navigate to `http://localhost:5173/admin/` → redirected to `/login`. Enter an incorrect key → error shown. Enter the correct key → redirected to `/`. Click logout → back to `/login`.

- [X] T015 [US1] Write failing component test for `LoginView.vue` in `admin-ui/tests/views/LoginView.spec.ts` — test: renders admin key input and submit button; submit with empty key shows validation error without API call; submit with a non-empty key calls `createAdminClient({ overrideKey: candidateKey }).GET('/clients', {})` — the override key carries the candidate key, sessionStorage is empty at call time; on success (200) sessionStorage contains the key and router navigates to `/`; on `UnauthorizedError` shows "Invalid admin key" error and sessionStorage remains empty
- [X] T016 [US1] Implement `admin-ui/src/views/LoginView.vue` — `<script setup>`: `adminKey` ref, submit handler calls `createAdminClient({ overrideKey: adminKey.value }).GET('/clients', {})` (the key is NOT yet in sessionStorage — sessionStorage must only be written after backend confirms 200); on success calls `setAdminKey(adminKey.value)` then `router.push('/')`; on `UnauthorizedError` shows inline error "Invalid admin key"; empty key shows validation error without API call (confirm T015 tests pass)
- [X] T017 [US1] Add navigation guard to `admin-ui/src/router/index.ts` — `router.beforeEach`: if `to.meta.requiresAuth && !isAuthenticated.value` → redirect to `/login`; if `to.name === 'login' && isAuthenticated.value` → redirect to `/`
- [X] T018 [US1] Implement `admin-ui/src/components/AppHeader.vue` — displays app title "Meister ProPR Admin", a **Logout** button that calls `clearAdminKey()` then `router.push('/login')`; integrate into `App.vue` (show `<AppHeader />` only when `isAuthenticated` is true)

**Checkpoint**: Login flow is end-to-end functional. Unauthenticated access to `/` redirects to `/login`. Logout clears session. US1 independently demonstrable.

---

## Phase 4: User Story 2 — View and Search Clients (Priority: P2)

**Goal**: Authenticated administrator sees a live-filtered table of all clients with name, status, and ADO credential indicator.

**Independent Test**: Log in → client list loads showing display name, Active/Inactive badge, ADO Credentials badge. Type in the search box → list filters immediately. Empty state shows friendly message when no clients exist.

- [X] T019 [P] [US2] Write failing component test for `ClientTable.vue` in `admin-ui/tests/components/ClientTable.spec.ts` — test: renders a row per client with displayName, isActive badge text, hasAdoCredentials badge; filter prop hides non-matching rows; empty clients array renders empty-state slot message
- [X] T020 [P] [US2] Write failing component test for `ClientsView.vue` in `admin-ui/tests/views/ClientsView.spec.ts` — test: on mount calls `GET /clients`; renders `<ClientTable>` with fetched data; search input updates filter prop passed to table; shows loading state while fetching
- [X] T021 [US2] Implement `admin-ui/src/components/ClientTable.vue` — props: `clients: Client[]`, `filter: string`; renders `<table>` with columns: Display Name, Status (Active/Inactive badge), ADO Credentials (Configured/None badge), Created; filters rows by `displayName.toLowerCase().includes(filter.toLowerCase())`; empty-state `<p>No clients found.</p>` when filtered list is empty (confirm T019 tests pass)
- [X] T022 [US2] Implement `admin-ui/src/views/ClientsView.vue` — on mount fetches `apiGet<Client[]>('/clients')`, stores in `clients` ref; `filter` ref bound to search `<input>`; passes `clients` and `filter` to `<ClientTable>`; shows loading spinner during fetch; catches errors and shows error message (confirm T020 tests pass)

**Checkpoint**: Client list view is fully functional end-to-end with search. US2 independently demonstrable.

---

## Phase 5: User Story 3 — Register a New Client (Priority: P2)

**Goal**: Administrator can open a create form, fill in display name and key, submit, and immediately see the new client in the list without a page reload.

**Independent Test**: Log in → click "New Client" → form appears. Submit with empty fields → inline error. Submit valid data → client appears at top of list. Submit duplicate key → conflict error shown inline.

- [X] T023 [US3] Write failing component test for `ClientForm.vue` (create mode) in `admin-ui/tests/components/ClientForm.spec.ts` — test: renders displayName and key inputs; submit with blank displayName shows "Display name is required"; submit with blank key shows "Client key is required"; valid submit calls `POST /clients` with correct body and emits `client-created` with the returned Client; on 409 shows "Key already in use" error; on 401 redirects to login
- [X] T024 [US3] Implement `admin-ui/src/components/ClientForm.vue` — `<script setup>`: `displayName` and `key` refs; inline required validation on submit (no API call if invalid); calls `apiPost<Client>('/clients', { displayName, key })`; on success emits `'client-created'` with created `Client`; on 409 shows conflict error message; on 401 `UnauthorizedError` propagates (confirm T023 tests pass)
- [X] T025 [US3] Integrate `ClientForm.vue` into `admin-ui/src/views/ClientsView.vue` — add "New Client" button that toggles `showCreateForm` ref; render `<ClientForm>` conditionally; on `client-created` event prepend new client to the `clients` array and hide the form; on cancel hide the form

**Checkpoint**: Full create-client flow works end-to-end. US3 independently demonstrable alongside US2.

---

## Phase 6: User Story 4 — Edit a Client (Priority: P3)

**Goal**: Administrator can navigate to a client detail page to rename it, toggle its active state, and manage its per-client ADO credentials (set or clear).

**Independent Test**: Log in → click a client row → detail page loads with current data. Change display name, click Save → change reflected. Toggle status → badge flips. Fill ADO credentials form → save succeeds; credentials status shows "Configured". Click Clear → status reverts to "None".

- [X] T026 [P] [US4] Write failing component test for `ClientDetailView.vue` in `admin-ui/tests/views/ClientDetailView.spec.ts` — test: on mount calls `GET /clients/{id}`; renders displayName in editable input; "Save" button calls `PATCH /clients/{id}` with updated displayName; "Enable" / "Disable" button calls `PATCH /clients/{id}` with toggled isActive; 404 on load shows "Client not found" and navigates back to list
- [X] T027 [P] [US4] Write failing component test for `AdoCredentialsForm.vue` in `admin-ui/tests/components/AdoCredentialsForm.spec.ts` — test: renders tenantId, clientId, secret (type=password) inputs; submit calls `PUT /clients/{id}/ado-credentials` with correct body; "Clear" button calls `DELETE /clients/{id}/ado-credentials` and emits `credentials-cleared`; secret field value is never pre-populated (always empty); on 400 shows inline field error
- [X] T028 [US4] Implement `admin-ui/src/views/ClientDetailView.vue` — on mount fetches `apiGet<Client>('/clients/' + route.params.id)`; editable `displayName` input with Save button → `apiPatch('/clients/{id}', { displayName })`; Enable/Disable button → `apiPatch('/clients/{id}', { isActive: !client.isActive })`; embeds `<AdoCredentialsForm :clientId="client.id" :hasCredentials="client.hasAdoCredentials" />`; on 404 shows error and calls `router.push('/')` (confirm T026 tests pass)
- [X] T029 [US4] Implement `admin-ui/src/components/AdoCredentialsForm.vue` — props: `clientId: string`, `hasCredentials: boolean`; tenantId + clientId + secret inputs (secret `type="password"`, never pre-filled); Save → `apiPut('/clients/{clientId}/ado-credentials', { tenantId, clientId: formClientId, secret })`; Clear → `apiDelete('/clients/{clientId}/ado-credentials')`; emits `credentials-updated` and `credentials-cleared`; inline 400 error display (confirm T027 tests pass)
- [X] T030 [US4] Add router-link to each row in `admin-ui/src/components/ClientTable.vue` — wrap the display name cell in `<RouterLink :to="'/' + client.id">` so clicking a row navigates to `ClientDetailView`

**Checkpoint**: Detail/edit flow is end-to-end functional. US4 independently demonstrable alongside US1–US3.

---

## Phase 7: User Story 5 — Delete a Client (Priority: P3)

**Goal**: Administrator can delete a client from the detail page after confirming in a dialog. Cancelling the dialog leaves the client intact.

**Independent Test**: Open a client detail page → click Delete → confirmation dialog appears. Click Cancel → client still exists. Open again → click Delete → click Confirm → navigate back to list; client is gone.

- [X] T031 [US5] Write failing component test for `ConfirmDialog.vue` in `admin-ui/tests/components/ConfirmDialog.spec.ts` — test: renders when `open` prop is true; hides when `open` is false; clicking Confirm emits `'confirm'`; clicking Cancel emits `'cancel'`; message prop is rendered in dialog body
- [X] T032 [US5] Implement `admin-ui/src/components/ConfirmDialog.vue` — props: `open: boolean`, `message: string`; emits: `'confirm'`, `'cancel'`; renders a modal overlay with the message, a Confirm button, and a Cancel button; no API calls (caller is responsible for the action) (confirm T031 tests pass)
- [X] T033 [US5] Add delete action to `admin-ui/src/views/ClientDetailView.vue` — "Delete" button sets `showDeleteDialog = true`; `<ConfirmDialog :open="showDeleteDialog" message="Delete this client permanently?" @confirm="handleDelete" @cancel="showDeleteDialog = false" />`; `handleDelete` calls `apiDelete('/clients/' + client.id)`; on 204 navigates to `/`; on 404 shows "Already deleted" message and navigates to `/`

**Checkpoint**: Delete flow works end-to-end with confirmation. All five user stories are independently functional.

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Visual consistency, error UX, validation of Docker deployment, and documentation.

- [X] T034 [P] Add CSS custom properties and base styles in `admin-ui/src/assets/styles/globals.css` — define colour tokens (`--color-primary`, `--color-danger`, `--color-success`, `--color-muted`), base typography, button styles, badge styles (Active/Inactive/ADO); import in `admin-ui/src/main.ts`; review all components for consistent class usage
- [X] T035 [P] Implement `admin-ui/src/components/AppNotification.vue` — props: `message: string`, `type: 'success' | 'error'`, `visible: boolean`; auto-dismisses after 4 s; integrate into `App.vue` with a `notify(message, type)` composable or provide/inject; wire success and error toasts for create, save, delete, credential operations in relevant views
- [X] T036 Run full Vitest suite in `admin-ui/` (`npm test`) and confirm all tests pass; run `npm run type-check` (`vue-tsc --noEmit`) and confirm zero type errors; fix any regressions
- [X] T037 [P] Validate Docker deployment — run `docker compose up --build` from repo root; confirm `https://localhost:5443/admin/` serves the Vue SPA; confirm SPA login flow works against the backend via nginx proxy; update `GETTING_STARTED.md` admin UI section with the correct local dev command (`cd admin-ui && npm run dev`) and Docker note

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: No dependencies — start immediately; T002 must complete before T003/T004; T001 must complete before all others
- **Phase 2 (Foundational)**: Depends on Phase 1 completion — BLOCKS all user story phases
- **User Story Phases (3–7)**: All depend on Phase 2; US1 must complete before US2–US5 (navigation guard required)
- **Polish (Phase 8)**: Depends on all desired user story phases

### User Story Dependencies

| Story | Depends on         | Independently testable |
|-------|--------------------|------------------------|
| US1 (Login)             | Phase 2 complete          | Yes — login/logout flow only |
| US2 (View & Search)     | US1 complete (auth guard)  | Yes — list/filter only |
| US3 (Register Client)   | US2 complete (list view)   | Yes — create + list |
| US4 (Edit Client)       | US2 complete (router-link) | Yes — detail/edit/credentials |
| US5 (Delete Client)     | US4 complete (detail page) | Yes — delete from detail |

### Within Each Phase

1. Write test tasks first — confirm they **fail** before implementing
2. Types and composables before services before views
3. Components before the views that embed them
4. Story complete and all its tests green before moving to next phase

### Parallel Opportunities

- Phase 1: T003, T004, T005, T006, T007 all run in parallel after T001/T002
- Phase 2: T009 and T010 (test files) run in parallel; T011 and T012 run in parallel after their tests
- Phase 4 US2: T019 and T020 (test files) run in parallel
- Phase 6 US4: T026 and T027 (test files) run in parallel
- Phase 8: T034, T035, T037 run in parallel

---

## Parallel Example: Phase 2

```
# Launch test files in parallel (both write to different files):
Task T009: Write useSession tests → admin-ui/tests/composables/useSession.spec.ts
Task T010: Write api.ts tests    → admin-ui/tests/services/api.spec.ts

# Then implement in parallel (different files, both depend on T008 types):
Task T011: Implement useSession.ts → admin-ui/src/composables/useSession.ts
Task T012: Implement api.ts        → admin-ui/src/services/api.ts
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (T001–T007)
2. Complete Phase 2: Foundational (T008–T014)
3. Complete Phase 3: US1 Login (T015–T018)
4. **STOP and VALIDATE**: Login/logout flow works; `npm test` green; navigate to `/admin/` redirects to login
5. Deploy/demo if ready

### Incremental Delivery

1. Setup + Foundational → infrastructure ready
2. US1 (Login) → admin can authenticate ← **MVP**
3. US2 (View & Search) → admin can see clients
4. US3 (Register) → admin can create clients
5. US4 (Edit) → admin can update/credential clients
6. US5 (Delete) → admin can remove clients
7. Polish → production-ready styling and Docker validation

Each phase is independently demonstrable without breaking prior phases.

---

## Notes

- `[P]` = parallel-safe (different file, no unmet dependency in same phase)
- `[US#]` = maps to user story from `spec.md`
- Test tasks **must fail** before implementing — this is not optional (constitution §II)
- `sessionStorage` key: `"meisterpropr_admin_key"` — consistent across all files
- ADO secret input: always `type="password"`, never pre-populated from API response
- `UnauthorizedError` thrown by `api.ts` response middleware on 401 must be caught in views to trigger redirect
- API types come exclusively from the generated `src/services/generated/openapi.ts` — never hand-write types that duplicate the schema
- Run `npm run generate:api` whenever `openapi.json` at repo root changes
- Vite `base: '/admin/'` must be set so `<RouterLink>` and asset paths work when served from nginx `/admin/`
