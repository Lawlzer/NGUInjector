# Mod Loader Restart Fix

## Cause

On 2026-10-19, `jshepler.ngu.mods` was installed but absent from the running game. The first game process loaded BepInEx at 16:55:37, then started Steam. Steam relaunched NGU at 16:56:06 with inherited `DOORSTOP_DISABLE=TRUE` and `DOORSTOP_INITIALIZED=TRUE`. Both flags were confirmed by read-only inspection of the Steam and game process environments. The replacement game mapped the local `winhttp.dll` but skipped BepInEx; its Unity log contained no BepInEx startup, and port 8088 refused connections. The earlier successful BepInEx log was stale.

BepInEx 5.4.21 bundles UnityDoorstop 3.4.0.0. In that version, `ignoreDisableSwitch=true` bypasses both inherited suppression flags, not just the disable flag named in the configuration comment. This is a loader/bootstrap problem, not a missing plugin, changed save, or NGUInjector failure.

## Fix

The installed game's `doorstop_config.ini` now contains:

```ini
[UnityDoorstop]
ignoreDisableSwitch=true
```

All other loader settings and mod files are unchanged. This is an installation-local setting, not a repository build output. It remains effective on restart unless the loader configuration is replaced. It bypasses Doorstop's child-process suppression for this game installation; do not apply it globally or assume identical behavior in Doorstop 4.

An alternative without changing configuration is to exit the game and Steam completely, start Steam independently from a clean desktop environment, then launch NGU. Starting Steam beneath a Doorstop-initialized game is the problematic sequence. Clearing flags in an unrelated shell cannot clean an already-running Steam process.

## Verification

Before restarting, the runtime, settings, preset library, custom plans, original loader configuration/log, and existing quicksaves were backed up under `C:\Users\Kevin\AppData\Local\Temp\opencode\ngu-mod-recovery-20261019-173000`. The injector's `quickSave` command then saved current progress locally and to Steam Cloud; that fresh save was backed up separately. NGU was closed gracefully and restarted with the existing Release injector and desktop UI, without rebuilding or force-killing.

The new game generated a fresh BepInEx log confirming `Plugin jshepler.ngu.mods is loaded!` for version 1.30.1. `GET http://localhost:8088/ngu/ngu2go/ngustats`, which refused connections before the fix, returned live energy/magic NGU data afterward. The injector reconnected with a fresh running protocol-v3 heartbeat and no startup error. The current `24hr` Time Machine stage and user preferences were preserved, including the existing automatic-rebirth setting; no plan or allocation changes were requested as part of this repair.

## References

- [BepInEx 5.4.21 bundled Doorstop version](https://github.com/BepInEx/BepInEx/blob/v5.4.21/build.cake)
- [Doorstop 3.4.0.0 initialization and inherited flags](https://github.com/NeighTools/UnityDoorstop/blob/v3.4.0.0/Proxy/main.c)
- [Doorstop 3.4.0.0 configuration handling](https://github.com/NeighTools/UnityDoorstop/blob/v3.4.0.0/Proxy/config.c)
- [Documented Steam relaunch failure](https://github.com/NeighTools/UnityDoorstop/issues/34)
