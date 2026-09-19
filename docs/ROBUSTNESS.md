# Stardust 3 robustness follow-up

This change addresses the reported shooting, spacing, aerial overshoot, fuel, defense, and goal-return problems on top of merged `v5` commit `bc2e7b80231275faf34bc697ba09f00f47fb1d3d`. The implementation and regression checks are real; an in-game win-rate improvement or successful reset rate is **not** established by this change.

## Verified defects versus tuning

The six `Stardust.BugRepro` cases were committed **before** the repairs. With the corrected fixture initialization, all six fail for their intended assertions at `a5c1f54e29b42108dfc4aaa6618ab256bdaf03a2` ([baseline CI](https://github.com/FlamingFury00/Stardust-2.0/actions/runs/35473259318)). They exercise production classes, not a separate reproduction of the algorithm:

| Defect or reproduced control failure | Before | Repair |
|---|---|---|
| Shared pad initialization | Initializing a one-pad field twice produced two pads; later entries could retain stale state | Rebuild the shared pad list rather than append |
| Boost timer semantics | A full pad picked up 4 seconds ago reported 4 seconds remaining rather than 6 | Convert elapsed timer to remaining cooldown; keep packet activity authoritative |
| Collapsed defensive positions | Deep-defense cover and support had zero separation | Distinct cover/support roles and targets |
| Nontransitive teammate election | With ETAs 1.12, 1.06, 1.00, observer-seeded epsilon comparisons produced winners 2, 2, 1 | Quantized ETA/index total ordering and one snapshot-based team assignment |
| Steering-axis mix-up | Ground driving permitted boost while the destination was 90 degrees sideways | Use absolute yaw, not the pitch element returned by `AimAt` |
| Aerial height overshoot | A synthetic closing-above-target fixture still requested upward boost | Relative-motion guidance, explicit support feed-forward and a minimum-burst clearance guard |

Further deterministic geometry/control checks cover the incorrect order in `Vec3.FlatAngle`: projecting already normalized vectors is not equivalent to normalizing their projections. Previously `(1,0,1).FlatAngle((1,0,1))` was 60 degrees instead of zero. The corrected flat angle ignores the removed component. Straight ground travel also now measures both endpoints on the same surface.

The aerial pre-turn position omitted the radius from its forward `sin(angle)` displacement. Both circular-arc components now scale with the radius. An airborne aerial also starts with its ground-launch phase already complete, rather than predicting/applying fresh ground-jump forces. Expired aerial times exit before division; finishing dodges require actual packet dodge availability.

Arrival-time estimation no longer assumes a stationary car is already driving at at least 1400 uu/s. A bounded longitudinal rollout includes speed-dependent throttle, available boost and braking from negative forward speed. The turning/wall portion remains an approximation; this is not a full physical reachability solver.

**The new spacing distances, shooting margins, pressure thresholds, fuel reserve and contact gains are tuning decisions, not proven bugs in the old numbers.** Their tests establish invariants and selected modeled behavior, not superiority in every live game state.

## Changes by reported symptom

### Shooting accuracy

The selector samples near-term intercepts densely and caps each search at 96 candidates. It checks a ground shot against the oriented arrival path that its executor actually follows. Offensive aim points stay inside the goal mouth rather than hugging posts or the crossbar. Clear targets still exclude the own goal. An unobstructed, soon-reachable shot is considered before an optional catch or dribble.

The original lateral-ball-velocity estimate for choosing the shot target is restored. Offensive aerial feasibility reserves 12 boost, without passing that reduced fuel into the executor's starting-fuel snapshot. Neither an open-lane heuristic nor an interior aim point is a calibrated probability of scoring.

### Team spacing and defense

`TeamShape` assigns a challenger, a deepest nonchallenging cover car, and separate support lanes. The cover target stays goal-side, and other support targets do not collapse onto it near the back wall. Assignments exclude demolished cars and use the same ordering for all observers. This does not communicate roles to unrelated bot implementations.

`Defense.Read` runs every control frame, before the expensive tactical timer. It recognizes incoming-box pressure even before the no-car-contact ball prediction shows a goal. Goal crossing is interpolated at the goal plane, and a save must be scheduled **before** that deadline. Only one elected saver attacks the emergency lane; backups hold distinct locations between tactical updates. The multi-frame integration fixture explicitly checks that a per-frame fallback update cannot overwrite all backup targets with the saver target.

Threat transitions force an immediate replan. Expensive planning runs at approximately 20 Hz under pressure and 10 Hz otherwise. This is a scheduling interval, **not measured end-to-end response latency**. Actual dodge/jump forces are protected from cancellation; a freely flying offensive aerial can be superseded by emergency defense.

### Goal returns and posts

`Navigate` uses braking-distance-aware arrival instead of driving through a defensive target at full speed. Routine targets are clipped in front of the goal line. A car inside the net first aligns with the aperture, exits forward, and only then travels sideways. A car outside the aperture approaches in front of the posts before crossing the goal mouth. Routine positioning does not powerslide or trigger travel dodges. This controller handles ground positioning; the retained surface driver handles wall/air transitions, and shot executors still own their contact paths.

### Air dribbles and boost

The carry controller no longer adds a permanent upward target velocity. Its look-ahead advances the car and reference consistently. A freely falling ball should not receive full hover compensation; close centered contact smoothly introduces an explicitly heuristic support target. The framework prediction itself has **no car collisions**, so that local support assumption is kept separate from the prediction [2].

`ImpulseBoost` accounts for the simulator's minimum 0.1-second boost activation [3], including residual thrust when a command is cancelled. It avoids treating repeated one-frame boost requests as arbitrarily short impulses. A clearance/closing-speed check rejects a new upward burst when it would push the car above its carry envelope. Air throttle stays neutral during this controller, because throttle is also thrust. A lost or fuel-starved carry yields to recovery.

Routine movement targets throttle speed and protects fuel; urgent defense may spend the reserve. Boost selection considers active small pads along the route as well as full pads, checks opponents' arrival **at the pad**, and yields to a closer low-fuel teammate. Under pressure, only a very nearby small pad with minimal detour is eligible. Sustained collection rate and average live boost remain unmeasured.

### Flip resets

**Reset attempts are still disabled unless `STARDUST_FLIP_RESETS=1`.** The flag must be inherited by the actual bot process. Starting a GUI separately from a configured shell does not guarantee that inheritance.

With the flag enabled, reset entry is available directly from airborne planning, not only from an already selected carry. The attempt requires a spent flip, enough height/fuel, opponent separation and defensive cover (or a sufficiently advanced solo attack). It now distinguishes an approach that needs closing velocity from one that can coast into wheel contact. Matching ball velocity while still separated is not enough to reach the ball.

The routine tracks `Approach`, `WheelContact`, `Confirmed`, `FollowThrough`, and `Aborted`. Confirmation still requires recent own wheel-facing contact and restored persistent packet flags; a nearby ball or generic airborne state cannot manufacture a reset. Failed approaches and acquired-but-unused resets have explicit timeout outcomes. The bot does not deliberately waste a usable flip just to manufacture a reset opportunity, and wall-to-reset setups are not newly implemented.

```powershell
# Set these before starting the RLBot launcher/bot process from this environment.
$env:STARDUST_TRACE = "1"
$env:STARDUST_FLIP_RESETS = "1"
```

At startup, trace output reports `experimentalResets=True` or `False`. Selection logs `mechanic / reset approach`; phase logs distinguish approach, contact evidence, a requested follow-through dodge, and abort reasons. `dodge-requested` is not a claim of a successful shot. No live reset has been observed by these automated tests.

The previous ablation flags remain: `STARDUST_GROUND_CONTROL=0` and `STARDUST_AERIAL_CARRY=0`. They disable those selections, not the strategy, geometry, timing or navigation repairs.

## Reproduce validation

```sh
git fetch origin
git checkout fix/stardust-3-match-robustness
chmod +x src/generate-flatbuffers.sh src/flatbuffers-schema/binaries/flatc
dotnet build src/Bot/Bot.csproj --configuration Release
dotnet run --project tests/Stardust.Tests/Stardust.Tests.csproj --configuration Release
dotnet run --project tests/Stardust.ModelChecks/Stardust.ModelChecks.csproj --configuration Release
dotnet run --project tests/Stardust.BugRepro/Stardust.BugRepro.csproj --configuration Release
dotnet run --project tests/Stardust.Robustness/Stardust.Robustness.csproj --configuration Release
```

CI requires all four executables. The original programs have 50 regression groups and 45 analytic checks. The follow-up adds six red-to-green reproductions, 32 robustness groups and nine geometry/aerial checks. The robustness executable includes 2,000 seeded formation targets, all six teammate-order permutations, multi-frame production-strategy scenarios, and longitudinal/vertical analytic rollouts at 120, 60 and 30 Hz. Those plants do not model collisions, tire friction, actual opponent behavior, or aerial attitude/contact coupling.

A diagnostic benchmark warms the planner and measures 100 batches of three sequential bots in one six-car, 721-slice stationary-ball fixture. It prints p50/p95/p99 time and allocated bytes on the CI runner. It has no hardware-specific pass threshold and is **not** a live tick-latency benchmark or a measured improvement versus the old build.

## In-game promotion checklist

Run both sides of each fixture with the same standard Soccar arena, car hitbox, gravity and boost settings. Compare with the exact merged base commit, not merely with optional mechanics disabled.

- Record shots attempted, on-target rate and goals; include awkward approaches and nearby defenders.
- Record teammate minimum separation, duplicate challenges, empty-net concessions and save attempts that began too late.
- Test returns from beside each post, inside each side of the net, and high-speed rotations toward goal.
- Measure vertical car/ball gap, useful touches and fuel spent throughout carries, not just the first successful touch.
- Separate reset selection, confirmed acquisition, follow-through dodge and useful final contact in the counts.
- Measure live p95/p99 complete `GetOutput` time and allocations with three bots, including ball touches and emergency replans.

Do not infer competitive improvement from passing unit tests alone. Opponent reachability is still principally a ground-race heuristic, contact offsets require hitbox tuning, and role stability against rapidly changing live states remains to be assessed.

## Primary references

[1] Samuel Mish, [Ground control](https://www.smish.dev/rocket_league/ground_control/): speed-dependent steering/throttle, braking and boost. Informs the longitudinal model and arrival braking; its model also has stated approximations.

[2] RLBot, [Ball path prediction](https://wiki.rlbot.org/v5/botmaking/ball-path-prediction/): timestamps, 120 Hz slices, and the no-car-collision assumption. Informs dense urgent sampling and the distinction between ballistic prediction and contact support.

[3] RocketSim, [`RLConst.h`](https://github.com/ZealanL/RocketSim/blob/c2baacb8f4b441dd8505e63c2aeb5a1679b60b02/src/RLConst.h) and [`Car.cpp`](https://github.com/ZealanL/RocketSim/blob/c2baacb8f4b441dd8505e63c2aeb5a1679b60b02/src/Sim/Car/Car.cpp): `BOOST_MIN_TIME` and minimum-duration boost handling. This source informs the analytic actuator model; RocketSim itself was not run here.

[4] RLBot, [Useful game values](https://wiki.rlbot.org/v5/botmaking/useful-game-values/): goal geometry, pad cooldowns, throttle speed and air/ground acceleration. Runtime boost-pad locations continue to come from `FieldInfo`.

[5] The repository's [pinned game-data schema](../src/flatbuffers-schema/schema/gamedata.fbs): elapsed boost timers, persistent jump/dodge flags and controller axes. This remains the integration contract.

The earlier training papers and residual-policy proposal in [STARDUST_3.md](STARDUST_3.md) remain research directions, not implemented training or evidence for this patch's playing strength.
