#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

use chrono::{DateTime, SecondsFormat, Utc};
use fs2::FileExt;
use serde::{Deserialize, Serialize};
use serde_json::{json, Value};
use sha2::{Digest, Sha256};
use std::cmp::Reverse;
use std::collections::BinaryHeap;
use std::fs::{self, File, OpenOptions};
use std::io::{Read, Seek, SeekFrom, Write};
use std::path::{Path, PathBuf};
use std::time::{Duration, Instant, SystemTime, UNIX_EPOCH};
use uuid::Uuid;

mod preset_library;
use preset_library::*;

#[cfg(test)]
#[path = "fight-schedule-tests.rs"]
mod fight_schedule_tests;

const MARKER_FILE: &str = "external-ui.enabled";
const OWNERSHIP_FILE: &str = ".injector.lock";
const REVISION_FIELD: &str = "__externalUiRevision";
const HEARTBEAT_MAX_AGE_MS: i64 = 5_000;
const FUTURE_TOLERANCE_MS: i64 = 2_000;
const COMMAND_TIMEOUT_MS: u64 = 10_000;
const MAX_JSON_BYTES: u64 = 4 * 1024 * 1024;
const MAX_RUNTIME_BYTES: u64 = 1024 * 1024;
const MAX_RESULT_BYTES: u64 = 64 * 1024;
// Preview payloads also carry attached runtime and a command-result envelope.
const ACTION_RESULT_MAX_BYTES: u64 = 256 * 1024;
const MAX_PLAN_RESULT_BYTES: u64 = MAX_JSON_BYTES + MAX_RESULT_BYTES;
const MAX_COMMAND_BYTES: u64 = 1024 * 1024;
const MAX_LOG_BYTES: u64 = 256 * 1024;
const MAX_LOG_LINES: usize = 2_000;
const MAX_RESULTS: usize = 20;
const PLAN_BLOOD_OVERRIDE_FIELDS: [&str; 5] = [
    "AutoBloodSpellsEnabled",
    "BloodSpaghettiGoalPercent",
    "BloodCounterfeitGoalPercent",
    "IronPillSaveSeconds",
    "IronPillMinWorth",
];
const AUTO_EXP_RATIO_DEFAULTS: [(&str, i32); 8] = [
    ("_customExpEnergyRatio", 5),
    ("_customExpMagicRatio", 1),
    ("_customExpEnergyPowerRatio", 5),
    ("_customExpEnergyCapRatio", 160000),
    ("_customExpEnergyBarsRatio", 4),
    ("_customExpMagicPowerRatio", 5),
    ("_customExpMagicCapRatio", 160000),
    ("_customExpMagicBarsRatio", 4),
];

#[derive(Debug, Serialize)]
struct NguiState {
    runtime_dir: String,
    marker_enabled: bool,
    connected: bool,
    protocol: Value,
    #[cfg(test)]
    #[serde(skip_serializing)]
    settings: Value,
    settings_json: String,
    settings_revision: String,
    runtime: Value,
    profiles: Vec<String>,
    plans: Vec<String>,
    logs: Vec<LogFile>,
    command_results: Vec<CommandResultSummary>,
}

#[derive(Debug, Serialize)]
struct LogFile {
    name: String,
    size: u64,
    modified_epoch_ms: Option<u128>,
}

#[derive(Debug, Deserialize, Serialize)]
struct CommandResultSummary {
    id: String,
    command: String,
    success: bool,
    message: String,
    #[serde(alias = "completedAt")]
    completed_at: String,
}

#[derive(Debug, Deserialize)]
struct CommandEnvelope {
    command: String,
    #[serde(default = "empty_object")]
    args: Value,
}

fn empty_object() -> Value {
    json!({})
}

fn protocol_info() -> Value {
    let mut protocol = json!({
        "version": 3,
        "planVersion": 18,
        "stageTimingVersion": 1,
        "stageTiming": {"horizon": "TimeTriggerSeconds when TimeTriggerEnabled and positive, otherwise StageDurationSeconds. Zero is allowed", "timing": "DurationSeconds > 0 is fixed actual applied time; DurationSeconds == 0 shares max(0, horizon minus fixed total) by positive Percent weight. AT-target stages are untimed mandatory work and excluded from timed totals. Fixed totals may equal or exceed the horizon. All-fixed plans retain their saved budget", "liveWrites": "Reading/saving/copying/creating, selecting, running, or enabling auto rebirth requires fresh runtime.stageTimingVersion == 1 AND runtime.fightScheduleVersion == 1 AND runtime.planVersion >= 18, regardless of plan contents"},
        "globalNguWeightsVersion": 1,
        "nguWeights": {"owner": "settings.json only; never plans or presets", "_energyNguWeights": "exactly 9 nonnegative Int32 entries", "_magicNguWeights": "exactly 7 nonnegative Int32 entries", "energyDefault": [0,0,0,0,1,0,1,0,0], "magicDefault": [1,0,0,0,0,0,0], "allZero": "Allowed; disables that resource's NGUs", "migration": "Injector durably captures the selected legacy plan once before retiring plan ratios; offline writes that could lose unmigrated ratios require connecting first", "requiredRuntimeFields": {"globalNguWeightsVersion": 1}},
        "stageDurations": {"DurationSeconds": "Int32: 0 uses positive Percent weight; > 0 is fixed whole seconds of actual applied time", "DurationUnit": "%, s, min, or h; display hint only", "effectiveDuration": "Weighted flex shares max(0, horizon minus fixed total). AT-target rows (CompletionMode 1) retain validated duration fields but are excluded from timed totals. No minimum duration or rounding is applied"},
        "requiredRuntimeField": {"protocolVersion": 3},
        "supportedCommandsField": "supportedCommands (must include the requested command)",
        "commandIdFormat": "canonical UUID v4",
        "timestampFormat": "ISO-8601 UTC (createdAt/expiresAt); updatedAt accepts an ISO-8601 offset",
        "heartbeatMaxAgeMs": HEARTBEAT_MAX_AGE_MS,
        "commandTimeoutMs": COMMAND_TIMEOUT_MS,
        "maxCommandBytes": MAX_COMMAND_BYTES,
        "maxResultBytes": ACTION_RESULT_MAX_BYTES,
        "maxPlanResultBytes": MAX_PLAN_RESULT_BYTES,
        "readiness": "externalUiEnabled == true, status == running, and a recent updatedAt heartbeat",
        "settingsCommand": {"command": "applySettingsPatch", "args": {"patch": "JSON object", "expectedRevision": "required exact-byte lowercase SHA-256; empty only for an absent settings file"}, "result": "data.settings and data.revision are the committed snapshot, not a heartbeat acknowledgement", "transport": "get_ngui_state returns settings_json + settings_revision from the same bytes; save_settings_patch_json takes patchJson + expectedRevision and returns a lossless JSON string"},
        "readPlanCommand": {"command": "readPlan", "args": {"name": "safe file stem"}, "resultPlan": "data.plan: normalized lossless JSON object with __externalUiRevision"},
        "planCommand": {"command": "savePlan", "args": {"name": "safe file stem", "plan": "JSON object without __externalUiRevision", "expectedRevision": "SHA-256 of exact original file bytes; required for existing plans; empty string only for a nonexistent file (never null)"}},
        "tauriPlanTransport": {"read_plan": "returns a JSON STRING with __externalUiRevision", "save_plan": "args runtimeDir/name/planJson (JSON STRING with revision)", "create_plan": "args runtimeDir/requestedName/sourceJson (optional JSON STRING)"},
        "revision": "__externalUiRevision is a transport-only lowercase SHA-256; never persist it; preserve it while editing and reload after saving",
        "offlineMigration": "Plans with missing/legacy Version < 14 must be read through a connected injector to normalize migrations",
        "createPlanCommand": {"command": "createPlan", "args": {"requestedName": "safe file stem", "source": "optional JSON object; copy history is cleared"}, "resultName": "data.name"},
        "deletePlanCommand": {"command": "deletePlan", "args": {"name": "safe file stem"}, "resultSelection": "data.selectedPlan"},
        "manualStageOverride": {"command": "setPlanStage", "transport": "enqueue_command", "connectedOnly": true, "args": {"name": "safe plan file stem", "expectedRevision": "nonempty current plan revision string; preserved exactly", "expectedEntry": "nonempty current stage entry token; preserved exactly", "action": "skip | set | restart", "stageIndex": "required nonnegative Int32 for every action"}, "requiredRuntimeFields": {"stageTimingVersion": 1, "fightScheduleVersion": 1, "planVersion": ">= 18", "stageOverridesV1": true}, "guards": "Injector checks the current selected plan, revision, entry token, and stage index immediately before execution. Overrides change runtime progress, never the plan definition; no offline fallback"},
        "augmentCommands": {"commands": ["previewAugments", "allocateAugments"], "args": "{} or {expectedSeconds: positive Int32}"},
        "loadoutPresets": {"previewCommand": "previewLoadoutPreset", "applyCommand": "applyLoadoutPreset", "args": {"preset": "inline Pascal-case LoadoutPresets row; Weights contains 6, 10, 12 or 14 numbers (adventurePower/adventureToughness at indices 10/11, energyCap/magicCap at 12/13); DiggerWeights remains empty or 12 numbers; DiggerBudgetPercent and SwapSeconds are integers; legacy HorizonSeconds is ignored and not required", "candidateKey": "apply only: safe ASCII key <=64 from latest matching preview", "previewId": "apply only: server-owned 32-hex token from latest matching preview; consumed on application"}, "requiredRuntimeFields": {"loadoutPresetsVersion": 2, "globalNguWeightsVersion": 1, "startedAt": "identified injector session"}, "storage": "optional LoadoutPresets array, max32, empty allowed; independent of legacy GoalPresets; retired NGU fields stripped, other unknown fields preserved; plan Version 18", "nguWeights": "Captured from global settings only; lowercase ratio args are rejected", "result": "data.result version 1 with previewId; same-snapshot candidates, specialists, normalization and optional two-phase estimate with authoritative backend assumptions", "application": "explicit confirmed cached snapshot application; backend validates material state and epoch without replacing the selected candidate with a fresh search; runtime.loadoutPreset records Last applied, active:false, requiresReapply:true, mode:applied-snapshot"},
        "combatPrep": {"previewCommand": "previewCombatPrep", "previewArgs": {"durationSeconds": "required integer 1..86400", "backgroundEnergyPercent": "required number 0..100", "backgroundMagicPercent": "required number 0..100"}, "startCommand": "startCombatPrep", "startArgs": {"previewId": "nonempty string <=128 UTF-16 units", "candidateId": "nonempty string <=128 UTF-16 units"}, "cancelCommand": "cancelCombatPrep", "cancelArgs": {"runId": "nonempty string <=128 UTF-16 units"}, "connectedOnly": true, "maxResultBytes": ACTION_RESULT_MAX_BYTES, "maxPreviewBytes": ACTION_RESULT_MAX_BYTES, "previewTtlSeconds": 120},
        "adventureModes": {"0": "Optimal ITOPOD", "1": "Farm selected zone", "2": "Farm boosts dynamically", "3": "Max ITOPOD push"},
        "maxItopodPush": {"modeSetting": "_customAdventureMode = 3", "deathLimitSetting": "_customAdventureMaxItopodDeaths: Int32 1..1000, default 3", "runtimeSetting": "settings.customAdventureMaxItopodDeaths", "startCommand": "startMaxItopodPush", "startArgs": {"deaths": "required integer 1..1000"}, "cancelCommand": "cancelMaxItopodPush", "cancelArgs": {}, "connectedOnly": true, "runtime": "maxItopodPush: {active: boolean, remainingDeaths: int, floor: int}", "behavior": "UI saves mode and death limit; custom Adventure enable starts the mode-owned push; changing mode away cancels it; after the death limit switches to Optimal ITOPOD. Start/cancel commands remain for external consumers"},
        "expiryRequirement": "Bridge must check expiresAt immediately before main-thread execution; expired requests must fail and never replay (including claimed requests)",
        "resultFields": ["id", "command", "success", "message", "completedAt"],
        "marker": "external-ui.enabled is a startup opt-in, not proof of connection",
        "planManagement": "Connected creation/deletion use the main-thread queue; offline deletion protects built-ins, the selected running plan, and the last plan",
        "offlineSettingsPatches": "Only known serialized settings fields are editable; unknown stored fields are preserved",
        "offlineWriterLock": ".injector.lock is held across every offline read/modify/write with Windows FileShare.None semantics (fs2 exclusive lock on non-Windows); stale/missing heartbeat does not authorize writes when injector ownership is held; .external-ui-write.lock serializes UI operations"
    });
    protocol["fightScheduleVersion"] = json!(1);
    protocol["fightSchedule"] = json!({"stageField": "FightSchedule: null or {Id, Anchor, DelaySeconds, IntervalSeconds}", "id": "1..128 ASCII letters, digits, underscore or hyphen; unique within plan", "anchor": "0 stage entry; 1 verified Augments started", "seconds": "nonnegative Int32; interval 0 is once", "migration": "Only absent FightSchedule and true FightOnEntry migrate to legacy-entry-v1-<zero-based index>; explicit null wins; FightOnEntry is removed", "edits": "Timing freezes for the entry; edits apply next entry or restart; disabling stops future admission"});
    protocol["settingsCasVersion"] = json!(1);
    protocol["loadoutPresetsVersion"] = json!(2);
    protocol["globalPresetsVersion"] = json!(2);
    protocol["globalPresets"] = global_preset_protocol();
    protocol
}

#[tauri::command]
fn get_ngui_state(runtime_dir: Option<String>) -> Result<NguiState, String> {
    let dir = resolve_runtime_dir(runtime_dir)?;
    let (settings, settings_revision) = read_settings_snapshot(&dir)?;
    let settings_json = settings.to_string();
    let runtime = read_runtime(&dir)?;
    Ok(NguiState {
        runtime_dir: dir.to_string_lossy().into_owned(),
        marker_enabled: dir.join(MARKER_FILE).is_file(),
        connected: runtime_is_recent(&runtime, Utc::now().timestamp_millis()),
        protocol: protocol_info(),
        #[cfg(test)]
        settings,
        settings_json,
        settings_revision,
        runtime,
        profiles: list_json_stems(&dir.join("profiles"))?,
        plans: list_json_stems(&dir.join("custom-plans"))?,
        logs: list_logs(&dir.join("logs"))?,
        command_results: list_command_results(&dir.join("command-results"))?,
    })
}

#[tauri::command]
async fn save_settings_patch_json(
    runtime_dir: String,
    patch_json: String,
    expected_revision: String,
) -> Result<String, String> {
    tauri::async_runtime::spawn_blocking(move || {
        let patch = parse_json_object(patch_json.as_bytes(), "Settings patch")?;
        validate_settings_revision(&expected_revision)?;
        validate_settings_patch(&patch)?;
        let dir = resolve_runtime_dir(Some(runtime_dir))?;
        let lock = lock_offline_writer(&dir)?;
        let runtime = read_runtime(&dir)?;
        if runtime_is_recent(&runtime, Utc::now().timestamp_millis()) {
            require_live_bridge(&runtime)?;
            let deadline = Instant::now() + Duration::from_millis(COMMAND_TIMEOUT_MS);
            let id = queue_command(&dir, "applySettingsPatch", json!({"patch": patch, "expectedRevision": expected_revision}))?;
            drop(lock);
            let response = wait_for_command(&dir, &id, "applySettingsPatch", deadline)?;
            let data = &response["result"]["data"];
            require_object(&data["settings"], "Committed settings")?;
            let revision = data["revision"]
                .as_str()
                .filter(|revision| !revision.is_empty())
                .ok_or("Committed settings revision is required")?;
            validate_settings_revision(revision)?;
            return Ok(response.to_string());
        }
        let _ownership = lock_injector_ownership(&dir)?;
        let path = dir.join("settings.json");
        let (mut settings, revision) = read_settings_snapshot(&dir)?;
        if revision != expected_revision {
            return Err("Settings revision conflict; reload settings before saving".into());
        }
        validate_offline_settings_patch(&patch)?;
        if patch.get("_customPlanFile").is_some() {
            require_durable_global_ngu_weights(&dir, &settings, None)?;
        }
        merge_object_patch(&mut settings, &patch)?;
        fill_auto_exp_defaults(&mut settings);
        for (key, value) in [
            ("_manualAdventureLoadout", json!(-1)),
            ("_manualAdventureGearWeights", json!(vec![0; 5])),
            ("_manualAdventureBeardWeights", json!(vec![0; 7])),
            ("_manualAdventureDiggerWeights", json!(vec![0; 12])),
            ("_manualTitanEnabled", json!(false)),
            ("_manualTitanZone", json!(16)),
            ("_manualTitanRequireAT", json!(true)),
            ("_manualTitanLoadout", json!(1)),
            ("_manualTitanMaxDeaths", json!(3)),
        ] {
            settings
                .as_object_mut()
                .unwrap()
                .entry(key)
                .or_insert(value);
        }
        settings
            .as_object_mut()
            .unwrap()
            .entry("_customAdventureMaxItopodDeaths")
            .or_insert(json!(3));
        settings
            .as_object_mut()
            .unwrap()
            .entry("_customAdventureRules")
            .or_insert(json!([]));
        write_json_atomic(&path, &settings, true)?;
        let (settings, revision) = read_settings_snapshot(&dir)?;
        Ok(json!({"success": true, "mode": "offline", "data": {"settings": settings, "revision": revision}, "protocol": protocol_info()}).to_string())
    })
    .await
    .map_err(|err| format!("Settings worker failed: {err}"))?
}

fn read_settings_snapshot(dir: &Path) -> Result<(Value, String), String> {
    match read_file_bytes(&dir.join("settings.json"), MAX_JSON_BYTES, true)? {
        Some(bytes) => Ok((
            parse_json_object(&bytes, "settings.json")?,
            file_revision(&bytes),
        )),
        None => Ok((empty_object(), String::new())),
    }
}

fn validate_settings_revision(revision: &str) -> Result<(), String> {
    if revision.is_empty()
        || revision.len() == 64
            && revision
                .bytes()
                .all(|byte| byte.is_ascii_digit() || (b'a'..=b'f').contains(&byte))
    {
        Ok(())
    } else {
        Err("Settings revision must be lowercase SHA-256, or empty only for an absent file".into())
    }
}

// Older persistence tests express a single fresh edit. Production has no revision-less entry point.
#[cfg(test)]
async fn save_settings_patch(runtime_dir: String, patch: Value) -> Result<Value, String> {
    let dir = resolve_runtime_dir(Some(runtime_dir.clone()))?;
    let (_, revision) = read_settings_snapshot(&dir)?;
    let result = save_settings_patch_json(runtime_dir, patch.to_string(), revision).await?;
    parse_json_object(result.as_bytes(), "Settings result")
}

#[tauri::command]
async fn read_plan(runtime_dir: String, name: String) -> Result<String, String> {
    tauri::async_runtime::spawn_blocking(move || {
        let dir = resolve_runtime_dir(Some(runtime_dir))?;
        let path = plan_path(&dir, &name)?;
        if runtime_is_recent(&read_runtime(&dir)?, Utc::now().timestamp_millis()) {
            let deadline = Instant::now() + Duration::from_millis(COMMAND_TIMEOUT_MS);
            let id = queue_command(&dir, "readPlan", json!({"name": name}))?;
            let response = wait_for_command(&dir, &id, "readPlan", deadline)?;
            connected_plan_json(&response)
        } else {
            let _ownership = lock_injector_ownership(&dir)?;
            read_plan_json(&path)
        }
    })
    .await
    .map_err(|err| format!("Plan read worker failed: {err}"))?
}

#[tauri::command]
async fn save_plan(runtime_dir: String, name: String, plan_json: String) -> Result<Value, String> {
    tauri::async_runtime::spawn_blocking(move || {
        validate_file_name(&name)?;
        let mut plan = parse_json_object(plan_json.as_bytes(), "Plan JSON")?;
        let expected_revision = take_plan_revision(&mut plan)?;
        let dir = resolve_runtime_dir(Some(runtime_dir))?;
        let lock = lock_offline_writer(&dir)?;
        let runtime = read_runtime(&dir)?;
        if runtime_is_recent(&runtime, Utc::now().timestamp_millis()) {
            require_live_bridge(&runtime)?;
            let deadline = Instant::now() + Duration::from_millis(COMMAND_TIMEOUT_MS);
            let id = queue_command(
                &dir,
                "savePlan",
                json!({"name": name, "plan": plan, "expectedRevision": expected_revision.as_deref().unwrap_or("")}),
            )?;
            drop(lock);
            return wait_for_command(&dir, &id, "savePlan", deadline);
        }
        save_plan_offline(&dir, &name, &plan, expected_revision.as_deref())?;
        Ok(json!({"success": true, "mode": "offline", "protocol": protocol_info()}))
    })
    .await
    .map_err(|err| format!("Plan worker failed: {err}"))?
}

#[tauri::command]
async fn read_preset_library(runtime_dir: String) -> Result<String, String> {
    tauri::async_runtime::spawn_blocking(move || {
        let dir = resolve_runtime_dir(Some(runtime_dir))?;
        let lock = lock_offline_writer(&dir)?;
        if runtime_is_recent(&read_runtime(&dir)?, Utc::now().timestamp_millis()) {
            let deadline = Instant::now() + Duration::from_millis(COMMAND_TIMEOUT_MS);
            let id = queue_command(&dir, "readPresetLibrary", json!({}))?;
            drop(lock);
            let response = wait_for_command(&dir, &id, "readPresetLibrary", deadline)?;
            connected_preset_library_json(&response)
        } else {
            let _ownership = lock_injector_ownership(&dir)?;
            let (mut library, revision) = read_preset_library_snapshot(&dir)?;
            library[REVISION_FIELD] = json!(revision);
            serde_json::to_string(&library).map_err(|err| err.to_string())
        }
    })
    .await
    .map_err(|err| format!("Preset library read worker failed: {err}"))?
}

#[tauri::command]
async fn save_preset_library(runtime_dir: String, library_json: String) -> Result<Value, String> {
    tauri::async_runtime::spawn_blocking(move || {
        if library_json.len() as u64 > MAX_PRESET_LIBRARY_RESULT_BYTES {
            return Err("Preset library JSON exceeds the transport size limit".into());
        }
        let mut library = parse_json_object(library_json.as_bytes(), "Preset library JSON")?;
        let revision = library.as_object_mut().unwrap().remove(REVISION_FIELD)
            .ok_or("Preset library revision is required; reload before saving")?;
        let expected = preset_library_revision(&revision)?;
        let library = normalize_preset_library(library)?;
        let dir = resolve_runtime_dir(Some(runtime_dir))?;
        let lock = lock_offline_writer(&dir)?;
        if runtime_is_recent(&read_runtime(&dir)?, Utc::now().timestamp_millis()) {
            let deadline = Instant::now() + Duration::from_millis(COMMAND_TIMEOUT_MS);
            let id = queue_command(&dir, "savePresetLibrary", json!({"library": library, "expectedRevision": expected}))?;
            drop(lock);
            let response = wait_for_command(&dir, &id, "savePresetLibrary", deadline)?;
            preset_library_revision(&response["result"]["data"]["revision"])?;
            Ok(response)
        } else {
            let revision = save_preset_library_offline(&dir, library, &expected)?;
            Ok(json!({"success": true, "mode": "offline", "data": {"revision": revision}, "protocol": protocol_info()}))
        }
    })
    .await
    .map_err(|err| format!("Preset library save worker failed: {err}"))?
}

#[tauri::command]
async fn create_plan(
    runtime_dir: String,
    requested_name: String,
    source_json: Option<String>,
) -> Result<String, String> {
    tauri::async_runtime::spawn_blocking(move || {
        validate_file_name(&requested_name)?;
        let source = source_json
            .as_deref()
            .map(|text| parse_json_object(text.as_bytes(), "Plan source JSON"))
            .transpose()?;
        let dir = resolve_runtime_dir(Some(runtime_dir))?;
        let lock = lock_offline_writer(&dir)?;
        let runtime = read_runtime(&dir)?;
        if runtime_is_recent(&runtime, Utc::now().timestamp_millis()) {
            let mut args = json!({"requestedName": requested_name});
            if let Some(source) = source {
                args["source"] = prepare_plan_copy(source, &requested_name)?;
            }
            let deadline = Instant::now() + Duration::from_millis(COMMAND_TIMEOUT_MS);
            let id = queue_command(&dir, "createPlan", args)?;
            drop(lock);
            let response = wait_for_command(&dir, &id, "createPlan", deadline)?;
            command_result_name(&response, "name")
        } else {
            let _ownership = lock_injector_ownership(&dir)?;
            if let Some(source) = &source {
                require_current_plan_version(source)?;
            }
            let name = available_plan_name(&dir.join("custom-plans"), &requested_name)?;
            let plan = prepare_plan_copy(source.unwrap_or_else(default_plan), &name)?;
            validate_plan_presets(&plan)?;
            validate_plan_durations(&plan)?;
            validate_offline_plan_preset_references(&dir, &plan)?;
            write_json_atomic(&plan_path(&dir, &name)?, &plan, false)?;
            clear_created_plan_progress(&dir, &name)?;
            Ok(name)
        }
    })
    .await
    .map_err(|err| format!("Plan creation worker failed: {err}"))?
}

#[tauri::command]
async fn delete_plan(runtime_dir: String, name: String) -> Result<Value, String> {
    tauri::async_runtime::spawn_blocking(move || {
        validate_file_name(&name)?;
        let dir = resolve_runtime_dir(Some(runtime_dir))?;
        let lock = lock_offline_writer(&dir)?;
        let runtime = read_runtime(&dir)?;
        if runtime_is_recent(&runtime, Utc::now().timestamp_millis()) {
            let deadline = Instant::now() + Duration::from_millis(COMMAND_TIMEOUT_MS);
            let id = queue_command(&dir, "deletePlan", json!({"name": name}))?;
            drop(lock);
            let response = wait_for_command(&dir, &id, "deletePlan", deadline)?;
            command_result_name(&response, "selectedPlan")?;
            Ok(response)
        } else {
            let selected = delete_plan_offline(&dir, &name)?;
            Ok(json!({"success": true, "mode": "offline", "data": {"name": name, "selectedPlan": selected}, "protocol": protocol_info()}))
        }
    })
    .await
    .map_err(|err| format!("Plan deletion worker failed: {err}"))?
}

#[tauri::command]
fn enqueue_command(runtime_dir: String, envelope: CommandEnvelope) -> Result<String, String> {
    let dir = resolve_runtime_dir(Some(runtime_dir))?;
    validate_command(&envelope.command, &envelope.args)?;
    require_live_bridge(&read_runtime(&dir)?)?;
    let deadline = Instant::now() + Duration::from_millis(COMMAND_TIMEOUT_MS);
    let id = queue_command(&dir, &envelope.command, envelope.args)?;
    let queued_id = id.clone();
    // Expiry in the bridge also protects against this process exiting before cleanup.
    tauri::async_runtime::spawn_blocking(move || {
        if let Err(err) = wait_for_command(&dir, &queued_id, &envelope.command, deadline) {
            eprintln!("External UI command failure: {err}");
        }
    });
    Ok(id)
}

#[tauri::command]
fn read_command_result(runtime_dir: String, id: String) -> Result<Option<Value>, String> {
    validate_command_id(&id)?;
    let dir = resolve_runtime_dir(Some(runtime_dir))?;
    read_result(&dir, &id, None)
}

#[tauri::command]
fn tail_log(runtime_dir: String, name: String, max_lines: usize) -> Result<String, String> {
    validate_file_name(&name)?;
    if !name.ends_with(".log") {
        return Err("Log name must end in .log".into());
    }
    let dir = resolve_runtime_dir(Some(runtime_dir))?;
    let path = dir.join("logs").join(name);
    let mut file = match File::open(&path) {
        Ok(file) => file,
        Err(err) if err.kind() == std::io::ErrorKind::NotFound => return Ok(String::new()),
        Err(err) => return Err(format!("Failed to read log: {err}")),
    };
    let size = file.metadata().map_err(|err| err.to_string())?.len();
    let start = size.saturating_sub(MAX_LOG_BYTES);
    file.seek(SeekFrom::Start(start))
        .map_err(|err| err.to_string())?;
    let mut bytes = Vec::new();
    file.take(MAX_LOG_BYTES)
        .read_to_end(&mut bytes)
        .map_err(|err| err.to_string())?;
    Ok(log_tail(&bytes, start > 0, max_lines))
}

#[tauri::command]
fn set_external_ui_enabled(runtime_dir: String, enabled: bool) -> Result<(), String> {
    let dir = resolve_runtime_dir(Some(runtime_dir))?;
    let _lock = lock_offline_writer(&dir)?;
    let _ownership = lock_injector_ownership(&dir)?;
    let marker = dir.join(MARKER_FILE);
    if enabled {
        write_atomic(&marker, b"NGUInjector external UI enabled\n", true)
    } else {
        remove_if_present(&marker)
    }
}

fn resolve_runtime_dir(runtime_dir: Option<String>) -> Result<PathBuf, String> {
    if let Some(path) = runtime_dir.filter(|path| !path.trim().is_empty()) {
        let path = PathBuf::from(path.trim());
        if !path.is_absolute() {
            return Err("Runtime directory must be an absolute path".into());
        }
        return Ok(path);
    }
    let profile = std::env::var_os("USERPROFILE")
        .filter(|value| !value.is_empty())
        .ok_or("USERPROFILE is not set; select a runtime directory explicitly")?;
    default_runtime_dir(Path::new(&profile))
}

fn default_runtime_dir(profile: &Path) -> Result<PathBuf, String> {
    if !profile.is_absolute() {
        return Err("USERPROFILE must be an absolute path".into());
    }
    // Match Main.cs exactly, rather than the Windows redirected/OneDrive Desktop.
    Ok(profile.join("Desktop").join("NGUInjector"))
}

fn require_object(value: &Value, label: &str) -> Result<(), String> {
    if value.is_object() {
        Ok(())
    } else {
        Err(format!("{label} must be a JSON object"))
    }
}

fn read_json_object(path: &Path, limit: u64, missing_ok: bool) -> Result<Value, String> {
    match read_file_bytes(path, limit, missing_ok)? {
        Some(bytes) => parse_json_object(&bytes, &path.display().to_string()),
        None => Ok(empty_object()),
    }
}

fn read_file_bytes(path: &Path, limit: u64, missing_ok: bool) -> Result<Option<Vec<u8>>, String> {
    read_file_bytes_with_open(path, limit, missing_ok, |path| File::open(path))
}

fn read_file_bytes_with_open(
    path: &Path,
    limit: u64,
    missing_ok: bool,
    open: impl FnOnce(&Path) -> std::io::Result<File>,
) -> Result<Option<Vec<u8>>, String> {
    let file = match open(path) {
        Ok(file) => file,
        Err(err) if missing_ok && err.kind() == std::io::ErrorKind::NotFound => {
            if fs::symlink_metadata(path).is_ok() {
                return Err(format!(
                    "{} exists but cannot be read; file was not modified",
                    path.display()
                ));
            }
            return Ok(None);
        }
        Err(err) => return Err(format!("Failed to read {}: {err}", path.display())),
    };
    if !file.metadata().map_err(|err| err.to_string())?.is_file() {
        return Err(format!("{} is not a regular file", path.display()));
    }
    let mut bytes = Vec::new();
    file.take(limit + 1)
        .read_to_end(&mut bytes)
        .map_err(|err| err.to_string())?;
    if bytes.len() as u64 > limit {
        return Err(format!(
            "{} exceeds the {limit} byte JSON limit",
            path.display()
        ));
    }
    Ok(Some(bytes))
}

fn parse_json_object(bytes: &[u8], label: &str) -> Result<Value, String> {
    if bytes.len() as u64 > MAX_PLAN_RESULT_BYTES {
        return Err(format!("{label} exceeds the JSON transport size limit"));
    }
    // Decode a UTF-8 BOM without changing the original bytes used for file revisions.
    let json_bytes = bytes.strip_prefix(b"\xef\xbb\xbf").unwrap_or(bytes);
    let value: Value = serde_json::from_slice(json_bytes)
        .map_err(|err| format!("Failed to parse {label}: {err}; file was not modified"))?;
    require_object(&value, label)?;
    Ok(value)
}

fn read_runtime(dir: &Path) -> Result<Value, String> {
    let path = dir.join("runtime.json");
    let bytes = read_file_bytes_with_open(&path, MAX_RUNTIME_BYTES, true, |path| {
        if cfg!(windows) {
            retry_windows_sharing(|| File::open(path), std::thread::sleep)
        } else {
            File::open(path)
        }
    })?;
    match bytes {
        Some(bytes) => parse_json_object(&bytes, &path.display().to_string()),
        None => Ok(empty_object()),
    }
}

fn retry_windows_sharing<T>(
    mut open: impl FnMut() -> std::io::Result<T>,
    mut sleep: impl FnMut(Duration),
) -> std::io::Result<T> {
    let mut retries = 0;
    loop {
        match open() {
            // C# atomic replacement briefly holds a non-shared runtime.json handle.
            Err(err) if err.raw_os_error() == Some(32) && retries < 5 => {
                retries += 1;
                sleep(Duration::from_millis(10));
            }
            result => return result,
        }
    }
}

fn runtime_is_recent(runtime: &Value, now_ms: i64) -> bool {
    if runtime.get("externalUiEnabled").and_then(Value::as_bool) != Some(true) {
        return false;
    }
    if runtime.get("status").and_then(Value::as_str) != Some("running") {
        return false;
    }
    let Some(timestamp) = runtime
        .get("updatedAt")
        .and_then(Value::as_str)
        .and_then(|text| DateTime::parse_from_rfc3339(text).ok())
    else {
        return false;
    };
    let age = i128::from(now_ms) - i128::from(timestamp.timestamp_millis());
    (-i128::from(FUTURE_TOLERANCE_MS)..=i128::from(HEARTBEAT_MAX_AGE_MS)).contains(&age)
}

fn require_live_bridge(runtime: &Value) -> Result<(), String> {
    if !runtime_is_recent(runtime, Utc::now().timestamp_millis()) {
        return Err("Injector is disconnected or its runtime heartbeat is stale; live command was not queued".into());
    }
    if runtime
        .get("protocolVersion")
        .and_then(Value::as_u64)
        .unwrap_or(0)
        < 3
    {
        return Err("Connected injector requires protocolVersion >= 3 with readiness, plan revisions, and expiry enforcement; command was not queued".into());
    }
    Ok(())
}

fn lock_injector_ownership(dir: &Path) -> Result<File, String> {
    fs::create_dir_all(dir).map_err(|err| err.to_string())?;
    let mut options = OpenOptions::new();
    options.create(true).truncate(false).read(true).write(true);
    #[cfg(windows)]
    {
        use std::os::windows::fs::OpenOptionsExt;
        options.share_mode(0);
    }
    let file = options.open(dir.join(OWNERSHIP_FILE))
        .map_err(|err| format!("Cannot acquire .injector.lock ownership; injector or another UI may be active even without a recent heartbeat. No offline mutation is permitted: {err}"))?;
    #[cfg(not(windows))]
    file.try_lock_exclusive().map_err(|err| {
        format!("Injector ownership is held; no offline mutation is permitted: {err}")
    })?;
    // Never remove this file: the open handle/lock, not file presence, is ownership.
    Ok(file)
}

fn lock_offline_writer(dir: &Path) -> Result<File, String> {
    fs::create_dir_all(dir).map_err(|err| format!("Failed to create runtime directory: {err}"))?;
    let lock = OpenOptions::new()
        .create(true)
        .truncate(false)
        .read(true)
        .write(true)
        .open(dir.join(".external-ui-write.lock"))
        .map_err(|err| err.to_string())?;
    lock.try_lock_exclusive()
        .map_err(|err| format!("Another UI writer is active; retry the operation: {err}"))?;
    Ok(lock)
}

fn merge_object_patch(target: &mut Value, patch: &Value) -> Result<(), String> {
    require_object(target, "Existing file")?;
    require_object(patch, "Patch")?;
    let object = target.as_object_mut().unwrap();
    for (key, value) in patch.as_object().unwrap() {
        if let (Some(existing), Value::Object(_)) = (object.get_mut(key), value) {
            if existing.is_object() {
                merge_object_patch(existing, value)?;
                continue;
            }
        }
        object.insert(key.clone(), value.clone());
    }
    Ok(())
}

fn merge_plan(mut existing: Value, plan: &Value, name: &str) -> Result<Value, String> {
    validate_file_name(name)?;
    require_object(&existing, "Existing plan")?;
    require_object(plan, "Plan")?;
    let history = existing
        .get("RebirthHistory")
        .cloned()
        .unwrap_or_else(|| json!([]));
    // These are full-plan optional overrides: omission clears a prior override.
    for field in PLAN_BLOOD_OVERRIDE_FIELDS {
        existing.as_object_mut().unwrap().remove(field);
    }
    merge_object_patch(&mut existing, plan)?;
    let object = existing.as_object_mut().unwrap();
    object.insert("RebirthHistory".into(), history);
    object.insert("Name".into(), json!(name));
    object.remove(REVISION_FIELD);
    normalize_offline_plan(existing)
}

fn prepare_plan_copy(mut plan: Value, name: &str) -> Result<Value, String> {
    validate_file_name(name)?;
    require_object(&plan, "Plan source")?;
    let object = plan.as_object_mut().unwrap();
    object.insert("Name".into(), json!(name));
    object.insert("RebirthHistory".into(), json!([]));
    object.remove(REVISION_FIELD);
    normalize_offline_plan(plan)
}

fn file_revision(bytes: &[u8]) -> String {
    format!("{:x}", Sha256::digest(bytes))
}

fn clear_created_plan_progress(dir: &Path, name: &str) -> Result<(), String> {
    // Match .NET ToLowerInvariant's per-character casing, not contextual/full lowercase.
    let lower: String = name
        .chars()
        .map(|ch| {
            let mut lowered = ch.to_lowercase();
            let first = lowered.next().unwrap();
            if lowered.next().is_none() {
                first
            } else {
                ch
            }
        })
        .collect();
    let checkpoint = dir.join(format!(
        "stage-progress-{}.json",
        file_revision(lower.as_bytes())
    ));
    remove_if_present(&checkpoint).map_err(|err| format!(
        "Plan file was persisted or deleted, but stage progress could not be cleared for {name}: {err}"
    ))
}

fn validate_revision(value: &Value) -> Result<String, String> {
    let revision = value
        .as_str()
        .filter(|text| text.len() == 64 && text.bytes().all(|byte| byte.is_ascii_hexdigit()))
        .ok_or("Plan revision must be a 64-character SHA-256 hex string; reload the plan")?;
    Ok(revision.to_ascii_lowercase())
}

fn take_plan_revision(plan: &mut Value) -> Result<Option<String>, String> {
    require_object(plan, "Plan")?;
    plan.as_object_mut()
        .unwrap()
        .remove(REVISION_FIELD)
        .map(|revision| validate_revision(&revision))
        .transpose()
}

fn require_current_plan_version(plan: &Value) -> Result<(), String> {
    require_object(plan, "Plan")?;
    if plan
        .get("Version")
        .and_then(Value::as_u64)
        .is_some_and(|version| version > 18)
    {
        return Err(
            "Future plan Version is not supported; update the controller before editing".into(),
        );
    }
    if !plan
        .get("Version")
        .and_then(Value::as_u64)
        .is_some_and(|version| version >= 14)
    {
        return Err("Legacy plan (missing Version or Version < 14) cannot be edited offline. Connect to the injector and read the plan to apply current migrations without losing fields".into());
    }
    Ok(())
}

fn normalize_offline_plan(mut plan: Value) -> Result<Value, String> {
    require_current_plan_version(&plan)?;
    validate_plan_presets(&plan)?;
    validate_plan_blood_overrides(&plan)?;
    normalize_fight_schedules(&mut plan)?;
    strip_plan_ngu_weights(&mut plan);
    let object = plan.as_object_mut().unwrap();
    object.entry("AutoIronPillEnabled").or_insert(json!(true));
    for field in PLAN_BLOOD_OVERRIDE_FIELDS {
        if object.get(field).is_some_and(Value::is_null) {
            object.remove(field);
        }
    }
    object.insert("Version".into(), json!(18));
    object.remove("ProtectedStageTiming");
    Ok(plan)
}

fn normalize_fight_schedules(plan: &mut Value) -> Result<(), String> {
    let Some(stages) = plan.get_mut("Stages") else {
        return Ok(());
    };
    let stages = stages
        .as_array_mut()
        .ok_or("Stages must be an array of objects")?;
    let mut ids = std::collections::HashSet::new();
    for (index, stage) in stages.iter_mut().enumerate() {
        let label = format!("Stage {}", index + 1);
        let row = stage
            .as_object_mut()
            .ok_or_else(|| format!("{label} must be an object"))?;
        if row
            .get("FightOnEntry")
            .is_some_and(|value| !value.is_boolean())
        {
            return Err(format!("{label} FightOnEntry must be boolean"));
        }
        let legacy = row.get("FightOnEntry").and_then(Value::as_bool) == Some(true);
        let schedule = row.entry("FightSchedule").or_insert_with(|| if legacy {
            json!({"Id": format!("legacy-entry-v1-{index}"), "Anchor": 0, "DelaySeconds": 0, "IntervalSeconds": 0})
        } else { Value::Null });
        if !schedule.is_null() {
            require_object(schedule, &format!("{label} FightSchedule"))?;
            let id = schedule["Id"].as_str().filter(|id| !id.is_empty() && id.len() <= 128 && id.bytes().all(|ch| ch.is_ascii_alphanumeric() || ch == b'_' || ch == b'-'))
                .ok_or_else(|| format!("{label} FightSchedule Id must contain 1..128 ASCII letters, digits, underscores or hyphens"))?;
            if !ids.insert(id.to_owned()) {
                return Err("FightSchedule IDs must be unique within the plan".into());
            }
            for field in ["Anchor", "DelaySeconds", "IntervalSeconds"] {
                let max = if field == "Anchor" {
                    1
                } else {
                    i32::MAX as u64
                };
                if !schedule[field].as_u64().is_some_and(|value| value <= max) {
                    return Err(format!(
                        "{label} FightSchedule {field} must be an integer between 0 and {max}"
                    ));
                }
            }
        }
        row.remove("FightOnEntry");
    }
    Ok(())
}

fn validate_plan_presets(plan: &Value) -> Result<(), String> {
    require_object(plan, "Plan")?;
    plan_preset_references(plan)?;
    validate_loadout_presets(plan.get("LoadoutPresets"))
}

fn strip_ngu_weights(value: &mut Value) {
    if let Some(object) = value.as_object_mut() {
        object.remove("EnergyNguWeights");
        object.remove("MagicNguWeights");
    }
}

fn strip_plan_ngu_weights(plan: &mut Value) {
    strip_ngu_weights(plan);
    plan.as_object_mut().unwrap().remove("NguGoal");
    for field in ["LoadoutPresets", "GoalPresets"] {
        if let Some(rows) = plan.get_mut(field).and_then(Value::as_array_mut) {
            for row in rows {
                strip_ngu_weights(row);
            }
        }
    }
}

fn validate_global_ngu_weights(value: &Value, key: &str, count: usize) -> Result<(), String> {
    let weights = value
        .as_array()
        .filter(|weights| weights.len() == count)
        .ok_or_else(|| format!("{key} must contain exactly {count} nonnegative Int32 integers"))?;
    for weight in weights {
        loadout_number(weight, key, 0, i32::MAX as u32, true)?;
    }
    Ok(())
}

// Caller holds the existing settings/plan write guards. Rust deliberately leaves the
// one-time selected-plan migration to the injector instead of guessing legacy defaults.
fn require_durable_global_ngu_weights(
    dir: &Path,
    settings: &Value,
    retiring_plan: Option<&Value>,
) -> Result<(), String> {
    if [("_energyNguWeights", 9), ("_magicNguWeights", 7)]
        .iter()
        .all(|(key, count)| validate_global_ngu_weights(&settings[*key], key, *count).is_ok())
    {
        return Ok(());
    }
    let has_legacy = |plan: &Value| {
        plan.get("EnergyNguWeights").is_some()
            || plan.get("MagicNguWeights").is_some()
            || plan.get("NguGoal").is_some()
            || plan.get("Version").and_then(Value::as_u64).unwrap_or(0) < 16
    };
    if retiring_plan.is_some_and(has_legacy) {
        return Err("Connect to the injector to durably migrate global NGU weights before retiring legacy plan ratios; files were not modified".into());
    }
    let selected = match settings.get("_customPlanFile") {
        None => "24hr",
        Some(value) => value
            .as_str()
            .ok_or("_customPlanFile must be a string; files were not modified")?,
    };
    let path = plan_path(dir, selected)?;
    if read_plan_snapshot(&path, true)?.is_some_and(|(plan, _)| has_legacy(&plan)) {
        return Err("Connect to the injector to durably migrate the selected legacy plan's global NGU weights before changing plan selection; files were not modified".into());
    }
    Ok(())
}

fn validate_plan_blood_overrides(plan: &Value) -> Result<(), String> {
    require_object(plan, "Plan")?;
    if plan
        .get("AutoIronPillEnabled")
        .is_some_and(|value| !value.is_boolean())
    {
        return Err("AutoIronPillEnabled must be a boolean".into());
    }
    if plan
        .get("AutoBloodSpellsEnabled")
        .is_some_and(|value| !value.is_null() && !value.is_boolean())
    {
        return Err("AutoBloodSpellsEnabled must be a boolean or null".into());
    }
    for (field, max) in [
        ("BloodSpaghettiGoalPercent", 100000),
        ("BloodCounterfeitGoalPercent", 100000),
        ("IronPillSaveSeconds", 604800),
    ] {
        if let Some(value) = plan.get(field).filter(|value| !value.is_null()) {
            loadout_number(value, field, 0, max, true)?;
        }
    }
    if let Some(value) = plan
        .get("IronPillMinWorth")
        .filter(|value| !value.is_null())
    {
        if !value
            .as_f64()
            .is_some_and(|number| number.is_finite() && (0.0..=1e8).contains(&number))
        {
            return Err(
                "IronPillMinWorth must be finite and between 0 and 100000000, or null".into(),
            );
        }
    }
    Ok(())
}

fn validate_plan_durations(plan: &Value) -> Result<(), String> {
    require_object(plan, "Plan")?;
    if plan.get("Version").is_some() {
        require_current_plan_version(plan)?;
    }
    normalize_fight_schedules(&mut plan.clone())?;
    if let Some(value) = plan.get("RebirthPrepStartSeconds") {
        loadout_number(value, "RebirthPrepStartSeconds", 0, i32::MAX as u32, true)?;
    }
    if let Some(value) = plan.get("StageDurationSeconds") {
        value
            .as_u64()
            .filter(|seconds| *seconds <= i32::MAX as u64)
            .ok_or("StageDurationSeconds must be a non-negative Int32")?;
    }
    // Missing stages retain the existing C# defaulting behavior, including v14 plans.
    let Some(stages) = plan.get("Stages") else {
        return Ok(());
    };
    let stages = stages
        .as_array()
        .filter(|stages| !stages.is_empty())
        .ok_or("Stages must be a nonempty array of objects")?;
    let mut fixed_total = 0_u64;
    let mut weight_total = 0.0;
    for (index, stage) in stages.iter().enumerate() {
        let label = format!("Stage {}", index + 1);
        require_object(stage, &label)?;
        if let Some(value) = stage.get("UseWandoosWhenTimeMachineLocked") {
            if !value.is_boolean() {
                return Err(format!(
                    "{label} UseWandoosWhenTimeMachineLocked must be boolean"
                ));
            }
        }
        if let Some(value) = stage.get("AdventureDynamicWeight") {
            if !value
                .as_f64()
                .is_some_and(|weight| weight.is_finite() && (0.0..=1e6).contains(&weight))
            {
                return Err(format!(
                    "{label} AdventureDynamicWeight must be a finite number between 0 and 1000000"
                ));
            }
        }
        let completion_mode = match stage.get("CompletionMode") {
            None => 0,
            Some(value) => value
                .as_u64()
                .filter(|mode| *mode <= 1)
                .ok_or_else(|| format!("{label} CompletionMode must be integer 0 or 1"))?,
        };
        let seconds = match stage.get("DurationSeconds") {
            None => 0,
            Some(value) => value
                .as_u64()
                .filter(|seconds| *seconds <= i32::MAX as u64)
                .ok_or_else(|| format!("{label} DurationSeconds must be a non-negative Int32"))?,
        };
        let unit = match stage.get("DurationUnit") {
            None => "%",
            Some(value) => value
                .as_str()
                .ok_or_else(|| format!("{label} DurationUnit must be %, s, min, or h"))?,
        };
        if !matches!(unit, "%" | "s" | "min" | "h") {
            return Err(format!("{label} DurationUnit must be %, s, min, or h"));
        }
        let weight = match stage.get("Percent") {
            None => 100.0,
            Some(value) => value
                .as_f64()
                .filter(|weight| weight.is_finite() && *weight > 0.0)
                .ok_or_else(|| format!("{label} Percent must be a finite positive weight"))?,
        };
        // AT-target stages finish by stats; their stored duration fields are dormant.
        if completion_mode == 1 {
            continue;
        }
        if seconds == 0 {
            weight_total += weight;
        }
        if !weight_total.is_finite() {
            return Err("Stage Percent weights must have a finite total".into());
        }
        fixed_total += seconds;
        if fixed_total > i32::MAX as u64 {
            return Err("Total fixed stage duration must not exceed Int32::MAX seconds".into());
        }
    }
    Ok(())
}

fn read_plan_snapshot(path: &Path, missing_ok: bool) -> Result<Option<(Value, String)>, String> {
    read_file_bytes(path, MAX_JSON_BYTES, missing_ok)?
        .map(|bytes| {
            let plan = parse_json_object(&bytes, &path.display().to_string())?;
            Ok((plan, file_revision(&bytes)))
        })
        .transpose()
}

fn read_plan_json(path: &Path) -> Result<String, String> {
    let (plan, revision) = read_plan_snapshot(path, false)?.ok_or("Plan does not exist")?;
    let mut plan = normalize_offline_plan(plan)?;
    plan[REVISION_FIELD] = json!(revision);
    serde_json::to_string(&plan).map_err(|err| err.to_string())
}

fn connected_plan_json(response: &Value) -> Result<String, String> {
    let plan = response
        .get("result")
        .and_then(|result| result.get("data"))
        .and_then(|data| data.get("plan"))
        .ok_or("readPlan succeeded but result.data.plan is missing")?;
    require_object(plan, "readPlan result.data.plan")?;
    require_current_plan_version(plan)?;
    if plan["Version"] != 18
        || plan
            .get("Stages")
            .and_then(Value::as_array)
            .is_some_and(|rows| {
                rows.iter().any(|row| {
                    row.get("FightSchedule").is_none() || row.get("FightOnEntry").is_some()
                })
            })
    {
        return Err(
            "readPlan requires canonical version 18 FightSchedule fields; update the injector"
                .into(),
        );
    }
    normalize_fight_schedules(&mut plan.clone())?;
    validate_plan_blood_overrides(plan)?;
    validate_revision(
        plan.get(REVISION_FIELD)
            .ok_or("readPlan result is missing __externalUiRevision; update the bridge")?,
    )?;
    let mut plan = plan.clone();
    strip_plan_ngu_weights(&mut plan);
    serde_json::to_string(&plan).map_err(|err| err.to_string())
}

fn check_plan_revision(actual: Option<&str>, expected: Option<&str>) -> Result<(), String> {
    match (actual, expected) {
        (Some(_), None) => Err("expectedRevision is required for an existing plan; reload before saving".into()),
        (Some(actual), Some(expected)) if actual == expected => Ok(()),
        (None, None) => Ok(()),
        _ => Err("Plan revision conflict: the file changed or was removed after reading. Reload before saving; file was not modified".into()),
    }
}

fn save_plan_offline(
    dir: &Path,
    name: &str,
    plan: &Value,
    expected: Option<&str>,
) -> Result<(), String> {
    let _ownership = lock_injector_ownership(dir)?;
    let path = plan_path(dir, name)?;
    let snapshot = read_plan_snapshot(&path, true)?;
    check_plan_revision(
        snapshot.as_ref().map(|(_, revision)| revision.as_str()),
        expected,
    )?;
    let creating = snapshot.is_none();
    let existing = match snapshot {
        Some((existing, _)) => {
            require_current_plan_version(&existing)?;
            validate_plan_presets(&existing)?;
            let settings = read_json_object(&dir.join("settings.json"), MAX_JSON_BYTES, true)?;
            require_durable_global_ngu_weights(dir, &settings, Some(&existing))?;
            let mut retired = existing.clone();
            strip_plan_ngu_weights(&mut retired);
            if retired != existing {
                let library =
                    read_json_object(&dir.join("presets.json"), MAX_PRESET_LIBRARY_BYTES, true)?;
                if let Some(receipts) = library.get("MigrationReceipts") {
                    let receipts = receipts
                        .as_object()
                        .ok_or("MigrationReceipts must be an object")?;
                    // Rust cannot prove historical import hashes have durable ratio-free aliases.
                    // Let the injector migrate and save this source before discarding hash inputs.
                    if receipts.values().any(|receipt| {
                        receipt
                            .get("Source")
                            .and_then(Value::as_str)
                            .is_none_or(|source| {
                                source
                                    .chars()
                                    .flat_map(char::to_lowercase)
                                    .eq(name.chars().flat_map(char::to_lowercase))
                            })
                    }) {
                        return Err("Connect to the injector and save this plan to migrate preset receipt identities before retiring legacy NGU fields; files were not modified".into());
                    }
                }
            }
            retired
        }
        None => empty_object(),
    };
    require_current_plan_version(plan)?;
    validate_plan_presets(plan)?;
    let merged = merge_plan(existing.clone(), plan, name)?;
    if !creating {
        for field in ["LoadoutPresets", "GoalPresets", "ActiveGoalPresetIndex"] {
            if merged.get(field) != existing.get(field) {
                return Err(format!(
                    "{field} is historical provenance; edit the global preset library instead. File was not modified"
                ));
            }
        }
    }
    validate_plan_presets(&merged)?;
    validate_plan_durations(&merged)?;
    validate_offline_plan_preset_references(dir, &merged)?;
    write_json_atomic(&path, &merged, expected.is_some())?;
    if creating {
        clear_created_plan_progress(dir, name)?;
    }
    Ok(())
}

fn command_result_name(response: &Value, field: &str) -> Result<String, String> {
    let name = response
        .get("result")
        .and_then(|result| result.get("data"))
        .and_then(|data| data.get(field))
        .and_then(Value::as_str)
        .ok_or_else(|| {
            format!("Command succeeded but result.data.{field} is missing; refresh before retrying")
        })?;
    validate_file_name(name)?;
    Ok(name.to_string())
}

fn validate_plan_deletion(name: &str, settings: &Value, count: usize) -> Result<bool, String> {
    validate_file_name(name)?;
    if [
        "24hr",
        "15m-NUMBER-increase",
        "30m-NUMBER-increase",
        "1h-1m-NUMBER-increase",
    ]
    .iter()
    .any(|builtin| name.eq_ignore_ascii_case(builtin))
    {
        return Err("Built-in plans cannot be deleted; copy one to make a removable plan".into());
    }
    require_object(settings, "Settings")?;
    let selected = match settings.get("_customPlanFile") {
        None => false,
        Some(Value::String(selected)) => selected.eq_ignore_ascii_case(name),
        Some(_) => return Err("_customPlanFile must be a string; plan was not deleted".into()),
    };
    let running = match settings.get("_useCustomRebirthPlan") {
        None => false,
        Some(Value::Bool(running)) => *running,
        Some(_) => {
            return Err("_useCustomRebirthPlan must be boolean; plan was not deleted".into())
        }
    };
    if selected && running {
        return Err("The selected running plan cannot be deleted. Pause it first".into());
    }
    if count <= 1 {
        return Err("The last plan cannot be deleted".into());
    }
    Ok(selected)
}

// Caller holds the UI writer lock; ownership also excludes injector startup throughout.
fn delete_plan_offline(dir: &Path, name: &str) -> Result<String, String> {
    let _ownership = lock_injector_ownership(dir)?;
    let path = plan_path(dir, name)?;
    let settings_path = dir.join("settings.json");
    let mut settings = read_json_object(&settings_path, MAX_JSON_BYTES, true)?;
    let selected = validate_plan_deletion(
        name,
        &settings,
        list_json_stems(&dir.join("custom-plans"))?.len(),
    )?;
    if !path.try_exists().map_err(|err| err.to_string())? {
        return Err(format!("Plan does not exist: {name}"));
    }
    let fallback = if selected {
        "24hr"
    } else {
        settings
            .get("_customPlanFile")
            .and_then(Value::as_str)
            .unwrap_or("24hr")
    }
    .to_string();
    validate_file_name(&fallback)?;
    if selected {
        require_durable_global_ngu_weights(dir, &settings, None)?;
        let fallback_path = plan_path(dir, &fallback)?;
        read_json_object(&fallback_path, MAX_JSON_BYTES, false).map_err(|err| {
            format!("Cannot select fallback plan {fallback}: {err}; plan was not deleted")
        })?;
        settings["_customPlanFile"] = json!(fallback);
        // Persist selection first: a settings write failure must leave the plan intact.
        write_json_atomic(&settings_path, &settings, true)?;
    }
    fs::remove_file(&path).map_err(|err| format!("Failed to delete plan: {err}"))?;
    clear_created_plan_progress(dir, name)?;
    Ok(fallback)
}

fn write_json_atomic(path: &Path, value: &Value, overwrite: bool) -> Result<(), String> {
    require_object(value, "JSON file")?;
    let bytes = serde_json::to_vec_pretty(value).map_err(|err| err.to_string())?;
    if bytes.len() as u64 > MAX_JSON_BYTES {
        return Err("Serialized JSON exceeds the file size limit; file was not modified".into());
    }
    write_atomic(path, &bytes, overwrite)
}

fn write_atomic(path: &Path, bytes: &[u8], overwrite: bool) -> Result<(), String> {
    let parent = path.parent().ok_or("File has no parent directory")?;
    fs::create_dir_all(parent).map_err(|err| err.to_string())?;
    let mut tmp = tempfile::Builder::new()
        .prefix(".ngui-")
        .suffix(".tmp")
        .tempfile_in(parent)
        .map_err(|err| format!("Failed to create temporary file: {err}"))?;
    tmp.write_all(bytes)
        .and_then(|_| tmp.as_file().sync_all())
        .map_err(|err| format!("Failed to flush temporary file: {err}"))?;
    // tempfile uses MoveFileExW(MOVEFILE_REPLACE_EXISTING) on Windows, not delete-then-rename.
    // PersistError owns the temp file; dropping it cleans up on failure as well.
    let result = if overwrite {
        tmp.persist(path)
    } else {
        tmp.persist_noclobber(path)
    };
    result.map(|_| ()).map_err(|err| {
        format!(
            "Failed to atomically publish {}: {}",
            path.display(),
            err.error
        )
    })
}

fn validate_file_name(name: &str) -> Result<(), String> {
    if name.is_empty()
        || name.trim() != name
        || name.ends_with('.')
        || name.encode_utf16().count() > 120
        || name
            .chars()
            .any(|ch| ch.is_control() || "<>:\"/\\|?*".contains(ch))
        || name == "."
        || name == ".."
    {
        return Err("Name must be a nonempty file name, at most 120 UTF-16 units, without paths, trailing dots/spaces, or Windows-invalid characters".into());
    }
    let base = name
        .split('.')
        .next()
        .unwrap_or_default()
        .trim_end()
        .to_uppercase();
    let numbered_device = base
        .strip_prefix("COM")
        .or_else(|| base.strip_prefix("LPT"))
        .is_some_and(|suffix| {
            matches!(
                suffix,
                "1" | "2"
                    | "3"
                    | "4"
                    | "5"
                    | "6"
                    | "7"
                    | "8"
                    | "9"
                    | "\u{b9}"
                    | "\u{b2}"
                    | "\u{b3}"
            )
        });
    if matches!(
        base.as_str(),
        "CON" | "PRN" | "AUX" | "NUL" | "CONIN$" | "CONOUT$"
    ) || numbered_device
    {
        return Err("Windows reserved device names are not allowed".into());
    }
    Ok(())
}

fn plan_path(runtime_dir: &Path, name: &str) -> Result<PathBuf, String> {
    validate_file_name(name)?;
    Ok(runtime_dir
        .join("custom-plans")
        .join(format!("{name}.json")))
}

fn available_plan_name(plans_dir: &Path, requested_name: &str) -> Result<String, String> {
    validate_file_name(requested_name)?;
    for suffix in 1..=10_000 {
        let name = if suffix == 1 {
            requested_name.to_string()
        } else {
            format!("{requested_name}-{suffix}")
        };
        validate_file_name(&name)?;
        match fs::symlink_metadata(plans_dir.join(format!("{name}.json"))) {
            Err(err) if err.kind() == std::io::ErrorKind::NotFound => return Ok(name),
            Err(err) => return Err(err.to_string()),
            Ok(_) => {}
        }
    }
    Err("Too many plans with the requested name".into())
}

fn validate_command_id(id: &str) -> Result<(), String> {
    if Uuid::parse_str(id).is_ok_and(|uuid| uuid.get_version_num() == 4 && uuid.to_string() == id) {
        Ok(())
    } else {
        Err("Command ID must be a canonical UUID v4".into())
    }
}

fn fill_auto_exp_defaults(settings: &mut Value) {
    let object = settings.as_object_mut().unwrap();
    object
        .entry("_customAutoExpEnabled")
        .or_insert(json!(false));
    for (key, default) in AUTO_EXP_RATIO_DEFAULTS {
        object.entry(key).or_insert(json!(default));
    }
}

fn validate_offline_settings_patch(patch: &Value) -> Result<(), String> {
    validate_settings_patch(patch)?;
    let is_int32 = |value: &Value| {
        let token = value.to_string();
        if value.is_number() && token.starts_with('-') {
            serde_json::from_str::<Value>(&token[1..]).is_ok_and(|positive| {
                loadout_number(&positive, "Int32", 0, 2147483648, true).is_ok()
            })
        } else {
            loadout_number(value, "Int32", 0, i32::MAX as u32, true).is_ok()
        }
    };
    // Keep this schema aligned with SavedSettings SerializeField declarations, not keys
    // found in settings.json: future fields may be preserved, but are not editable.
    for (key, value) in patch.as_object().unwrap() {
        let valid = match key.as_str() {
            "_energyNguWeights" | "_magicNguWeights" => true,
            "_manualAdventureGearWeights"
            | "_manualAdventureBeardWeights"
            | "_manualAdventureDiggerWeights" => true,
            "_customAdventureRules" => value.is_array(),
            "_precastBuffs"
            | "_swapTitanLoadouts"
            | "_swapYggdrasilLoadouts"
            | "_manageEnergy"
            | "_manageMagic"
            | "_fastCombat"
            | "_manageGear"
            | "_manageDiggers"
            | "_upgradeDiggers"
            | "_manageYggdrasil"
            | "_manageInventory"
            | "_autoFight"
            | "_autoQuest"
            | "_allowMajorQuests"
            | "_autoConvertBoosts"
            | "_autoQuestItopod"
            | "_autoMoneyPit"
            | "_autoSpin"
            | "_autoRebirth"
            | "_manageWandoos"
            | "_doGoldSwap"
            | "_snipeBossOnly"
            | "_recoverHealth"
            | "_allowZoneFallback"
            | "_abandonMinors"
            | "_questFastCombat"
            | "_autoSpellSwap"
            | "_castBloodSpells"
            | "_autoBuyEm"
            | "_globalEnabled"
            | "_combatEnabled"
            | "_useButterMajor"
            | "_useButterMinor"
            | "_manualMinors"
            | "_manageR3"
            | "_activateFruits"
            | "_wishSortPriorities"
            | "_wishSortOrder"
            | "_beastMode"
            | "_manageNguDiff"
            | "_manageGoldLoadouts"
            | "_goldCBlockMode"
            | "_debugAllocation"
            | "_optimizeItopodFloor"
            | "_itopodBeastMode"
            | "_itopodFastCombat"
            | "_itopodRecoverHp"
            | "_itopodPrecastBuffs"
            | "_adventureTargetItopod"
            | "_disableOverlay"
            | "_moreBlockParry"
            | "_hackAdvance"
            | "_useCustomRebirthPlan"
            | "_customAdventureEnabled"
            | "_customAdventureGoldPush"
            | "_customAutoMoneyPitEnabled"
            | "_customAutoExpEnabled"
            | "_customAutoBoostEnabled"
            | "_customAutoBloodSpellsEnabled"
            | "_manualAdventureEnabled"
            | "_manualTitanEnabled"
            | "_manualTitanRequireAT"
            | "_manualAdventureBossOnly"
            | "_manualAdventureRecoverHealth"
            | "_manualAdventurePrecastBuffs"
            | "_manualAdventureFastCombat"
            | "_manualAdventureBeastMode"
            | "_manualAdventureAllowFallback" => value.is_boolean(),
            "_snipeZone"
            | "_combatMode"
            | "_minorAbandonThreshold"
            | "_questCombatMode"
            | "_spaghettiThreshold"
            | "_counterfeitThreshold"
            | "_ironPillThreshold"
            | "_bloodMacGuffinAThreshold"
            | "_bloodMacGuffinBThreshold"
            | "_cubePriority"
            | "_resnipeTime"
            | "_itopodCombatMode"
            | "_yggSwapThreshold"
            | "_customAdventureMode"
            | "_customAdventureMaxItopodDeaths"
            | "_customAdventureZone"
            | "_customAutoBoostCubePowerRatio"
            | "_customExpEnergyRatio"
            | "_customExpMagicRatio"
            | "_customExpEnergyPowerRatio"
            | "_customExpEnergyCapRatio"
            | "_customExpEnergyBarsRatio"
            | "_customExpMagicPowerRatio"
            | "_customExpMagicCapRatio"
            | "_customExpMagicBarsRatio"
            | "_customAugmentsBloodEstimateSeconds"
            | "_customBloodSpaghettiGoalPercent"
            | "_customBloodCounterfeitGoalPercent"
            | "_manualAdventureZone"
            | "_manualAdventureLoadout"
            | "_manualTitanZone"
            | "_manualTitanLoadout"
            | "_manualTitanMaxDeaths"
            | "_customIronPillSaveSeconds" => is_int32(value),
            "_moneyPitThreshold" | "_bloodNumberThreshold" | "_customIronPillMinWorth" => {
                value.as_f64().is_some_and(f64::is_finite)
            }
            "_allocationFile" | "_customPlanFile" => {
                let name = value
                    .as_str()
                    .ok_or_else(|| format!("{key} must be a string"))?;
                validate_file_name(name)?;
                true
            }
            "_priorityBoosts"
            | "_titanLoadout"
            | "_yggdrasilLoadout"
            | "_goldDropLoadout"
            | "_moneyPitLoadout"
            | "_boostBlacklist"
            | "_quickLoadout"
            | "_quickDiggers"
            | "_wishPriorities"
            | "_specialBoostBlacklist"
            | "_blacklistedBosses" => value
                .as_array()
                .is_some_and(|array| array.iter().all(&is_int32)),
            "_titanMoneyDone" | "_titanGoldTargets" | "_titanSwapTargets" => value
                .as_array()
                .is_some_and(|array| array.len() == 12 && array.iter().all(Value::is_boolean)),
            _ => {
                return Err(format!(
                    "Unknown settings field: {key}; unknown stored fields are read-only"
                ))
            }
        };
        if !valid {
            return Err(format!("Invalid value type or array length for {key}"));
        }
    }
    Ok(())
}

fn validate_settings_patch(patch: &Value) -> Result<(), String> {
    require_object(patch, "Settings patch")?;
    for (key, count) in [
        ("_manualAdventureGearWeights", 5),
        ("_manualAdventureBeardWeights", 7),
        ("_manualAdventureDiggerWeights", 12),
    ] {
        if let Some(value) = patch.get(key) {
            if !value.as_array().is_some_and(|weights| {
                weights.len() == count
                    && weights.iter().all(|weight| {
                        weight.as_f64().is_some_and(|number| {
                            number.is_finite() && (0.0..=1_000_000.0).contains(&number)
                        })
                    })
            }) {
                return Err(format!(
                    "{key} must contain exactly {count} finite numbers between 0 and 1000000"
                ));
            }
        }
    }
    for (key, count) in [("_energyNguWeights", 9), ("_magicNguWeights", 7)] {
        if let Some(value) = patch.get(key) {
            validate_global_ngu_weights(value, key, count)?;
        }
    }
    for key in ["_manualTitanEnabled", "_manualTitanRequireAT"] {
        if patch.get(key).is_some_and(|value| !value.is_boolean()) {
            return Err(format!("{key} must be boolean"));
        }
    }
    for (key, min, max) in [
        ("_manualAdventureLoadout", -1, 2),
        ("_manualTitanLoadout", -1, 2),
        ("_manualTitanMaxDeaths", 1, 1000),
    ] {
        if patch.get(key).is_some_and(|value| {
            !value
                .as_i64()
                .is_some_and(|number| (min..=max).contains(&number))
        }) {
            return Err(format!("{key} must be an integer between {min} and {max}"));
        }
    }
    if patch.get("_manualTitanZone").is_some_and(|value| {
        !value.as_i64().is_some_and(|zone| {
            matches!(
                zone,
                6 | 8 | 11 | 14 | 16 | 19 | 23 | 26 | 30 | 34 | 38 | 42
            )
        })
    }) {
        return Err("_manualTitanZone must be a titan zone".into());
    }
    if let Some(value) = patch.get("_customAdventureRules") {
        let rules = value
            .as_array()
            .filter(|rules| rules.len() <= 32)
            .ok_or("_customAdventureRules must be an array of at most 32 rules")?;
        let fields = [
            "Enabled",
            "MinPower",
            "MinToughness",
            "MinRegen",
            "Mode",
            "Zone",
        ];
        for (index, rule) in rules.iter().enumerate() {
            let label = format!("_customAdventureRules[{index}]");
            let row = rule
                .as_object()
                .filter(|row| {
                    row.len() == fields.len() && fields.iter().all(|key| row.contains_key(*key))
                })
                .ok_or_else(|| format!("{label} must contain exactly {}", fields.join(", ")))?;
            if !row["Enabled"].is_boolean() {
                return Err(format!("{label} Enabled must be boolean"));
            }
            for key in ["MinPower", "MinToughness", "MinRegen"] {
                if !row[key]
                    .as_f64()
                    .is_some_and(|number| number.is_finite() && number >= 0.0)
                {
                    return Err(format!("{label} {key} must be a finite number >= 0"));
                }
            }
            if !row["Mode"]
                .as_i64()
                .is_some_and(|mode| matches!(mode, 0 | 1 | 2 | 4))
            {
                return Err(format!("{label} Mode must be integer 0, 1, 2, or 4"));
            }
            if !row["Zone"]
                .as_i64()
                .is_some_and(|zone| (-1..=43).contains(&zone) || zone == 1000)
            {
                return Err(format!("{label} Zone must be an integer -1..43 or 1000"));
            }
        }
    }
    for (key, min, max) in [
        ("_customAdventureMode", 0, 3),
        ("_customAdventureMaxItopodDeaths", 1, 1000),
    ] {
        if let Some(value) = patch.get(key) {
            if !value
                .as_u64()
                .is_some_and(|number| (min..=max).contains(&number))
            {
                return Err(format!("{key} must be an integer between {min} and {max}"));
            }
        }
    }
    if patch
        .get("_customAutoExpEnabled")
        .is_some_and(|value| !value.is_boolean())
    {
        return Err("_customAutoExpEnabled must be boolean".into());
    }
    for (key, _) in AUTO_EXP_RATIO_DEFAULTS {
        if let Some(value) = patch.get(key) {
            // Exact integer tokens avoid accepting fractions rounded into range by f64.
            if !value
                .as_u64()
                .is_some_and(|number| (1..=1_000_000_000).contains(&number))
            {
                return Err(format!("{key} must be an integer between 1 and 1000000000"));
            }
        }
    }
    if patch.get("_customIronPillHistory").is_some() {
        return Err("_customIronPillHistory is backend-owned and read-only".into());
    }
    for (key, min, max, integer) in [
        ("_customIronPillSaveSeconds", 0.0, 604800.0, true),
        ("_customIronPillMinWorth", 0.0, 1e8, false),
        ("_customAutoBoostCubePowerRatio", 1.0, 4.0, true),
        ("_customAugmentsBloodEstimateSeconds", 1.0, 100000.0, true),
        ("_customBloodSpaghettiGoalPercent", 0.0, 100000.0, true),
        ("_customBloodCounterfeitGoalPercent", 0.0, 100000.0, true),
    ] {
        if let Some(value) = patch.get(key) {
            let number = value
                .as_f64()
                .ok_or_else(|| format!("{key} must be a number"))?;
            if !number.is_finite()
                || number < min
                || number > max
                || integer && number.fract() != 0.0
            {
                return Err(format!(
                    "{key} must be a finite {} between {min} and {max}",
                    if integer { "integer" } else { "number" }
                ));
            }
        }
    }
    Ok(())
}

// Compare decimal tokens before f64 rounding at the validation boundary.
fn loadout_number(
    value: &Value,
    label: &str,
    min: u32,
    max: u32,
    integer: bool,
) -> Result<(), String> {
    let valid = value.as_number().is_some_and(|number| {
        if !number.as_f64().is_some_and(f64::is_finite) {
            return false;
        }
        let token = number.to_string();
        let (mantissa, exponent) = token.split_once(['e', 'E']).unwrap_or((&token, "0"));
        let digits = mantissa.replace('.', "");
        let digits = digits.trim_start_matches('-').trim_start_matches('0');
        if digits.is_empty() {
            return min == 0;
        }
        if mantissa.starts_with('-') {
            return false;
        }
        let exponent = exponent
            .parse::<i64>()
            .unwrap_or(if exponent.starts_with('-') {
                i64::MIN
            } else {
                i64::MAX
            });
        let fractional = mantissa.split_once('.').map_or(0, |(_, tail)| tail.len());
        let order = exponent
            .saturating_add(digits.len() as i64)
            .saturating_sub(fractional as i64);
        let significant = digits.trim_end_matches('0');
        if integer && order < significant.len() as i64 {
            return false;
        }
        let compare = |bound: u32| {
            if bound == 0 {
                return std::cmp::Ordering::Greater;
            }
            let bound = bound.to_string();
            order.cmp(&(bound.len() as i64)).then_with(|| {
                let length = digits.len().max(bound.len());
                digits
                    .bytes()
                    .chain(std::iter::repeat(b'0'))
                    .take(length)
                    .cmp(bound.bytes().chain(std::iter::repeat(b'0')).take(length))
            })
        };
        compare(min) != std::cmp::Ordering::Less && compare(max) != std::cmp::Ordering::Greater
    });
    if valid {
        Ok(())
    } else {
        Err(format!(
            "{label} must be a finite {} between {min} and {max}",
            if integer { "integer" } else { "number" }
        ))
    }
}

fn loadout_safe_key(value: &Value) -> bool {
    value.as_str().is_some_and(|text| {
        !text.is_empty()
            && text.len() <= 64
            && text
                .bytes()
                .all(|byte| byte.is_ascii_alphanumeric() || byte == b'_' || byte == b'-')
    })
}

fn validate_loadout_preset(row: &Value) -> Result<(), String> {
    require_object(row, "Loadout preset")?;
    preset_library::validate_resource_lane_priorities(row)?;
    if !loadout_safe_key(&row["Id"]) {
        return Err("Preset Id must be 1..64 safe ASCII characters".into());
    }
    if !row["Name"]
        .as_str()
        .is_some_and(|name| !name.trim().is_empty() && name.encode_utf16().count() <= 100)
    {
        return Err("Preset Name must contain 1..100 characters".into());
    }
    let weights = row["Weights"]
        .as_array()
        .filter(|weights| matches!(weights.len(), 6 | 10 | 12 | 14))
        .ok_or("Weights must contain exactly six, ten, twelve or fourteen numbers")?;
    for weight in weights {
        loadout_number(weight, "Weight", 0, 1_000_000, false)?;
    }
    if let Some(weight) = row.get("AdventureDynamicWeight") {
        loadout_number(weight, "AdventureDynamicWeight", 0, 1_000_000, false)?;
    }
    if !weights
        .iter()
        .any(|weight| weight.as_f64().is_some_and(|value| value > 0.0))
        && !row["AdventureDynamicWeight"]
            .as_f64()
            .is_some_and(|value| value > 0.0)
    {
        return Err("At least one weight must be positive".into());
    }
    for (key, min, max) in [("DiggerBudgetPercent", 0, 95), ("SwapSeconds", 0, 3600)] {
        loadout_number(&row[key], key, min, max, true)?;
    }
    let blood = row["MagicBloodPercent"].to_string();
    if let Some(magnitude) = blood.strip_prefix('-') {
        // -1 is the sole negative sentinel; compare exactly, not after f64 rounding.
        let magnitude: Value =
            serde_json::from_str(magnitude).map_err(|_| "Invalid MagicBloodPercent")?;
        loadout_number(&magnitude, "Auto MagicBloodPercent magnitude", 1, 1, true)?;
    } else {
        loadout_number(&row["MagicBloodPercent"], "MagicBloodPercent", 0, 100, true)?;
    }
    if !matches!(row["SupportMode"].as_str(), Some("current" | "planned")) {
        return Err("SupportMode must be current or planned".into());
    }
    let zone = row["Zone"]
        .as_i64()
        .ok_or("Zone must be an integer game zone ID")?;
    if zone != -1
        && (!(0..=43).contains(&zone)
            || [6, 8, 11, 14, 16, 19, 23, 26, 30, 34, 38, 42].contains(&zone))
    {
        return Err("Zone must be auto (-1) or a regular non-titan zone; PP competes using the ITOPOD route".into());
    }
    for key in ["MinimumPower", "MinimumToughness"] {
        if !row[key].as_number().is_some_and(|value| {
            value
                .as_f64()
                .is_some_and(|number| number.is_finite() && number >= 0.0)
                && !value.to_string().starts_with('-')
        }) {
            return Err(format!("{key} must be finite and nonnegative"));
        }
    }
    let beards = row["BeardPriority"]
        .as_array()
        .ok_or("BeardPriority must be an array")?;
    let mut seen = std::collections::HashSet::new();
    for beard in beards {
        loadout_number(beard, "Beard ID", 0, 6, true)?;
        if !seen.insert(beard.as_f64().unwrap() as u32) {
            return Err("BeardPriority IDs must be unique".into());
        }
    }
    if let Some(rotation) = row.get("BeardRotation").filter(|value| !value.is_null()) {
        let rotation = rotation
            .as_array()
            .ok_or("BeardRotation must be an array or null")?;
        let mut optional = std::collections::HashSet::new();
        for beard in rotation {
            loadout_number(beard, "BeardRotation ID", 0, 6, true)?;
            let id = beard.as_f64().unwrap() as u32;
            if !optional.insert(id) {
                return Err("BeardRotation IDs must be unique".into());
            }
            if seen.contains(&id) {
                return Err("BeardPriority and BeardRotation must be disjoint".into());
            }
        }
    }
    let diggers = row["DiggerWeights"]
        .as_array()
        .filter(|weights| weights.is_empty() || weights.len() == 12)
        .ok_or("DiggerWeights must be empty or contain exactly 12 numbers")?;
    for weight in diggers {
        loadout_number(weight, "Digger weight", 0, 1_000_000, false)?;
    }
    if row
        .get("AutoUpdate")
        .is_some_and(|value| !value.is_boolean())
    {
        return Err("AutoUpdate must be boolean".into());
    }
    for (key, count) in [("AdvancedTrainingWeights", 5)] {
        if let Some(value) = row.get(key) {
            let weights = value
                .as_array()
                .filter(|weights| weights.len() == count)
                .ok_or_else(|| format!("{key} must contain exactly {count} integers"))?;
            for weight in weights {
                loadout_number(weight, key, 0, i32::MAX as u32, true)?;
            }
        }
    }
    Ok(())
}

fn validate_loadout_presets(value: Option<&Value>) -> Result<(), String> {
    let Some(value) = value else {
        return Ok(());
    };
    let rows = value
        .as_array()
        .filter(|rows| rows.len() <= 32)
        .ok_or("LoadoutPresets must be an array of at most 32 presets")?;
    let definitions: Value = serde_json::from_str(include_str!(
        "../../../NGUInjector/Data/PresetDefinitions.json"
    ))
    .expect("Embedded preset definitions must be valid JSON");
    let mut ids = std::collections::HashSet::new();
    for row in rows {
        require_object(row, "Loadout preset")?;
        // Historical rows keep their exact source shape; defaults are only for validation.
        let mut candidate = definitions["defaults"].clone();
        for (key, value) in row.as_object().unwrap() {
            candidate[key] = value.clone();
        }
        validate_loadout_preset(&candidate)?;
        if !ids.insert(row["Id"].as_str().unwrap()) {
            return Err("LoadoutPresets IDs must be unique".into());
        }
    }
    Ok(())
}

fn validate_command(command: &str, args: &Value) -> Result<(), String> {
    require_object(args, "Command args")?;
    match command {
        "previewExecutionRestart" => {
            let fields = args.as_object().unwrap();
            if fields.len() != 3
                || fields.keys().any(|key| {
                    !["stageIndex", "checkpointRevision", "planRevision"].contains(&key.as_str())
                })
                || !args["stageIndex"]
                    .as_u64()
                    .is_some_and(|index| index < 1024)
                || !["checkpointRevision", "planRevision"].iter().all(|key| {
                    args[*key].as_str().is_some_and(|value| {
                        value.len() == 64
                            && value
                                .bytes()
                                .all(|b| b.is_ascii_digit() || (b'a'..=b'f').contains(&b))
                    })
                })
            {
                return Err("Restart review requires an explicit stage and current checkpoint/plan revisions".into());
            }
            Ok(())
        }
        "applyExecutionRebind" => {
            let fields = args.as_object().unwrap();
            if fields.len() != 2
                || fields
                    .keys()
                    .any(|key| key != "token" && key != "checkpointRevision")
                || !args["token"].as_str().is_some_and(|value| {
                    value.len() == 32
                        && value
                            .bytes()
                            .all(|b| b.is_ascii_digit() || (b'a'..=b'f').contains(&b))
                })
                || !args["checkpointRevision"].as_str().is_some_and(|value| {
                    value.len() == 64
                        && value
                            .bytes()
                            .all(|b| b.is_ascii_digit() || (b'a'..=b'f').contains(&b))
                })
            {
                return Err(
                    "Apply selection review requires its token and checkpointRevision".into(),
                );
            }
            Ok(())
        }
        "previewExecutionRebind" => {
            if args.as_object().unwrap().is_empty() {
                Ok(())
            } else {
                Err("Selection review preview does not accept args".into())
            }
        }
        "readPresetLibrary"
        | "savePresetLibrary"
        | "previewGlobalPreset"
        | "applyGlobalPreset"
        | "equipGlobalPreset" => validate_global_preset_command(command, args),
        "previewLoadoutPreset" | "applyLoadoutPreset" => {
            let keys: &[&str] = if command == "applyLoadoutPreset" {
                &["preset", "candidateKey", "previewId"]
            } else {
                &["preset"]
            };
            let fields = args.as_object().unwrap();
            if fields.len() != keys.len() || fields.keys().any(|key| !keys.contains(&key.as_str()))
            {
                return Err(format!("{command} accepts exactly {}", keys.join(", ")));
            }
            validate_loadout_preset(&args["preset"])?;
            if command == "applyLoadoutPreset" && !loadout_safe_key(&args["candidateKey"]) {
                return Err(
                    "candidateKey must be 1..64 safe ASCII characters from the latest preview"
                        .into(),
                );
            }
            if command == "applyLoadoutPreset"
                && !args["previewId"].as_str().is_some_and(|token| {
                    token.len() == 32 && token.bytes().all(|byte| byte.is_ascii_hexdigit())
                })
            {
                return Err(
                    "previewId must be the server-owned 32-hex token from the latest preview"
                        .into(),
                );
            }
            Ok(())
        }
        "previewCombatPrep" | "startCombatPrep" | "cancelCombatPrep" => {
            let keys: &[&str] = match command {
                "previewCombatPrep" => &[
                    "durationSeconds",
                    "backgroundEnergyPercent",
                    "backgroundMagicPercent",
                ],
                "startCombatPrep" => &["previewId", "candidateId"],
                _ => &["runId"],
            };
            let fields = args.as_object().unwrap();
            if fields.len() != keys.len() || fields.keys().any(|key| !keys.contains(&key.as_str()))
            {
                return Err(format!("{command} accepts exactly {}", keys.join(", ")));
            }
            if command == "previewCombatPrep" {
                if !args["durationSeconds"]
                    .as_u64()
                    .is_some_and(|seconds| (1..=86400).contains(&seconds))
                {
                    return Err("durationSeconds must be an integer between 1 and 86400".into());
                }
                for key in &keys[1..] {
                    let valid = args[*key].as_number().is_some_and(|number| {
                        if !number.as_f64().is_some_and(f64::is_finite) {
                            return false;
                        }
                        // Compare exact decimal magnitude: f64 rounds 100.000...001 to 100.
                        let token = number.to_string();
                        let (mantissa, exponent) =
                            token.split_once(['e', 'E']).unwrap_or((&token, "0"));
                        let digits = mantissa.replace('.', "");
                        let digits = digits.trim_start_matches('-').trim_start_matches('0');
                        if digits.is_empty() {
                            return true;
                        }
                        if mantissa.starts_with('-') {
                            return false;
                        }
                        let exponent =
                            exponent
                                .parse::<i64>()
                                .unwrap_or(if exponent.starts_with('-') {
                                    i64::MIN
                                } else {
                                    i64::MAX
                                });
                        let fractional = mantissa.split_once('.').map_or(0, |(_, tail)| tail.len());
                        let order = exponent
                            .saturating_add(digits.len() as i64)
                            .saturating_sub(fractional as i64);
                        order < 3
                            || order == 3
                                && digits.starts_with('1')
                                && digits[1..].bytes().all(|byte| byte == b'0')
                    });
                    if !valid {
                        return Err(format!("{key} must be a finite number between 0 and 100"));
                    }
                }
            } else {
                for key in keys {
                    if !args[*key].as_str().is_some_and(|text| {
                        !text.trim().is_empty() && text.encode_utf16().count() <= 128
                    }) {
                        return Err(format!(
                            "{key} must be a nonempty string of at most 128 UTF-16 units"
                        ));
                    }
                }
            }
            Ok(())
        }
        "applySettingsPatch" => {
            validate_settings_revision(
                args.get("expectedRevision")
                    .and_then(Value::as_str)
                    .ok_or("Missing args.expectedRevision")?,
            )?;
            validate_settings_patch(args.get("patch").ok_or("Missing args.patch")?)
        }
        "savePlan" => {
            validate_file_name(
                args.get("name")
                    .and_then(Value::as_str)
                    .ok_or("Missing args.name")?,
            )?;
            let plan = args.get("plan").ok_or("Missing args.plan")?;
            validate_plan_presets(plan)?;
            validate_plan_blood_overrides(plan)?;
            validate_plan_durations(plan)?;
            if let Some(revision) = args.get("expectedRevision") {
                if revision.as_str() != Some("") {
                    validate_revision(revision)?;
                }
            }
            Ok(())
        }
        "createPlan" => {
            if args
                .as_object()
                .unwrap()
                .keys()
                .any(|key| key != "requestedName" && key != "source")
            {
                return Err("createPlan accepts only requestedName and optional source".into());
            }
            validate_file_name(
                args.get("requestedName")
                    .and_then(Value::as_str)
                    .ok_or("Missing args.requestedName")?,
            )?;
            if let Some(source) = args.get("source") {
                validate_plan_presets(source)?;
                validate_plan_blood_overrides(source)?;
                validate_plan_durations(source)?;
            }
            Ok(())
        }
        "deletePlan" | "readPlan" => {
            if args.as_object().unwrap().len() != 1 {
                return Err(format!("{command} accepts only args.name"));
            }
            validate_file_name(
                args.get("name")
                    .and_then(Value::as_str)
                    .ok_or("Missing args.name")?,
            )
        }
        "setPlanStage" => {
            if args.as_object().unwrap().keys().any(|key| {
                !matches!(
                    key.as_str(),
                    "name" | "expectedRevision" | "expectedEntry" | "action" | "stageIndex"
                )
            }) {
                return Err("setPlanStage accepts only name, expectedRevision, expectedEntry, action, and stageIndex".into());
            }
            validate_file_name(
                args.get("name")
                    .and_then(Value::as_str)
                    .ok_or("Missing args.name")?,
            )?;
            for field in ["expectedRevision", "expectedEntry"] {
                args.get(field)
                    .and_then(Value::as_str)
                    .filter(|text| !text.trim().is_empty())
                    .ok_or_else(|| format!("args.{field} must be a nonempty string"))?;
            }
            if !matches!(
                args.get("action").and_then(Value::as_str),
                Some("skip" | "set" | "restart")
            ) {
                return Err("args.action must be skip, set, or restart".into());
            }
            if !args
                .get("stageIndex")
                .and_then(Value::as_u64)
                .is_some_and(|index| index <= i32::MAX as u64)
            {
                return Err("args.stageIndex must be a nonnegative Int32".into());
            }
            Ok(())
        }
        "startMaxItopodPush" => {
            if args.as_object().unwrap().keys().any(|key| key != "deaths") {
                return Err("startMaxItopodPush accepts only deaths".into());
            }
            if !args
                .get("deaths")
                .and_then(Value::as_u64)
                .is_some_and(|deaths| (1..=1000).contains(&deaths))
            {
                return Err("args.deaths must be an integer between 1 and 1000".into());
            }
            Ok(())
        }
        "previewAugments" | "allocateAugments" => {
            if args
                .as_object()
                .unwrap()
                .keys()
                .any(|key| key != "expectedSeconds")
            {
                return Err("Augment commands accept only optional expectedSeconds".into());
            }
            if let Some(seconds) = args.get("expectedSeconds") {
                if !seconds.as_f64().is_some_and(|number| {
                    number >= 1.0 && number <= i32::MAX as f64 && number.fract() == 0.0
                }) {
                    return Err("args.expectedSeconds must be a positive Int32".into());
                }
            }
            Ok(())
        }
        "focusGoal" => {
            require_enum_argument(
                args,
                "goal",
                &[
                    "AugmentsBloodMagic",
                    "TimeMachine",
                    "Wandoos",
                    "EnergyNgu",
                    "MagicNgu",
                    "Ngu",
                    "AdvancedTraining",
                    "Adventure",
                    "Fight",
                    "ManualTitans",
                ],
            )?;
            if args
                .get("mode")
                .is_some_and(|mode| mode != "toggle" && mode != "apply")
            {
                return Err("Focus mode must be toggle or apply".into());
            }
            Ok(())
        }
        "optimizeGear" | "previewGear" => {
            require_enum_argument(
                args,
                "goal",
                &[
                    "EnergyCap",
                    "MagicCap",
                    "EnergyNgu",
                    "MagicNgu",
                    "AllNgu",
                    "TimeMachine",
                    "Blood",
                    "Gold",
                    "Drop",
                    "Fight",
                    "Augments",
                    "AdvancedTraining",
                    "Wandoos",
                    "ManualAdventure",
                ],
            )?;
            if args.get("fightScoreMode").is_some() {
                require_enum_argument(
                    args,
                    "fightScoreMode",
                    &[
                        "SqrtPowerToughness",
                        "PowerTimesToughness",
                        "PowerPlusToughness",
                    ],
                )?;
            }
            Ok(())
        }
        "manualFightPrep"
        | "manualRebirth"
        | "safeProbe"
        | "probeCommands"
        | "precacheLoadouts"
        | "resetRebirthPrep"
        | "reloadSettings"
        | "quickSave"
        | "unload"
        | "stopFocus"
        | "status"
        | "cancelMaxItopodPush" => {
            if args.as_object().unwrap().is_empty() {
                Ok(())
            } else {
                Err("This command does not accept args".into())
            }
        }
        _ => Err(format!("Command is not allowed: {command}")),
    }
}

fn require_enum_argument(args: &Value, key: &str, names: &[&str]) -> Result<(), String> {
    let value = args.get(key).ok_or_else(|| format!("Missing args.{key}"))?;
    if value.as_str().is_some_and(|name| names.contains(&name))
        || value
            .as_u64()
            .is_some_and(|index| index < names.len() as u64)
    {
        Ok(())
    } else {
        Err(format!("Invalid args.{key}"))
    }
}

fn check_resource_lane_execution(
    dir: &Path,
    runtime: &Value,
    command: &str,
    args: &Value,
) -> Result<(), String> {
    let require_capability = |row: &Value| -> Result<(), String> {
        if row["ResourcePrioritiesReviewed"].as_bool() == Some(true) {
            if runtime["resourceLanePrioritiesVersion"].as_u64() != Some(2) {
                return Err("Reviewed resource priorities require runtime.resourceLanePrioritiesVersion == 2; command was not queued".into());
            }
            if !runtime["startedAt"]
                .as_str()
                .is_some_and(|session| !session.trim().is_empty())
            {
                return Err("Reviewed resource priorities require a fresh identified injector session; command was not queued".into());
            }
            if runtime["globalPresetsVersion"].as_u64() != Some(2)
                && !matches!(command, "previewLoadoutPreset" | "applyLoadoutPreset")
            {
                return Err("Reviewed global presets require runtime.globalPresetsVersion == 2; command was not queued".into());
            }
        }
        Ok(())
    };
    match command {
        "previewLoadoutPreset" | "applyLoadoutPreset" => {
            return require_capability(&args["preset"])
        }
        "previewGlobalPreset" | "applyGlobalPreset" | "equipGlobalPreset" => {
            let (library, revision) = read_preset_library_snapshot(dir)?;
            if let Some(row) = library["Presets"]
                .as_array()
                .unwrap()
                .iter()
                .find(|row| row["Id"] == args["presetId"])
            {
                if row["ResourcePrioritiesReviewed"].as_bool() == Some(true)
                    && args["libraryRevision"].as_str() != Some(revision.as_str())
                {
                    return Err("Preset library revision changed; reload before using reviewed resource priorities. Command was not queued".into());
                }
                require_capability(row)?;
            }
            return Ok(());
        }
        _ => {}
    }
    let mut plan_revision = None;
    let plan = match command {
        "savePlan" => Some(args["plan"].clone()),
        "createPlan" => args.get("source").cloned(),
        "setPlanStage" | "applySettingsPatch" => {
            let name = if command == "setPlanStage" {
                args["name"].as_str().map(str::to_owned)
            } else {
                let patch = &args["patch"];
                if patch.get("_customPlanFile").is_none()
                    && patch["_useCustomRebirthPlan"].as_bool() != Some(true)
                    && patch["_autoRebirth"].as_bool() != Some(true)
                {
                    return Ok(());
                }
                match patch["_customPlanFile"]
                    .as_str()
                    .or(runtime["settings"]["customPlanFile"].as_str())
                {
                    Some(name) => Some(name.to_owned()),
                    None => read_json_object(&dir.join("settings.json"), MAX_JSON_BYTES, true)?
                        ["_customPlanFile"]
                        .as_str()
                        .map(str::to_owned),
                }
            };
            // Missing legacy/default files remain backend-owned; never infer migration aliases.
            match name {
                Some(name) => read_file_bytes(&plan_path(dir, &name)?, MAX_JSON_BYTES, true)?
                    .map(|bytes| {
                        plan_revision = Some(file_revision(&bytes));
                        parse_json_object(&bytes, "Committed plan")
                    })
                    .transpose()?,
                None => None,
            }
        }
        _ => None,
    };
    if let Some(plan) = plan {
        let references = plan_preset_references(&plan)?;
        if !references.is_empty() {
            let (library, _) = read_preset_library_snapshot(dir)?;
            for id in references {
                if let Some(row) = library["Presets"]
                    .as_array()
                    .unwrap()
                    .iter()
                    .find(|row| row["Id"].as_str() == Some(id))
                {
                    if command == "setPlanStage"
                        && row["ResourcePrioritiesReviewed"].as_bool() == Some(true)
                        && args["expectedRevision"].as_str() != plan_revision.as_deref()
                    {
                        return Err("Plan revision changed; reload before changing a reviewed preset stage. Command was not queued".into());
                    }
                    require_capability(row)?;
                }
            }
        }
    }
    Ok(())
}

fn queue_command(dir: &Path, command: &str, mut args: Value) -> Result<String, String> {
    validate_command(command, &args)?;
    if command == "savePlan" {
        let revision = take_plan_revision(args.get_mut("plan").unwrap())?;
        let expected = args
            .get("expectedRevision")
            .filter(|value| value.as_str() != Some(""))
            .map(validate_revision)
            .transpose()?;
        if let (Some(revision), Some(expected)) = (&revision, &expected) {
            if revision != expected {
                return Err("Plan transport revision disagrees with args.expectedRevision".into());
            }
        }
        let expected = expected.or(revision);
        if expected.is_none()
            && plan_path(dir, args["name"].as_str().unwrap())?
                .try_exists()
                .map_err(|err| err.to_string())?
        {
            return Err(
                "expectedRevision is required for an existing plan; reload before saving".into(),
            );
        }
        args["expectedRevision"] = json!(expected.unwrap_or_default());
    }
    if command == "createPlan" {
        if let Some(source) = args.get_mut("source") {
            source.as_object_mut().unwrap().remove(REVISION_FIELD);
            source
                .as_object_mut()
                .unwrap()
                .insert("RebirthHistory".into(), json!([]));
        }
    }
    let runtime = read_runtime(dir)?;
    require_live_bridge(&runtime)?;
    let referencing_plan = match command {
        "savePlan" => Some(&args["plan"]),
        "createPlan" => args.get("source"),
        _ => None,
    }
    .map(plan_preset_references)
    .transpose()?
    .is_some_and(|references| !references.is_empty());
    if (referencing_plan
        || matches!(
            command,
            "readPresetLibrary"
                | "savePresetLibrary"
                | "previewGlobalPreset"
                | "applyGlobalPreset"
                | "equipGlobalPreset"
        ))
        && runtime["globalPresetsVersion"].as_u64() != Some(2)
    {
        return Err(
            "Global presets require runtime.globalPresetsVersion == 2; command was not queued"
                .into(),
        );
    }
    if command == "savePresetLibrary" {
        if !runtime["supportedCommands"]
            .as_array()
            .is_some_and(|commands| {
                commands
                    .iter()
                    .any(|name| name.as_str() == Some("readPresetLibrary"))
            })
        {
            return Err(
                "Connected injector must advertise readPresetLibrary before saving the library"
                    .into(),
            );
        }
        args["library"] = normalize_preset_library(args["library"].clone())?;
    }
    if matches!(
        command,
        "previewGlobalPreset" | "applyGlobalPreset" | "equipGlobalPreset"
    ) && !runtime["startedAt"]
        .as_str()
        .is_some_and(|session| !session.trim().is_empty())
    {
        return Err("Global preset actions require a fresh identified injector session; command was not queued".into());
    }
    if matches!(command, "previewLoadoutPreset" | "applyLoadoutPreset")
        && (runtime["loadoutPresetsVersion"].as_u64() != Some(2)
            || !runtime["startedAt"]
                .as_str()
                .is_some_and(|session| !session.trim().is_empty()))
    {
        return Err("Presets require loadoutPresetsVersion 2 and a fresh identified injector session; command was not queued".into());
    }
    if matches!(
        command,
        "previewCombatPrep" | "startCombatPrep" | "cancelCombatPrep"
    ) && !runtime["startedAt"]
        .as_str()
        .is_some_and(|session| !session.trim().is_empty())
    {
        return Err(
            "Combat prep requires a fresh identified injector session; command was not queued"
                .into(),
        );
    }
    let timing_write = matches!(
        command,
        "readPlan" | "savePlan" | "createPlan" | "setPlanStage"
    ) || command == "applySettingsPatch"
        && (args["patch"].get("_customPlanFile").is_some()
            || args["patch"]["_useCustomRebirthPlan"].as_bool() == Some(true)
            || args["patch"]["_autoRebirth"].as_bool() == Some(true));
    if timing_write
        && (runtime["stageTimingVersion"].as_u64() != Some(1)
            || runtime["fightScheduleVersion"].as_u64() != Some(1)
            || runtime["planVersion"].as_u64().unwrap_or(0) < 18)
    {
        return Err("Connected injector requires runtime.stageTimingVersion == 1 AND runtime.fightScheduleVersion == 1 AND runtime.planVersion >= 18 before reading, saving, copying, creating, selecting, running, overriding stages, or enabling auto rebirth; command was not queued".into());
    }
    if (timing_write
        || matches!(
            command,
            "previewLoadoutPreset"
                | "applyLoadoutPreset"
                | "readPresetLibrary"
                | "savePresetLibrary"
                | "previewGlobalPreset"
                | "applyGlobalPreset"
                | "equipGlobalPreset"
        )
        || command == "applySettingsPatch"
            && (args["patch"].get("_energyNguWeights").is_some()
                || args["patch"].get("_magicNguWeights").is_some()))
        && runtime["globalNguWeightsVersion"].as_u64() != Some(1)
    {
        return Err("Connected injector requires runtime.globalNguWeightsVersion == 1 for global-only NGU weights; command was not queued".into());
    }
    if runtime["adventureDynamicPresetsVersion"].as_u64() != Some(1) {
        let dynamic = match command {
            "previewLoadoutPreset" | "applyLoadoutPreset" => args["preset"]
                ["AdventureDynamicWeight"]
                .as_f64()
                .is_some_and(|weight| weight > 0.0),
            "previewGlobalPreset" | "applyGlobalPreset" | "equipGlobalPreset" => {
                let (library, _) = read_preset_library_snapshot(dir)?;
                library["Presets"].as_array().unwrap().iter().any(|row| {
                    row["Id"] == args["presetId"]
                        && row["AdventureDynamicWeight"]
                            .as_f64()
                            .is_some_and(|weight| weight > 0.0)
                })
            }
            _ => false,
        };
        if dynamic {
            return Err("Dynamic adventure requires runtime.adventureDynamicPresetsVersion == 1; command was not queued".into());
        }
    }
    match command {
        "savePlan" => {
            args["plan"] = normalize_offline_plan(args["plan"].clone())?;
        }
        "createPlan" => {
            if let Some(source) = args.get_mut("source") {
                *source = normalize_offline_plan(source.clone())?;
            }
        }
        "previewLoadoutPreset" | "applyLoadoutPreset" => strip_ngu_weights(&mut args["preset"]),
        _ => {}
    }
    if command == "setPlanStage" && runtime["stageOverridesV1"].as_bool() != Some(true) {
        return Err("Connected injector requires runtime.stageOverridesV1 == true for setPlanStage; command was not queued".into());
    }
    if !runtime
        .get("supportedCommands")
        .and_then(Value::as_array)
        .is_some_and(|commands| commands.iter().any(|name| name.as_str() == Some(command)))
    {
        return Err(format!(
            "Connected injector does not advertise support for {command}; command was not queued"
        ));
    }
    if command == "savePlan"
        && !runtime["supportedCommands"]
            .as_array()
            .unwrap()
            .iter()
            .any(|name| name.as_str() == Some("readPlan"))
    {
        return Err("Connected injector must advertise readPlan and revision-aware savePlan; update the bridge before saving".into());
    }
    check_resource_lane_execution(dir, &runtime, command, &args)?;
    if command == "applySettingsPatch" && runtime["settingsCasVersion"].as_u64() != Some(1) {
        return Err("Connected injector requires runtime.settingsCasVersion == 1; update the injector before saving settings".into());
    }
    require_live_bridge(&runtime)?;
    let id = Uuid::new_v4().to_string();
    let now = Utc::now();
    let expires = now + chrono::Duration::milliseconds(COMMAND_TIMEOUT_MS as i64);
    let payload = json!({
        "id": id, "command": command, "args": args,
        "createdAt": now.to_rfc3339_opts(SecondsFormat::Millis, true),
        "expiresAt": expires.to_rfc3339_opts(SecondsFormat::Millis, true)
    });
    let bytes = serde_json::to_vec_pretty(&payload).map_err(|err| err.to_string())?;
    let command_limit = if command == "savePresetLibrary" {
        MAX_PRESET_LIBRARY_COMMAND_BYTES
    } else {
        MAX_COMMAND_BYTES
    };
    let limit = runtime
        .get("maxCommandBytes")
        .and_then(Value::as_u64)
        .unwrap_or(command_limit)
        .min(command_limit);
    if bytes.len() as u64 > limit {
        return Err(format!(
            "Command exceeds the {limit} byte bridge limit; command was not queued"
        ));
    }
    write_atomic(
        &dir.join("commands").join(format!("{id}.json")),
        &bytes,
        false,
    )?;
    Ok(id)
}

fn read_result(dir: &Path, id: &str, command: Option<&str>) -> Result<Option<Value>, String> {
    let path = dir.join("command-results").join(format!("{id}.json"));
    if !path.try_exists().map_err(|err| err.to_string())? {
        return Ok(None);
    }
    let limit = if command == Some("readPresetLibrary") {
        MAX_PRESET_LIBRARY_RESULT_BYTES
    } else if command == Some("readPlan")
        || command == Some("applySettingsPatch")
        || command.is_none()
    {
        MAX_PLAN_RESULT_BYTES
    } else {
        ACTION_RESULT_MAX_BYTES
    };
    let bytes = read_file_bytes(&path, limit, false)?.ok_or("Command result disappeared")?;
    let result = parse_json_object(&bytes, "Command result")?;
    let summary: CommandResultSummary = serde_json::from_value(result.clone())
        .map_err(|err| format!("Invalid command result: {err}"))?;
    if summary.command == "readPresetLibrary"
        && bytes.len() as u64 > MAX_PRESET_LIBRARY_RESULT_BYTES
    {
        return Err("Preset library result exceeds the library transport size limit".into());
    }
    if !matches!(
        summary.command.as_str(),
        "readPlan" | "readPresetLibrary" | "applySettingsPatch"
    ) && bytes.len() as u64 > ACTION_RESULT_MAX_BYTES
    {
        return Err("Command result exceeds the 256 KiB action-result limit".into());
    }
    if summary.id != id || command.is_some_and(|expected| summary.command != expected) {
        return Err("Command result identity does not match the request".into());
    }
    DateTime::parse_from_rfc3339(&summary.completed_at)
        .map_err(|err| format!("Invalid command result completedAt: {err}"))?;
    Ok(Some(result))
}

fn wait_for_command(
    dir: &Path,
    id: &str,
    command: &str,
    deadline: Instant,
) -> Result<Value, String> {
    let queued_path = dir.join("commands").join(format!("{id}.json"));
    let outcome = loop {
        match read_result(dir, id, Some(command)) {
            Ok(Some(result)) => {
                if result.get("success").and_then(Value::as_bool) == Some(true) {
                    break Ok(
                        json!({"success": true, "mode": "connected", "id": id, "result": result, "protocol": protocol_info()}),
                    );
                }
                break Err(format!(
                    "Command {command} ({id}) failed: {}",
                    result
                        .get("message")
                        .and_then(Value::as_str)
                        .unwrap_or("Unknown failure")
                ));
            }
            Err(err) => break Err(format!("Command {command} ({id}): {err}")),
            Ok(None) => {}
        }
        if Instant::now() >= deadline {
            break Err(format!("Command {command} ({id}) timed out; outcome is unknown if already running. Queued request will be removed and expiresAt prevents later execution"));
        }
        std::thread::sleep(Duration::from_millis(50));
    };
    let outcome = match remove_if_present(&queued_path) {
        Ok(()) => outcome,
        Err(err) => Err(format!(
            "Command {id}: queue cleanup failed ({err}); bridge must enforce expiresAt"
        )),
    };
    if let Err(message) = &outcome {
        let result_path = dir.join("command-results").join(format!("{id}.json"));
        let result = json!({
            "id": id, "command": command, "success": false, "message": message,
            "completedAt": Utc::now().to_rfc3339_opts(SecondsFormat::Millis, true), "source": "external-ui"
        });
        // Never overwrite a bridge result. It may still finish an already-running action.
        if !result_path.try_exists().map_err(|err| err.to_string())? {
            if let Err(err) = write_json_atomic(&result_path, &result, false) {
                if !result_path.try_exists().map_err(|err| err.to_string())? {
                    return Err(format!("{message}; failed to record failure result: {err}"));
                }
            }
        }
    }
    outcome
}

fn remove_if_present(path: &Path) -> Result<(), String> {
    match fs::remove_file(path) {
        Ok(()) => Ok(()),
        Err(err) if err.kind() == std::io::ErrorKind::NotFound => Ok(()),
        Err(err) => Err(format!("Failed to remove {}: {err}", path.display())),
    }
}

fn list_json_stems(dir: &Path) -> Result<Vec<String>, String> {
    let entries = match fs::read_dir(dir) {
        Ok(entries) => entries,
        Err(err) if err.kind() == std::io::ErrorKind::NotFound => return Ok(Vec::new()),
        Err(err) => return Err(err.to_string()),
    };
    let mut names = Vec::new();
    for entry in entries {
        let entry = entry.map_err(|err| err.to_string())?;
        let path = entry.path();
        if !entry.file_type().map_err(|err| err.to_string())?.is_file()
            || path.extension().and_then(|ext| ext.to_str()) != Some("json")
        {
            continue;
        }
        if let Some(stem) = path
            .file_stem()
            .and_then(|stem| stem.to_str())
            .filter(|stem| validate_file_name(stem).is_ok())
        {
            names.push(stem.to_string());
        }
    }
    names.sort_unstable();
    Ok(names)
}

fn list_logs(dir: &Path) -> Result<Vec<LogFile>, String> {
    let entries = match fs::read_dir(dir) {
        Ok(entries) => entries,
        Err(err) if err.kind() == std::io::ErrorKind::NotFound => return Ok(Vec::new()),
        Err(err) => return Err(err.to_string()),
    };
    let mut logs = Vec::new();
    for entry in entries {
        let entry = entry.map_err(|err| err.to_string())?;
        let name = entry.file_name().to_string_lossy().into_owned();
        if !entry.file_type().map_err(|err| err.to_string())?.is_file()
            || !name.ends_with(".log")
            || validate_file_name(&name).is_err()
        {
            continue;
        }
        let metadata = entry.metadata().map_err(|err| err.to_string())?;
        logs.push(LogFile {
            name,
            size: metadata.len(),
            modified_epoch_ms: metadata.modified().ok().and_then(system_time_ms),
        });
    }
    logs.sort_by(|a, b| a.name.cmp(&b.name));
    Ok(logs)
}

fn list_command_results(dir: &Path) -> Result<Vec<CommandResultSummary>, String> {
    let entries = match fs::read_dir(dir) {
        Ok(entries) => entries,
        Err(err) if err.kind() == std::io::ErrorKind::NotFound => return Ok(Vec::new()),
        Err(err) => return Err(err.to_string()),
    };
    // Keep only the newest paths, before parsing: both memory and parsed bytes are bounded.
    let mut newest = BinaryHeap::new();
    for entry in entries {
        let entry = entry.map_err(|err| err.to_string())?;
        let path = entry.path();
        if path.extension().and_then(|ext| ext.to_str()) != Some("json") {
            continue;
        }
        let metadata = entry.metadata().map_err(|err| err.to_string())?;
        if !metadata.is_file() || metadata.len() > MAX_PRESET_LIBRARY_RESULT_BYTES {
            continue;
        }
        newest.push(Reverse((
            metadata
                .modified()
                .ok()
                .and_then(system_time_ms)
                .unwrap_or(0),
            path,
        )));
        if newest.len() > MAX_RESULTS {
            newest.pop();
        }
    }
    let mut results = Vec::new();
    for Reverse((_, path)) in newest.into_sorted_vec() {
        // Malformed historical results must not hide the otherwise usable runtime state.
        if let Ok(bytes) = read_file_bytes(&path, MAX_PRESET_LIBRARY_RESULT_BYTES, false) {
            let Some(bytes) = bytes else {
                continue;
            };
            if let Ok(summary) = serde_json::from_slice::<CommandResultSummary>(&bytes) {
                if summary.command != "readPresetLibrary"
                    && bytes.len() as u64 > ACTION_RESULT_MAX_BYTES
                {
                    continue;
                }
                results.push(summary);
            }
        }
    }
    Ok(results)
}

fn log_tail(bytes: &[u8], truncated: bool, max_lines: usize) -> String {
    let bytes = if truncated {
        bytes
            .iter()
            .position(|byte| *byte == b'\n')
            .map_or(&[][..], |index| &bytes[index + 1..])
    } else {
        bytes
    };
    let text = String::from_utf8_lossy(bytes);
    let mut lines: Vec<&str> = text
        .lines()
        .rev()
        .take(max_lines.clamp(1, MAX_LOG_LINES))
        .collect();
    lines.reverse();
    lines.join("\n")
}

fn default_plan() -> Value {
    // Match CustomPlan.DefaultNumberClimbStages and the serialized stage defaults.
    let stages: Vec<Value> = [
        ("Time Machine opener", 16.667, 1, 1, 5),
        ("Augments + Blood", 10.0, 2, 4, 10),
        ("Time Machine retry", 10.0, 1, 1, 5),
        ("Augments top-up", 3.333, 2, 4, 10),
        ("Wandoos push", 10.0, 3, 3, 9),
        ("Time Machine long", 16.667, 1, 1, 5),
        ("Augments + Blood long", 16.667, 2, 4, 10),
        ("Wandoos long", 16.667, 3, 3, 9),
    ]
    .into_iter()
    .map(|(name, percent, energy, magic, gear)| {
        json!({
            "Name": name, "Percent": percent, "EnergyFocus": energy, "MagicFocus": magic,
            "DurationSeconds": 0, "DurationUnit": "%", "FightSchedule": null,
            "GearGoal": gear, "CompletionMode": 0, "BlackBarLookaheadSeconds": 5,
            "EnergyPriorities": [], "MagicPriorities": [],
            "TargetAdvancedTrainingPower": "0", "TargetAdvancedTrainingToughness": "0"
        })
    })
    .collect();
    json!({
        "Version": 18, "Name": "new-plan", "AutoRebirthEnabled": true,
        "AutoIronPillEnabled": true,
        "TimeTriggerEnabled": true, "TimeTriggerSeconds": 3600,
        "NumberTriggerEnabled": false, "NumberMultiplierTarget": 100.0,
        "NumberClimbEnabled": true, "NumberClimbMinSeconds": 480, "NumberClimbMultiplierTarget": 100.0,
        "TriggerMode": 0, "StageSchedulerEnabled": true, "StageDurationSeconds": 1800,
        "StageLoopStartIndex": 5, "Stages": stages, "AutoActivateFruits": true,
        "GearGoal": 0, "FightScoreMode": 0, "ActiveGoalPresetIndex": 0,
        "GoalPresets": [{"Name": "Default", "GearGoal": 0}], "PrepRequireMaxedFruit": false,
        "RebirthHistory": []
    })
}

fn system_time_ms(value: SystemTime) -> Option<u128> {
    value
        .duration_since(UNIX_EPOCH)
        .ok()
        .map(|duration| duration.as_millis())
}

fn main() {
    tauri::Builder::default()
        .plugin(tauri_plugin_dialog::init())
        .invoke_handler(tauri::generate_handler![
            get_ngui_state,
            save_settings_patch_json,
            read_plan,
            save_plan,
            read_preset_library,
            save_preset_library,
            create_plan,
            delete_plan,
            enqueue_command,
            read_command_result,
            tail_log,
            set_external_ui_enabled
        ])
        .run(tauri::generate_context!())
        .expect("error while running NGU Injector UI");
}

#[cfg(test)]
#[path = "persistence-regression-tests.rs"]
mod persistence_regression_tests;

#[cfg(test)]
#[path = "execution-rebind-tests.rs"]
mod execution_rebind_tests;

#[cfg(test)]
#[path = "combat-prep-tests.rs"]
mod combat_prep_tests;

#[cfg(test)]
#[path = "adventure-rules-tests.rs"]
mod adventure_rules_tests;

#[cfg(test)]
#[path = "manual-combat-tests.rs"]
mod manual_combat_tests;

#[cfg(test)]
#[path = "plan-blood-tests.rs"]
mod plan_blood_tests;

#[cfg(test)]
mod action_result_transport_tests {
    use super::*;

    #[test]
    fn bounded_preview_payloads_with_runtime_envelopes_survive_all_result_read_paths() {
        let dir = tempfile::tempdir().unwrap();
        for (command, field, payload_bytes, runtime_bytes) in [
            ("previewLoadoutPreset", "result", 63 * 1024, 64 * 1024),
            (
                "previewCombatPrep",
                "combatPrepPreview",
                252 * 1024,
                2 * 1024,
            ),
        ] {
            let id = Uuid::new_v4().to_string();
            let mut preview = json!({"version": 1, "schemaVersion": 1, "previewId": "0123456789abcdef0123456789abcdef", "padding": ""});
            let padding = payload_bytes - serde_json::to_vec(&preview).unwrap().len();
            preview["padding"] = json!("p".repeat(padding));
            assert_eq!(serde_json::to_vec(&preview).unwrap().len(), payload_bytes);
            let mut result = json!({"id": id, "command": command, "success": true, "message": "Preview completed", "completedAt": Utc::now().to_rfc3339(), "data": {"runtime": {"startedAt": "session", "context": "r".repeat(runtime_bytes)}}});
            result["data"][field] = preview.clone();
            let bytes = serde_json::to_vec(&result).unwrap();
            assert!(bytes.len() as u64 > MAX_RESULT_BYTES);
            assert!((bytes.len() as u64) < ACTION_RESULT_MAX_BYTES);
            let path = dir
                .path()
                .join("command-results")
                .join(format!("{id}.json"));
            write_atomic(&path, &bytes, false).unwrap();
            let queued = dir.path().join("commands").join(format!("{id}.json"));
            write_json_atomic(&queued, &json!({"id": id, "command": command}), false).unwrap();

            assert_eq!(
                read_result(dir.path(), &id, Some(command)).unwrap(),
                Some(result.clone())
            );
            assert_eq!(
                read_command_result(dir.path().to_string_lossy().into_owned(), id.clone()).unwrap(),
                Some(result.clone())
            );
            let response = wait_for_command(dir.path(), &id, command, Instant::now()).unwrap();
            assert_eq!(response["result"], result);
            assert_eq!(response["result"]["data"][field], preview);
            assert!(!queued.exists());
            assert!(list_command_results(&dir.path().join("command-results"))
                .unwrap()
                .iter()
                .any(|row| row.id == id && row.command == command && row.success));
        }
    }

    #[test]
    fn action_result_limit_accepts_exact_boundary_and_rejects_one_extra_byte() {
        let dir = tempfile::tempdir().unwrap();
        let id = Uuid::new_v4().to_string();
        let mut result = json!({"id": id, "command": "previewLoadoutPreset", "success": true, "message": "ok", "completedAt": Utc::now().to_rfc3339(), "padding": ""});
        let padding =
            ACTION_RESULT_MAX_BYTES as usize - serde_json::to_vec_pretty(&result).unwrap().len();
        result["padding"] = json!("p".repeat(padding));
        let path = dir
            .path()
            .join("command-results")
            .join(format!("{id}.json"));
        write_json_atomic(&path, &result, false).unwrap();
        assert_eq!(fs::metadata(&path).unwrap().len(), ACTION_RESULT_MAX_BYTES);
        assert_eq!(
            read_result(dir.path(), &id, Some("previewLoadoutPreset")).unwrap(),
            Some(result.clone())
        );
        assert_eq!(
            read_result(dir.path(), &id, None).unwrap(),
            Some(result.clone())
        );
        assert_eq!(
            list_command_results(&dir.path().join("command-results"))
                .unwrap()
                .len(),
            1
        );

        result["padding"] = json!("p".repeat(padding + 1));
        write_json_atomic(&path, &result, true).unwrap();
        assert_eq!(
            fs::metadata(&path).unwrap().len(),
            ACTION_RESULT_MAX_BYTES + 1
        );
        assert!(read_result(dir.path(), &id, Some("previewLoadoutPreset"))
            .unwrap_err()
            .contains("262144 byte JSON limit"));
        assert!(read_result(dir.path(), &id, None)
            .unwrap_err()
            .contains("256 KiB action-result limit"));
        assert!(list_command_results(&dir.path().join("command-results"))
            .unwrap()
            .is_empty());
        assert_eq!(protocol_info()["maxResultBytes"], ACTION_RESULT_MAX_BYTES);
        assert_eq!(MAX_JSON_BYTES, 4 * 1024 * 1024);
        assert_eq!(MAX_RUNTIME_BYTES, 1024 * 1024);
        assert_eq!(MAX_PLAN_RESULT_BYTES, 4 * 1024 * 1024 + 64 * 1024);
        assert_eq!(MAX_LOG_BYTES, 256 * 1024);
    }
}

#[cfg(test)]
mod loadout_preset_tests {
    use super::*;

    fn preset() -> Value {
        json!({"Id": "balanced", "Name": "Balanced NGU", "Weights": [100,100,0,0,0,0], "MagicBloodPercent": -1, "SupportMode": "current", "DiggerBudgetPercent": 75, "Zone": -1, "MinimumPower": 0, "MinimumToughness": 0, "SwapSeconds": 5, "BeardPriority": [], "DiggerWeights": [], "Future": {"keep": 42}})
    }

    fn args() -> Value {
        json!({"preset": preset()})
    }

    #[test]
    fn validates_inline_preview_and_apply_boundaries() {
        validate_command("previewLoadoutPreset", &args()).unwrap();
        let mut apply = args();
        apply["candidateKey"] = json!("mixed_1-2");
        apply["previewId"] = json!("0123456789abcdef0123456789abcdef");
        validate_command("applyLoadoutPreset", &apply).unwrap();
        for key in ["", "../escape", "a b", &"x".repeat(65)] {
            apply["candidateKey"] = json!(key);
            assert!(validate_command("applyLoadoutPreset", &apply).is_err());
        }
        let mut extra = args();
        extra["extra"] = json!(true);
        assert!(validate_command("previewLoadoutPreset", &extra).is_err());
        assert!(validate_command("applyLoadoutPreset", &args()).is_err());
        for key in ["preset"] {
            let mut missing = args();
            missing.as_object_mut().unwrap().remove(key);
            assert!(validate_command("previewLoadoutPreset", &missing).is_err());
        }
        for key in ["energyNguWeights", "magicNguWeights"] {
            let mut retired = args();
            retired[key] = json!(vec![0; 9]);
            assert!(validate_command("previewLoadoutPreset", &retired).is_err());
            retired["candidateKey"] = json!("mixed");
            retired["previewId"] = json!("a".repeat(32));
            assert!(validate_command("applyLoadoutPreset", &retired).is_err());
        }
    }

    #[test]
    fn apply_requires_exact_preview_token_shape_and_preview_rejects_tokens() {
        let mut apply = args();
        apply["candidateKey"] = json!("mixed");
        assert!(validate_command("applyLoadoutPreset", &apply).is_err());
        for token in [
            json!(""),
            json!("a".repeat(31)),
            json!("a".repeat(33)),
            json!("g".repeat(32)),
            json!(" ../token "),
            json!(null),
            json!(123),
        ] {
            apply["previewId"] = token;
            assert!(validate_command("applyLoadoutPreset", &apply)
                .unwrap_err()
                .contains("previewId"));
        }
        apply["previewId"] = json!("0123456789abcdef0123456789abcdef");
        validate_command("applyLoadoutPreset", &apply).unwrap();
        for key in ["preset", "candidateKey", "previewId"] {
            let mut missing = apply.clone();
            missing.as_object_mut().unwrap().remove(key);
            assert!(
                validate_command("applyLoadoutPreset", &missing).is_err(),
                "missing {key}"
            );
        }
        let mut preview = args();
        preview["previewId"] = apply["previewId"].clone();
        assert!(validate_command("previewLoadoutPreset", &preview).is_err());
    }

    #[test]
    fn accepts_legacy_and_fourteen_objective_weights() {
        for count in [6, 10, 12, 14] {
            let mut row = preset();
            row["Weights"] = json!(vec![1; count]);
            validate_loadout_preset(&row).unwrap();
        }
        for index in [10, 11, 12, 13] {
            let mut row = preset();
            row["Weights"] = json!(vec![0; 14]);
            row["Weights"][index] = json!(0.5);
            validate_loadout_preset(&row).unwrap();
            row["Weights"][index] = json!(1_000_000);
            validate_loadout_preset(&row).unwrap();
        }
        for count in [0, 5, 7, 9, 11, 13, 15] {
            let mut row = preset();
            row["Weights"] = json!(vec![1; count]);
            assert!(validate_loadout_preset(&row)
                .unwrap_err()
                .contains("Weights"));
        }
        for index in [10, 11, 12, 13] {
            for token in [
                "-1",
                "1000001",
                "1000000.0000000000000001",
                "-0.000000000000000001",
                "1e999",
                "null",
                "true",
                "\"1\"",
            ] {
                let mut row = preset();
                row["Weights"] = json!(vec![1; 14]);
                row["Weights"][index] = serde_json::from_str(token).unwrap();
                assert!(
                    validate_loadout_preset(&row)
                        .unwrap_err()
                        .contains("Weight"),
                    "accepted weight {index}: {token}"
                );
            }
        }
        let mut row = preset();
        row["Weights"] = json!(vec![0; 14]);
        assert!(validate_loadout_preset(&row)
            .unwrap_err()
            .contains("At least one weight must be positive"));
    }

    #[test]
    fn accepts_missing_or_invalid_legacy_horizon() {
        let mut request = args();
        assert!(request["preset"].get("HorizonSeconds").is_none());
        for horizon in [
            None,
            Some(json!(0)),
            Some(json!(-1)),
            Some(json!(1.5)),
            Some(json!(604801)),
            Some(json!(null)),
            Some(json!("invalid")),
            Some(json!(true)),
            Some(json!([])),
            Some(json!({})),
            Some(serde_json::from_str("1.00000000000000001").unwrap()),
            Some(serde_json::from_str("1e999").unwrap()),
        ] {
            if let Some(horizon) = horizon {
                request["preset"]["HorizonSeconds"] = horizon;
            }
            validate_loadout_preset(&request["preset"]).unwrap();
            validate_command("previewLoadoutPreset", &request).unwrap();
            let mut apply = request.clone();
            apply["candidateKey"] = json!("mixed");
            apply["previewId"] = json!("0123456789abcdef0123456789abcdef");
            validate_command("applyLoadoutPreset", &apply).unwrap();
        }
    }

    #[test]
    fn rejects_malformed_definitions_and_exact_decimal_overflows() {
        for (key, bad) in [
            ("Id", json!("../bad")),
            ("Name", json!("")),
            ("Name", json!("x".repeat(101))),
            ("Weights", json!([0, 0, 0, 0, 0, 0])),
            ("Weights", json!([1])),
            ("Weights", json!([1, -1, 0, 0, 0, 0])),
            ("MagicBloodPercent", json!(-0.5)),
            ("MagicBloodPercent", json!(100.5)),
            ("SupportMode", json!("unlimited")),
            ("DiggerBudgetPercent", json!(75.5)),
            ("DiggerBudgetPercent", json!(96)),
            ("MinimumPower", json!(-1)),
            ("MinimumToughness", json!("1")),
            ("SwapSeconds", json!(5.5)),
            ("SwapSeconds", json!(3601)),
            ("BeardPriority", json!([0, 0])),
            ("BeardPriority", json!([7])),
            ("BeardPriority", json!([0.5])),
            ("DiggerWeights", json!([1])),
        ] {
            let mut row = preset();
            row[key] = bad;
            assert!(validate_loadout_preset(&row).is_err(), "{key}: {row}");
        }
        for token in ["1000000.0000000000000001", "-0.000000000000000001", "1e999"] {
            let mut row = preset();
            row["Weights"][0] = serde_json::from_str(token).unwrap();
            assert!(validate_loadout_preset(&row).is_err(), "{token}");
        }
        for token in [
            "100.0000000000000001",
            "-1.0000000000000001",
            "-0.99999999999999999",
        ] {
            let mut row = preset();
            row["MagicBloodPercent"] = serde_json::from_str(token).unwrap();
            assert!(validate_loadout_preset(&row).is_err(), "{token}");
        }
        for key in ["DiggerBudgetPercent", "SwapSeconds"] {
            let mut row = preset();
            row[key] = serde_json::from_str("1.00000000000000001").unwrap();
            assert!(
                validate_loadout_preset(&row).is_err(),
                "{key} must not round a fractional token to an integer"
            );
        }
        for zone in [-2, 6, 8, 11, 14, 16, 19, 23, 26, 30, 34, 38, 42, 44, 1000] {
            let mut row = preset();
            row["Zone"] = json!(zone);
            assert!(validate_loadout_preset(&row).is_err(), "zone {zone}");
        }
    }

    #[test]
    fn normalization_preserves_library_unknowns_and_legacy_gear_at_current_version() {
        let source = json!({"Version": 18, "AutoIronPillEnabled": true, "LoadoutPresets": [preset()], "GoalPresets": [{"Name": "Legacy", "GearGoal": 2}], "GearGoal": 2, "ActiveGoalPresetIndex": 0});
        let normalized = normalize_offline_plan(source.clone()).unwrap();
        assert_eq!(normalized, source);
        validate_loadout_presets(None).unwrap();
        validate_loadout_presets(Some(&json!([]))).unwrap();
        assert!(validate_loadout_presets(Some(&json!([preset(), preset()]))).is_err());
        let rows = (0..32)
            .map(|id| {
                let mut row = preset();
                row["Id"] = json!(format!("preset_{id}"));
                row
            })
            .collect::<Vec<_>>();
        validate_loadout_presets(Some(&json!(rows))).unwrap();
        let mut too_many = rows;
        too_many.push(preset());
        assert!(validate_loadout_presets(Some(&json!(too_many))).is_err());
    }

    #[test]
    fn offline_writer_preserves_library_and_lossless_unknowns_through_normalized_reads() {
        let dir = tempfile::tempdir().unwrap();
        let runtime_dir = dir.path().to_string_lossy().into_owned();
        let mut source = default_plan();
        let mut row = preset();
        row["Future"] = serde_json::from_str(r#"{"integer":9007199254740993,"decimal":12345678901234567890.123456789,"nested":[{"keep":true}]}"#).unwrap();
        source["LoadoutPresets"] = json!([row]);
        source["FutureRoot"] = row["Future"].clone();
        source["GoalPresets"] = json!([{"Name":"Legacy","GearGoal":2}]);
        source["GearGoal"] = json!(2);
        source["ActiveGoalPresetIndex"] = json!(0);
        let saved = tauri::async_runtime::block_on(save_plan(
            runtime_dir.clone(),
            "presets".into(),
            source.to_string(),
        ))
        .unwrap();
        assert_eq!(saved["mode"], "offline");
        let path = plan_path(dir.path(), "presets").unwrap();
        let read: Value = serde_json::from_str(&read_plan_json(&path).unwrap()).unwrap();
        assert_eq!(read["LoadoutPresets"], source["LoadoutPresets"]);
        assert_eq!(read["Version"], 18);
        let patch = json!({"Version": 17, "TimeTriggerSeconds": 7200});
        let mut patch = patch;
        patch[REVISION_FIELD] = read[REVISION_FIELD].clone();
        tauri::async_runtime::block_on(save_plan(
            runtime_dir.clone(),
            "presets".into(),
            patch.to_string(),
        ))
        .unwrap();
        let reread: Value = serde_json::from_str(&read_plan_json(&path).unwrap()).unwrap();
        for key in [
            "LoadoutPresets",
            "FutureRoot",
            "GoalPresets",
            "GearGoal",
            "ActiveGoalPresetIndex",
        ] {
            assert_eq!(reread[key], source[key], "{key}");
        }
        tauri::async_runtime::block_on(save_plan(
            runtime_dir.clone(),
            "presets".into(),
            reread.to_string(),
        ))
        .unwrap();
        let saved: Value = serde_json::from_str(&read_plan_json(&path).unwrap()).unwrap();
        assert_eq!(saved["LoadoutPresets"], source["LoadoutPresets"]);
        assert_eq!(saved["GoalPresets"], source["GoalPresets"]);
        assert_eq!(saved["Version"], 18);
        let bytes = fs::read_to_string(path).unwrap();
        assert!(bytes.contains("9007199254740993"));
        assert!(bytes.contains("12345678901234567890.123456789"));
        assert!(!bytes.contains(REVISION_FIELD));
    }

    #[test]
    fn offline_plan_provenance_changes_are_rejected_without_writes() {
        let dir = tempfile::tempdir().unwrap();
        let runtime_dir = dir.path().to_string_lossy().into_owned();
        let mut source = default_plan();
        let mut row = preset();
        row["Future"] = serde_json::from_str("900719925474099312345.123456789").unwrap();
        source["LoadoutPresets"] = json!([row]);
        source["GoalPresets"] =
            json!([{"Name": "First", "GearGoal": 2}, {"Name": "Second", "GearGoal": 3}]);
        save_plan_offline(dir.path(), "provenance", &source, None).unwrap();
        let path = plan_path(dir.path(), "provenance").unwrap();
        let before = fs::read(&path).unwrap();
        let loaded: Value = serde_json::from_str(&read_plan_json(&path).unwrap()).unwrap();
        let mut renamed = row.clone();
        renamed["Name"] = json!("Changed");
        let mut changed_unknown = row;
        changed_unknown["Future"] =
            serde_json::from_str("900719925474099312345.123456788").unwrap();
        for (field, value) in [
            ("LoadoutPresets", json!([])),
            ("LoadoutPresets", json!([renamed])),
            ("LoadoutPresets", json!([changed_unknown])),
            ("GoalPresets", json!([])),
            ("GoalPresets", json!([{"Name": "Changed", "GearGoal": 2}])),
            ("ActiveGoalPresetIndex", json!(1)),
        ] {
            let mut proposed = loaded.clone();
            proposed[field] = value;
            proposed["TimeTriggerSeconds"] = json!(7200);
            let error = tauri::async_runtime::block_on(save_plan(
                runtime_dir.clone(),
                "provenance".into(),
                proposed.to_string(),
            ))
            .unwrap_err();
            assert!(
                error.contains(&format!("{field} is historical provenance")),
                "{error}"
            );
            assert_eq!(fs::read(&path).unwrap(), before);
        }
        assert!(!dir.path().join("presets.json").exists());
        assert!(!dir.path().join("settings.json").exists());
    }

    #[test]
    fn offline_plan_provenance_allows_retired_ngu_normalization_only() {
        let dir = tempfile::tempdir().unwrap();
        write_json_atomic(
            &dir.path().join("settings.json"),
            &global_ngu_tests::globals(),
            false,
        )
        .unwrap();
        let path = plan_path(dir.path(), "legacy").unwrap();
        let mut row = preset();
        row["EnergyNguWeights"] = json!(vec![1; 9]);
        row["MagicNguWeights"] = json!(vec![1; 7]);
        row["Future"] = serde_json::from_str("12345678901234567890.123456789").unwrap();
        let source = json!({"Version": 16, "LoadoutPresets": [row],
            "GoalPresets": [{"Name": "Legacy", "GearGoal": 2, "EnergyNguWeights": [1], "MagicNguWeights": [1], "Future": row["Future"]}],
            "ActiveGoalPresetIndex": 0});
        write_json_atomic(&path, &source, false).unwrap();
        let mut plan: Value = serde_json::from_str(&read_plan_json(&path).unwrap()).unwrap();
        let revision = take_plan_revision(&mut plan).unwrap().unwrap();
        plan["TimeTriggerSeconds"] = json!(7200);
        save_plan_offline(dir.path(), "legacy", &plan, Some(&revision)).unwrap();
        let saved = read_json_object(&path, MAX_JSON_BYTES, false).unwrap();
        assert_eq!(saved["TimeTriggerSeconds"], 7200);
        assert_eq!(saved["ActiveGoalPresetIndex"], 0);
        for field in ["LoadoutPresets", "GoalPresets"] {
            assert_eq!(saved[field], plan[field]);
            assert_eq!(
                saved[field][0]["Future"].to_string(),
                "12345678901234567890.123456789"
            );
            assert!(saved[field][0].get("EnergyNguWeights").is_none());
            assert!(saved[field][0].get("MagicNguWeights").is_none());
        }
    }

    #[test]
    fn offline_copy_writer_preserves_preset_unknown_fields() {
        let dir = tempfile::tempdir().unwrap();
        let runtime_dir = dir.path().to_string_lossy().into_owned();
        let mut source = default_plan();
        source["LoadoutPresets"] = json!([preset()]);
        source["LoadoutPresets"][0]["Future"] =
            serde_json::from_str(r#"{"exact":9007199254740993}"#).unwrap();
        let name = tauri::async_runtime::block_on(create_plan(
            runtime_dir,
            "copy".into(),
            Some(source.to_string()),
        ))
        .unwrap();
        let copied: Value =
            serde_json::from_str(&read_plan_json(&plan_path(dir.path(), &name).unwrap()).unwrap())
                .unwrap();
        assert_eq!(copied["LoadoutPresets"], source["LoadoutPresets"]);
        assert_eq!(copied["Version"], 18);
    }

    #[test]
    fn offline_writer_rejects_fractional_preset_settings_without_modifying_file() {
        let dir = tempfile::tempdir().unwrap();
        let runtime_dir = dir.path().to_string_lossy().into_owned();
        let mut source = default_plan();
        source["LoadoutPresets"] = json!([preset()]);
        tauri::async_runtime::block_on(save_plan(
            runtime_dir.clone(),
            "valid".into(),
            source.to_string(),
        ))
        .unwrap();
        let path = plan_path(dir.path(), "valid").unwrap();
        let bytes = fs::read(&path).unwrap();
        let read: Value = serde_json::from_str(&read_plan_json(&path).unwrap()).unwrap();
        for key in ["DiggerBudgetPercent", "SwapSeconds"] {
            let mut invalid = read.clone();
            invalid["LoadoutPresets"][0][key] = json!(1.5);
            assert!(tauri::async_runtime::block_on(save_plan(
                runtime_dir.clone(),
                "valid".into(),
                invalid.to_string()
            ))
            .unwrap_err()
            .contains(key));
            assert_eq!(fs::read(&path).unwrap(), bytes);
        }
    }
}

#[cfg(test)]
mod max_itopod_push_transport_tests {
    use super::*;

    fn runtime() -> Value {
        json!({"externalUiEnabled": true, "status": "running", "updatedAt": Utc::now().to_rfc3339(),
            "protocolVersion": 3, "supportedCommands": ["startMaxItopodPush", "cancelMaxItopodPush"]})
    }

    #[test]
    fn max_itopod_push_accepts_exact_start_and_cancel_contracts() {
        for deaths in [1, 3, 1000] {
            validate_command("startMaxItopodPush", &json!({"deaths": deaths})).unwrap();
        }
        validate_command("cancelMaxItopodPush", &json!({})).unwrap();
        assert_eq!(protocol_info()["maxItopodPush"]["connectedOnly"], true);
    }

    #[test]
    fn max_itopod_push_rejects_invalid_args_before_queueing() {
        let dir = tempfile::tempdir().unwrap();
        write_json_atomic(&dir.path().join("runtime.json"), &runtime(), true).unwrap();
        let mut invalid = vec![
            Value::Null,
            json!([]),
            json!({}),
            json!({"deaths": 3, "extra": true}),
        ];
        for deaths in [
            Value::Null,
            json!(true),
            json!("3"),
            json!(0),
            json!(-1),
            json!(1.5),
            json!(1001),
            json!([]),
            json!({}),
            serde_json::from_str("1.0000000000000001").unwrap(),
        ] {
            invalid.push(json!({"deaths": deaths}));
        }
        for args in invalid {
            assert!(queue_command(dir.path(), "startMaxItopodPush", args).is_err());
        }
        for args in [
            Value::Null,
            json!([]),
            json!({"deaths": 3}),
            json!({"extra": true}),
        ] {
            assert!(queue_command(dir.path(), "cancelMaxItopodPush", args).is_err());
        }
        assert!(!dir.path().join("commands").exists());
    }

    #[test]
    fn max_itopod_push_queues_exact_payloads_without_changing_saved_settings() {
        let dir = tempfile::tempdir().unwrap();
        let settings_path = dir.path().join("settings.json");
        let settings = b"{\"_customAdventureMode\":1,\"_customAdventureZone\":12,\"_manualAdventureZone\":4,\"Future\":123}";
        write_atomic(&settings_path, settings, true).unwrap();
        write_json_atomic(&dir.path().join("runtime.json"), &runtime(), true).unwrap();
        for (command, args) in [
            ("startMaxItopodPush", json!({"deaths": 3})),
            ("cancelMaxItopodPush", json!({})),
        ] {
            let id = queue_command(dir.path(), command, args.clone()).unwrap();
            validate_command_id(&id).unwrap();
            let payload = read_json_object(
                &dir.path().join("commands").join(format!("{id}.json")),
                MAX_COMMAND_BYTES,
                false,
            )
            .unwrap();
            assert_eq!(payload["command"], command);
            assert_eq!(payload["args"], args);
            assert_eq!(fs::read(&settings_path).unwrap(), settings);
        }
    }

    #[test]
    fn max_itopod_push_requires_fresh_live_advertised_commands_without_offline_fallback() {
        let dir = tempfile::tempdir().unwrap();
        for (command, args) in [
            ("startMaxItopodPush", json!({"deaths": 3})),
            ("cancelMaxItopodPush", json!({})),
        ] {
            for (key, value, expected) in [
                ("externalUiEnabled", json!(false), "disconnected"),
                ("updatedAt", json!("2000-01-01T00:00:00Z"), "stale"),
                ("status", json!("unloaded"), "disconnected"),
                ("protocolVersion", json!(2), "protocolVersion"),
                ("supportedCommands", json!([]), "does not advertise"),
            ] {
                let mut state = runtime();
                state[key] = value;
                write_json_atomic(&dir.path().join("runtime.json"), &state, true).unwrap();
                let error = queue_command(dir.path(), command, args.clone()).unwrap_err();
                assert!(error.contains(expected), "{error}");
                assert!(!dir.path().join("commands").exists());
                assert!(!dir.path().join("settings.json").exists());
            }
        }
    }
}

#[cfg(test)]
mod stage_override_transport_tests {
    use super::*;

    fn args() -> Value {
        json!({"name": "selected", "expectedRevision": "Opaque-Revision:17",
            "expectedEntry": "Epoch:7/Entry:23", "action": "set", "stageIndex": 0})
    }

    fn runtime() -> Value {
        json!({"externalUiEnabled": true, "status": "running", "updatedAt": Utc::now().to_rfc3339(),
            "protocolVersion": 3, "planVersion": 18, "fightScheduleVersion": 1, "stageTimingVersion": 1, "stageOverridesV1": true, "globalNguWeightsVersion": 1,
            "settingsCasVersion": 1, "supportedCommands": ["setPlanStage", "applySettingsPatch", "status", "quickSave", "unload", "stopFocus"]})
    }

    #[test]
    fn override_contract_uses_generic_connected_command_and_accepts_all_actions_and_int32_bounds() {
        let contract = protocol_info()["manualStageOverride"].clone();
        assert_eq!(contract["command"], "setPlanStage");
        assert_eq!(contract["transport"], "enqueue_command");
        assert_eq!(contract["connectedOnly"], true);
        assert_eq!(contract["requiredRuntimeFields"]["stageTimingVersion"], 1);
        assert_eq!(contract["requiredRuntimeFields"]["planVersion"], ">= 18");
        assert_eq!(contract["requiredRuntimeFields"]["fightScheduleVersion"], 1);
        assert_eq!(contract["requiredRuntimeFields"]["stageOverridesV1"], true);
        for action in ["skip", "set", "restart"] {
            for index in [0, 1, i32::MAX] {
                let mut request = args();
                request["action"] = json!(action);
                request["stageIndex"] = json!(index);
                let before = request.clone();
                validate_command("setPlanStage", &request).unwrap();
                assert_eq!(request, before);
            }
        }
    }

    #[test]
    fn override_rejects_missing_extra_and_malformed_args_before_queueing() {
        let dir = tempfile::tempdir().unwrap();
        let mut invalid = vec![Value::Null, json!([]), json!(1), json!("args"), json!({})];
        for field in [
            "name",
            "expectedRevision",
            "expectedEntry",
            "action",
            "stageIndex",
        ] {
            let mut missing = args();
            missing.as_object_mut().unwrap().remove(field);
            invalid.push(missing);
        }
        for field in ["unexpected", "plan", "ProtectedStageTiming"] {
            let mut extra = args();
            extra[field] = json!(true);
            invalid.push(extra);
        }
        for field in ["name", "expectedRevision", "expectedEntry", "action"] {
            for value in [
                Value::Null,
                json!(true),
                json!(0),
                json!([]),
                json!({}),
                json!(""),
                json!(" "),
            ] {
                let mut request = args();
                request[field] = value;
                invalid.push(request);
            }
        }
        for name in ["../escape", "a/b", "a\\b", "CON", "plan.", " plan", "plan "] {
            let mut request = args();
            request["name"] = json!(name);
            invalid.push(request);
        }
        for action in ["Skip", "SET", "restart ", "previous", "jump"] {
            let mut request = args();
            request["action"] = json!(action);
            invalid.push(request);
        }
        for index in [
            Value::Null,
            json!(true),
            json!("0"),
            json!(-1),
            json!(0.5),
            json!(2147483648_u64),
            json!([]),
            json!({}),
            serde_json::from_str("1.0000000000000001").unwrap(),
        ] {
            let mut request = args();
            request["stageIndex"] = index;
            invalid.push(request);
        }
        for request in invalid {
            let error = validate_command("setPlanStage", &request).unwrap_err();
            assert_eq!(
                queue_command(dir.path(), "setPlanStage", request).unwrap_err(),
                error
            );
            assert!(!dir.path().join("commands").exists());
        }
    }

    #[test]
    fn override_requires_all_three_fresh_capabilities_and_advertised_command() {
        let dir = tempfile::tempdir().unwrap();
        let path = dir.path().join("runtime.json");
        for overrides in [
            None,
            Some(Value::Null),
            Some(json!(false)),
            Some(json!("true")),
            Some(json!(1)),
            Some(json!(true)),
        ] {
            for timing in [
                None,
                Some(Value::Null),
                Some(json!(true)),
                Some(json!("1")),
                Some(json!(0)),
                Some(json!(1)),
            ] {
                for version in [
                    None,
                    Some(Value::Null),
                    Some(json!("17")),
                    Some(json!(16)),
                    Some(json!(17)),
                    Some(json!(18)),
                ] {
                    let mut state = runtime();
                    for (key, value) in [
                        ("stageOverridesV1", &overrides),
                        ("stageTimingVersion", &timing),
                        ("planVersion", &version),
                    ] {
                        state.as_object_mut().unwrap().remove(key);
                        if let Some(value) = value {
                            state[key] = value.clone();
                        }
                    }
                    write_json_atomic(&path, &state, true).unwrap();
                    let supported = state["stageOverridesV1"].as_bool() == Some(true)
                        && state["stageTimingVersion"].as_u64() == Some(1)
                        && state["planVersion"].as_u64().unwrap_or(0) >= 18;
                    let result = queue_command(dir.path(), "setPlanStage", args());
                    if supported {
                        let id = result.unwrap();
                        remove_if_present(&dir.path().join("commands").join(format!("{id}.json")))
                            .unwrap();
                    } else {
                        let error = result.unwrap_err();
                        assert!(
                            error.contains("runtime.stageTimingVersion")
                                || error.contains("runtime.stageOverridesV1"),
                            "{error}"
                        );
                    }
                    assert!(list_json_stems(&dir.path().join("commands"))
                        .unwrap()
                        .is_empty());
                }
            }
        }
        for commands in [
            Value::Null,
            json!([]),
            json!(["status"]),
            json!("setPlanStage"),
        ] {
            let mut state = runtime();
            state["supportedCommands"] = commands;
            write_json_atomic(&path, &state, true).unwrap();
            assert!(queue_command(dir.path(), "setPlanStage", args())
                .unwrap_err()
                .contains("does not advertise support for setPlanStage"));
        }
    }

    #[test]
    fn disconnected_stale_and_unready_overrides_never_fall_back_to_offline_edits() {
        let dir = tempfile::tempdir().unwrap();
        let paths = [
            dir.path().join("settings.json"),
            plan_path(dir.path(), "selected").unwrap(),
            dir.path().join(format!(
                "stage-progress-{}.json",
                file_revision(b"selected")
            )),
        ];
        for path in &paths {
            write_atomic(path, br#"{"keep":"exact bytes"}"#, false).unwrap();
        }
        let runtime_path = dir.path().join("runtime.json");
        let mut states = vec![None];
        for (field, value) in [
            (
                "updatedAt",
                json!((Utc::now() - chrono::Duration::seconds(10)).to_rfc3339()),
            ),
            (
                "updatedAt",
                json!((Utc::now() + chrono::Duration::seconds(10)).to_rfc3339()),
            ),
            ("externalUiEnabled", json!(false)),
            ("status", json!("starting")),
            ("status", json!("unloaded")),
            ("protocolVersion", json!(2)),
            ("stageOverridesV1", json!(false)),
        ] {
            let mut state = runtime();
            state[field] = value;
            states.push(Some(state));
        }
        for state in states {
            if let Some(state) = state {
                write_json_atomic(&runtime_path, &state, true).unwrap();
            }
            let envelope: CommandEnvelope =
                serde_json::from_value(json!({"command": "setPlanStage", "args": args()})).unwrap();
            assert!(enqueue_command(dir.path().to_string_lossy().into_owned(), envelope).is_err());
            assert!(!dir.path().join("commands").exists());
            assert!(!dir.path().join(OWNERSHIP_FILE).exists());
            assert!(!dir.path().join(".external-ui-write.lock").exists());
            for path in &paths {
                assert_eq!(fs::read(path).unwrap(), br#"{"keep":"exact bytes"}"#);
            }
        }
        write_atomic(&runtime_path, b"{broken", true).unwrap();
        let envelope: CommandEnvelope =
            serde_json::from_value(json!({"command": "setPlanStage", "args": args()})).unwrap();
        assert!(enqueue_command(dir.path().to_string_lossy().into_owned(), envelope).is_err());
        assert!(!dir.path().join("commands").exists());
        for path in &paths {
            assert_eq!(fs::read(path).unwrap(), br#"{"keep":"exact bytes"}"#);
        }
    }

    #[test]
    fn override_envelope_preserves_guards_has_uuid_and_expiry_and_retains_runtime_result() {
        let dir = tempfile::tempdir().unwrap();
        write_json_atomic(&dir.path().join("runtime.json"), &runtime(), false).unwrap();
        for action in ["skip", "set", "restart"] {
            let mut request = args();
            request["action"] = json!(action);
            request["expectedRevision"] = json!("ExactCase:Revision");
            request["expectedEntry"] = json!("ExactCase:Entry/42");
            let envelope: CommandEnvelope =
                serde_json::from_value(json!({"command": "setPlanStage", "args": request}))
                    .unwrap();
            let id = queue_command(dir.path(), &envelope.command, envelope.args).unwrap();
            validate_command_id(&id).unwrap();
            let payload = read_json_object(
                &dir.path().join("commands").join(format!("{id}.json")),
                MAX_COMMAND_BYTES,
                false,
            )
            .unwrap();
            assert_eq!(payload["id"], id);
            assert_eq!(payload["command"], "setPlanStage");
            assert_eq!(payload["args"], request);
            let created =
                DateTime::parse_from_rfc3339(payload["createdAt"].as_str().unwrap()).unwrap();
            let expires =
                DateTime::parse_from_rfc3339(payload["expiresAt"].as_str().unwrap()).unwrap();
            assert_eq!(
                (expires - created).num_milliseconds(),
                COMMAND_TIMEOUT_MS as i64
            );
            let data = json!({"runtime": {"planProgress": {"stageIndex": 2, "entry": "next-entry"}}, "action": action});
            write_json_atomic(&dir.path().join("command-results").join(format!("{id}.json")),
                &json!({"id": id, "command": "setPlanStage", "success": true, "message": "stage overridden",
                    "completedAt": Utc::now().to_rfc3339(), "data": data}), false).unwrap();
            let response =
                wait_for_command(dir.path(), &id, "setPlanStage", Instant::now()).unwrap();
            assert_eq!(response["mode"], "connected");
            assert_eq!(response["result"]["data"], data);
        }
        let mut state = runtime();
        state["maxCommandBytes"] = json!(1);
        write_json_atomic(&dir.path().join("runtime.json"), &state, true).unwrap();
        assert!(queue_command(dir.path(), "setPlanStage", args())
            .unwrap_err()
            .contains("byte bridge limit"));
        assert!(list_json_stems(&dir.path().join("commands"))
            .unwrap()
            .is_empty());
    }

    #[test]
    fn absent_override_capability_never_blocks_disable_unload_or_quicksave() {
        let dir = tempfile::tempdir().unwrap();
        let mut state = runtime();
        for field in [
            "stageOverridesV1",
            "stageTimingVersion",
            "fightScheduleVersion",
            "planVersion",
        ] {
            state.as_object_mut().unwrap().remove(field);
        }
        write_json_atomic(&dir.path().join("runtime.json"), &state, false).unwrap();
        assert!(queue_command(dir.path(), "setPlanStage", args()).is_err());
        for (command, request) in [
            ("status", json!({})),
            ("quickSave", json!({})),
            ("unload", json!({})),
            ("stopFocus", json!({})),
            (
                "applySettingsPatch",
                json!({"patch": {"_useCustomRebirthPlan": false, "_autoRebirth": false}, "expectedRevision":""}),
            ),
        ] {
            queue_command(dir.path(), command, request).unwrap();
        }
    }
}

#[cfg(test)]
mod stage_timing_tests {
    use super::*;

    #[test]
    fn obsolete_flag_cannot_change_timing_and_is_removed_on_normalized_reads_copies_and_saves() {
        for flag in [
            None,
            Some(json!(false)),
            Some(json!(true)),
            Some(json!("obsolete")),
            Some(Value::Null),
        ] {
            let mut source =
                json!({"Version": 14, "Future": {"keep": true}, "Stages": [{"FutureRow": 42}]});
            if let Some(flag) = flag {
                source["ProtectedStageTiming"] = flag;
            }
            validate_plan_durations(&source).unwrap();
            let copy = prepare_plan_copy(source.clone(), "copy").unwrap();
            let merged = merge_plan(source.clone(), &json!({"Version": 14}), "merged").unwrap();
            for plan in [copy, merged] {
                assert!(plan.get("ProtectedStageTiming").is_none());
                assert_eq!(plan["Future"], source["Future"]);
                assert_eq!(
                    plan["Stages"],
                    normalize_offline_plan(source.clone()).unwrap()["Stages"]
                );
                assert_eq!(plan["Version"], 18);
            }
        }
    }

    #[test]
    fn budgets_allow_zero_flex_and_ignore_dormant_fixed_and_at_weights() {
        for fixed in [100, 101] {
            for time_trigger in [false, true] {
                let mut plan = json!({"Version": 14, "ProtectedStageTiming": true,
                    "StageDurationSeconds": 100, "TimeTriggerEnabled": time_trigger, "TimeTriggerSeconds": 50,
                    "Stages": [{"DurationSeconds": fixed, "DurationUnit": "%", "Percent": 1e308},
                        {"DurationSeconds": 0, "DurationUnit": "h", "Percent": 1},
                        {"CompletionMode": 1, "DurationSeconds": i32::MAX, "Percent": 1e308}]});
                let before = plan.clone();
                validate_plan_durations(&plan).unwrap();
                assert_eq!(plan, before);
                plan["ProtectedStageTiming"] = json!(false);
                validate_plan_durations(&plan).unwrap();
            }
        }
        let mut plan = json!({"ProtectedStageTiming": true, "Stages": [{"Percent": 1e308}, {"Percent": 1e308}]});
        assert!(validate_plan_durations(&plan)
            .unwrap_err()
            .contains("finite total"));
        plan["Stages"] = json!([{"DurationSeconds": i32::MAX}, {"DurationSeconds": 1}]);
        assert!(validate_plan_durations(&plan)
            .unwrap_err()
            .contains("Total fixed"));
    }

    #[test]
    fn zero_budget_survives_offline_round_trips_without_an_opt_in() {
        let dir = tempfile::tempdir().unwrap();
        let runtime_dir = dir.path().to_string_lossy().into_owned();
        for trigger_enabled in [false, true] {
            for trigger_seconds in [0, 50] {
                for stages in [
                    json!([{"Percent": 1}, {"Percent": 3}]),
                    json!([{"DurationSeconds": 100}, {"Percent": 3}]),
                    json!([{"DurationSeconds": 100}, {"CompletionMode": 1}]),
                ] {
                    let plan = json!({"Version": 15, "ProtectedStageTiming": false,
                        "StageDurationSeconds": 0, "TimeTriggerEnabled": trigger_enabled,
                        "TimeTriggerSeconds": trigger_seconds, "Stages": stages, "Future": {"keep": true}});
                    validate_plan_durations(&plan).unwrap();
                    validate_command("savePlan", &json!({"name": "zero", "plan": plan})).unwrap();
                    validate_command(
                        "createPlan",
                        &json!({"requestedName": "zero", "source": plan}),
                    )
                    .unwrap();
                    let name = tauri::async_runtime::block_on(create_plan(
                        runtime_dir.clone(),
                        "zero".into(),
                        Some(plan.to_string()),
                    ))
                    .unwrap();
                    let path = plan_path(dir.path(), &name).unwrap();
                    let bytes = fs::read(&path).unwrap();
                    save_plan_offline(
                        dir.path(),
                        &name,
                        &json!({"Version": 17, "StageDurationSeconds": 0}),
                        Some(&file_revision(&bytes)),
                    )
                    .unwrap();
                    let saved = read_json_object(&path, MAX_JSON_BYTES, false).unwrap();
                    assert_eq!(saved["StageDurationSeconds"], 0);
                    assert!(saved.get("ProtectedStageTiming").is_none());
                    assert_eq!(saved["Version"], 18);
                    assert_eq!(
                        saved["Stages"],
                        normalize_offline_plan(plan.clone()).unwrap()["Stages"]
                    );
                    assert_eq!(saved["Future"], plan["Future"]);
                    for legacy in [
                        json!({"StageDurationSeconds": 0}),
                        json!({"StageDurationSeconds": 0, "ProtectedStageTiming": false}),
                    ] {
                        validate_plan_durations(&legacy).unwrap();
                    }
                }
            }
        }
        for budget in [
            json!(-1),
            json!(0.5),
            json!(2147483648_u64),
            json!(null),
            json!("0"),
            json!(false),
        ] {
            assert!(validate_plan_durations(
                &json!({"ProtectedStageTiming": true, "StageDurationSeconds": budget})
            )
            .unwrap_err()
            .contains("non-negative Int32"));
        }
    }

    #[test]
    fn offline_defaults_and_old_copies_normalize_to_17_preserving_unknowns_and_saved_horizon() {
        let dir = tempfile::tempdir().unwrap();
        let runtime_dir = dir.path().to_string_lossy().into_owned();
        let name =
            tauri::async_runtime::block_on(create_plan(runtime_dir.clone(), "new".into(), None))
                .unwrap();
        let plan = read_json_object(
            &plan_path(dir.path(), &name).unwrap(),
            MAX_JSON_BYTES,
            false,
        )
        .unwrap();
        assert_eq!(plan["Version"], 18);
        assert!(plan.get("ProtectedStageTiming").is_none());
        assert_eq!(protocol_info()["stageTimingVersion"], 1);
        for flag in [None, Some(false), Some(true)] {
            let mut source = parse_json_object(br#"{"Version":14,"StageDurationSeconds":100,"Stages":[{"DurationSeconds":200,"FutureRow":18446744073709551616123456789}],"Future":1.2300e+42}"#, "source").unwrap();
            if let Some(flag) = flag {
                source["ProtectedStageTiming"] = json!(flag);
            }
            let name = tauri::async_runtime::block_on(create_plan(
                runtime_dir.clone(),
                "copy".into(),
                Some(source.to_string()),
            ))
            .unwrap();
            let path = plan_path(dir.path(), &name).unwrap();
            let bytes = fs::read(&path).unwrap();
            save_plan_offline(
                dir.path(),
                &name,
                &json!({"Version": 17, "TimeTriggerSeconds": 50}),
                Some(&file_revision(&bytes)),
            )
            .unwrap();
            let saved = read_json_object(&path, MAX_JSON_BYTES, false).unwrap();
            assert!(saved.get("ProtectedStageTiming").is_none());
            assert_eq!(saved["Version"], 18);
            assert_eq!(saved["StageDurationSeconds"], 100);
            assert_eq!(saved["Future"].to_string(), "1.2300e+42");
            assert_eq!(
                saved["Stages"],
                normalize_offline_plan(source.clone()).unwrap()["Stages"]
            );
        }
        let bad = json!({"Version": 14, "StageDurationSeconds": -1});
        assert!(tauri::async_runtime::block_on(create_plan(
            runtime_dir,
            "bad".into(),
            Some(bad.to_string())
        ))
        .is_err());
        assert!(save_plan_offline(dir.path(), "bad", &bad, None).is_err());
        assert!(!plan_path(dir.path(), "bad").unwrap().exists());
    }

    #[test]
    fn offline_saves_and_obsolete_flag_cleanup_preserve_existing_checkpoints() {
        let dir = tempfile::tempdir().unwrap();
        let name = "MixedCase";
        let path = plan_path(dir.path(), name).unwrap();
        let checkpoint = dir.path().join(format!(
            "stage-progress-{}.json",
            file_revision(b"mixedcase")
        ));
        let other = dir
            .path()
            .join(format!("stage-progress-{}.json", file_revision(b"other")));
        let initial = json!({"Version": 15, "ProtectedStageTiming": true, "StageDurationSeconds": 100,
            "Stages": [{"DurationSeconds": 20}, {"Percent": 1}]});
        write_json_atomic(&path, &initial, false).unwrap();
        write_json_atomic(
            &dir.path().join("settings.json"),
            &json!({"_customPlanFile": "other", "_energyNguWeights": [0,0,0,0,1,0,1,0,0], "_magicNguWeights": [1,0,0,0,0,0,0]}),
            false,
        )
        .unwrap();
        write_atomic(&checkpoint, b"old progress", false).unwrap();
        write_atomic(&other, b"other progress", false).unwrap();
        for patch in [
            json!({"Version": 17, "Name": "cosmetic"}),
            json!({"Version": 17, "StageDurationSeconds": 200}),
            json!({"Version": 17, "Stages": [{"DurationSeconds": 30}, {"Percent": 3}]}),
        ] {
            let revision = file_revision(&fs::read(&path).unwrap());
            let mut plan = normalize_offline_plan(initial.clone()).unwrap();
            merge_object_patch(&mut plan, &patch).unwrap();
            save_plan_offline(dir.path(), name, &plan, Some(&revision)).unwrap();
            assert_eq!(fs::read(&checkpoint).unwrap(), b"old progress");
        }
        for obsolete in [false, true] {
            let revision = file_revision(&fs::read(&path).unwrap());
            let mut plan = read_json_object(&path, MAX_JSON_BYTES, false).unwrap();
            plan["ProtectedStageTiming"] = json!(obsolete);
            save_plan_offline(dir.path(), name, &plan, Some(&revision)).unwrap();
            assert!(read_json_object(&path, MAX_JSON_BYTES, false)
                .unwrap()
                .get("ProtectedStageTiming")
                .is_none());
            assert_eq!(fs::read(&checkpoint).unwrap(), b"old progress");
            assert_eq!(fs::read(&other).unwrap(), b"other progress");
        }
        write_atomic(&checkpoint, b"source progress", true).unwrap();
        let source = read_json_object(&path, MAX_JSON_BYTES, false).unwrap();
        tauri::async_runtime::block_on(create_plan(
            dir.path().to_string_lossy().into_owned(),
            "copy".into(),
            Some(source.to_string()),
        ))
        .unwrap();
        assert_eq!(fs::read(&checkpoint).unwrap(), b"source progress");

        let legacy_path = plan_path(dir.path(), "legacy").unwrap();
        let legacy_checkpoint = dir
            .path()
            .join(format!("stage-progress-{}.json", file_revision(b"legacy")));
        write_json_atomic(&legacy_path, &json!({"Version": 15}), false).unwrap();
        write_atomic(&legacy_checkpoint, b"legacy progress", false).unwrap();
        let revision = file_revision(&fs::read(&legacy_path).unwrap());
        save_plan_offline(
            dir.path(),
            "legacy",
            &json!({"Version": 15, "ProtectedStageTiming": false}),
            Some(&revision),
        )
        .unwrap();
        assert!(legacy_checkpoint.exists());
        let revision = file_revision(&fs::read(&legacy_path).unwrap());
        save_plan_offline(
            dir.path(),
            "legacy",
            &normalize_offline_plan(json!({"Version": 15, "ProtectedStageTiming": true})).unwrap(),
            Some(&revision),
        )
        .unwrap();
        assert_eq!(fs::read(&legacy_checkpoint).unwrap(), b"legacy progress");
    }

    #[test]
    fn new_offline_plans_and_recreated_copies_clear_only_destination_progress() {
        let dir = tempfile::tempdir().unwrap();
        let runtime_dir = dir.path().to_string_lossy().into_owned();
        let name = "ReCreated";
        let checkpoint = dir.path().join(format!(
            "stage-progress-{}.json",
            file_revision(b"recreated")
        ));
        let source_checkpoint = dir
            .path()
            .join(format!("stage-progress-{}.json", file_revision(b"source")));
        let completed = br#"{"completed":true,"stageIndex":1}"#;
        let source_name =
            tauri::async_runtime::block_on(create_plan(runtime_dir.clone(), "source".into(), None))
                .unwrap();
        let source = read_json_object(
            &plan_path(dir.path(), &source_name).unwrap(),
            MAX_JSON_BYTES,
            false,
        )
        .unwrap();
        write_atomic(&source_checkpoint, completed, false).unwrap();

        write_atomic(&checkpoint, completed, false).unwrap();
        assert_eq!(
            tauri::async_runtime::block_on(create_plan(runtime_dir.clone(), name.into(), None))
                .unwrap(),
            name
        );
        assert!(!checkpoint.exists());
        for copy in [false, true] {
            write_atomic(&checkpoint, completed, false).unwrap();
            delete_plan_offline(dir.path(), name).unwrap();
            assert!(!plan_path(dir.path(), name).unwrap().exists());
            assert!(!checkpoint.exists());
            // An orphan may also predate this version's deletion cleanup.
            write_atomic(&checkpoint, completed, false).unwrap();
            if copy {
                assert_eq!(
                    tauri::async_runtime::block_on(create_plan(
                        runtime_dir.clone(),
                        name.into(),
                        Some(source.to_string())
                    ))
                    .unwrap(),
                    name
                );
            } else {
                save_plan_offline(dir.path(), name, &source, None).unwrap();
            }
            assert!(!checkpoint.exists());
            assert_eq!(fs::read(&source_checkpoint).unwrap(), completed);
        }

        write_atomic(&checkpoint, completed, false).unwrap();
        let collision_checkpoint = dir.path().join(format!(
            "stage-progress-{}.json",
            file_revision(b"recreated-2")
        ));
        write_atomic(&collision_checkpoint, completed, false).unwrap();
        let collision = tauri::async_runtime::block_on(create_plan(
            runtime_dir,
            name.into(),
            Some(source.to_string()),
        ))
        .unwrap();
        assert_eq!(collision, "ReCreated-2");
        assert!(!collision_checkpoint.exists());
        assert_eq!(fs::read(&checkpoint).unwrap(), completed);

        write_json_atomic(
            &dir.path().join("settings.json"),
            &json!({"_customPlanFile": name, "_useCustomRebirthPlan": true}),
            false,
        )
        .unwrap();
        assert!(delete_plan_offline(dir.path(), name).is_err());
        assert!(plan_path(dir.path(), name).unwrap().exists());
        assert_eq!(fs::read(&checkpoint).unwrap(), completed);
        write_json_atomic(
            &dir.path().join("settings.json"),
            &json!({"_customPlanFile": name, "_useCustomRebirthPlan": false}),
            true,
        )
        .unwrap();
        let fallback_checkpoint = dir
            .path()
            .join(format!("stage-progress-{}.json", file_revision(b"24hr")));
        write_atomic(&fallback_checkpoint, completed, false).unwrap();
        write_json_atomic(
            &plan_path(dir.path(), "24hr").unwrap(),
            &json!({"Version": 17, "Name": "24hr", "TimeTriggerSeconds": 86400,
                "Stages": [{"Name": "NGU", "Percent": 1}]}),
            false,
        )
        .unwrap();
        delete_plan_offline(dir.path(), name).unwrap();
        assert!(!checkpoint.exists());
        assert_eq!(fs::read(&fallback_checkpoint).unwrap(), completed);
        assert!(plan_path(dir.path(), "24hr").unwrap().exists());
        assert_eq!(fs::read(&source_checkpoint).unwrap(), completed);
    }

    #[test]
    fn failed_offline_creations_preserve_progress_and_cleanup_errors_follow_persistence() {
        let dir = tempfile::tempdir().unwrap();
        let path = plan_path(dir.path(), "new").unwrap();
        let checkpoint = dir
            .path()
            .join(format!("stage-progress-{}.json", file_revision(b"new")));
        write_atomic(&checkpoint, b"completed", false).unwrap();
        let mut invalid = default_plan();
        invalid["StageDurationSeconds"] = json!(-1);
        let mut oversized = default_plan();
        oversized["Future"] = json!("x".repeat(MAX_JSON_BYTES as usize));
        for plan in [invalid, oversized] {
            assert!(save_plan_offline(dir.path(), "new", &plan, None).is_err());
            assert!(tauri::async_runtime::block_on(create_plan(
                dir.path().to_string_lossy().into_owned(),
                "new".into(),
                Some(plan.to_string())
            ))
            .is_err());
            assert!(!path.exists());
            assert_eq!(fs::read(&checkpoint).unwrap(), b"completed");
        }
        assert!(save_plan_offline(
            dir.path(),
            "new",
            &default_plan(),
            Some(&file_revision(b"stale"))
        )
        .is_err());
        assert!(!path.exists());
        assert_eq!(fs::read(&checkpoint).unwrap(), b"completed");

        fs::remove_file(&checkpoint).unwrap();
        fs::create_dir(&checkpoint).unwrap();
        for copy in [false, true] {
            let err = if copy {
                tauri::async_runtime::block_on(create_plan(
                    dir.path().to_string_lossy().into_owned(),
                    "new".into(),
                    None,
                ))
                .unwrap_err()
            } else {
                save_plan_offline(dir.path(), "new", &default_plan(), None).unwrap_err()
            };
            assert!(err.contains("Plan file was persisted or deleted"), "{err}");
            assert!(path.exists());
            assert!(checkpoint.is_dir());
            fs::remove_file(&path).unwrap();
        }
        write_json_atomic(&path, &default_plan(), false).unwrap();
        write_json_atomic(
            &plan_path(dir.path(), "other").unwrap(),
            &default_plan(),
            false,
        )
        .unwrap();
        assert!(delete_plan_offline(dir.path(), "new")
            .unwrap_err()
            .contains("Plan file was persisted or deleted"));
        assert!(!path.exists());
        assert!(checkpoint.is_dir());
        // All error exits release ownership; they must not strand the injector lease.
        let _ownership = lock_injector_ownership(dir.path()).unwrap();
    }

    #[test]
    fn created_checkpoint_cleanup_matches_invariant_simple_unicode_casing() {
        let dir = tempfile::tempdir().unwrap();
        let name = "\u{039f}\u{03a3}\u{0130}";
        let lower = "\u{03bf}\u{03c3}\u{0130}";
        let checkpoint = dir.path().join(format!(
            "stage-progress-{}.json",
            file_revision(lower.as_bytes())
        ));
        write_atomic(&checkpoint, b"completed", false).unwrap();
        save_plan_offline(dir.path(), name, &default_plan(), None).unwrap();
        assert!(plan_path(dir.path(), name).unwrap().exists());
        assert!(!checkpoint.exists());
    }

    #[test]
    fn failed_writes_preserve_plan_and_progress_and_saves_ignore_checkpoint_sidecars() {
        let dir = tempfile::tempdir().unwrap();
        write_json_atomic(
            &dir.path().join("settings.json"),
            &global_ngu_tests::globals(),
            false,
        )
        .unwrap();
        let path = plan_path(dir.path(), "mode").unwrap();
        let checkpoint = dir
            .path()
            .join(format!("stage-progress-{}.json", file_revision(b"mode")));
        write_json_atomic(
            &path,
            &json!({"Version": 15, "ProtectedStageTiming": false}),
            false,
        )
        .unwrap();
        write_atomic(&checkpoint, b"progress", false).unwrap();
        let original = fs::read(&path).unwrap();
        let revision = file_revision(&original);
        for (patch, expected) in [
            (
                json!({"Version": 15, "ProtectedStageTiming": true}),
                file_revision(b"stale"),
            ),
            (
                json!({"Version": 15, "ProtectedStageTiming": true, "StageDurationSeconds": -1}),
                revision.clone(),
            ),
            (
                json!({"Version": 15, "ProtectedStageTiming": true, "Future": "x".repeat(MAX_JSON_BYTES as usize)}),
                revision.clone(),
            ),
        ] {
            assert!(save_plan_offline(dir.path(), "mode", &patch, Some(&expected)).is_err());
            assert_eq!(fs::read(&path).unwrap(), original);
            assert_eq!(fs::read(&checkpoint).unwrap(), b"progress");
        }
        fs::remove_file(&checkpoint).unwrap();
        fs::create_dir(&checkpoint).unwrap();
        save_plan_offline(
            dir.path(),
            "mode",
            &json!({"Version": 15, "ProtectedStageTiming": true}),
            Some(&revision),
        )
        .unwrap();
        assert!(read_json_object(&path, MAX_JSON_BYTES, false)
            .unwrap()
            .get("ProtectedStageTiming")
            .is_none());
        assert!(checkpoint.is_dir());
    }

    #[test]
    fn enabling_auto_rebirth_always_checks_capability_and_allows_disabling() {
        let dir = tempfile::tempdir().unwrap();
        let runtime_path = dir.path().join("runtime.json");
        write_json_atomic(
            &plan_path(dir.path(), "protected").unwrap(),
            &json!({"ProtectedStageTiming": true}),
            false,
        )
        .unwrap();
        write_json_atomic(
            &dir.path().join("settings.json"),
            &json!({"_customPlanFile": "protected", "_useCustomRebirthPlan": true}),
            false,
        )
        .unwrap();
        let mut runtime = json!({"externalUiEnabled": true, "status": "running", "protocolVersion": 3,
            "planVersion": 18, "fightScheduleVersion": 1, "globalNguWeightsVersion": 1, "settingsCasVersion": 1, "updatedAt": Utc::now().to_rfc3339(), "supportedCommands": ["applySettingsPatch"]});
        for runtime_settings in [
            json!({}),
            json!({"customPlanFile": "protected", "useCustomRebirthPlan": true}),
        ] {
            runtime["settings"] = runtime_settings;
            for capability in [json!(null), json!(false), json!("1"), json!(true), json!(1)] {
                runtime["stageTimingVersion"] = capability;
                write_json_atomic(&runtime_path, &runtime, true).unwrap();
                let result = queue_command(
                    dir.path(),
                    "applySettingsPatch",
                    json!({"patch": {"_autoRebirth": true}, "expectedRevision":""}),
                );
                if runtime["stageTimingVersion"].as_u64() == Some(1) {
                    let id = result.unwrap();
                    remove_if_present(&dir.path().join("commands").join(format!("{id}.json")))
                        .unwrap();
                } else {
                    assert!(result
                        .unwrap_err()
                        .contains("runtime.stageTimingVersion == 1"));
                }
            }
        }
        runtime["stageTimingVersion"] = json!(0);
        write_json_atomic(&runtime_path, &runtime, true).unwrap();
        for patch in [
            json!({"_autoRebirth": false}),
            json!({"_useCustomRebirthPlan": false}),
        ] {
            queue_command(
                dir.path(),
                "applySettingsPatch",
                json!({"patch": patch, "expectedRevision":""}),
            )
            .unwrap();
        }
        runtime["settings"]["useCustomRebirthPlan"] = json!(false);
        write_json_atomic(&runtime_path, &runtime, true).unwrap();
        queue_command(
            dir.path(),
            "applySettingsPatch",
            json!({"patch": {"_autoRebirth": true}, "expectedRevision":""}),
        )
        .unwrap_err();
    }

    #[test]
    fn live_saves_copies_defaults_selections_and_enables_require_fresh_strict_capability() {
        let dir = tempfile::tempdir().unwrap();
        let runtime_path = dir.path().join("runtime.json");
        let settings_path = dir.path().join("settings.json");
        let protected = json!({"Version": 14, "ProtectedStageTiming": false});
        write_json_atomic(
            &plan_path(dir.path(), "protected").unwrap(),
            &protected,
            false,
        )
        .unwrap();
        write_json_atomic(
            &plan_path(dir.path(), "legacy").unwrap(),
            &json!({"Version": 14}),
            false,
        )
        .unwrap();
        write_json_atomic(
            &settings_path,
            &json!({"_customPlanFile": "legacy", "_useCustomRebirthPlan": false}),
            false,
        )
        .unwrap();
        let settings_before = fs::read(&settings_path).unwrap();
        let mut runtime = json!({"externalUiEnabled": true, "status": "running", "protocolVersion": 3,
            "planVersion": 18, "fightScheduleVersion": 1, "globalNguWeightsVersion": 1, "settings": {"customPlanFile": "protected"},
            "settingsCasVersion": 1, "supportedCommands": ["readPlan", "savePlan", "createPlan", "applySettingsPatch", "status", "quickSave", "unload"]});
        for runtime_version in [Value::Null, json!(16), json!("17"), json!(17), json!(18)] {
            runtime["planVersion"] = runtime_version;
            runtime
                .as_object_mut()
                .unwrap()
                .remove("stageTimingVersion");
            for capability in [
                None,
                Some(json!(null)),
                Some(json!(false)),
                Some(json!("true")),
                Some(json!(true)),
                Some(json!(1)),
            ] {
                if let Some(capability) = capability {
                    runtime["stageTimingVersion"] = capability;
                }
                runtime["updatedAt"] = json!(Utc::now().to_rfc3339());
                write_json_atomic(&runtime_path, &runtime, true).unwrap();
                for (command, args) in [
                    ("readPlan", json!({"name": "protected"})),
                    (
                        "savePlan",
                        json!({"name": "new", "plan": protected, "expectedRevision": ""}),
                    ),
                    (
                        "createPlan",
                        json!({"requestedName": "copy", "source": protected}),
                    ),
                    ("createPlan", json!({"requestedName": "new"})),
                    (
                        "applySettingsPatch",
                        json!({"patch": {"_customPlanFile": "protected"}, "expectedRevision":""}),
                    ),
                    (
                        "applySettingsPatch",
                        json!({"patch": {"_useCustomRebirthPlan": true}, "expectedRevision":""}),
                    ),
                    (
                        "applySettingsPatch",
                        json!({"patch": {"_customPlanFile": "protected", "_useCustomRebirthPlan": false}, "expectedRevision":""}),
                    ),
                ] {
                    let result = queue_command(dir.path(), command, args);
                    if runtime["stageTimingVersion"].as_u64() == Some(1)
                        && runtime["planVersion"].as_u64().unwrap_or(0) >= 18
                    {
                        let id = result.unwrap();
                        remove_if_present(&dir.path().join("commands").join(format!("{id}.json")))
                            .unwrap();
                    } else {
                        let err = result.unwrap_err();
                        assert!(err.contains("runtime.stageTimingVersion == 1"), "{err}");
                        assert!(list_json_stems(&dir.path().join("commands"))
                            .unwrap()
                            .is_empty());
                    }
                }
            }
        }
        runtime["planVersion"] = json!(18);
        runtime["stageTimingVersion"] = json!(0);
        write_json_atomic(&runtime_path, &runtime, true).unwrap();
        for (command, args) in [
            ("readPlan", json!({"name": "protected"})),
            ("savePlan", json!({"name": "new", "plan": {"Version": 14}})),
            (
                "createPlan",
                json!({"requestedName": "copy", "source": {"Version": 14, "ProtectedStageTiming": false}}),
            ),
            (
                "applySettingsPatch",
                json!({"patch": {"_customPlanFile": "legacy", "_useCustomRebirthPlan": true}, "expectedRevision":""}),
            ),
            (
                "applySettingsPatch",
                json!({"patch": {"_autoRebirth": true}, "expectedRevision":""}),
            ),
        ] {
            assert!(queue_command(dir.path(), command, args)
                .unwrap_err()
                .contains("runtime.stageTimingVersion == 1"));
            assert!(list_json_stems(&dir.path().join("commands"))
                .unwrap()
                .is_empty());
        }
        for (command, args) in [
            ("status", json!({})),
            ("quickSave", json!({})),
            ("unload", json!({})),
            (
                "applySettingsPatch",
                json!({"patch": {"_useCustomRebirthPlan": false, "_autoRebirth": false}, "expectedRevision":""}),
            ),
        ] {
            queue_command(dir.path(), command, args).unwrap();
        }
        runtime["stageTimingVersion"] = json!(1);
        runtime["updatedAt"] = json!((Utc::now() - chrono::Duration::seconds(10)).to_rfc3339());
        write_json_atomic(&runtime_path, &runtime, true).unwrap();
        assert!(queue_command(
            dir.path(),
            "savePlan",
            json!({"name": "new", "plan": protected})
        )
        .unwrap_err()
        .contains("heartbeat is stale"));
        assert_eq!(fs::read(&settings_path).unwrap(), settings_before);
        assert_eq!(
            read_json_object(
                &plan_path(dir.path(), "protected").unwrap(),
                MAX_JSON_BYTES,
                false
            )
            .unwrap(),
            protected
        );
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn merge_preserves_unknown_and_nested_fields_without_deleting_null() {
        let mut settings = json!({"known": true, "future": 42, "nested": {"old": 1, "keep": 2}});
        merge_object_patch(
            &mut settings,
            &json!({"known": false, "nested": {"old": 3}, "nullable": null}),
        )
        .unwrap();
        assert_eq!(
            settings,
            json!({"known": false, "future": 42, "nested": {"old": 3, "keep": 2}, "nullable": null})
        );
    }

    #[test]
    fn merge_rejects_nonobjects_without_mutating_target() {
        for bad in [Value::Null, json!([]), json!(12), json!("text")] {
            let mut target = json!({"keep": 1});
            assert!(merge_object_patch(&mut target, &bad).is_err());
            assert_eq!(target, json!({"keep": 1}));
            let mut target = bad.clone();
            assert!(merge_object_patch(&mut target, &json!({"new": 2})).is_err());
            assert_eq!(target, bad);
        }
    }

    #[test]
    fn filenames_reject_traversal_empty_and_windows_devices() {
        for bad in [
            "",
            " ",
            ".",
            "..",
            "../plan",
            "a/b",
            "a\\b",
            "C:plan",
            "plan.",
            "plan ",
            " plan",
            "a\0b",
            "CON",
            "nul.json",
            "COM1",
            "Lpt9.log",
            "CON .json",
            "CONIN$",
            "LPT\u{b2}",
        ] {
            assert!(validate_file_name(bad).is_err(), "accepted {bad:?}");
        }
        assert!(validate_file_name(&"x".repeat(121)).is_err());
        for good in [
            "new-plan",
            "24 Hour Plan",
            "a.b",
            "COM10",
            "console",
            "probe.log",
        ] {
            assert!(validate_file_name(good).is_ok(), "rejected {good:?}");
        }
    }

    #[test]
    fn runtime_requires_recent_enabled_iso_heartbeat() {
        let now = DateTime::parse_from_rfc3339("2026-10-14T12:00:00Z")
            .unwrap()
            .timestamp_millis();
        for (updated, enabled, expected) in [
            ("2026-10-14T12:00:00Z", true, true),
            ("2026-10-14T14:00:00+02:00", true, true),
            ("2026-10-14T11:59:55Z", true, true),
            ("2026-10-14T11:59:54.999Z", true, false),
            ("2026-10-14T12:00:02Z", true, true),
            ("2026-10-14T12:00:02.001Z", true, false),
            ("2026-10-14T12:00:00Z", false, false),
            ("not-a-time", true, false),
        ] {
            assert_eq!(
                runtime_is_recent(
                    &json!({"updatedAt": updated, "externalUiEnabled": enabled, "status": "running"}),
                    now
                ),
                expected
            );
        }
        assert!(!runtime_is_recent(&json!({}), now));
    }

    #[test]
    fn plan_merge_preserves_history_unknown_fields_and_forces_name() {
        let existing = json!({"Version": 14, "Name": "old", "Future": {"keep": 9}, "RebirthHistory": [{"id": 123}], "Stages": [{"Future": 1}]});
        let incoming =
            json!({"Name": "untrusted", "RebirthHistory": [], "Stages": [{"Future": 2}]});
        assert_eq!(
            merge_plan(existing, &incoming, "safe-name").unwrap(),
            json!({
                "Version": 18, "Name": "safe-name", "Future": {"keep": 9}, "RebirthHistory": [{"id": 123}], "Stages": [{"Future": 2, "FightSchedule": null}],
                "AutoIronPillEnabled": true,
            })
        );
        assert!(merge_plan(json!([]), &incoming, "safe-name").is_err());
        assert!(merge_plan(json!({}), &json!(null), "safe-name").is_err());
        assert_eq!(
            merge_plan(
                json!({}),
                &json!({"Version": 14, "RebirthHistory": [1]}),
                "new"
            )
            .unwrap()["RebirthHistory"],
            json!([])
        );
    }

    #[test]
    fn commands_require_allowlist_object_args_and_safe_plan_names() {
        assert!(validate_command("manualRebirth", &json!({})).is_ok());
        assert!(validate_command("unknown", &json!({})).is_err());
        assert!(validate_command("manualRebirth", &json!(null)).is_err());
        assert!(validate_command("manualRebirth", &json!({"unexpected": true})).is_err());
        assert!(validate_command(
            "applySettingsPatch",
            &json!({"patch": {"future": 1}, "expectedRevision":""})
        )
        .is_ok());
        assert!(validate_command(
            "applySettingsPatch",
            &json!({"patch": [], "expectedRevision":""})
        )
        .is_err());
        assert!(validate_command("savePlan", &json!({"name": "../escape", "plan": {}})).is_err());
        assert!(validate_command("savePlan", &json!({"name": "new", "plan": {}})).is_ok());
        assert!(validate_command_id(&Uuid::new_v4().to_string()).is_ok());
        assert!(validate_command_id("../escape").is_err());
        assert!(validate_command_id("00000000-0000-0000-0000-000000000000").is_err());
    }

    #[test]
    fn max_itopod_mode_settings_validate_connected_and_offline() {
        for (key, valid, invalid) in [
            ("_customAdventureMode", vec![0, 1, 2, 3], vec![-1, 4]),
            (
                "_customAdventureMaxItopodDeaths",
                vec![1, 3, 1000],
                vec![-1, 0, 1001, i32::MAX],
            ),
        ] {
            for value in valid {
                let patch = json!({key: value});
                validate_settings_patch(&patch).unwrap();
                validate_offline_settings_patch(&patch).unwrap();
                validate_command(
                    "applySettingsPatch",
                    &json!({"patch": patch, "expectedRevision":""}),
                )
                .unwrap();
            }
            let mut invalid: Vec<Value> = invalid.into_iter().map(|value| json!(value)).collect();
            invalid.extend([
                json!(1.5),
                json!("3"),
                json!(""),
                Value::Null,
                json!(true),
                json!([]),
                json!({}),
                serde_json::from_str("1.0000000000000001").unwrap(),
                serde_json::from_str("1e-400").unwrap(),
            ]);
            for value in invalid {
                let patch = json!({key: value});
                assert!(validate_settings_patch(&patch).is_err(), "accepted {patch}");
                assert!(
                    validate_offline_settings_patch(&patch).is_err(),
                    "accepted offline {patch}"
                );
                assert!(validate_command(
                    "applySettingsPatch",
                    &json!({"patch": patch, "expectedRevision":""})
                )
                .is_err());
            }
        }
    }

    #[test]
    fn max_itopod_mode_offline_defaults_preserve_settings_and_reject_invalid_writes() {
        for deaths in [None, Some(7)] {
            let dir = tempfile::tempdir().unwrap();
            let runtime_dir = dir.path().to_string_lossy().into_owned();
            let path = dir.path().join("settings.json");
            let mut original = json!({"_customAdventureMode": 1, "_customAdventureZone": 12,
                "_manualAdventureZone": 4, "Future": {"counter": 9007199254740993_u64}});
            if let Some(deaths) = deaths {
                original["_customAdventureMaxItopodDeaths"] = json!(deaths);
            }
            write_json_atomic(&path, &original, false).unwrap();
            let before = fs::read(&path).unwrap();
            assert_eq!(
                get_ngui_state(Some(runtime_dir.clone())).unwrap().settings,
                original
            );
            assert_eq!(fs::read(&path).unwrap(), before);
            tauri::async_runtime::block_on(save_settings_patch(
                runtime_dir.clone(),
                json!({"_customAdventureMode": 3}),
            ))
            .unwrap();
            let saved = read_json_object(&path, MAX_JSON_BYTES, false).unwrap();
            assert_eq!(saved["_customAdventureMode"], 3);
            assert_eq!(
                saved["_customAdventureMaxItopodDeaths"],
                deaths.unwrap_or(3)
            );
            for key in ["_customAdventureZone", "_manualAdventureZone", "Future"] {
                assert_eq!(saved[key], original[key]);
            }
            let before = fs::read(&path).unwrap();
            for patch in [
                json!({"_customAdventureMode": 4}),
                json!({"_customAdventureMaxItopodDeaths": 0}),
            ] {
                assert!(tauri::async_runtime::block_on(save_settings_patch(
                    runtime_dir.clone(),
                    patch
                ))
                .is_err());
                assert_eq!(fs::read(&path).unwrap(), before);
            }
            for limit in [1, 1000] {
                tauri::async_runtime::block_on(save_settings_patch(
                    runtime_dir.clone(),
                    json!({"_customAdventureMaxItopodDeaths": limit}),
                ))
                .unwrap();
                let saved = read_json_object(&path, MAX_JSON_BYTES, false).unwrap();
                assert_eq!(saved["_customAdventureMaxItopodDeaths"], limit);
            }
        }
    }

    #[test]
    fn auto_exp_patch_accepts_all_ratios_and_strict_boolean() {
        for enabled in [false, true] {
            let patch = json!({"_customAutoExpEnabled": enabled});
            assert!(validate_settings_patch(&patch).is_ok());
            assert!(validate_offline_settings_patch(&patch).is_ok());
        }
        for (key, default) in AUTO_EXP_RATIO_DEFAULTS {
            for value in [1, default, 1_000_000_000] {
                let patch = json!({key: value});
                assert!(validate_settings_patch(&patch).is_ok(), "rejected {patch}");
                assert!(
                    validate_offline_settings_patch(&patch).is_ok(),
                    "rejected offline {patch}"
                );
            }
        }
    }

    #[test]
    fn auto_exp_patch_rejects_invalid_types_ranges_and_exact_fractions() {
        for value in [
            json!(0),
            json!(1),
            json!("true"),
            Value::Null,
            json!([]),
            json!({}),
        ] {
            let patch = json!({"_customAutoExpEnabled": value});
            assert!(validate_settings_patch(&patch).is_err(), "accepted {patch}");
            assert!(validate_offline_settings_patch(&patch).is_err());
        }
        for (key, _) in AUTO_EXP_RATIO_DEFAULTS {
            for value in [
                json!(0),
                json!(-1),
                json!(1.5),
                json!(1_000_000_001_u64),
                json!("160000"),
                json!("160k"),
                json!(""),
                Value::Null,
                json!(true),
                json!([]),
                json!({}),
                serde_json::from_str("1.0000000000000001").unwrap(),
                serde_json::from_str("1e-400").unwrap(),
            ] {
                let patch = json!({key: value});
                assert!(validate_settings_patch(&patch).is_err(), "accepted {patch}");
                assert!(
                    validate_offline_settings_patch(&patch).is_err(),
                    "accepted offline {patch}"
                );
                assert!(validate_command(
                    "applySettingsPatch",
                    &json!({"patch": patch, "expectedRevision":""})
                )
                .is_err());
            }
        }
    }

    #[test]
    fn auto_exp_offline_new_settings_persist_disabled_defaults() {
        let dir = tempfile::tempdir().unwrap();
        let runtime_dir = dir.path().to_string_lossy().into_owned();
        tauri::async_runtime::block_on(save_settings_patch(runtime_dir, json!({}))).unwrap();
        let settings =
            read_json_object(&dir.path().join("settings.json"), MAX_JSON_BYTES, false).unwrap();
        assert_eq!(
            settings,
            json!({
                "_manualAdventureLoadout": -1, "_manualTitanEnabled": false,
                "_manualTitanZone": 16, "_manualTitanRequireAT": true,
                "_manualTitanLoadout": 1, "_manualTitanMaxDeaths": 3,
                "_customAdventureMaxItopodDeaths": 3,
                "_customAdventureRules": [],
                "_customAutoExpEnabled": false,
                "_manualAdventureGearWeights": vec![0;5], "_manualAdventureBeardWeights": vec![0;7], "_manualAdventureDiggerWeights": vec![0;12],
                "_customExpEnergyRatio": 5, "_customExpMagicRatio": 1,
                "_customExpEnergyPowerRatio": 5, "_customExpEnergyCapRatio": 160000, "_customExpEnergyBarsRatio": 4,
                "_customExpMagicPowerRatio": 5, "_customExpMagicCapRatio": 160000, "_customExpMagicBarsRatio": 4
            })
        );
    }

    #[test]
    fn auto_exp_offline_migration_preserves_existing_values_and_future_fields() {
        let dir = tempfile::tempdir().unwrap();
        let runtime_dir = dir.path().to_string_lossy().into_owned();
        let path = dir.path().join("settings.json");
        let original = json!({"_customExpEnergyRatio": 7, "_customAutoExpEnabled": true, "_manageEnergy": false, "Future": {"counter": 9007199254740993_u64}});
        write_json_atomic(&path, &original, false).unwrap();
        let result = tauri::async_runtime::block_on(save_settings_patch(
            runtime_dir.clone(),
            json!({"_customExpMagicBarsRatio": 12}),
        ))
        .unwrap();
        assert_eq!(result["mode"], "offline");
        let saved = read_json_object(&path, MAX_JSON_BYTES, false).unwrap();
        assert_eq!(
            saved,
            json!({
                "_manualAdventureLoadout": -1, "_manualTitanEnabled": false,
                "_manualTitanZone": 16, "_manualTitanRequireAT": true,
                "_manualTitanLoadout": 1, "_manualTitanMaxDeaths": 3,
                "_customAdventureMaxItopodDeaths": 3,
                "_customAdventureRules": [],
                "_customAutoExpEnabled": true,
                "_manualAdventureGearWeights": vec![0;5], "_manualAdventureBeardWeights": vec![0;7], "_manualAdventureDiggerWeights": vec![0;12],
                "_customExpEnergyRatio": 7, "_customExpMagicRatio": 1,
                "_customExpEnergyPowerRatio": 5, "_customExpEnergyCapRatio": 160000, "_customExpEnergyBarsRatio": 4,
                "_customExpMagicPowerRatio": 5, "_customExpMagicCapRatio": 160000, "_customExpMagicBarsRatio": 12,
                "_manageEnergy": false, "Future": {"counter": 9007199254740993_u64}
            })
        );
        let before = fs::read(&path).unwrap();
        assert!(tauri::async_runtime::block_on(save_settings_patch(
            runtime_dir,
            json!({"_customExpEnergyRatio": 0})
        ))
        .is_err());
        assert_eq!(fs::read(&path).unwrap(), before);
    }

    #[test]
    fn iron_pill_patch_protects_history_and_threshold_bounds() {
        for patch in [
            json!({"_customIronPillSaveSeconds": 0, "_customIronPillMinWorth": 0}),
            json!({"_customIronPillSaveSeconds": 604800, "_customIronPillMinWorth": 1e8}),
            json!({"_customIronPillMinWorth": 0.125}),
        ] {
            assert!(validate_settings_patch(&patch).is_ok(), "rejected {patch}");
            assert!(
                validate_offline_settings_patch(&patch).is_ok(),
                "rejected offline {patch}"
            );
        }
        for patch in [
            json!({"_customIronPillHistory": []}),
            json!({"_customIronPillHistory": null}),
            json!({"_customIronPillSaveSeconds": -1}),
            json!({"_customIronPillSaveSeconds": 604801}),
            json!({"_customIronPillSaveSeconds": 1.5}),
            json!({"_customIronPillSaveSeconds": "900"}),
            json!({"_customIronPillMinWorth": -1}),
            json!({"_customIronPillMinWorth": 100000000.01}),
            json!({"_customIronPillMinWorth": 100000001}),
            json!({"_customIronPillMinWorth": 1e100}),
            json!({"_customIronPillMinWorth": ""}),
            json!({"_customIronPillMinWorth": null}),
        ] {
            assert!(validate_settings_patch(&patch).is_err(), "accepted {patch}");
            assert!(
                validate_offline_settings_patch(&patch).is_err(),
                "accepted offline {patch}"
            );
            assert!(validate_command(
                "applySettingsPatch",
                &json!({"patch": patch, "expectedRevision":""})
            )
            .is_err());
        }
    }

    #[test]
    fn default_directory_is_literal_profile_desktop() {
        let profile = std::env::current_dir().unwrap().join("profile");
        assert_eq!(
            default_runtime_dir(&profile).unwrap(),
            profile.join("Desktop").join("NGUInjector")
        );
        assert!(default_runtime_dir(Path::new("relative-profile")).is_err());
    }

    #[test]
    fn log_tail_clamps_lines_and_discards_truncated_prefix() {
        assert_eq!(log_tail(b"one\ntwo\nthree\n", false, 2), "two\nthree");
        assert_eq!(log_tail(b"one\ntwo\n", false, 0), "two");
        assert_eq!(
            log_tail(b"partial\ncomplete\n", true, usize::MAX),
            "complete"
        );
        assert_eq!(log_tail(b"partial", true, 5), "");
        let lines = "line\n".repeat(MAX_LOG_LINES + 5);
        assert_eq!(
            log_tail(lines.as_bytes(), false, usize::MAX)
                .lines()
                .count(),
            MAX_LOG_LINES
        );
    }

    #[test]
    fn atomic_replace_and_noclobber_leave_no_temporary_files() {
        let dir = tempfile::tempdir().unwrap();
        let path = dir.path().join("settings.json");
        write_json_atomic(&path, &json!({"old": 1}), false).unwrap();
        assert!(write_json_atomic(&path, &json!({"bad": 2}), false).is_err());
        assert_eq!(
            read_json_object(&path, MAX_JSON_BYTES, false).unwrap(),
            json!({"old": 1})
        );
        write_json_atomic(&path, &json!({"new": 3}), true).unwrap();
        assert_eq!(
            read_json_object(&path, MAX_JSON_BYTES, false).unwrap(),
            json!({"new": 3})
        );
        assert_eq!(fs::read_dir(dir.path()).unwrap().count(), 1);
    }

    #[test]
    fn corrupt_empty_and_oversized_files_are_not_valid_objects() {
        let dir = tempfile::tempdir().unwrap();
        let path = dir.path().join("settings.json");
        assert_eq!(read_json_object(&path, 100, true).unwrap(), json!({}));
        for bytes in [b"".as_slice(), b"{broken", b"[]", b"null", b"123"] {
            write_atomic(&path, bytes, true).unwrap();
            assert!(read_json_object(&path, 100, true).is_err());
            assert_eq!(fs::read(&path).unwrap(), bytes);
        }
        write_atomic(&path, b"{\"large\":12345}", true).unwrap();
        assert!(read_json_object(&path, 5, false).is_err());
    }

    #[test]
    fn cross_instance_writer_lock_is_released_on_drop() {
        let dir = tempfile::tempdir().unwrap();
        let lock = lock_offline_writer(dir.path()).unwrap();
        assert!(lock_offline_writer(dir.path()).is_err());
        drop(lock);
        assert!(lock_offline_writer(dir.path()).is_ok());
    }

    #[test]
    fn queue_requires_fresh_supported_protocol_and_writes_uuid_and_iso_expiry() {
        let dir = tempfile::tempdir().unwrap();
        assert!(queue_command(dir.path(), "safeProbe", json!({})).is_err());
        let mut runtime = json!({
            "externalUiEnabled": true, "updatedAt": Utc::now().to_rfc3339(), "status": "running",
            "protocolVersion": 1, "supportedCommands": ["safeProbe"], "maxCommandBytes": MAX_COMMAND_BYTES
        });
        let runtime_path = dir.path().join("runtime.json");
        write_json_atomic(&runtime_path, &runtime, true).unwrap();
        assert!(queue_command(dir.path(), "safeProbe", json!({})).is_err());
        runtime["protocolVersion"] = json!(2);
        write_json_atomic(&runtime_path, &runtime, true).unwrap();
        assert!(queue_command(dir.path(), "safeProbe", json!({})).is_err());
        runtime["protocolVersion"] = json!(3);
        write_json_atomic(&runtime_path, &runtime, true).unwrap();
        assert!(queue_command(dir.path(), "manualRebirth", json!({})).is_err());
        let id = queue_command(dir.path(), "safeProbe", json!({})).unwrap();
        validate_command_id(&id).unwrap();
        let payload = read_json_object(
            &dir.path().join("commands").join(format!("{id}.json")),
            MAX_COMMAND_BYTES,
            false,
        )
        .unwrap();
        assert_eq!(payload["id"], id);
        assert_eq!(payload["args"], json!({}));
        let created = DateTime::parse_from_rfc3339(payload["createdAt"].as_str().unwrap()).unwrap();
        let expires = DateTime::parse_from_rfc3339(payload["expiresAt"].as_str().unwrap()).unwrap();
        assert_eq!(
            (expires - created).num_milliseconds(),
            COMMAND_TIMEOUT_MS as i64
        );
        runtime["updatedAt"] = json!("2000-01-01T00:00:00Z");
        write_json_atomic(&runtime_path, &runtime, true).unwrap();
        assert!(queue_command(dir.path(), "safeProbe", json!({})).is_err());
        assert_eq!(
            fs::read_dir(dir.path().join("commands")).unwrap().count(),
            1
        );
    }

    #[test]
    fn timeout_removes_queue_and_exposes_failure_without_waiting_again() {
        let dir = tempfile::tempdir().unwrap();
        let id = Uuid::new_v4().to_string();
        let path = dir.path().join("commands").join(format!("{id}.json"));
        write_json_atomic(&path, &json!({"id": id}), false).unwrap();
        let err = wait_for_command(dir.path(), &id, "savePlan", Instant::now()).unwrap_err();
        assert!(err.contains("timed out"));
        assert!(!path.exists());
        let result = read_result(dir.path(), &id, Some("savePlan"))
            .unwrap()
            .unwrap();
        assert_eq!(result["success"], false);
        assert_eq!(result["source"], "external-ui");
        assert!(result["message"].as_str().unwrap().contains("timed out"));
    }

    #[test]
    fn command_result_success_failure_identity_and_size_are_checked() {
        let dir = tempfile::tempdir().unwrap();
        let id = Uuid::new_v4().to_string();
        let path = dir
            .path()
            .join("command-results")
            .join(format!("{id}.json"));
        assert!(read_result(dir.path(), &id, None).unwrap().is_none());
        let mut result = json!({"id": id, "command": "savePlan", "success": true, "message": "saved", "completedAt": Utc::now().to_rfc3339(), "data": {"keep": 1}});
        write_json_atomic(&path, &result, true).unwrap();
        let response = wait_for_command(dir.path(), &id, "savePlan", Instant::now()).unwrap();
        assert_eq!(response["mode"], "connected");
        assert_eq!(response["result"]["data"], json!({"keep": 1}));
        assert!(read_result(dir.path(), &id, Some("safeProbe")).is_err());
        result["success"] = json!(false);
        result["message"] = json!("rejected patch");
        write_json_atomic(&path, &result, true).unwrap();
        assert!(
            wait_for_command(dir.path(), &id, "savePlan", Instant::now())
                .unwrap_err()
                .contains("rejected patch")
        );
        result["id"] = json!(Uuid::new_v4().to_string());
        write_json_atomic(&path, &result, true).unwrap();
        assert!(read_result(dir.path(), &id, None).is_err());
        result["id"] = json!(id);
        result["message"] = json!("x".repeat(ACTION_RESULT_MAX_BYTES as usize));
        write_json_atomic(&path, &result, true).unwrap();
        assert!(read_result(dir.path(), &id, None)
            .unwrap_err()
            .contains("action-result limit"));
    }

    #[test]
    fn historical_results_are_bounded_and_skip_malformed_entries() {
        let dir = tempfile::tempdir().unwrap();
        for index in 0..(MAX_RESULTS + 5) {
            let id = Uuid::new_v4().to_string();
            let result = json!({"id": id, "command": "safeProbe", "success": true, "message": index.to_string(), "completedAt": Utc::now().to_rfc3339()});
            write_json_atomic(&dir.path().join(format!("{id}.json")), &result, false).unwrap();
        }
        assert_eq!(list_command_results(dir.path()).unwrap().len(), MAX_RESULTS);
        write_atomic(&dir.path().join("malformed.json"), b"{broken", false).unwrap();
        let results = list_command_results(dir.path()).unwrap();
        assert!((MAX_RESULTS - 1..=MAX_RESULTS).contains(&results.len()));
        assert!(results.iter().all(|result| result.command == "safeProbe"));
    }

    #[test]
    fn new_commands_validate_optional_objects_and_positive_int32_seconds() {
        for command in ["previewAugments", "allocateAugments"] {
            for args in [
                json!({}),
                json!({"expectedSeconds": 1}),
                json!({"expectedSeconds": i32::MAX}),
                json!({"expectedSeconds": 2.0}),
            ] {
                assert!(
                    validate_command(command, &args).is_ok(),
                    "rejected {command}: {args}"
                );
            }
            for args in [
                json!(null),
                json!([]),
                json!({"expectedSeconds": 0}),
                json!({"expectedSeconds": -1}),
                json!({"expectedSeconds": 1.5}),
                json!({"expectedSeconds": 2147483648_u64}),
                json!({"expectedSeconds": "300"}),
                json!({"expectedSeconds": null}),
                json!({"unexpected": true}),
            ] {
                assert!(
                    validate_command(command, &args).is_err(),
                    "accepted {command}: {args}"
                );
            }
        }
        assert!(validate_command("createPlan", &json!({"requestedName": "copy"})).is_ok());
        assert!(validate_command(
            "createPlan",
            &json!({"requestedName": "copy", "source": {}})
        )
        .is_ok());
        for args in [
            json!({}),
            json!({"requestedName": "../escape"}),
            json!({"requestedName": "copy", "source": []}),
            json!({"requestedName": "copy", "source": null}),
            json!({"requestedName": "copy", "extra": true}),
        ] {
            assert!(validate_command("createPlan", &args).is_err());
        }
        assert!(validate_command("deletePlan", &json!({"name": "copy"})).is_ok());
        for args in [
            json!({}),
            json!({"name": "CON"}),
            json!({"name": "copy", "extra": true}),
        ] {
            assert!(validate_command("deletePlan", &args).is_err());
        }
    }

    #[test]
    fn offline_new_plan_enables_auto_rebirth_by_default() {
        let dir = tempfile::tempdir().unwrap();
        let name = tauri::async_runtime::block_on(create_plan(
            dir.path().to_string_lossy().into_owned(),
            "new".into(),
            None,
        ))
        .unwrap();
        let saved = read_json_object(
            &plan_path(dir.path(), &name).unwrap(),
            MAX_JSON_BYTES,
            false,
        )
        .unwrap();
        assert_eq!(saved["AutoRebirthEnabled"], true);
        assert!(!dir.path().join("settings.json").exists());
    }

    #[test]
    fn offline_plan_saves_and_copies_preserve_auto_rebirth_opt_out() {
        let dir = tempfile::tempdir().unwrap();
        let runtime_dir = dir.path().to_string_lossy().into_owned();
        let path = plan_path(dir.path(), "opt-out").unwrap();
        let mut source = default_plan();
        source["AutoRebirthEnabled"] = json!(false);
        source["Future"] = serde_json::from_str("900719925474099312345.123456789").unwrap();
        write_json_atomic(&path, &source, false).unwrap();
        let mut plan: Value = serde_json::from_str(&read_plan_json(&path).unwrap()).unwrap();
        assert_eq!(plan["AutoRebirthEnabled"], false);
        plan["TimeTriggerSeconds"] = json!(7200);
        tauri::async_runtime::block_on(save_plan(
            runtime_dir.clone(),
            "opt-out".into(),
            plan.to_string(),
        ))
        .unwrap();
        let before_copy = fs::read(&path).unwrap();
        let copy = tauri::async_runtime::block_on(create_plan(
            runtime_dir,
            "copy".into(),
            Some(plan.to_string()),
        ))
        .unwrap();
        for name in ["opt-out", copy.as_str()] {
            let saved =
                read_json_object(&plan_path(dir.path(), name).unwrap(), MAX_JSON_BYTES, false)
                    .unwrap();
            assert_eq!(saved["AutoRebirthEnabled"], false);
            assert_eq!(saved["TimeTriggerSeconds"], 7200);
            assert_eq!(
                saved["Future"].to_string(),
                "900719925474099312345.123456789"
            );
        }
        assert_eq!(fs::read(&path).unwrap(), before_copy);
    }

    #[test]
    fn copies_clear_history_and_default_plan_has_valid_enabled_stages() {
        let copy = prepare_plan_copy(
            json!({"Version": 14, "Name": "old", "RebirthHistory": [{"old": true}], "Future": 42}),
            "copy",
        )
        .unwrap();
        assert_eq!(
            copy,
            json!({"Version": 18, "Name": "copy", "RebirthHistory": [], "Future": 42,
            "AutoIronPillEnabled": true,
            })
        );
        assert!(prepare_plan_copy(json!([]), "copy").is_err());
        let mut plan = default_plan();
        assert_eq!(plan["Version"], 18);
        validate_plan_durations(&plan).unwrap();
        plan["AutoRebirthEnabled"] = json!(true);
        assert_eq!(plan["StageSchedulerEnabled"], true);
        let stages = plan["Stages"].as_array().unwrap();
        assert_eq!(stages.len(), 8);
        assert!((plan["StageLoopStartIndex"].as_u64().unwrap() as usize) < stages.len());
        assert_eq!(plan["GearGoal"], plan["GoalPresets"][0]["GearGoal"]);
        for stage in stages {
            assert_eq!(stage["DurationSeconds"], 0);
            assert_eq!(stage["DurationUnit"], "%");
            assert!(!stage["Name"].as_str().unwrap().trim().is_empty());
            assert!(stage["Percent"].as_f64().unwrap() >= 0.001);
            assert!(stage["EnergyFocus"].as_u64().unwrap() <= 7);
            assert!(stage["MagicFocus"].as_u64().unwrap() <= 7);
            assert!((-1..=13).contains(&stage["GearGoal"].as_i64().unwrap()));
            assert_eq!(stage["CompletionMode"], 0);
            assert!(stage["BlackBarLookaheadSeconds"].as_u64().unwrap() >= 3);
            assert_eq!(stage["TargetAdvancedTrainingPower"], "0");
            assert_eq!(stage["TargetAdvancedTrainingToughness"], "0");
            assert_eq!(stage["EnergyPriorities"], json!([]));
            assert_eq!(stage["MagicPriorities"], json!([]));
        }
    }

    #[test]
    fn offline_settings_only_edit_known_typed_fields_and_preserve_future_data() {
        let mut settings =
            json!({"_globalEnabled": false, "Future": {"value": 42}, "_customPlanFile": "copy"});
        let patch = json!({"_globalEnabled": true, "_customPlanFile": "default", "_customAugmentsBloodEstimateSeconds": 300});
        validate_offline_settings_patch(&patch).unwrap();
        merge_object_patch(&mut settings, &patch).unwrap();
        assert_eq!(settings["Future"], json!({"value": 42}));
        assert_eq!(settings["_globalEnabled"], true);
        for patch in [
            json!({"Future": {"value": 43}}),
            json!({"_futureEnabled": true}),
            json!({"_globalEnabled": 1}),
            json!({"_customPlanFile": "../escape"}),
            json!({"_manualAdventureZone": 1.5}),
            json!({"_quickLoadout": ["1"]}),
            json!({"_titanGoldTargets": [true]}),
        ] {
            assert!(
                validate_offline_settings_patch(&patch).is_err(),
                "accepted {patch}"
            );
        }
        assert!(
            validate_offline_settings_patch(&json!({"_titanGoldTargets": vec![false; 12]})).is_ok()
        );
    }

    #[test]
    fn missing_plan_selection_checks_daily_plan_before_retiring_legacy_ngu_weights() {
        let dir = tempfile::tempdir().unwrap();
        write_json_atomic(
            &plan_path(dir.path(), "24hr").unwrap(),
            &json!({"Version": 15, "EnergyNguWeights": vec![1; 9]}),
            false,
        )
        .unwrap();
        let error = require_durable_global_ngu_weights(dir.path(), &json!({}), None).unwrap_err();
        assert!(
            error.contains("durably migrate the selected legacy plan"),
            "{error}"
        );
    }

    #[test]
    fn optional_plan_prep_fields_roundtrip_without_changing_unknown_tokens() {
        for seconds in [None, Some(json!(0)), Some(json!(i32::MAX))] {
            for wandoos in [None, Some(json!(false)), Some(json!(true))] {
                let dir = tempfile::tempdir().unwrap();
                let mut plan = json!({"Version": 17, "Future": 9007199254740993_u64,
                    "Stages": [{"Name": "Time Machine", "Future": 9223372036854775807_u64}]});
                if let Some(seconds) = &seconds {
                    plan["RebirthPrepStartSeconds"] = seconds.clone();
                }
                if let Some(wandoos) = &wandoos {
                    plan["Stages"][0]["UseWandoosWhenTimeMachineLocked"] = wandoos.clone();
                }
                let before = plan.to_string();
                validate_plan_durations(&plan).unwrap();
                validate_command("savePlan", &json!({"name": "copy", "plan": plan})).unwrap();
                validate_command(
                    "createPlan",
                    &json!({"requestedName": "copy", "source": plan}),
                )
                .unwrap();
                assert_eq!(plan.to_string(), before);
                save_plan_offline(dir.path(), "copy", &plan, None).unwrap();
                let readback: Value = serde_json::from_str(
                    &read_plan_json(&plan_path(dir.path(), "copy").unwrap()).unwrap(),
                )
                .unwrap();
                assert_eq!(readback.get("RebirthPrepStartSeconds"), seconds.as_ref());
                assert_eq!(
                    readback["Stages"],
                    normalize_offline_plan(plan.clone()).unwrap()["Stages"]
                );
                assert_eq!(readback["Future"].to_string(), "9007199254740993");
            }
        }
    }

    #[test]
    fn rebirth_prep_start_seconds_accepts_exact_whole_tokens_without_rewriting() {
        for token in [
            "0",
            "900",
            "2147483647",
            "900.0",
            "9e2",
            "9000e-1",
            "-0.0",
            "0e-400",
            "2147483647.0",
            "214748364700e-2",
        ] {
            let dir = tempfile::tempdir().unwrap();
            let text = format!(
                r#"{{"Version":17,"RebirthPrepStartSeconds":{token},"Stages":[{{"DurationSeconds":300}}],"Future":{{"large":900719925474099312345.123456789,"tiny":1e-400}}}}"#
            );
            let plan = parse_json_object(text.as_bytes(), "Plan").unwrap();
            let before = plan.to_string();
            let exact = plan["RebirthPrepStartSeconds"].to_string();
            validate_plan_durations(&plan).unwrap_or_else(|error| panic!("{token}: {error}"));
            validate_command("savePlan", &json!({"name": "copy", "plan": plan})).unwrap();
            validate_command(
                "createPlan",
                &json!({"requestedName": "copy", "source": plan}),
            )
            .unwrap();
            let copy = prepare_plan_copy(plan.clone(), "copy").unwrap();
            assert_eq!(copy["RebirthPrepStartSeconds"].to_string(), exact);
            assert_eq!(copy["Future"], plan["Future"]);
            assert_eq!(plan.to_string(), before);

            save_plan_offline(dir.path(), "copy", &plan, None).unwrap();
            let path = plan_path(dir.path(), "copy").unwrap();
            let stored = read_json_object(&path, MAX_JSON_BYTES, false).unwrap();
            let readback: Value = serde_json::from_str(&read_plan_json(&path).unwrap()).unwrap();
            for saved in [&stored, &readback] {
                assert_eq!(saved["RebirthPrepStartSeconds"].to_string(), exact);
                assert_eq!(saved["Future"], plan["Future"]);
            }
        }
    }

    #[test]
    fn rebirth_prep_start_seconds_rejects_inexact_or_out_of_range_tokens_without_writes() {
        let dir = tempfile::tempdir().unwrap();
        let plan = json!({"Version": 17, "RebirthPrepStartSeconds": 900,
            "Stages": [{"DurationSeconds": 300}], "Future": 9007199254740993_u64});
        save_plan_offline(dir.path(), "copy", &plan, None).unwrap();
        let path = plan_path(dir.path(), "copy").unwrap();
        let before = fs::read(&path).unwrap();
        let revision = file_revision(&before);
        for token in [
            "1e-400",
            "-1e-400",
            "1.0000000000000001",
            "2147483647.0000000001",
            "2147483648",
            "2.147483648e9",
            "1e400",
            "1e-9223372036854775809",
            "-1",
            "null",
            "\"900\"",
            "false",
            "{}",
            "[]",
        ] {
            let mut bad = plan.clone();
            bad["RebirthPrepStartSeconds"] = serde_json::from_str(token).unwrap();
            let error = validate_plan_durations(&bad).unwrap_err();
            assert!(
                error.contains("RebirthPrepStartSeconds"),
                "{token}: {error}"
            );
            for (command, args) in [
                ("savePlan", json!({"name": "copy", "plan": bad})),
                (
                    "createPlan",
                    json!({"requestedName": "invalid", "source": bad}),
                ),
            ] {
                assert_eq!(validate_command(command, &args).unwrap_err(), error);
            }
            assert_eq!(
                save_plan_offline(dir.path(), "copy", &bad, Some(&revision)).unwrap_err(),
                error
            );
            assert_eq!(fs::read(&path).unwrap(), before);
        }
    }

    #[test]
    fn malformed_optional_plan_prep_fields_reject_writes_without_changing_file() {
        let dir = tempfile::tempdir().unwrap();
        let plan = json!({"Version": 17, "Stages": [{"DurationSeconds": 300}]});
        save_plan_offline(dir.path(), "copy", &plan, None).unwrap();
        let path = plan_path(dir.path(), "copy").unwrap();
        let before = fs::read(&path).unwrap();
        let revision = file_revision(&before);
        for (field, invalid_values) in [
            (
                "RebirthPrepStartSeconds",
                vec![
                    json!(null),
                    json!(-1),
                    json!(1.5),
                    json!(2147483648_u64),
                    json!("900"),
                    json!(true),
                    json!({}),
                    json!([]),
                ],
            ),
            (
                "UseWandoosWhenTimeMachineLocked",
                vec![
                    json!(null),
                    json!(0),
                    json!(1),
                    json!("true"),
                    json!({}),
                    json!([]),
                ],
            ),
        ] {
            for invalid in invalid_values {
                let mut bad = plan.clone();
                if field == "RebirthPrepStartSeconds" {
                    bad[field] = invalid;
                } else {
                    bad["Stages"][0][field] = invalid;
                }
                let error = validate_plan_durations(&bad).unwrap_err();
                assert!(error.contains(field), "{error}");
                for (command, args) in [
                    ("savePlan", json!({"name": "copy", "plan": bad})),
                    (
                        "createPlan",
                        json!({"requestedName": "copy", "source": bad}),
                    ),
                ] {
                    assert_eq!(validate_command(command, &args).unwrap_err(), error);
                }
                assert_eq!(
                    save_plan_offline(dir.path(), "copy", &bad, Some(&revision)).unwrap_err(),
                    error
                );
                assert_eq!(fs::read(&path).unwrap(), before);
            }
        }
    }

    #[test]
    fn deletion_guards_builtins_last_plan_and_case_insensitive_running_selection() {
        for name in [
            "24hr",
            "15m-NUMBER-increase",
            "30m-NUMBER-increase",
            "1h-1m-NUMBER-increase",
        ] {
            for variant in [name.to_string(), name.to_uppercase(), name.to_lowercase()] {
                assert!(validate_plan_deletion(&variant, &json!({}), 5)
                    .unwrap_err()
                    .contains("Built-in plans cannot be deleted"));
            }
        }
        for name in ["default", "DEFAULT", "1h-3m-rebirth", "Infinite"] {
            assert_eq!(validate_plan_deletion(name, &json!({}), 5).unwrap(), false);
        }
        assert!(validate_plan_deletion("copy", &json!({}), 1).is_err());
        assert!(validate_plan_deletion(
            "copy",
            &json!({"_customPlanFile": "COPY", "_useCustomRebirthPlan": true}),
            5
        )
        .is_err());
        assert_eq!(
            validate_plan_deletion(
                "copy",
                &json!({"_customPlanFile": "COPY", "_useCustomRebirthPlan": false}),
                5
            )
            .unwrap(),
            true
        );
        assert_eq!(
            validate_plan_deletion(
                "copy",
                &json!({"_customPlanFile": "other", "_useCustomRebirthPlan": true}),
                5
            )
            .unwrap(),
            false
        );
        assert!(
            validate_plan_deletion("copy", &json!({"_useCustomRebirthPlan": "false"}), 5).is_err()
        );
    }

    #[test]
    fn offline_deletion_persists_paused_selection_and_preserves_unknown_settings() {
        let dir = tempfile::tempdir().unwrap();
        let _lock = lock_offline_writer(dir.path()).unwrap();
        for name in ["copy", "other"] {
            write_json_atomic(
                &plan_path(dir.path(), name).unwrap(),
                &prepare_plan_copy(default_plan(), name).unwrap(),
                false,
            )
            .unwrap();
        }
        let fallback_path = plan_path(dir.path(), "24hr").unwrap();
        let fallback = json!({"Version": 17, "Name": "24hr", "TimeTriggerSeconds": 86400,
            "StageDurationSeconds": 86400, "Stages": [{"Name": "NGU", "Percent": 1}],
            "RebirthHistory": [{"Future": 42}], "Future": 9007199254740993_u64});
        write_json_atomic(&fallback_path, &fallback, false).unwrap();
        let fallback_bytes = fs::read(&fallback_path).unwrap();
        let settings_path = dir.path().join("settings.json");
        let settings = json!({"_customPlanFile": "copy", "_useCustomRebirthPlan": false, "Future": {"keep": 42}});
        write_json_atomic(&settings_path, &settings, true).unwrap();
        assert_eq!(delete_plan_offline(dir.path(), "copy").unwrap(), "24hr");
        assert!(!plan_path(dir.path(), "copy").unwrap().exists());
        assert!(plan_path(dir.path(), "other").unwrap().exists());
        let saved = read_json_object(&settings_path, MAX_JSON_BYTES, false).unwrap();
        assert_eq!(saved["_customPlanFile"], "24hr");
        assert_eq!(saved["_useCustomRebirthPlan"], false);
        assert_eq!(saved["Future"], settings["Future"]);
        assert_eq!(fs::read(&fallback_path).unwrap(), fallback_bytes);
        assert!(!plan_path(dir.path(), "default").unwrap().exists());
        assert!(delete_plan_offline(dir.path(), "24hr").is_err());
    }

    #[test]
    fn missing_daily_fallback_leaves_selected_plan_settings_and_progress_unchanged() {
        let dir = tempfile::tempdir().unwrap();
        let _lock = lock_offline_writer(dir.path()).unwrap();
        for name in ["copy", "other"] {
            write_json_atomic(
                &plan_path(dir.path(), name).unwrap(),
                &prepare_plan_copy(default_plan(), name).unwrap(),
                false,
            )
            .unwrap();
        }
        let path = plan_path(dir.path(), "copy").unwrap();
        let plan_bytes = fs::read(&path).unwrap();
        let checkpoint = dir
            .path()
            .join(format!("stage-progress-{}.json", file_revision(b"copy")));
        write_atomic(&checkpoint, b"progress", false).unwrap();
        let settings_path = dir.path().join("settings.json");
        write_json_atomic(
            &settings_path,
            &json!({"_customPlanFile": "copy", "_useCustomRebirthPlan": false, "Future": 42}),
            false,
        )
        .unwrap();
        let settings_bytes = fs::read(&settings_path).unwrap();
        let error = delete_plan_offline(dir.path(), "copy").unwrap_err();
        assert!(
            error.contains("Cannot select fallback plan 24hr"),
            "{error}"
        );
        assert_eq!(fs::read(&path).unwrap(), plan_bytes);
        assert_eq!(fs::read(&settings_path).unwrap(), settings_bytes);
        assert_eq!(fs::read(&checkpoint).unwrap(), b"progress");
        assert!(!plan_path(dir.path(), "24hr").unwrap().exists());
        assert!(!plan_path(dir.path(), "default").unwrap().exists());
    }

    #[test]
    fn offline_deletion_allows_retired_builtins_without_changing_another_selection() {
        for name in ["default", "1h-3m-rebirth", "infinite"] {
            let dir = tempfile::tempdir().unwrap();
            for plan_name in [name, "other"] {
                write_json_atomic(
                    &plan_path(dir.path(), plan_name).unwrap(),
                    &prepare_plan_copy(default_plan(), plan_name).unwrap(),
                    false,
                )
                .unwrap();
            }
            let settings_path = dir.path().join("settings.json");
            write_json_atomic(
                &settings_path,
                &json!({"_customPlanFile": "other", "_useCustomRebirthPlan": true}),
                false,
            )
            .unwrap();
            let settings_bytes = fs::read(&settings_path).unwrap();
            assert_eq!(delete_plan_offline(dir.path(), name).unwrap(), "other");
            assert!(!plan_path(dir.path(), name).unwrap().exists());
            assert_eq!(fs::read(&settings_path).unwrap(), settings_bytes);
            assert!(!plan_path(dir.path(), "24hr").unwrap().exists());
        }
    }

    #[test]
    fn corrupt_settings_or_fallback_never_delete_the_selected_plan() {
        let dir = tempfile::tempdir().unwrap();
        let _lock = lock_offline_writer(dir.path()).unwrap();
        let copy_path = plan_path(dir.path(), "copy").unwrap();
        let fallback_path = plan_path(dir.path(), "24hr").unwrap();
        write_json_atomic(&copy_path, &json!({"Name": "copy"}), false).unwrap();
        write_atomic(&fallback_path, b"{broken", false).unwrap();
        let settings_path = dir.path().join("settings.json");
        write_json_atomic(
            &settings_path,
            &json!({"_customPlanFile": "copy", "_useCustomRebirthPlan": false}),
            false,
        )
        .unwrap();
        assert!(delete_plan_offline(dir.path(), "copy").is_err());
        assert!(copy_path.exists());
        assert_eq!(
            read_json_object(&settings_path, MAX_JSON_BYTES, false).unwrap()["_customPlanFile"],
            "copy"
        );
        write_atomic(&settings_path, b"{broken", true).unwrap();
        assert!(delete_plan_offline(dir.path(), "copy").is_err());
        assert!(copy_path.exists());
        assert_eq!(fs::read(&settings_path).unwrap(), b"{broken");
    }

    #[test]
    fn connected_plan_management_clears_copy_history_and_exposes_names() {
        let dir = tempfile::tempdir().unwrap();
        let runtime = json!({"externalUiEnabled": true, "updatedAt": Utc::now().to_rfc3339(), "status": "running", "protocolVersion": 3, "planVersion": 18, "fightScheduleVersion": 1, "stageTimingVersion": 1, "globalNguWeightsVersion": 1, "supportedCommands": ["createPlan", "deletePlan"]});
        write_json_atomic(&dir.path().join("runtime.json"), &runtime, false).unwrap();
        let id = queue_command(
            dir.path(),
            "createPlan",
            json!({"requestedName": "copy", "source": {"Version": 18, "RebirthHistory": [1], "Future": 42}}),
        )
        .unwrap();
        let payload = read_json_object(
            &dir.path().join("commands").join(format!("{id}.json")),
            MAX_COMMAND_BYTES,
            false,
        )
        .unwrap();
        assert_eq!(payload["args"]["source"]["Version"], 18);
        assert_eq!(payload["args"]["source"]["RebirthHistory"], json!([]));
        assert_eq!(payload["args"]["source"]["Future"], 42);
        for (id, command, data, field, name) in [
            (
                id,
                "createPlan",
                json!({"name": "copy-2"}),
                "name",
                "copy-2",
            ),
            (
                queue_command(dir.path(), "deletePlan", json!({"name": "copy"})).unwrap(),
                "deletePlan",
                json!({"name": "copy", "selectedPlan": "24hr"}),
                "selectedPlan",
                "24hr",
            ),
        ] {
            let result = json!({"id": id, "command": command, "success": true, "message": "ok", "completedAt": Utc::now().to_rfc3339(), "data": data});
            write_json_atomic(
                &dir.path()
                    .join("command-results")
                    .join(format!("{id}.json")),
                &result,
                false,
            )
            .unwrap();
            let response = wait_for_command(dir.path(), &id, command, Instant::now()).unwrap();
            assert_eq!(command_result_name(&response, field).unwrap(), name);
            assert!(!dir
                .path()
                .join("commands")
                .join(format!("{id}.json"))
                .exists());
        }
        assert!(command_result_name(&json!({"result": {"data": {}}}), "name").is_err());
        assert!(
            command_result_name(&json!({"result": {"data": {"name": "../escape"}}}), "name")
                .is_err()
        );
    }
}

#[cfg(test)]
mod runtime_read_tests {
    use super::*;

    #[test]
    fn sharing_retry_recovers_with_at_most_five_ten_millisecond_delays() {
        for failures in 0..=5 {
            let mut attempts = 0;
            let mut delays = Vec::new();
            let result = retry_windows_sharing(
                || {
                    attempts += 1;
                    if attempts <= failures {
                        Err(std::io::Error::from_raw_os_error(32))
                    } else {
                        Ok("runtime")
                    }
                },
                |delay| delays.push(delay),
            )
            .unwrap();
            assert_eq!(result, "runtime");
            assert_eq!(attempts, failures + 1);
            assert_eq!(delays, vec![Duration::from_millis(10); failures]);
        }
    }

    #[test]
    fn persistent_sharing_failure_stops_after_fifty_milliseconds_of_retry_delays() {
        let mut attempts = 0;
        let mut delays = Vec::new();
        let err = retry_windows_sharing::<()>(
            || {
                attempts += 1;
                Err(std::io::Error::from_raw_os_error(32))
            },
            |delay| delays.push(delay),
        )
        .unwrap_err();
        assert_eq!(err.raw_os_error(), Some(32));
        assert_eq!(attempts, 6);
        assert_eq!(delays, vec![Duration::from_millis(10); 5]);
    }

    #[test]
    fn sharing_retry_never_retries_other_io_errors() {
        for error in [
            std::io::Error::from_raw_os_error(2),
            std::io::Error::from_raw_os_error(5),
            std::io::Error::from_raw_os_error(33),
            std::io::Error::new(std::io::ErrorKind::InvalidData, "invalid data"),
        ] {
            let expected = error.to_string();
            let mut error = Some(error);
            let mut attempts = 0;
            let err = retry_windows_sharing::<()>(
                || {
                    attempts += 1;
                    Err(error.take().expect("must not retry this error"))
                },
                |_| panic!("must not delay for errors other than OS 32"),
            )
            .unwrap_err();
            assert_eq!(err.to_string(), expected);
            assert_eq!(attempts, 1);
        }
    }

    #[test]
    fn runtime_parsing_is_outside_open_retry_and_malformed_json_is_not_retried() {
        let dir = tempfile::tempdir().unwrap();
        let path = dir.path().join("runtime.json");
        assert_eq!(read_runtime(dir.path()).unwrap(), json!({}));
        for malformed in [b"{broken".as_slice(), b"[]", b"null", b""] {
            write_atomic(&path, malformed, true).unwrap();
            let mut opens = 0;
            let bytes = read_file_bytes_with_open(&path, MAX_RUNTIME_BYTES, true, |path| {
                retry_windows_sharing(
                    || {
                        opens += 1;
                        File::open(path)
                    },
                    |_| panic!("JSON errors must not cause a retry delay"),
                )
            })
            .unwrap()
            .unwrap();
            assert!(parse_json_object(&bytes, "runtime").is_err());
            assert_eq!(opens, 1);
            assert!(read_runtime(dir.path()).is_err());
            assert_eq!(fs::read(&path).unwrap(), malformed);
        }
    }
}

#[cfg(test)]
#[path = "global-ngu-tests.rs"]
mod global_ngu_tests;

#[cfg(test)]
mod stage_adventure_weight_tests {
    use super::*;

    #[test]
    fn optional_weights_validate_without_mutation_in_all_modes() {
        for mode in [None, Some(0), Some(1)] {
            for weight in [
                None,
                Some(json!(0)),
                Some(json!(0.125)),
                Some(json!(1000000)),
            ] {
                let mut stage = json!({"DurationSeconds": 300, "FightSchedule": null, "Future": 9007199254740993_u64});
                if let Some(mode) = mode {
                    stage["ExecutionMode"] = json!(mode);
                }
                if let Some(weight) = weight {
                    stage["AdventureDynamicWeight"] = weight;
                }
                let plan = json!({"Version": 18, "Stages": [stage]});
                let before = serde_json::to_string(&plan).unwrap();
                validate_plan_durations(&plan).unwrap();
                validate_command("savePlan", &json!({"name": "weight", "plan": plan})).unwrap();
                validate_command(
                    "createPlan",
                    &json!({"requestedName": "weight", "source": plan}),
                )
                .unwrap();
                assert_eq!(serde_json::to_string(&plan).unwrap(), before);
                assert_eq!(
                    prepare_plan_copy(plan.clone(), "copy").unwrap()["Stages"],
                    plan["Stages"]
                );
            }
        }
    }

    #[test]
    fn malformed_weights_reject_connected_and_offline_writes_without_changing_file() {
        let dir = tempfile::tempdir().unwrap();
        let plan = json!({"Version": 17, "Stages": [{"DurationSeconds": 300}]});
        save_plan_offline(dir.path(), "weight", &plan, None).unwrap();
        write_json_atomic(
            &dir.path().join("settings.json"),
            &json!({"_energyNguWeights": vec![0; 9], "_magicNguWeights": vec![0; 7]}),
            false,
        )
        .unwrap();
        let path = plan_path(dir.path(), "weight").unwrap();
        let before = fs::read(&path).unwrap();
        let revision = file_revision(&before);
        for invalid in [
            json!(null),
            json!(-1),
            json!(1000000.01),
            json!("0"),
            json!("NaN"),
            json!(true),
            json!(false),
            json!({}),
            json!([]),
            serde_json::from_str::<Value>("1e400").unwrap(),
            serde_json::from_str::<Value>("-1e400").unwrap(),
        ] {
            for mode in [None, Some(0), Some(1)] {
                let mut bad = plan.clone();
                bad["Stages"][0]["AdventureDynamicWeight"] = invalid.clone();
                if let Some(mode) = mode {
                    bad["Stages"][0]["ExecutionMode"] = json!(mode);
                }
                let error = validate_plan_durations(&bad).unwrap_err();
                assert!(error.contains("Stage 1 AdventureDynamicWeight"), "{error}");
                for (command, args) in [
                    ("savePlan", json!({"name": "weight", "plan": bad})),
                    (
                        "createPlan",
                        json!({"requestedName": "weight", "source": bad}),
                    ),
                ] {
                    assert_eq!(validate_command(command, &args).unwrap_err(), error);
                }
                assert_eq!(
                    save_plan_offline(dir.path(), "weight", &bad, Some(&revision)).unwrap_err(),
                    error
                );
                assert_eq!(fs::read(&path).unwrap(), before);
            }
        }
    }

    #[test]
    fn offline_readback_preserves_weight_when_switching_modes() {
        let dir = tempfile::tempdir().unwrap();
        write_json_atomic(
            &dir.path().join("settings.json"),
            &json!({"_energyNguWeights": vec![0; 9], "_magicNguWeights": vec![0; 7]}),
            false,
        )
        .unwrap();
        let mut plan = json!({"Version": 17, "Stages": [{"ExecutionMode": 1, "AdventureDynamicWeight": 0.125, "DurationSeconds": 300, "Future": 9007199254740993_u64}]});
        save_plan_offline(dir.path(), "weight", &plan, None).unwrap();
        let path = plan_path(dir.path(), "weight").unwrap();
        for mode in [0, 1] {
            plan = serde_json::from_str(&read_plan_json(&path).unwrap()).unwrap();
            let revision = take_plan_revision(&mut plan).unwrap().unwrap();
            plan["Stages"][0]["ExecutionMode"] = json!(mode);
            save_plan_offline(dir.path(), "weight", &plan, Some(&revision)).unwrap();
            let readback: Value = serde_json::from_str(&read_plan_json(&path).unwrap()).unwrap();
            assert_eq!(
                readback["Stages"],
                normalize_offline_plan(plan.clone()).unwrap()["Stages"]
            );
            assert_eq!(
                readback["Stages"][0]["AdventureDynamicWeight"],
                json!(0.125)
            );
            assert_eq!(readback["Stages"][0]["Future"], json!(9007199254740993_u64));
        }
    }
}

#[cfg(test)]
mod duration_tests {
    use super::*;

    #[test]
    fn at_target_rows_do_not_consume_timed_budget_or_aggregate_limits() {
        for stages in [
            json!([
                {"CompletionMode": 1, "DurationSeconds": 60, "Percent": 1},
                {"CompletionMode": 0, "Percent": 1}
            ]),
            json!([
                {"CompletionMode": 1, "DurationSeconds": i32::MAX, "Percent": 1e308},
                {"CompletionMode": 1, "DurationSeconds": i32::MAX, "Percent": 1e308},
                {"DurationSeconds": 1, "Percent": 1e308}
            ]),
            json!([
                {"CompletionMode": 1, "DurationSeconds": i32::MAX, "Percent": 1e308},
                {"CompletionMode": 1, "Percent": 1e308}
            ]),
            json!([
                {"CompletionMode": 1, "DurationSeconds": 0, "Percent": 1},
                {"DurationSeconds": 100, "Percent": 1}
            ]),
        ] {
            let plan = json!({"Version": 15, "StageDurationSeconds": 60, "Stages": stages});
            let before = serde_json::to_string(&plan).unwrap();
            validate_plan_durations(&plan).unwrap();
            assert_eq!(serde_json::to_string(&plan).unwrap(), before);
        }
        let mut plan = json!({"Version": 15, "StageDurationSeconds": 60, "Stages": [
            {"CompletionMode": 1, "DurationSeconds": 60, "Percent": 1},
            {"DurationSeconds": 60, "Percent": 1},
            {"Percent": 1}
        ]});
        validate_plan_durations(&plan).unwrap();
        plan["StageDurationSeconds"] = json!(61);
        validate_plan_durations(&plan).unwrap();
        plan["Stages"][1]["Percent"] = json!(1e308);
        plan["Stages"][2]["Percent"] = json!(1e308);
        validate_plan_durations(&plan).unwrap();
        plan["Stages"]
            .as_array_mut()
            .unwrap()
            .push(json!({"Percent": 1e308}));
        assert!(validate_plan_durations(&plan)
            .unwrap_err()
            .contains("finite total"));
        plan["Stages"].as_array_mut().unwrap().pop();
        plan["Stages"][1]["Percent"] = json!(1);
        plan["Stages"][2]["Percent"] = json!(1);
        plan["Stages"][1]["DurationSeconds"] = json!(i32::MAX);
        plan["Stages"][2]["DurationSeconds"] = json!(1);
        assert!(validate_plan_durations(&plan)
            .unwrap_err()
            .contains("Total fixed"));
    }

    #[test]
    fn at_target_rows_still_require_valid_stored_duration_fields_and_completion_modes() {
        for (field, invalid) in [
            ("DurationSeconds", json!(-1)),
            ("DurationSeconds", json!(2147483648_u64)),
            ("DurationSeconds", json!(1.5)),
            ("DurationSeconds", json!("60")),
            ("DurationUnit", json!("minutes")),
            ("DurationUnit", json!(null)),
            ("Percent", json!(0)),
            ("Percent", json!(-1)),
            ("Percent", json!("1")),
            ("Percent", serde_json::from_str::<Value>("1e1000").unwrap()),
            ("CompletionMode", json!(-1)),
            ("CompletionMode", json!(2)),
            ("CompletionMode", json!(0.5)),
            ("CompletionMode", json!("1")),
            ("CompletionMode", json!(null)),
        ] {
            let mut stage = json!({"CompletionMode": 1, "DurationSeconds": 60, "DurationUnit": "min", "Percent": 1});
            stage[field] = invalid;
            let plan = json!({"Version": 15, "StageDurationSeconds": 60, "Stages": [stage]});
            assert!(
                validate_plan_durations(&plan).unwrap_err().contains(field),
                "{plan}"
            );
        }
    }

    #[test]
    fn connected_write_validation_rejects_invalid_plans_before_creating_queue_files() {
        let dir = tempfile::tempdir().unwrap();
        let runtime = json!({"externalUiEnabled": true, "updatedAt": Utc::now().to_rfc3339(), "status": "running", "protocolVersion": 3, "planVersion": 15, "supportedCommands": ["readPlan", "savePlan", "createPlan"]});
        write_json_atomic(&dir.path().join("runtime.json"), &runtime, false).unwrap();
        for (plan, expected_error) in [
            (
                json!({"Version": 15, "Stages": [{"Percent": 0}]}),
                "Percent",
            ),
            (
                json!({"Version": 15, "StageDurationSeconds": -1, "Stages": [{"Percent": 1}]}),
                "StageDurationSeconds",
            ),
            (
                json!({"Version": 15, "Stages": [{"CompletionMode": 1, "Percent": 0}]}),
                "Percent",
            ),
            (
                json!({"Version": 15, "Stages": [null]}),
                "Stage 1 must be an object",
            ),
            (
                json!({"Version": 15, "Stages": [{"DurationSeconds": i32::MAX}, {"DurationSeconds": 1}]}),
                "Total fixed",
            ),
        ] {
            for (command, args) in [
                (
                    "savePlan",
                    json!({"name": "invalid", "plan": plan, "expectedRevision": ""}),
                ),
                (
                    "createPlan",
                    json!({"requestedName": "invalid", "source": plan}),
                ),
            ] {
                let err = validate_command(command, &args).unwrap_err();
                assert!(err.contains(expected_error), "{err}");
                assert_eq!(queue_command(dir.path(), command, args).unwrap_err(), err);
                assert!(!dir.path().join("commands").exists());
            }
        }
    }

    #[test]
    fn connected_write_validation_accepts_mixed_scales_and_at_sequences_losslessly() {
        let dir = tempfile::tempdir().unwrap();
        let runtime = json!({"externalUiEnabled": true, "updatedAt": Utc::now().to_rfc3339(), "status": "running", "protocolVersion": 3, "planVersion": 18, "fightScheduleVersion": 1, "stageTimingVersion": 1, "globalNguWeightsVersion": 1, "supportedCommands": ["readPlan", "savePlan", "createPlan"]});
        write_json_atomic(&dir.path().join("runtime.json"), &runtime, false).unwrap();
        let mut plans = vec![
            json!({"Version": 15, "StageDurationSeconds": 60, "Stages": [{"DurationSeconds": 20, "DurationUnit": "min", "Percent": 7}, {"Percent": 1}, {"Percent": 3}]}),
            json!({"Version": 15, "StageDurationSeconds": 60, "Stages": [{"CompletionMode": 1, "DurationSeconds": 60, "Percent": 1}, {"Percent": 1}]}),
            json!({"Version": 15, "StageDurationSeconds": 1, "Stages": [{"CompletionMode": 1, "DurationSeconds": i32::MAX, "Percent": 1e308}, {"CompletionMode": 1, "DurationSeconds": i32::MAX, "Percent": 1e308}]}),
        ];
        for version in [14, 15] {
            for scale in [1e-200, 0.0001, 1000.0, 1e200] {
                plans.push(json!({"Version": version, "Stages": [{"Percent": scale}, {"Percent": 3.0 * scale}]}));
            }
        }
        for plan in plans {
            for (command, field, args) in [
                (
                    "savePlan",
                    "plan",
                    json!({"name": "valid", "plan": plan, "expectedRevision": ""}),
                ),
                (
                    "createPlan",
                    "source",
                    json!({"requestedName": "valid", "source": plan}),
                ),
            ] {
                let id = queue_command(dir.path(), command, args).unwrap();
                let path = dir.path().join("commands").join(format!("{id}.json"));
                let payload = read_json_object(&path, MAX_COMMAND_BYTES, false).unwrap();
                let mut expected = normalize_offline_plan(plan.clone()).unwrap();
                if command == "createPlan" {
                    expected["RebirthHistory"] = json!([]);
                }
                assert_eq!(payload["args"][field], expected);
                remove_if_present(&path).unwrap();
            }
        }
        assert_eq!(
            fs::read_dir(dir.path().join("commands")).unwrap().count(),
            0
        );
    }

    #[test]
    fn weighted_v14_and_v15_plans_keep_legacy_weight_scales() {
        for version in [14, 15] {
            for scale in [1e-200, 0.0001, 1.0, 1000.0, 1e200] {
                let plan = json!({"Version": version, "StageDurationSeconds": 100, "Stages": [
                    {"Percent": scale},
                    {"Percent": 3.0 * scale, "DurationSeconds": 0, "DurationUnit": "%"}
                ]});
                let before = serde_json::to_string(&plan).unwrap();
                validate_plan_durations(&plan).unwrap();
                assert_eq!(serde_json::to_string(&plan).unwrap(), before);
                require_current_plan_version(&plan).unwrap();
            }
        }
    }

    #[test]
    fn mixed_durations_preserve_exact_seconds_with_exhausted_flex_budget() {
        for unit in ["%", "s", "min", "h"] {
            // Units are display hints, not multipliers or divisibility constraints.
            let mut plan = json!({"Version": 15, "StageDurationSeconds": 100, "Stages": [
                {"DurationSeconds": 40, "DurationUnit": unit, "Percent": 9999},
                {"DurationSeconds": 0, "DurationUnit": "%", "Percent": 1},
                {"Percent": 3}
            ]});
            validate_plan_durations(&plan).unwrap();
            assert_eq!(plan["Stages"][0]["DurationSeconds"], 40);
            for budget in [40, 39] {
                plan["StageDurationSeconds"] = json!(budget);
                validate_plan_durations(&plan).unwrap();
                assert_eq!(plan["StageDurationSeconds"], budget);
            }
            plan["StageDurationSeconds"] = json!(41);
            validate_plan_durations(&plan).unwrap();
        }
    }

    #[test]
    fn all_fixed_duration_total_is_independent_of_budget_but_bounded_by_int32() {
        let mut plan = json!({"Version": 15, "StageDurationSeconds": 1, "Stages": [
            {"DurationSeconds": 60, "DurationUnit": "s", "Percent": 1},
            {"DurationSeconds": 3600, "DurationUnit": "h", "Percent": 2}
        ]});
        validate_plan_durations(&plan).unwrap();
        plan["Stages"][0]["DurationSeconds"] = json!(i32::MAX - 3600);
        validate_plan_durations(&plan).unwrap();
        plan["Stages"][0]["DurationSeconds"] = json!(i32::MAX - 3599);
        assert!(validate_plan_durations(&plan)
            .unwrap_err()
            .contains("Total fixed"));
    }

    #[test]
    fn duration_validation_rejects_invalid_units_types_limits_and_nonobjects() {
        for stage in [
            json!(null),
            json!([]),
            json!(1),
            json!("stage"),
            json!({"DurationSeconds": -1}),
            json!({"DurationSeconds": 1.5, "DurationUnit": "s"}),
            json!({"DurationSeconds": "60", "DurationUnit": "s"}),
            json!({"DurationSeconds": null}),
            json!({"DurationSeconds": true}),
            json!({"DurationSeconds": 2147483648_u64, "DurationUnit": "s"}),
            json!({"DurationUnit": "minutes"}),
            json!({"DurationUnit": null}),
            json!({"DurationUnit": 1}),
            json!({"DurationUnit": "S"}),
            json!({"Percent": 0}),
            json!({"Percent": -1}),
            json!({"Percent": "1"}),
            json!({"Percent": null}),
            json!({"DurationSeconds": 1, "DurationUnit": "s", "Percent": 0}),
        ] {
            let plan = json!({"Version": 15, "StageDurationSeconds": 100, "Stages": [stage]});
            assert!(validate_plan_durations(&plan).is_err(), "accepted {plan}");
        }
        for stages in [json!(null), json!({}), json!([]), json!("stages")] {
            assert!(validate_plan_durations(&json!({"Stages": stages})).is_err());
        }
        for plan in [json!(null), json!([]), json!(1), json!("plan")] {
            assert!(validate_plan_durations(&plan).is_err());
        }
        for budget in [
            json!(-1),
            json!(1.5),
            json!(2147483648_u64),
            json!("100"),
            json!(null),
        ] {
            assert!(validate_plan_durations(&json!({"StageDurationSeconds": budget})).is_err());
        }
        for token in [
            "18446744073709551616123456789",
            "2147483647.00000000001",
            "1e1000",
        ] {
            for field in ["DurationSeconds", "StageDurationSeconds"] {
                let mut plan = json!({"Stages": [{"DurationSeconds": 1, "DurationUnit": "s"}]});
                let value: Value = serde_json::from_str(token).unwrap();
                if field == "DurationSeconds" {
                    plan["Stages"][0][field] = value;
                } else {
                    plan[field] = value;
                }
                assert!(validate_plan_durations(&plan).is_err(), "accepted {plan}");
            }
        }
        let too_large: Value = serde_json::from_str("1e1000").unwrap();
        assert!(validate_plan_durations(&json!({"Stages": [{"Percent": too_large}]})).is_err());
        assert!(validate_plan_durations(
            &json!({"Stages": [{"Percent": 1e308}, {"Percent": 1e308}]})
        )
        .unwrap_err()
        .contains("finite total"));
    }

    #[test]
    fn duration_seconds_selects_mode_without_rewriting_display_hints() {
        for unit in ["%", "s", "min", "h"] {
            let plan = json!({"Version": 15, "StageDurationSeconds": 100, "Stages": [
                {"DurationSeconds": 40, "DurationUnit": unit},
                {"DurationSeconds": 0, "DurationUnit": unit},
                {"DurationSeconds": 1}
            ]});
            let before = serde_json::to_string(&plan).unwrap();
            validate_plan_durations(&plan).unwrap();
            assert_eq!(serde_json::to_string(&plan).unwrap(), before);
        }
    }

    #[test]
    fn offline_creation_merge_and_persistence_are_lossless_and_validate_before_writing() {
        let dir = tempfile::tempdir().unwrap();
        let runtime_dir = dir.path().to_string_lossy().into_owned();
        let mut source = parse_json_object(br#"{"Version":15,"StageDurationSeconds":100,"Stages":[{"DurationSeconds":40,"DurationUnit":"min","Percent":0.00001,"FutureStage":18446744073709551616123456789},{"Percent":0.00003}],"Future":1.2300e+42,"RebirthHistory":[123]}"#, "source").unwrap();
        source[REVISION_FIELD] = json!(file_revision(b"source"));
        let name = tauri::async_runtime::block_on(create_plan(
            runtime_dir.clone(),
            "copy".into(),
            Some(serde_json::to_string(&source).unwrap()),
        ))
        .unwrap();
        let path = plan_path(dir.path(), &name).unwrap();
        let mut stored = read_json_object(&path, MAX_JSON_BYTES, false).unwrap();
        assert_eq!(
            stored["Stages"],
            normalize_offline_plan(source.clone()).unwrap()["Stages"]
        );
        assert_eq!(stored["Future"].to_string(), "1.2300e+42");
        assert_eq!(stored["RebirthHistory"], json!([]));
        assert!(stored.get(REVISION_FIELD).is_none());
        stored["RebirthHistory"] = json!([{"id": 123}]);
        write_json_atomic(&path, &stored, true).unwrap();
        let original = fs::read(&path).unwrap();
        let revision = file_revision(&original);
        let mut patch = stored.clone();
        patch["StageDurationSeconds"] = json!(-1);
        patch["RebirthHistory"] = json!([]);
        assert!(save_plan_offline(dir.path(), &name, &patch, Some(&revision)).is_err());
        assert_eq!(fs::read(&path).unwrap(), original);
        patch["StageDurationSeconds"] = json!(200);
        save_plan_offline(dir.path(), &name, &patch, Some(&revision)).unwrap();
        let saved = read_json_object(&path, MAX_JSON_BYTES, false).unwrap();
        stored["StageDurationSeconds"] = json!(200);
        assert_eq!(saved, stored);
        assert_eq!(
            saved["Stages"][0]["FutureStage"].to_string(),
            "18446744073709551616123456789"
        );
        assert_eq!(saved["Future"].to_string(), "1.2300e+42");
        assert!(
            save_plan_offline(dir.path(), &name, &patch, Some(&revision))
                .unwrap_err()
                .contains("conflict")
        );
        source["StageDurationSeconds"] = json!(-1);
        assert!(tauri::async_runtime::block_on(create_plan(
            runtime_dir.clone(),
            "bad-copy".into(),
            Some(serde_json::to_string(&source).unwrap())
        ))
        .is_err());
        assert!(!plan_path(dir.path(), "bad-copy").unwrap().exists());
        assert!(save_plan_offline(dir.path(), "bad-save", &source, None).is_err());
        assert!(!plan_path(dir.path(), "bad-save").unwrap().exists());
        let default_name =
            tauri::async_runtime::block_on(create_plan(runtime_dir, "new".into(), None)).unwrap();
        let default = read_json_object(
            &plan_path(dir.path(), &default_name).unwrap(),
            MAX_JSON_BYTES,
            false,
        )
        .unwrap();
        assert_eq!(default["Version"], 18);
        assert_eq!(default["Stages"], default_plan()["Stages"]);
    }

    #[test]
    fn all_plan_reads_and_writes_require_runtime_v18() {
        let dir = tempfile::tempdir().unwrap();
        let runtime_path = dir.path().join("runtime.json");
        let mut runtime = json!({"externalUiEnabled": true, "updatedAt": Utc::now().to_rfc3339(), "status": "running", "protocolVersion": 3, "stageTimingVersion": 1, "fightScheduleVersion": 1, "globalNguWeightsVersion": 1, "supportedCommands": ["readPlan", "savePlan", "createPlan"]});
        let fixed = json!({"Version": 15, "StageDurationSeconds": 100, "Stages": [
            {"DurationSeconds": 40, "DurationUnit": "h", "Percent": 1, "Future": {"keep": true}},
            {"DurationSeconds": 0, "DurationUnit": "%", "Percent": 3}
        ]});
        for version in [None, Some(14), Some(15), Some(16), Some(17), Some(18)] {
            if let Some(version) = version {
                runtime["planVersion"] = json!(version);
            }
            write_json_atomic(&runtime_path, &runtime, true).unwrap();
            for (command, field, mut args) in [
                (
                    "savePlan",
                    "plan",
                    json!({"name": "copy", "expectedRevision": ""}),
                ),
                ("createPlan", "source", json!({"requestedName": "copy"})),
            ] {
                args[field] = fixed.clone();
                let outcome = queue_command(dir.path(), command, args);
                if version.unwrap_or(0) < 18 {
                    let err = outcome.unwrap_err();
                    assert!(err.contains("runtime.planVersion >= 18"));
                    assert!(!dir.path().join("commands").exists());
                } else {
                    let id = outcome.unwrap();
                    let path = dir.path().join("commands").join(format!("{id}.json"));
                    let payload = read_json_object(&path, MAX_COMMAND_BYTES, false).unwrap();
                    let mut expected = normalize_offline_plan(fixed.clone()).unwrap();
                    if command == "createPlan" {
                        expected["RebirthHistory"] = json!([]);
                    }
                    assert_eq!(payload["args"][field], expected);
                    remove_if_present(&path).unwrap();
                }
            }
        }
        runtime["planVersion"] = json!(14);
        write_json_atomic(&runtime_path, &runtime, true).unwrap();
        let legacy_weighted = json!({"Version": 14, "Stages": [{"Percent": 1}]});
        for (command, args) in [
            (
                "savePlan",
                json!({"name": "weighted", "plan": legacy_weighted, "expectedRevision": ""}),
            ),
            (
                "createPlan",
                json!({"requestedName": "weighted", "source": legacy_weighted}),
            ),
            (
                "createPlan",
                json!({"requestedName": "default", "source": {"Version": 14, "ProtectedStageTiming": false}}),
            ),
        ] {
            assert!(queue_command(dir.path(), command, args)
                .unwrap_err()
                .contains("runtime.planVersion >= 18"));
        }
        assert!(queue_command(dir.path(), "readPlan", json!({"name": "copy"})).is_err());
        assert_eq!(
            fs::read_dir(dir.path().join("commands")).unwrap().count(),
            0
        );
        assert_eq!(protocol_info()["planVersion"], 18);
    }

    #[test]
    fn connected_canonical_plan_and_runtime_result_data_are_not_filtered() {
        let dir = tempfile::tempdir().unwrap();
        let id = Uuid::new_v4().to_string();
        let mut plan = default_plan();
        plan["Version"] = json!(18);
        plan["Stages"][0]["DurationSeconds"] = json!(61);
        plan["Stages"][0]["DurationUnit"] = json!("min");
        plan[REVISION_FIELD] = json!(file_revision(b"original"));
        let runtime = json!({"planVersion": 15, "fps": 60.25, "Future": {"keep": true}});
        write_json_atomic(&dir.path().join("runtime.json"), &runtime, false).unwrap();
        assert_eq!(read_runtime(dir.path()).unwrap(), runtime);
        let result = json!({"id": id, "command": "readPlan", "success": true, "message": "ok", "completedAt": Utc::now().to_rfc3339(), "data": {"plan": plan, "runtime": runtime}});
        write_json_atomic(
            &dir.path()
                .join("command-results")
                .join(format!("{id}.json")),
            &result,
            false,
        )
        .unwrap();
        let response = wait_for_command(dir.path(), &id, "readPlan", Instant::now()).unwrap();
        assert_eq!(response["result"], result);
        assert_eq!(response["result"]["data"]["runtime"], runtime);
        let returned = connected_plan_json(&response).unwrap();
        assert_eq!(
            parse_json_object(returned.as_bytes(), "plan").unwrap(),
            plan
        );
        // Read-only transport must not apply write validation or normalize stored values.
        let mut unvalidated_response = response;
        unvalidated_response["result"]["data"]["plan"]["StageDurationSeconds"] = json!(0);
        unvalidated_response["result"]["data"]["plan"]["Stages"][0]["Percent"] = json!(0);
        let returned = connected_plan_json(&unvalidated_response).unwrap();
        assert_eq!(
            parse_json_object(returned.as_bytes(), "unvalidated plan").unwrap(),
            unvalidated_response["result"]["data"]["plan"]
        );
    }
}

#[cfg(test)]
mod protocol_tests {
    use super::*;

    #[test]
    fn live_readiness_requires_running_not_just_a_recent_heartbeat() {
        let now = Utc::now().timestamp_millis();
        let mut runtime = json!({"externalUiEnabled": true, "updatedAt": Utc::now().to_rfc3339(), "status": "running"});
        assert!(runtime_is_recent(&runtime, now));
        for status in ["failed", "starting", "unloaded", "paused", ""] {
            runtime["status"] = json!(status);
            assert!(
                !runtime_is_recent(&runtime, now),
                "accepted status {status}"
            );
        }
        runtime.as_object_mut().unwrap().remove("status");
        assert!(!runtime_is_recent(&runtime, now));
    }

    #[test]
    fn ownership_excludes_other_owners_and_is_released_only_on_drop() {
        let dir = tempfile::tempdir().unwrap();
        let owner = lock_injector_ownership(dir.path()).unwrap();
        assert!(lock_injector_ownership(dir.path()).is_err());
        #[cfg(windows)]
        {
            use std::os::windows::fs::OpenOptionsExt;
            assert!(OpenOptions::new()
                .create(true)
                .truncate(false)
                .read(true)
                .write(true)
                .share_mode(0)
                .open(dir.path().join(OWNERSHIP_FILE))
                .is_err());
        }
        drop(owner);
        assert!(dir.path().join(OWNERSHIP_FILE).exists());
        assert!(lock_injector_ownership(dir.path()).is_ok());
    }

    #[test]
    fn owned_directory_rejects_all_offline_mutations_despite_bad_or_missing_heartbeat() {
        let dir = tempfile::tempdir().unwrap();
        let runtime_dir = dir.path().to_string_lossy().into_owned();
        let settings_path = dir.path().join("settings.json");
        write_json_atomic(&settings_path, &json!({"_globalEnabled": false}), false).unwrap();
        let settings_before = fs::read(&settings_path).unwrap();
        for name in ["copy", "default"] {
            write_json_atomic(
                &plan_path(dir.path(), name).unwrap(),
                &prepare_plan_copy(default_plan(), name).unwrap(),
                false,
            )
            .unwrap();
        }
        let copy_path = plan_path(dir.path(), "copy").unwrap();
        let copy_before = fs::read(&copy_path).unwrap();
        let owner = lock_injector_ownership(dir.path()).unwrap();
        for runtime in [
            None,
            Some(
                json!({"externalUiEnabled": true, "status": "running", "updatedAt": "2000-01-01T00:00:00Z"}),
            ),
            Some(
                json!({"externalUiEnabled": true, "status": "starting", "updatedAt": Utc::now().to_rfc3339()}),
            ),
            Some(
                json!({"externalUiEnabled": true, "status": "failed", "updatedAt": Utc::now().to_rfc3339()}),
            ),
        ] {
            if let Some(runtime) = runtime {
                write_json_atomic(&dir.path().join("runtime.json"), &runtime, true).unwrap();
            }
            let err = tauri::async_runtime::block_on(save_settings_patch(
                runtime_dir.clone(),
                json!({"_globalEnabled": true}),
            ))
            .unwrap_err();
            assert!(err.contains("ownership"));
            assert!(tauri::async_runtime::block_on(create_plan(
                runtime_dir.clone(),
                "new-copy".into(),
                None
            ))
            .is_err());
            assert!(tauri::async_runtime::block_on(delete_plan(
                runtime_dir.clone(),
                "copy".into()
            ))
            .is_err());
            assert!(
                tauri::async_runtime::block_on(read_plan(runtime_dir.clone(), "copy".into()))
                    .is_err()
            );
            assert!(save_plan_offline(
                dir.path(),
                "copy",
                &default_plan(),
                Some(&file_revision(&copy_before))
            )
            .is_err());
            assert!(set_external_ui_enabled(runtime_dir.clone(), true).is_err());
            assert_eq!(fs::read(&settings_path).unwrap(), settings_before);
            assert_eq!(fs::read(&copy_path).unwrap(), copy_before);
            assert!(!plan_path(dir.path(), "new-copy").unwrap().exists());
            assert!(!dir.path().join(MARKER_FILE).exists());
        }
        drop(owner);
        tauri::async_runtime::block_on(save_settings_patch(
            runtime_dir,
            json!({"_globalEnabled": true}),
        ))
        .unwrap();
        assert_eq!(
            read_json_object(&settings_path, MAX_JSON_BYTES, false).unwrap()["_globalEnabled"],
            true
        );
    }

    #[test]
    fn cas_hashes_exact_original_bytes_and_never_persists_transport_metadata() {
        assert_eq!(
            file_revision(b"abc"),
            "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"
        );
        let dir = tempfile::tempdir().unwrap();
        let path = plan_path(dir.path(), "copy").unwrap();
        let original = b"\xef\xbb\xbf{\r\n  \"Version\":17,\"Name\":\"copy\",\"Future\":42,\"RebirthHistory\":[1]\r\n}\r\n";
        write_atomic(&path, original, false).unwrap();
        let transport = read_plan_json(&path).unwrap();
        let mut plan = parse_json_object(transport.as_bytes(), "Test plan").unwrap();
        let expected = take_plan_revision(&mut plan).unwrap().unwrap();
        assert_eq!(expected, file_revision(original));
        assert_ne!(
            expected,
            file_revision(serde_json::to_string(&plan).unwrap().as_bytes())
        );
        plan["TimeTriggerSeconds"] = json!(7200);
        plan["RebirthHistory"] = json!([]);
        save_plan_offline(dir.path(), "copy", &plan, Some(&expected)).unwrap();
        let saved_bytes = fs::read(&path).unwrap();
        let saved = parse_json_object(&saved_bytes, "Saved plan").unwrap();
        assert!(saved.get(REVISION_FIELD).is_none());
        assert_eq!(saved["RebirthHistory"], json!([1]));
        assert_eq!(saved["Future"], 42);
        assert_eq!(saved["TimeTriggerSeconds"], 7200);
        assert!(save_plan_offline(dir.path(), "copy", &plan, None)
            .unwrap_err()
            .contains("required"));
        assert!(
            save_plan_offline(dir.path(), "copy", &plan, Some(&expected))
                .unwrap_err()
                .contains("conflict")
        );
        assert_eq!(fs::read(&path).unwrap(), saved_bytes);
        let revision = file_revision(&saved_bytes);
        let mut reformatted = saved_bytes.clone();
        reformatted.push(b'\n');
        write_atomic(&path, &reformatted, true).unwrap();
        assert!(save_plan_offline(dir.path(), "copy", &plan, Some(&revision)).is_err());
        assert_eq!(fs::read(&path).unwrap(), reformatted);
        remove_if_present(&path).unwrap();
        assert!(save_plan_offline(dir.path(), "copy", &plan, Some(&revision)).is_err());
        assert!(!path.exists());
        save_plan_offline(dir.path(), "copy", &plan, None).unwrap();
        assert!(path.exists());
    }

    #[test]
    fn legacy_plan_requires_connected_migration_and_is_never_silently_rewritten() {
        let dir = tempfile::tempdir().unwrap();
        let path = plan_path(dir.path(), "legacy").unwrap();
        for legacy in [
            b"{\"Version\":13,\"Future\":42}".as_slice(),
            b"{\"Future\":42}",
        ] {
            write_atomic(&path, legacy, true).unwrap();
            let err = read_plan_json(&path).unwrap_err();
            assert!(err.contains("Connect to the injector"));
            assert!(save_plan_offline(
                dir.path(),
                "legacy",
                &default_plan(),
                Some(&file_revision(legacy))
            )
            .is_err());
            assert_eq!(fs::read(&path).unwrap(), legacy);
        }
        let legacy = "{\"Version\":13}".to_string();
        assert!(tauri::async_runtime::block_on(create_plan(
            dir.path().to_string_lossy().into_owned(),
            "legacy-copy".into(),
            Some(legacy)
        ))
        .is_err());
        assert!(!plan_path(dir.path(), "legacy-copy").unwrap().exists());
    }

    #[test]
    fn lossless_transport_and_offline_merge_preserve_large_numeric_lexemes() {
        let dir = tempfile::tempdir().unwrap();
        let path = plan_path(dir.path(), "precise").unwrap();
        let original = br#"{"Version":17,"FutureInteger":18446744073709551616123456789,"FutureNegative":-9223372036854775809123456789,"FutureDecimal":1.2345678901234567890123456789,"FutureExponent":1.2300e+42}"#;
        write_atomic(&path, original, false).unwrap();
        let transport = read_plan_json(&path).unwrap();
        let mut plan = parse_json_object(transport.as_bytes(), "Test plan").unwrap();
        for (field, lexeme) in [
            ("FutureInteger", "18446744073709551616123456789"),
            ("FutureNegative", "-9223372036854775809123456789"),
            ("FutureDecimal", "1.2345678901234567890123456789"),
            ("FutureExponent", "1.2300e+42"),
        ] {
            assert_eq!(plan[field].to_string(), lexeme);
        }
        let expected = take_plan_revision(&mut plan).unwrap().unwrap();
        plan["TimeTriggerSeconds"] = json!(7200);
        plan[REVISION_FIELD] = json!(expected);
        tauri::async_runtime::block_on(save_plan(
            dir.path().to_string_lossy().into_owned(),
            "precise".into(),
            serde_json::to_string(&plan).unwrap(),
        ))
        .unwrap();
        let saved = read_json_object(&path, MAX_JSON_BYTES, false).unwrap();
        for field in [
            "FutureInteger",
            "FutureNegative",
            "FutureDecimal",
            "FutureExponent",
        ] {
            assert_eq!(saved[field].to_string(), plan[field].to_string());
        }
        assert!(saved.get(REVISION_FIELD).is_none());
        let settings_path = dir.path().join("settings.json");
        write_atomic(
            &settings_path,
            br#"{"_globalEnabled":false,"Future":18446744073709551616123456789}"#,
            false,
        )
        .unwrap();
        tauri::async_runtime::block_on(save_settings_patch(
            dir.path().to_string_lossy().into_owned(),
            json!({"_globalEnabled": true}),
        ))
        .unwrap();
        assert_eq!(
            read_json_object(&settings_path, MAX_JSON_BYTES, false).unwrap()["Future"].to_string(),
            "18446744073709551616123456789"
        );
    }

    #[test]
    fn connected_revision_payload_and_plan_result_remain_lossless_and_bounded() {
        let dir = tempfile::tempdir().unwrap();
        let runtime = json!({"externalUiEnabled": true, "updatedAt": Utc::now().to_rfc3339(), "status": "running", "protocolVersion": 3, "planVersion": 18, "fightScheduleVersion": 1, "stageTimingVersion": 1, "globalNguWeightsVersion": 1, "supportedCommands": ["readPlan", "savePlan", "createPlan"]});
        write_json_atomic(&dir.path().join("runtime.json"), &runtime, false).unwrap();
        let new_id = queue_command(
            dir.path(),
            "savePlan",
            json!({"name": "new-plan", "plan": {"Version": 14}, "expectedRevision": ""}),
        )
        .unwrap();
        let new_payload = read_json_object(
            &dir.path().join("commands").join(format!("{new_id}.json")),
            MAX_COMMAND_BYTES,
            false,
        )
        .unwrap();
        assert_eq!(new_payload["args"]["expectedRevision"], "");
        assert!(queue_command(
            dir.path(),
            "savePlan",
            json!({"name": "new-plan", "plan": {"Version": 14}, "expectedRevision": null})
        )
        .is_err());
        write_json_atomic(
            &plan_path(dir.path(), "existing").unwrap(),
            &default_plan(),
            false,
        )
        .unwrap();
        assert!(queue_command(
            dir.path(),
            "savePlan",
            json!({"name": "existing", "plan": {"Version": 14}, "expectedRevision": ""})
        )
        .is_err());
        let mut plan = parse_json_object(
            br#"{"Version":14,"Future":18446744073709551616123456789}"#,
            "Test plan",
        )
        .unwrap();
        let revision = file_revision(b"original file bytes");
        plan[REVISION_FIELD] = json!(revision);
        let id = queue_command(
            dir.path(),
            "savePlan",
            json!({"name": "precise", "plan": plan}),
        )
        .unwrap();
        let payload = read_json_object(
            &dir.path().join("commands").join(format!("{id}.json")),
            MAX_COMMAND_BYTES,
            false,
        )
        .unwrap();
        assert_eq!(payload["args"]["expectedRevision"], revision);
        assert!(payload["args"]["plan"].get(REVISION_FIELD).is_none());
        assert_eq!(
            payload["args"]["plan"]["Future"].to_string(),
            "18446744073709551616123456789"
        );
        assert!(queue_command(
            dir.path(),
            "savePlan",
            json!({"name": "precise", "plan": plan, "expectedRevision": "invalid"})
        )
        .is_err());
        let source_id = queue_command(
            dir.path(),
            "createPlan",
            json!({"requestedName": "copy", "source": plan}),
        )
        .unwrap();
        let source = read_json_object(
            &dir.path()
                .join("commands")
                .join(format!("{source_id}.json")),
            MAX_COMMAND_BYTES,
            false,
        )
        .unwrap();
        assert!(source["args"]["source"].get(REVISION_FIELD).is_none());
        assert_eq!(source["args"]["source"]["RebirthHistory"], json!([]));
        assert_eq!(
            source["args"]["source"]["Future"].to_string(),
            "18446744073709551616123456789"
        );
        let id = queue_command(dir.path(), "readPlan", json!({"name": "precise"})).unwrap();
        plan = normalize_offline_plan(plan).unwrap();
        plan["FutureBlob"] = json!("x".repeat(ACTION_RESULT_MAX_BYTES as usize));
        let mut result = json!({"id": id, "command": "readPlan", "success": true, "message": "ok", "completedAt": Utc::now().to_rfc3339(), "data": {"plan": plan}});
        let result_path = dir
            .path()
            .join("command-results")
            .join(format!("{id}.json"));
        write_json_atomic(&result_path, &result, false).unwrap();
        assert!(read_result(dir.path(), &id, None).unwrap().is_some());
        let response = wait_for_command(dir.path(), &id, "readPlan", Instant::now()).unwrap();
        let transport = connected_plan_json(&response).unwrap();
        assert!(transport.contains("18446744073709551616123456789"));
        let parsed = parse_json_object(transport.as_bytes(), "Returned plan").unwrap();
        assert_eq!(parsed[REVISION_FIELD], revision);
        assert_eq!(
            parsed["FutureBlob"].as_str().unwrap().len(),
            ACTION_RESULT_MAX_BYTES as usize
        );
        assert!(
            connected_plan_json(&json!({"result": {"data": {"plan": {"Version":14}}}})).is_err()
        );
        result["command"] = json!("safeProbe");
        write_json_atomic(&result_path, &result, true).unwrap();
        assert!(read_result(dir.path(), &id, None).is_err());
        assert!(read_file_bytes(&result_path, 10, false).is_err());
    }

    #[test]
    fn saved_custom_tuning_matches_frontend_and_injector_bounds() {
        for (key, min, max) in [
            ("_customAutoBoostCubePowerRatio", 1, 4),
            ("_customAugmentsBloodEstimateSeconds", 1, 100000),
            ("_customBloodSpaghettiGoalPercent", 0, 100000),
            ("_customBloodCounterfeitGoalPercent", 0, 100000),
        ] {
            for valid in [min, max] {
                let mut patch = json!({});
                patch[key] = json!(valid);
                assert!(validate_settings_patch(&patch).is_ok(), "{key}: {valid}");
                assert!(validate_offline_settings_patch(&patch).is_ok());
            }
            for invalid in [json!(min - 1), json!(max + 1), json!(1.5), json!("2")] {
                let mut patch = json!({});
                patch[key] = invalid;
                assert!(validate_settings_patch(&patch).is_err(), "{key}: {patch}");
            }
        }
    }
}
