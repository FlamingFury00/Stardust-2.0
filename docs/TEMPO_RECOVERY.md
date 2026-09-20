# Restore pace, commitments, and full-pad pickups

This is a correction to the rejected gameplay changes, not a new 3.0 strength claim.
Base: merged `v5` at `12ed398f414bae987f2766236c1c2b05f7fa08af` (PR #2).
Pre-upgrade reference: `0ec1f3784d0ecf8115430d78af9eb7b2de1813c6`.
The competing PR #3 was withdrawn rather than merged on top.

## What went wrong

The previous change optimized prohibitions instead of preserving playing behavior.
Most ordinary navigation bypassed `Drive`, capped cruising at 1400-1800, and could
never enter its dodge/speed-flip subactions. In solo games the car is always last
back, and the route selector categorically excluded large pads in that role.
A current-ball opponent ETA cutoff rejected future shots; unsuccessful searches
fell back to a parking/shadow slot instead of closing the gap. The extra setup
estimate and front-loaded solver budget further restricted available shots.
These restrictions are observable in source and executable behavior fixtures.
They were poor policy choices, not all separate mathematical implementation bugs.

## Narrow correction

- Long rotations use the existing `Drive` at `Car.MaxSpeed` (2300). Its existing
  speed flips, dodges, half flips and wall handling are retained, not rewritten.
  A `Positioning` wrapper propagates interruptibility and retains the same travel
  action after takeoff. Local parking/braking and goal-mouth waypoints remain.
- The shot search uses the original target-clamping calculation, family order,
  and solver validity checks. Opponent ETA is no longer a categorical veto.
  Coarse search covers the three-second horizon before local refinement; urgent
  saves retain denser close-time sampling and respect the scoring deadline.
  The old experimental `ScoringPoint`/`HasSetupTime` helpers remain callable but
  no longer constrain baseline shot selection.
- Pressure changes planning cadence without turning every action into retreat.
  When a grounded, goal-side challenger cannot find a scoring/clearing solution,
  it drives toward a short-lead contact rather than parking behind the opponent.
  This fallback is a heuristic, not an opponent-policy model or guaranteed 50/50.
- Deliberate full-pad refills use the existing `GetBoost`, including its fast
  `Drive`. Selection compares pad races and, without another defender, estimates
  the trip plus the return against an attack window. It does not ban solo refills.
  The same refill action survives ordinary replans; a newly predicted goal can
  cancel it before a physically committed flip. The 3500 ball-flight speed in the
  attack-window estimate is tuning, not a guarantee against every possible shot.
- Repeated `Field.Initialize` calls replace the static pad table instead of
  appending stale duplicate entries. This is an idempotence defect with a direct
  reproduction, relevant to multiple bot instances sharing a process.

The patch does not modify the existing `Drive`, `GetBoost`, `Arrive`, shot executor,
`Kickoff`, `Dodge`, or `SpeedFlip` implementations. It restores their use. Team-role
allocation, packet/jump-state repairs, and touch invalidation remain in place.

## Experimental possession is opt-in again

The experimental ground carry/catch/flick, air-carry and reset selectors default
to off. This does NOT disable standard aerial shots, shot dodges or travel flips.
The code is retained for isolated testing. Explicitly setting a flag to `1`
enables that selector; the reset handoff additionally requires aerial carry.

For a clean recovery test, including shells where earlier instructions set flags:

```powershell
$env:STARDUST_GROUND_CONTROL = "0"
$env:STARDUST_AERIAL_CARRY = "0"
$env:STARDUST_FLIP_RESETS = "0"
$env:STARDUST_TRACE = "1"
git fetch origin
git switch fix/restore-stardust-tempo
dotnet build src/Bot/Bot.csproj -c Release
```

The existing Windows bot configuration starts
`src/Bot/bin/Release/net8.0/Bot.exe`. Rebuild the checkout registered with RLBot.
The startup banner starts with `Stardust tempo recovery` and prints the actual
possession switches; do not confuse a stale binary with the newly built branch.
No registration identity or match configuration is changed by this patch.

## Reproduction and validation

The first commit on the branch adds `tests/Stardust.Recovery` and its CI step
WITHOUT production changes. Against merged PR #2, the same 15 fixtures produce
1 pass and 14 failures. The first eight independently exercise the reported lost
pace/shot/refill behavior; later tests cover pad initialization, the delegated
jump/dodge input sequence, pressure, pickup interruption/completion and defaults.
Some later failures are prerequisites failing (e.g. a refill never started), not
additional independent bugs. Switching experimental defaults is an intentional
policy reversal, not a discovered defect in environment-variable parsing.

With the correction, the recovery suite has 15 passes. The pre-existing core and
model suites are also rerun. Two old robustness assertions are deliberately
updated: the no-travel-flip requirement is local to goal-mouth parking, not every
last-back movement; canonical shots must pass their original solver validity and
prediction checks, not the removed extra conservative setup veto.

```sh
# Linux only, before building:
chmod +x src/generate-flatbuffers.sh src/flatbuffers-schema/binaries/flatc

dotnet build src/Bot/Bot.csproj -c Release
dotnet run --project tests/Stardust.Tests -c Release
dotnet run --project tests/Stardust.ModelChecks -c Release
dotnet run --project tests/Stardust.Robustness -c Release
dotnet run --project tests/Stardust.Recovery -c Release
```

CI runs the normal project options on Linux and Windows. Local offline checks use
`IsAotCompatible=false` solely because the downloaded toolchain lacks its exact
analyzer package; this override is not committed to the projects or CI.
The suites manipulate world snapshots and execute selected production planners
and actions. The speed-flip input test uses minimal steering integration to align
its setup; it does not measure actual flip acceleration, suspension or landing.
No Rocket League match, contact simulation or win-rate evaluation was run here.
Passing tests support the specific behavior restoration, not a claim that all
match regressions have disappeared or that the bot is stronger than pre-upgrade v5.

## References checked during the correction

- [Original strategy](https://github.com/FlamingFury00/Stardust-2.0/blob/0ec1f3784d0ecf8115430d78af9eb7b2de1813c6/src/Bot/Bot.cs): original `Drive`, `GetBoost`, and shot-selection usage.
- [RLBot target shooting](https://wiki.rlbot.org/v5/botmaking/shooting-the-ball-towards-or-away-from-a-target/): target intervals, approach direction and ball-radius offsets.
- [RLBot game values](https://wiki.rlbot.org/v5/botmaking/useful-game-values/): travel speeds and actual full/small pad geometry.
- [Ground-control measurements](https://www.smish.dev/rocket_league/ground_control/): throttle speed limit, boost acceleration and braking. They support using braking locally, not treating throttle-only pace as a universal travel cap.

This pass introduces no trained policy, neural weights, framework migration,
new runtime package or claim derived from a paper without implementation.
