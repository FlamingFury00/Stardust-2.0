# Stardust 3.0 — robustness revision

The current implementation and evaluation notes are in **[ROBUSTNESS.md](ROBUSTNESS.md)**.

The [initial 3.0 design and research report](STARDUST_3_INITIAL.md) is retained as a historical reference. Its original feature defaults and planning rates are superseded by the robustness revision: reset attempts now use guarded automatic selection (disable with `STARDUST_FLIP_RESETS=0`), and support/defensive positioning and aerial thrust control have changed.

This remains an evaluation candidate, not a benchmark-certified professional-level release. Software regressions and analytic rollouts do not establish match win rate or in-game reset success.
