# Stardust: A Rocket League Bot

## Pace and commitment recovery on v5

The previous conservative 3.0 changes regressed live play. This correction restores the existing fast `Drive`/`GetBoost` paths, travel flips, original shot selection contracts and active challenges. Local goal-mouth braking remains. Experimental possession selectors are **off by default**; standard aerial shots and travel/shot dodges remain enabled.

See **[the recovery notes and reproduction commands](docs/TEMPO_RECOVERY.md)**. The original registration identity is retained. There is no claim of improved match strength from synthetic tests alone; the earlier 3.0 reports are historical, not the current default behavior.

## Project background

Stardust is a Rocket League bot built on the [RLBot](http://www.rlbot.org/) framework for offline matches, using RedUtils (C#). This branch targets the vendored RLBot v5 integration; use a compatible v5 setup rather than assuming an older GUI/runtime is interchangeable.

![Stardust Logo](./logo.png)

## Stardust 2.0 history

The original Stardust 2.0 was optimized for 2v2 and 3v3 and was redeveloped for the RLBot Championship finals. The repository records a fourth-place finish in the **RLBot Championship 2023**; the original finals video is [here](https://www.youtube.com/watch?v=6A8_6RR4vR0&t=305s).

The 3.0 candidate retains the existing shot solvers, kickoff routines, and drive subactions, while improving the control lifecycle and introducing possession controllers. The [evaluation guide](docs/ROBUSTNESS.md#live-acceptance-checks-still-required) separates software checks from the in-game evidence needed to establish playing strength.
