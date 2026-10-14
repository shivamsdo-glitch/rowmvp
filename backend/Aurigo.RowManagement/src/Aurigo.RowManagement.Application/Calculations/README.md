# Calculations

Pure, stateless functions (offer and compensation thresholds, approval-chain gating, statutory
deadlines, settlement variance). Rules:

- `static` classes and methods only. No DB, no DI, no `DateTime.UtcNow`; pass `now` in.
- Every function has dedicated unit tests in `Aurigo.RowManagement.UnitTests/Calculations/`.
- Handlers call these; they never contain the arithmetic themselves.
