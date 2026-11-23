# Solo app - Backend API

## Application Database

The database connection string is provided by the platform and available in the Squad Room project info. Use it with pgAdmin or DBeaver to inspect your schema locally.

## Web API

**WebApi URL:** https://webapi6b058224467e5df38b3d99d1-production.up.railway.app

**Swagger API Tester URL:** https://webapi6b058224467e5df38b3d99d1-production.up.railway.app/swagger

## Clinic booking

Patients book visits online without an account; the front desk runs the day from a staff view.

### Rules

| Rule | Where |
|---|---|
| Times are stored in UTC; working hours are clinic-local (`Clinic:TimeZoneId`, default Asia/Jerusalem). Clock changes are handled only in `Domain/ClinicClock.cs`. | `ClinicClock` |
| Slots start on a 15-minute grid, at least 2 hours from now, at most 60 days ahead. The visit must end by closing time; the cleanup buffer may run past it. | `SlotCalculator`, `ClinicOptions` |
| A booked appointment blocks its time plus its service's buffer. Time off blocks its whole range. | `SlotCalculator`, `ScheduleStore` |
| No double booking: every write takes a per-practitioner lock, re-reads what is busy and re-runs the slot check inside the transaction. A unique index on (practitioner, start) for live appointments is the backstop. | `AppointmentStore` |
| Patients change or cancel through the secret link in their confirmation, until 24 hours before the visit. Only the SHA-256 of the link token is stored. | `BookingRules`, `ManageToken` |
| Staff endpoints need the `X-Staff-Key` header to match the `CLINIC_STAFF_KEY` environment variable. Without the variable the staff screens are closed. | `StaffKeyAttribute` |
| Time off that clashes with booked visits is refused with the list of clashes; nothing is cancelled automatically. | `ScheduleStore.AddTimeOffAsync` |

### API

| Method | Path | What |
|---|---|---|
| GET | `/api/clinic` | Name, time zone, notice hours, today (clinic time) |
| GET | `/api/services` | Services with duration, buffer and price |
| GET | `/api/practitioners?serviceId=` | Practitioners, optionally only those offering a service |
| GET | `/api/availability/days?serviceId&practitionerId&from&days` | Free slots per day (date strip) |
| GET | `/api/availability?serviceId&practitionerId&date` | Free start times on one day |
| POST | `/api/appointments` | Book. 201 with the manage token (shown once), 409 `slot_taken`, 400 `invalid` with field errors |
| GET | `/api/appointments/manage/{token}` | The patient's appointment |
| POST | `/api/appointments/manage/{token}/cancel` | Cancel (422 `too_late` inside the notice period) |
| POST | `/api/appointments/manage/{token}/reschedule` | Move to another free slot, same service and practitioner |
| GET | `/api/staff/day?date=` | Staff: every appointment and time off that day, per practitioner |
| POST | `/api/staff/appointments/{id}/status` | Staff: completed, no_show or cancelled |
| POST / DELETE | `/api/staff/time-off` | Staff: add (409 `time_off_clash`) or remove time off |

Errors always have the shape `{ code, message, fields? }`; the HTTP status for each code is set in `Controllers/ApiErrors.cs`.

### Tests

`dotnet test Tests/Tests.csproj` covers the slot rules (buffers, lead time, horizon, split days, both clock changes), the patient and staff rules, the form validation and the link and staff key checks. The SQL in the stores is not covered by automated tests yet (see below).

### Not in v1

In this order: a rate limit on `POST /api/appointments`, a confirmation email with the manage link, a patient lookup for the front desk, Hebrew (right to left). Also: integration tests for the busy-range query and the booking transaction against a real Postgres (Testcontainers), a length limit on the time-off reason, and an index on `appointments (starts_at)` once the staff day view slows down (about 7 ms at a year of history).

## Google APIs (Gemini, Maps, Speech-to-Text)

The backend can use Google API keys provided via environment variables (set on Railway): **GOOGLE_API_KEY** for the Gemini LLM, and **GOOGLE_MAPS_API_KEY** for Geocoding, Maps, Directions, Places, and Speech-to-Text (Google requires Gemini keys to be separate from other APIs). Check **GET /api/google/status** and **GET /api/google/health** to verify the keys are set and reachable.

## Recommended Tools

**Recommended SQL Editor tool (Free):** [pgAdmin](https://www.pgadmin.org/download/)

## Deployment

This backend is configured for Railway deployment using nixpacks.toml.
