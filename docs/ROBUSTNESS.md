> **Superseded policy:** the movement caps, blanket last-back refill restriction and automatic possession defaults described below were rejected after playtesting. See [TEMPO_RECOVERY.md](TEMPO_RECOVERY.md) for the current correction. This report is retained as the record of PR #2, not as evidence of improved playing strength.

# Robustness revision following in-game feedback

Base: `v5` at `bc2e7b80231275faf34bc697ba09f00f47fb1d3d` (merged PR #1). This revision addresses reported shooting, spacing, aerial overshoot, reset visibility, boost, defensive reaction, and goal-return failures. It retains the existing RLBot/RedUtils stack. It does not add neural weights or claim professional-level play.

## Changes and their limits

| Report | Identified code issue | Change |
|---|---|---|
| Less accurate shots | Aiming used a changed ball-velocity approximation; coarse selection favored early marginal interceptions. Arrival feasibility ignored the actual setup waypoint. | Restore lateral-velocity treatment; aim inside the posts/crossbar; inspect denser near-term slices; check the same setup waypoint used by Arrive for grounded shot families. Retain the existing contact solvers. |
| Teammate clustering/open goals | Support cars shared nearly the same shadow target; fuzzy pairwise ownership was unsuitable for a multi-car ordering. | Snapshot-consistent challenger, wide support, and deeper anchor allocation; deterministic total-order tie breaking; support separation. Incoming-goal defense assigns a trajectory-specific defender instead of the current-ball chaser. |
| Car climbs above ball | Carry controller added permanent upward target velocity and treated a ballistic ball as a hovering target; boost demand above a small threshold became continuous full boost. | Match predicted ball acceleration as well as relative position/velocity; remove upward velocity bias; modulate impulse with a minimum-burst budget and predict relative-height overshoot before starting a burst. |
| No visible resets | Previous feature default was off; entry excluded an expired first-jump window; inverted coasting at matched velocity did not close the contact gap. | Guarded automatic selection, spent-availability evidence including expiry, active approach closure, earlier wheel orientation with room to rotate, cooldown, and stage/eligibility telemetry. These remain experimental attempts, not demonstrated in-game successes. |
| Low boost | Support refilling started late and was banned deep in defense; the shared drive code used pitch instead of yaw to gate ground boost. | Earlier on-route refilling, nearly free small-pad pickups during pressure, reserve-aware positioning, role/teammate constraints, corrected angle gating. No corner excursions for the anchor under pressure. |
| Slow defense | Possession could retain control until a predicted goal; tactical cadence and long movement commitments delayed reassessment. | Re-evaluate pressure before a goal trajectory exists, process danger edges immediately, shorten defensive planning interval, reserve a defender, and disallow speculative travel flips during defensive arrivals. |
| Posts/deep-net returns | Fast arrival without stopping margins; unsafe lateral cuts from behind posts; using pitch also corrupted drift decisions. | Shallow role targets, staging waypoints around the posts, braking-distance margins, reverse escape near the net, and no positioning handbrake or travel dodges. Wall returns retain the surface-aware Drive implementation. |

Not every missed shot is a selection bug. Existing collision/contact approximations, hitbox-specific offsets and aerial/jump shot controllers still require live evaluation. Conservative setup margins can intentionally decline an earlier marginal shot. The role/defender ETA estimates are kinematic heuristics, not opponent policies or proofs of interception.

## Reproducible checks

Use the .NET 8 SDK (or a compatible newer SDK targeting .NET 8). On Linux, first enable the bundled schema generator:

```sh
chmod +x src/generate-flatbuffers.sh src/flatbuffers-schema/binaries/flatc
dotnet build src/Bot/Bot.csproj -c Release
dotnet run --project tests/Stardust.Tests -c Release
dotnet run --project tests/Stardust.ModelChecks -c Release
dotnet run --project tests/Stardust.Robustness -c Release
```

The normal CI workflow builds and runs all three executables on Linux and Windows. It does not ignore failed checks. No new runtime dependency is required.

The regression programs exercise real production methods and selected actual action/planner calls using synthetic state snapshots. Coverage includes grounded yaw and boost, Arrive's flip flag, mirrored defensive reaction, setup-aware shooting, all six three-car order permutations, 2,000 seeded mirrored allocations, reset evidence, fuel reserves, active-pad ownership and pressure routing. The historical small-pad test now explicitly permits a zero-detour defensive pickup; a new test separately rejects an actual lateral detour. The historical reset-entry fixture supplies room to orient before contact.

Independent approximate plants exercise turn-limited goal returns and ball-relative flight, including 120/60/30 Hz timing and a 0.1-second minimum boost burst. The isolated translation plant assumes perfect attitude; the separate attitude checks cover angular dynamics. Neither contains Rocket League's full contact, wheel/suspension, collision, or tire-slip model. They are **not RocketSim matches**, and cannot establish actual dribble/reset success or match strength.

The robustness executable also emits an observational planning-time benchmark. Its timings depend on hardware, warm-up and load; it is not live p95/p99 tick latency. Shot search is bounded to 96 inspected candidate samples and 64 expensive solver constructions. Impossible height families and physically unreachable early samples are skipped before expensive checks. Existing near-term solutions stop later search, even when later samples are invalid.

## Runtime controls and reset diagnostics

Set environment variables before launching the bot process:

| Variable | Default | Override |
|---|---|---|
| `STARDUST_GROUND_CONTROL` | enabled | `0` disables new catch/carry/flick selection |
| `STARDUST_AERIAL_CARRY` | enabled | `0` disables aerial carry selection, including its reset handoff |
| `STARDUST_FLIP_RESETS` | guarded automatic attempts | `0` disables attempts; `1` explicitly enables them |
| `STARDUST_TRACE` | off | `1` logs strategy transitions, reset eligibility and stage counters |

```powershell
$env:STARDUST_TRACE = "1"
$env:STARDUST_FLIP_RESETS = "1"
```

Automatic reset selection still requires the assigned ball owner, an offensive-half high ball, at least 42 boost, opponent separation, cover in team modes and a four-second attempt cooldown. Mechanical entry additionally requires spent dodge availability, controlled relative speed, and room below the ball to orient. The bot does not deliberately spend a fresh flip merely to manufacture a reset opportunity.

Trace lines distinguish policy eligibility, mechanical geometry, spent availability, fuel and opponent ETA. Stage counters distinguish `attempt`, packet-evidenced `acquired`, `dodge`, and `follow-through`. Follow-through requires a later own touch moving the ball toward the attacking lane; it is not a goal or possession-duration metric. Internal timeout/loss paths report `abort`; external supervisor interruption can end an attempt without that counter. No success should be inferred just from seeing an attempt or acquisition counter increase.

## Review iterations

The new stress fixtures exposed a near-back-wall navigation deadlock: a car facing inward could not drive forward safely or reverse through a lateral target change. The fix widens reverse-escape eligibility near the own goal and preserves an established reverse motion. The same mirrored 2D fixtures now check post clearance, convergence to cover and avoidance of deep-net endpoints.

Profiling also exposed redundant solver validity calls and search continuing past an existing solution when later samples were invalid. Height-family filtering, single validation per constructed candidate, a solver-construction cap and an early search exit removed that unnecessary work. Timings are observations, not a gameplay multiplier.

## Primary references

- [RLBot game values](https://wiki.rlbot.org/v5/botmaking/useful-game-values/): field/goal geometry, braking, boost acceleration and pad values. Runtime pad positions still come from FieldInfo.
- [RLBot target shooting](https://wiki.rlbot.org/v5/botmaking/shooting-the-ball-towards-or-away-from-a-target/): target direction and correction concepts. Existing contact solvers remain empirical.
- [RocketSim RLConst.h](https://github.com/ZealanL/RocketSim/blob/c2baacb8f4b441dd8505e63c2aeb5a1679b60b02/src/RLConst.h) and [Car.cpp](https://github.com/ZealanL/RocketSim/blob/c2baacb8f4b441dd8505e63c2aeb5a1679b60b02/src/Sim/Car/Car.cpp): the simulator's 0.1-second minimum boost activation and airborne acceleration. Constants informed the independent impulse tests; RocketSim itself was not executed.
- [Samuel Mish's aerial-control model](https://www.smish.dev/rocket_league/aerial_control/): empirical angular dynamics used by the existing separate attitude tests.
- [Pinned v5 packet schema](../src/flatbuffers-schema/schema/gamedata.fbs): persistent jump/dodge state and timeouts remain the integration contract.

## Live acceptance checks still required

Compare this commit against the exact base, using mirrored scenarios and paired matches. Record actual shot-to-goal conversion, teammate spacing/double commitments, uncovered-goal concessions, boost spent per useful touch, time below a fuel threshold, save reaction latency, post collisions, air-carry duration and reset follow-through. Keep hitbox, field, mutators and opponents fixed. Examine held-out initial states rather than only tuning the same examples.

Disable automatic resets during the first shooting/defense comparison, then evaluate them separately. A positive software check is not evidence that all seven in-game symptoms are solved. No live match series or contact-simulator validation was available during this revision.
