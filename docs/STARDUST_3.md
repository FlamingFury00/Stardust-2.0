# Stardust 3.0 candidate: control, possession, and an evaluation path

This branch is an implemented C# controller upgrade built from `v5` at `0ec1f3784d0ecf8115430d78af9eb7b2de1813c6`. It is **not a verified professional-level bot**. Build success and deterministic tests establish specific software/control properties, not a win-rate improvement. No trained neural policy, match series, or in-game reset success rate is included.

## What changed

| Layer | Implementation | Intended benefit |
|---|---|---|
| Packet lifecycle | Current-frame delta before execution; duplicate-packet handling; clock-discontinuity invalidation; cancellation before action execution; safe action replacement | Avoid stale plans, timing drift, and accidental removal of a replacement action |
| Jump state | Persistent `HasJumped`, `HasDoubleJumped`, `HasDodged`, `DodgeTimeout` from the pinned v5 schema | Do not mistake the end of an animation for a restored flip |
| Prediction | Exact first-match search; timestamp interpolation; null/short/duplicate handling; public shot-validity check | Remove missed coarse-index/tail intercepts and unsafe interpolation |
| Strategy | Predicted-goal supervision before possession/boost; separate defensive-shot identity; approximately 8 Hz tactical replanning; at most 48 prediction candidates per shot search | React to danger without repeatedly restarting mechanics |
| Possession | Ground catches, hood-relative carries, pressure-triggered flicks, velocity-matched aerial carries | Preserve a useful touch instead of always booming the ball |
| Orientation | Quaternion shortest-rotation error and angular-rate damping | Recover from inverted/backward attitudes without a 180-degree cross-product dead zone |
| Reset attempts | Entry constraints, spent-flip evidence, own wheel-contact evidence, restored packet flags, timed follow-through | Avoid claiming a reset merely because the car is near the ball |
| Team play | Deterministic equal-ETA ownership; invariant-culture shot claims; heartbeat/expiry; left-goes kickoff and same-side cheat/cover | Reduce mutually yielding claims and duplicate commitments |
| Boost/recovery | Goal-side small/large on-route pickups; landing-surface orientation | Avoid abandoning defense for a corner boost and reduce unproductive aerial tumbling |

The existing ground/jump/double-jump/aerial shot solvers and kickoff/drive subactions remain. This is deliberately not a wholesale framework migration.

## Build and regressions

Use the .NET 8 SDK. On Linux, enable the existing FlatBuffers generator first:

```sh
chmod +x src/generate-flatbuffers.sh src/flatbuffers-schema/binaries/flatc
dotnet build src/Bot/Bot.csproj --configuration Release
dotnet run --project tests/Stardust.Tests/Stardust.Tests.csproj --configuration Release
dotnet run --project tests/Stardust.ModelChecks/Stardust.ModelChecks.csproj --configuration Release
```

The first executable tests actual production methods: packet timing, persistent jump flags, reset evidence, action interruption, finite outputs, claims, prediction interpolation, team symmetry, control signs, possession gates, relative-motion invariance, and release-before-dodge sequencing. Seeded loops additionally exercise 10,000 clock updates and 2,000 attitude targets.

The second executable integrates an **analytic attitude model**, with the torque/damping equations described in [1], at 120, 60, and 30 Hz. It checks convergence across fixed and seeded initial orientations. It also tests boost-route constraints. It is not RocketSim, does not simulate ball contact, and must not be described as an in-game mechanics benchmark.

The GitHub Actions workflow builds the bot and runs both programs. Failures are not ignored. No additional runtime package or training service was added to the bot.

## Runtime switches and rollback

Set environment variables **before starting the bot process**:

| Variable | Default | Behavior |
|---|---|---|
| `STARDUST_GROUND_CONTROL` | enabled | Set `0` to disable the new catch/carry/flick selection |
| `STARDUST_AERIAL_CARRY` | enabled | Set `0` to disable aerial possession control |
| `STARDUST_FLIP_RESETS` | disabled | Set `1` to enable experimental reset attempts within an aerial carry |
| `STARDUST_TRACE` | disabled | Set `1` to log strategy transitions and ETA estimates |

Example in PowerShell, before launching the bot from that environment:

```powershell
$env:STARDUST_TRACE = "1"
$env:STARDUST_FLIP_RESETS = "1"
```

A reset attempt is not selected on every aerial: it requires adequate height, fuel, proximity, low relative speed, an already spent flip, and apparent opponent separation. A confirmed reset additionally needs a recent own touch with the wheels facing the ball and persistent flags showing that the flip was restored. Neither a normal airborne state nor a touch by another player is sufficient. Unsuccessful attempts time out into replanning/recovery.

For an exact A/B baseline, use the original commit in a separate checkout; disabling feature flags does **not** restore the original strategy or packet lifecycle. The original `v5` branch and bot registration identity are not overwritten by this feature branch.

## Control design

The ground carry projects **relative** ball/car motion before placing the ball over a hood target. Advancing only the ball would inject a false position error whenever both objects share a high world velocity. `PossessionControl` makes this invariant directly testable. Throttle follows longitudinal error and ball velocity; lateral error changes the heading. Boost and handbrake are suppressed during the carry.

The aerial carry samples the framework ball prediction at a short horizon, advances the car to the same time, and computes a velocity-matching PD acceleration with gravity compensation. A shortest-rotation quaternion error drives pitch/yaw/roll. Boost is gated by nose alignment, fuel, contact closing speed, and hysteresis. This is a **receding-horizon PD controller**, not a full nonlinear MPC optimizer or learned policy.

Strategy uses a conservative ground-intercept race estimate for ownership and pressure. Shot selection prefers a lower-cost ground/jump option over a more expensive aerial when a similar intercept is available. Emergency clears use the **own** goal as an exclusion target; the previous strategy constructed an away-from-opponent-goal target. Expensive searches are separated from per-frame control, but actual p95/p99 tick latency must still be measured on the target hardware with multiple bots.

## Research and what was actually used

**[1] Samuel Mish, Rocket League aerial-control notes.** [Primary source](https://www.smish.dev/rocket_league/aerial_control/). The orientation/angular-velocity representation, torque signs, and damping model inform the independent controller and analytic tests. The implementation here is not copied from RLUtilities and adds no RLUtilities dependency.

**[2] RLGym, Training an Agent.** [Official documentation](https://rlgym.org/Rocket%20League/training_an_agent/). Describes the RocketSim/PPO training workflow. This supports a practical next stage: train individual possession skills in a fast simulator and evaluate them separately. No such training was executed in this PR.

**[3] Moschopoulos et al. (2023), Lucy-SKG: Learning to Play Rocket League Efficiently Using Deep Reinforcement Learning.** [Paper](https://arxiv.org/abs/2305.15801). Its reward-combination, auxiliary-task, and ablation ideas are relevant to a future learned controller. The historical results in that paper are not a statement about current bot rankings, and the Lucy-SKG policy or training algorithm is not included here.

**[4] Pleines et al. (2022), On the Verge of Solving Rocket League using Deep Reinforcement Learning and Sim-to-sim Transfer.** [Paper](https://arxiv.org/abs/2205.05061). Demonstrates transfer of particular goalie/striker behaviors rather than proving general professional match strength. This motivates separate skill tests plus transfer validation, not equating a simulator score with full-game success.

**[5] RLBot v5 protocol.** The authoritative integration contract for this change is the repository's [pinned game-data schema](../src/flatbuffers-schema/schema/gamedata.fbs), especially the persistent jump/dodge flags. [Official match-communication documentation](https://wiki.rlbot.org/v5/botmaking/matchcomms/) explains the teammate coordination transport.

## Promotion criteria: run these in Rocket League before a release

Use ordinary Soccar with the same car hitbox, arena, gravity, boost mutator, bot count, and game/framework versions for both builds. Preserve initial-state fixtures, replay files, random seeds where supported, commit hashes, and feature flags. Mirror every fixture and swap blue/orange sides.

1. **Mechanic fixtures.** Start with stationary/moving hood carries, descending catches, pressured flicks, low/high aerial carries, spent-flip reset approaches, zero-boost recoveries, and inverted landings. Vary initial lateral error, speed, height, boost, and opponent approach. Record carry duration, own-touch continuity, ball-control loss, boost per useful touch, landing orientation, and recovery time. A reset is successful only if packet evidence confirms acquisition **and** a subsequent useful dodge/touch occurs; attempts and acquired-but-wasted resets must be separate counters.
2. **Match evaluation.** Run paired 1v1, 2v2, and 3v3 matches against the original `v5` and fixed, named reference opponents. Keep a held-out fixture/opponent set. Report wins/draws/losses, goal differential, own goals, double commitments, empty-net concessions, and a confidence interval. Preselect the sample size and acceptance rule rather than stopping after a winning streak. A practical first pass is 100 paired matches per mode, followed by more runs when uncertainty remains large.
3. **Ablation and deployment.** Compare baseline, runtime/strategy changes, ground-control enabled, aerial-carry enabled, and reset attempts enabled. Do not enable resets by default unless their net contribution is positive and they do not worsen defensive concessions. Measure actual p95/p99 control latency and allocations, including multiple instances in one process. Recheck on the real game even after simulator tests pass.

## A concrete route toward stronger learned mechanics

Keep the deterministic supervisor as a fallback and train **skill-conditioned residual controllers**, not an unvalidated replacement for every decision. A policy could propose bounded corrections to the analytic target acceleration/hood setpoint while the existing availability, resource, and interruption checks remain authoritative. This is a proposed architecture, not an implemented model.

A useful curriculum is: controlled contact -> sustained moving carry -> target-directed carry -> opponent pressure -> flick or aerial finish -> reset acquisition -> useful post-reset continuation. Reset rewards should require a spent-to-restored state transition plus contact evidence, and success should depend on retained possession or a useful follow-up rather than on touching the underside of the ball alone. Randomize contact offsets, boost, latency, opponent starts, and ball velocity; mix harder earlier stages back into later training to avoid forgetting. Evaluate held-out starts and opponents before selecting a checkpoint.

Use RLGym/RocketSim [2] for experience generation, and consider reward-shaping/auxiliary-task ideas from [3]. Export only a policy that has passed the held-out skill and game-transfer checks [4]. Version its observation normalization, action semantics, action-repeat rate, and recurrent-state reset behavior with its weights. Keep controls bounded and inference failure recoverable.

## Known limitations

No in-game match series, RocketSim contact rollout, GPU training, professional-player comparison, or measured multiplier is available from this change. Contact offsets and gains are heuristics requiring hitbox-specific tuning. The reset routine is an experimental acquisition/follow-through attempt, not a guarantee of advanced freestyle chains. Wall/ceiling setups, doubles, musty flicks, and learned opponent modeling are not newly implemented. The new tactical dimensions assume standard Soccar, and opponent ETA is a ground-race heuristic rather than a full opponent aerial predictor. Existing static RedUtils world data is serialized across bot instances, but broader multi-match/process isolation remains a separate architectural task.
