# Snap Points and H/V Lock: Change Report

Audience: a teammate (or a future Claude session) who needs to upgrade or change snapping without re-reading the code.
This report follows the **merged code**; where the code differs from `docs/SNAP_PATTERN_PLAN.md`, the code wins and
the difference is noted. Baseline: `grid-and-android` at `9270ba3` (plan commit `2a0f6b4` on `feature/snap-pattern`).

## 1. Summary

_In progress. Filled in by the final pass (R1)._

## 2. Files changed

### C0 (contracts)
- **Contracts**
  - `Contracts/Tools/ToolTypes.cs`: appended `SnapKind.Intersection` (last, so existing ordinals are unchanged).
  - `Contracts/Session/ISnapMetric.cs` (new): board -> screen projection plus tolerance in pixels for snapping and taps.
  - `Contracts/Session/IEditorSession.cs`: added `OrthoLock` and `SetOrthoLock(bool)`.
- **Engine**
  - `Engine/EditorSession.cs`: snap state now lives here: `SnapMetric`, `FallbackSnapTolerance`, `SnapEnabled` (default
    true), `SetSnapEnabled`, `OrthoLock`, `SetOrthoLock`, and `SnapCandidates` (empty when snapping is off). It also adds
    three `partial void` hooks for the snap partial, and `RaiseBoardChanged` now calls `OnModelChanged` first.
  - `Engine/EditorSession.Tools.cs`: removed the old `SnapEnabled => false` and the no-op `SetSnapEnabled` (moved to core).
- **Mocks**
  - `Mocks/ForBoard/StubEditorSession.cs`: `OrthoLock` / `SetOrthoLock` (state only, raises `StateChanged`).
- **Tests**
  - `Tests/PlayMode/Verifier/VerifierInputRouterPlayTests.cs`: `RecordingSession` forwards `OrthoLock` / `SetOrthoLock`.
  - `tools/enginecheck/ToolChecks.cs`: `DefaultToolIsLine` now expects snapping on, lock off and no metric. New check
    `SnapAndOrthoTogglesRaiseStateOnce`.

## 3. Contract changes

### C0
```csharp
// Contracts/Tools/ToolTypes.cs (append-only)
public enum SnapKind { None, UserPoint, Endpoint, Center, CircleDivision, CellCorner, CellEdgeMid, CellCenter, Intersection }

// Contracts/Session/ISnapMetric.cs (new)
public interface ISnapMetric
{
    bool TryToScreen(Vec2 board, out Vec2 screenPx);   // board -> screen px, perspective-correct; false if not projectable
    double TolerancePx { get; }                         // snap and tap radius in screen px
}

// Contracts/Session/IEditorSession.cs (added)
bool OrthoLock { get; }
void SetOrthoLock(bool on);                             // raises StateChanged when the value changes

// Engine/EditorSession.cs (concrete class only, not on the interface)
public ISnapMetric SnapMetric { get; set; }             // null = board-unit fallback
public const double FallbackSnapTolerance = 0.02;       // board units, used when SnapMetric is null
```
`SnapEnabled`, `SnapCandidates` and `SetSnapEnabled` were already on `IEditorSession`. They are now implemented:
`SnapEnabled` defaults to **true**, and `SetSnapEnabled` raises `StateChanged` only when the value changes.

Implementers updated: `Engine/EditorSession` (real), `Mocks/ForBoard/StubEditorSession`, and `RecordingSession` in
`Tests/PlayMode/Verifier/VerifierInputRouterPlayTests.cs`. There are no other `: IEditorSession` types.

## 4. How it works

### C0: extension points in the shared core
`EditorSession.cs` is the shared core file. It declares three `partial void` hooks so that snapping can live entirely
in `EditorSession.Snap.cs` without editing the core again:
- `EnsureCandidates()`: called by the `SnapCandidates` getter while snapping is on. It brings the private
  `_candidates` list up to date, lazily, behind a dirty flag. If no snap partial implements it, the list stays empty.
- `OnModelChanged(BoardChangeKind)`: called inside `RaiseBoardChanged`, before any `BoardChanged` listener runs, so the
  candidate cache can be marked dirty before the renderer reads `SnapCandidates`.
- `OnSnapSettingsChanged()`: called when `SnapEnabled` or `OrthoLock` changes, before `StateChanged` is raised.

When snapping is off, `SnapCandidates` returns a shared empty array and never calls `EnsureCandidates`.

## 5. Invariants

_Filled in by the final pass (R1)._

## 6. Tuning knobs

| Constant | Value | File and member | Ticket |
|---|---|---|---|
| Snap tolerance without a screen metric | 0.02 board units | `Engine/EditorSession.cs` `FallbackSnapTolerance` | C0 |

## 7. How to extend

_Filled in by the final pass (R1)._

## 8. Tests

How to run the engine checks on Windows (Luis):
`DOTNET_ROLL_FORWARD=Major DOTNET_NOLOGO=1 DOTNET_CLI_TELEMETRY_OPTOUT=1 dotnet run --project tools/enginecheck -c Release`
On the Mac (teammates), use the command in `tools/enginecheck/EngineCheck.csproj`.

### C0
- `ToolChecks.DefaultToolIsLine`: snapping on, H/V lock off, `SnapMetric` null, candidate list available.
- `ToolChecks.SnapAndOrthoTogglesRaiseStateOnce`: each toggle raises exactly one `StateChanged`, repeats are no-ops,
  and candidates are empty while snapping is off.

## 9. Known limitations and untested areas

_Filled in by the final pass (R1)._ It must say "Editor-verified only" and list the device checks still owed (plan Q12).
