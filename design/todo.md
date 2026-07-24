# Design todo — HDT rank-tracker plugin

Open design questions identified before writing plugin code, tracked against
[rank-data-fetch-plugin.md](rank-data-fetch-plugin.md). All folded in.

- [x] 1. Guard against `OnGameEnd` firing with incomplete `GameStats` (0-turn early-return path)
- [x] 2. Represent conceded games in the payload (`WasConceded` is captured by HDT but missing from JSON)
- [x] 3. Decide the failure/retry story for failed uploads (currently: silent drop)
- [x] 4. Confirm/document the idempotency key for the backend (`gameId`)
- [x] 5. Threading + TLS setup for the HTTP call (net472, UI-thread polling loop)
- [x] 6. Defensive error handling in `OnUpdate()` against HDT's auto-disable-after-100-exceptions behavior
- [x] 7. Add payload schema/version field for forward compatibility
- [x] 8. Decide which game modes get uploaded (filter)
- [x] 9. Plugin project setup and deployment mechanics (target framework, references, output path)
