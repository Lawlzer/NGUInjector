# Route Session Restarts

Route claims, borrowed native-field return rights, selected route gear, boss action
steps and manual death cursors are process-session state. They are not exported,
imported or rebound. Retired route/action journals and `combat-prep-os.json` are
not route startup inputs.

- Native `isFighting` and `nukeBoss` flags block disruptive route setup even when
  the new injector has no claim. Ordinary enabled routing resumes when native
  combat is idle, observing the actual current route and setup.
- A superseded coordinator cannot service route ticks or use old cleanup rights.
- A manual titan first observed due in the session waits for a native timer reset
  and the following due transition. This bounded observation covers every native
  titan index, including while the manual rule is disabled. Settings edits and
  enable toggles do not supply another spawn opportunity.
- Startup-selected max ITOPOD mode grants no death budget. A new explicit push
  command or an observed mode change is required. Cancellation consumes the local
  request without rewriting the user's selected mode.
- Lost capacity-route authority ends that bounded claim without recalling foreign
  resources, moving a foreign route, or restoring historical gear. Verified,
  still-owned session handoffs can apply the current successor directly.
- A verified background digger purchase can renew both cached capacity-route
  grants through a one-use before/after approval. The original owner, selection,
  permissions, allocations and conflict version must remain exact; only the
  expected Support revision may advance. Acquisition and attempt budgets do not.
- Boss/OS leaves use the runner's actual `Invoked`, `Committed` and
  `AuthorityCurrent` results. Unverified effects cancel the current attempt; they
  do not blacklist later actions of the same kind.
- Combat preparation retains its verified OS choice only in memory. Native
  Wandoos work remains a separate no-reset guard. Automatic cancellation does not
  credit completion or extend the acquired entry's original deadline. Stage
  progress/checkpoints still own earned AT/augment progress, Wandoos-started state,
  acquired resource pools and the original stage budget.

`RouteExecutorCutoverChecks` replaces only the injector coordinator for restart
checks, preserving native controllers, timers, flags, gear, allocations and user
settings. Its boss/OS observers count or interrupt real native method boundaries;
they do not replace native busy/completion behavior or grant cleanup authority.
The next-spawn regression supplies native kill-counter/reset outcomes and calls
the actual `PlayerTime.setTime(double)` overload for reset/due transitions. Native
idle/heal scene controls must be initialized: a scene callback exception after
admission is not evidence that the opportunity was rejected.
