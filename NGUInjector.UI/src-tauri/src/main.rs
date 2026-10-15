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

const MARKER_FILE: &str = "external-ui.enabled";
const OWNERSHIP_FILE: &str = ".injector.lock";
const REVISION_FIELD: &str = "__externalUiRevision";
const HEARTBEAT_MAX_AGE_MS: i64 = 5_000;
const FUTURE_TOLERANCE_MS: i64 = 2_000;
const COMMAND_TIMEOUT_MS: u64 = 10_000;
const MAX_JSON_BYTES: u64 = 4 * 1024 * 1024;
const MAX_RUNTIME_BYTES: u64 = 1024 * 1024;
const MAX_RESULT_BYTES: u64 = 64 * 1024;
const MAX_PLAN_RESULT_BYTES: u64 = MAX_JSON_BYTES + MAX_RESULT_BYTES;
const MAX_COMMAND_BYTES: u64 = 1024 * 1024;
const MAX_LOG_BYTES: u64 = 256 * 1024;
const MAX_LOG_LINES: usize = 2_000;
const MAX_RESULTS: usize = 20;

#[derive(Debug, Serialize)]
struct NguiState {
    runtime_dir: String,
    marker_enabled: bool,
    connected: bool,
    protocol: Value,
    settings: Value,
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
    json!({
        "version": 3,
        "planVersion": 16,
        "nguWeights": {"EnergyNguWeights": "9 integer entries, each 0..2147483647", "MagicNguWeights": "7 integer entries, each 0..2147483647", "allZero": "Allowed; disables that resource's NGUs", "version": "Both arrays are required for Version >= 16; either field requires Version >= 16", "liveWrites": "savePlan/createPlan sources containing either weight field require runtime.planVersion >= 16, including all-zero arrays", "offlineMigration": "Version 14/15 plans remain editable without weights. Connect to an updated injector and reload the legacy plan to migrate to Version 16 before editing NGU weights"},
        "stageDurations": {"DurationSeconds": "Int32: 0 uses positive Percent weight; > 0 is fixed whole seconds", "DurationUnit": "%, s, min, or h; display hint only, DurationSeconds selects weighted or fixed mode", "effectiveDuration": "Fixed seconds are exact; weighted stages share StageDurationSeconds minus fixed total in proportion to their Percent weights. Fixed total must be less than the budget when weighted stages exist; all-fixed total is the sum of fixed seconds (Int32). AT-target rows (CompletionMode 1) retain validated duration fields but are excluded from timed totals.", "liveWrites": "Fixed-duration savePlan/createPlan source requires runtime.planVersion >= 15; update and reload an older injector before writing"},
        "requiredRuntimeField": {"protocolVersion": 3},
        "supportedCommandsField": "supportedCommands (must include the requested command)",
        "commandIdFormat": "canonical UUID v4",
        "timestampFormat": "ISO-8601 UTC (createdAt/expiresAt); updatedAt accepts an ISO-8601 offset",
        "heartbeatMaxAgeMs": HEARTBEAT_MAX_AGE_MS,
        "commandTimeoutMs": COMMAND_TIMEOUT_MS,
        "maxCommandBytes": MAX_COMMAND_BYTES,
        "maxResultBytes": MAX_RESULT_BYTES,
        "maxPlanResultBytes": MAX_PLAN_RESULT_BYTES,
        "readiness": "externalUiEnabled == true, status == running, and a recent updatedAt heartbeat",
        "settingsCommand": {"command": "applySettingsPatch", "args": {"patch": "JSON object"}},
        "readPlanCommand": {"command": "readPlan", "args": {"name": "safe file stem"}, "resultPlan": "data.plan: normalized lossless JSON object with __externalUiRevision"},
        "planCommand": {"command": "savePlan", "args": {"name": "safe file stem", "plan": "JSON object without __externalUiRevision", "expectedRevision": "SHA-256 of exact original file bytes; required for existing plans; empty string only for a nonexistent file (never null)"}},
        "tauriPlanTransport": {"read_plan": "returns a JSON STRING with __externalUiRevision", "save_plan": "args runtimeDir/name/planJson (JSON STRING with revision)", "create_plan": "args runtimeDir/requestedName/sourceJson (optional JSON STRING)"},
        "revision": "__externalUiRevision is a transport-only lowercase SHA-256; never persist it; preserve it while editing and reload after saving",
        "offlineMigration": "Plans with missing/legacy Version < 14 must be read through a connected injector to normalize migrations",
        "createPlanCommand": {"command": "createPlan", "args": {"requestedName": "safe file stem", "source": "optional JSON object; copy history is cleared"}, "resultName": "data.name"},
        "deletePlanCommand": {"command": "deletePlan", "args": {"name": "safe file stem"}, "resultSelection": "data.selectedPlan"},
        "augmentCommands": {"commands": ["previewAugments", "allocateAugments"], "args": "{} or {expectedSeconds: positive Int32}"},
        "expiryRequirement": "Bridge must check expiresAt immediately before main-thread execution; expired requests must fail and never replay (including claimed requests)",
        "resultFields": ["id", "command", "success", "message", "completedAt"],
        "marker": "external-ui.enabled is a startup opt-in, not proof of connection",
        "planManagement": "Connected creation/deletion use the main-thread queue; offline deletion protects built-ins, the selected running plan, and the last plan",
        "offlineSettingsPatches": "Only known serialized settings fields are editable; unknown stored fields are preserved",
        "offlineWriterLock": ".injector.lock is held across every offline read/modify/write with Windows FileShare.None semantics (fs2 exclusive lock on non-Windows); stale/missing heartbeat does not authorize writes when injector ownership is held; .external-ui-write.lock serializes UI operations"
    })
}

#[tauri::command]
fn get_ngui_state(runtime_dir: Option<String>) -> Result<NguiState, String> {
    let dir = resolve_runtime_dir(runtime_dir)?;
    let settings = read_json_object(&dir.join("settings.json"), MAX_JSON_BYTES, true)?;
    let runtime = read_runtime(&dir)?;
    Ok(NguiState {
        runtime_dir: dir.to_string_lossy().into_owned(),
        marker_enabled: dir.join(MARKER_FILE).is_file(),
        connected: runtime_is_recent(&runtime, Utc::now().timestamp_millis()),
        protocol: protocol_info(),
        settings,
        runtime,
        profiles: list_json_stems(&dir.join("profiles"))?,
        plans: list_json_stems(&dir.join("custom-plans"))?,
        logs: list_logs(&dir.join("logs"))?,
        command_results: list_command_results(&dir.join("command-results"))?,
    })
}

#[tauri::command]
async fn save_settings_patch(runtime_dir: String, patch: Value) -> Result<Value, String> {
    tauri::async_runtime::spawn_blocking(move || {
        validate_settings_patch(&patch)?;
        let dir = resolve_runtime_dir(Some(runtime_dir))?;
        let lock = lock_offline_writer(&dir)?;
        let runtime = read_runtime(&dir)?;
        if runtime_is_recent(&runtime, Utc::now().timestamp_millis()) {
            require_live_bridge(&runtime)?;
            let deadline = Instant::now() + Duration::from_millis(COMMAND_TIMEOUT_MS);
            let id = queue_command(&dir, "applySettingsPatch", json!({"patch": patch}))?;
            drop(lock);
            return wait_for_command(&dir, &id, "applySettingsPatch", deadline);
        }
        let _ownership = lock_injector_ownership(&dir)?;
        let path = dir.join("settings.json");
        let mut settings = read_json_object(&path, MAX_JSON_BYTES, true)?;
        validate_offline_settings_patch(&patch)?;
        merge_object_patch(&mut settings, &patch)?;
        write_json_atomic(&path, &settings, true)?;
        Ok(json!({"success": true, "mode": "offline", "protocol": protocol_info()}))
    })
    .await
    .map_err(|err| format!("Settings worker failed: {err}"))?
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
            validate_plan_ngu_weights(&plan)?;
            validate_plan_durations(&plan)?;
            write_json_atomic(&plan_path(&dir, &name)?, &plan, false)?;
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
    merge_object_patch(&mut existing, plan)?;
    let object = existing.as_object_mut().unwrap();
    if object.get("Version").and_then(Value::as_u64).unwrap_or(0) >= 16 {
        object.remove("NguGoal");
    }
    object.insert("RebirthHistory".into(), history);
    object.insert("Name".into(), json!(name));
    object.remove(REVISION_FIELD);
    Ok(existing)
}

fn prepare_plan_copy(mut plan: Value, name: &str) -> Result<Value, String> {
    validate_file_name(name)?;
    require_object(&plan, "Plan source")?;
    let object = plan.as_object_mut().unwrap();
    if object.get("Version").and_then(Value::as_u64).unwrap_or(0) >= 16 {
        object.remove("NguGoal");
    }
    object.insert("Name".into(), json!(name));
    object.insert("RebirthHistory".into(), json!([]));
    object.remove(REVISION_FIELD);
    Ok(plan)
}

fn file_revision(bytes: &[u8]) -> String {
    format!("{:x}", Sha256::digest(bytes))
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
    if !plan
        .get("Version")
        .and_then(Value::as_u64)
        .is_some_and(|version| version >= 14)
    {
        return Err("Legacy plan (missing Version or Version < 14) cannot be edited offline. Connect to the injector and read the plan to apply current migrations without losing fields".into());
    }
    Ok(())
}

fn validate_plan_ngu_weights(plan: &Value) -> Result<(), String> {
    require_object(plan, "Plan")?;
    let version = plan.get("Version").and_then(Value::as_u64).unwrap_or(0);
    let fields = [("EnergyNguWeights", 9), ("MagicNguWeights", 7)];
    if version < 16 {
        if fields.iter().any(|(field, _)| plan.get(*field).is_some()) {
            return Err("NGU weight fields require Version >= 16. Connect to an updated injector and reload the legacy plan to apply migrations before editing NGU weights".into());
        }
        return Ok(());
    }
    for (field, length) in fields {
        let weights = plan
            .get(field)
            .and_then(Value::as_array)
            .filter(|weights| weights.len() == length)
            .ok_or_else(|| format!("{field} is required for Version >= 16 and must be an array of exactly {length} integers"))?;
        if !weights.iter().all(|weight| {
            let Value::Number(number) = weight else {
                return false;
            };
            // Match C# using exact decimal digits; f64 would round or underflow fractions.
            let token = number.to_string();
            let (mantissa, exponent) = token.split_once(['e', 'E']).unwrap_or((&token, "0"));
            let digits = mantissa.replace('.', "");
            let digits = digits.trim_start_matches('-').trim_start_matches('0');
            if digits.is_empty() {
                return true;
            }
            if mantissa.starts_with('-') {
                return false;
            }
            let Ok(exponent) = exponent.parse::<i64>() else {
                return false;
            };
            let significant = digits.trim_end_matches('0');
            let fractional = mantissa.split_once('.').map_or(0, |(_, tail)| tail.len());
            let adjustment = (digits.len() - significant.len()) as i64 - fractional as i64;
            if exponent < -adjustment || exponent > 10 - significant.len() as i64 - adjustment {
                return false;
            }
            let integer = format!(
                "{significant}{}",
                "0".repeat((exponent + adjustment) as usize)
            );
            integer.parse::<i32>().is_ok()
        }) {
            return Err(format!(
                "{field} entries must be integers between 0 and 2147483647"
            ));
        }
    }
    Ok(())
}

fn validate_plan_durations(plan: &Value) -> Result<(), String> {
    require_object(plan, "Plan")?;
    let budget = match plan.get("StageDurationSeconds") {
        None => 1800,
        Some(value) => value
            .as_u64()
            .filter(|seconds| (1..=i32::MAX as u64).contains(seconds))
            .ok_or("StageDurationSeconds must be a positive Int32")?,
    };
    // Missing stages retain the existing C# defaulting behavior, including v14 plans.
    let Some(stages) = plan.get("Stages") else {
        return Ok(());
    };
    let stages = stages
        .as_array()
        .filter(|stages| !stages.is_empty())
        .ok_or("Stages must be a nonempty array of objects")?;
    let mut fixed_total = 0_u64;
    let mut has_weighted = false;
    let mut weight_total = 0.0;
    for (index, stage) in stages.iter().enumerate() {
        let label = format!("Stage {}", index + 1);
        require_object(stage, &label)?;
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
        weight_total += weight;
        if !weight_total.is_finite() {
            return Err("Stage Percent weights must have a finite total".into());
        }
        fixed_total += seconds;
        if fixed_total > i32::MAX as u64 {
            return Err("Total fixed stage duration must not exceed Int32::MAX seconds".into());
        }
        has_weighted |= seconds == 0;
    }
    if has_weighted && fixed_total >= budget {
        return Err("Total fixed stage duration must be less than StageDurationSeconds so weighted stages have time remaining".into());
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
    let (mut plan, revision) = read_plan_snapshot(path, false)?.ok_or("Plan does not exist")?;
    require_current_plan_version(&plan)?;
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
    validate_revision(
        plan.get(REVISION_FIELD)
            .ok_or("readPlan result is missing __externalUiRevision; update the bridge")?,
    )?;
    serde_json::to_string(plan).map_err(|err| err.to_string())
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
    let existing = match snapshot {
        Some((existing, _)) => {
            require_current_plan_version(&existing)?;
            validate_plan_ngu_weights(&existing)?;
            if existing["Version"].as_u64().unwrap_or(0) < 16
                && plan["Version"].as_u64().unwrap_or(0) >= 16
            {
                return Err("Legacy offline plans cannot be migrated to Version 16 here. Connect to an updated injector and reload the legacy plan before editing NGU weights".into());
            }
            existing
        }
        None => empty_object(),
    };
    require_current_plan_version(plan)?;
    validate_plan_ngu_weights(plan)?;
    let merged = merge_plan(existing, plan, name)?;
    validate_plan_ngu_weights(&merged)?;
    validate_plan_durations(&merged)?;
    write_json_atomic(&path, &merged, expected.is_some())
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
    if ["default", "24hr", "1h-3m-rebirth", "infinite"]
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
        "default"
    } else {
        settings
            .get("_customPlanFile")
            .and_then(Value::as_str)
            .unwrap_or("default")
    }
    .to_string();
    validate_file_name(&fallback)?;
    if selected {
        let fallback_path = plan_path(dir, &fallback)?;
        if fallback_path.try_exists().map_err(|err| err.to_string())? {
            read_json_object(&fallback_path, MAX_JSON_BYTES, false)?;
        } else {
            write_json_atomic(
                &fallback_path,
                &prepare_plan_copy(default_plan(), &fallback)?,
                false,
            )?;
        }
        settings["_customPlanFile"] = json!(fallback);
        // Persist selection first: a settings write failure must leave the plan intact.
        write_json_atomic(&settings_path, &settings, true)?;
    }
    fs::remove_file(&path).map_err(|err| format!("Failed to delete plan: {err}"))?;
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

fn validate_offline_settings_patch(patch: &Value) -> Result<(), String> {
    validate_settings_patch(patch)?;
    let is_int32 = |value: &Value| {
        value.as_f64().is_some_and(|number| {
            number.is_finite()
                && number >= i32::MIN as f64
                && number <= i32::MAX as f64
                && number.fract() == 0.0
        })
    };
    // Keep this schema aligned with SavedSettings SerializeField declarations, not keys
    // found in settings.json: future fields may be preserved, but are not editable.
    for (key, value) in patch.as_object().unwrap() {
        let valid = match key.as_str() {
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
            | "_customAutoBoostEnabled"
            | "_customAutoBloodSpellsEnabled"
            | "_manualAdventureEnabled"
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
            | "_customAdventureZone"
            | "_customAutoBoostCubePowerRatio"
            | "_customAugmentsBloodEstimateSeconds"
            | "_customBloodSpaghettiGoalPercent"
            | "_customBloodCounterfeitGoalPercent"
            | "_manualAdventureZone"
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

fn validate_command(command: &str, args: &Value) -> Result<(), String> {
    require_object(args, "Command args")?;
    match command {
        "applySettingsPatch" => {
            validate_settings_patch(args.get("patch").ok_or("Missing args.patch")?)
        }
        "savePlan" => {
            validate_file_name(
                args.get("name")
                    .and_then(Value::as_str)
                    .ok_or("Missing args.name")?,
            )?;
            let plan = args.get("plan").ok_or("Missing args.plan")?;
            validate_plan_ngu_weights(plan)?;
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
                validate_plan_ngu_weights(source)?;
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
        "manualFightPrep" | "manualRebirth" | "safeProbe" | "probeCommands"
        | "precacheLoadouts" | "resetRebirthPrep" | "reloadSettings" | "quickSave" | "unload"
        | "stopFocus" | "status" => {
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
    let written_plan = match command {
        "savePlan" => args.get("plan"),
        "createPlan" => args.get("source"),
        _ => None,
    };
    if written_plan.is_some_and(|plan| {
        plan.get("EnergyNguWeights").is_some() || plan.get("MagicNguWeights").is_some()
    }) && runtime
        .get("planVersion")
        .and_then(Value::as_u64)
        .unwrap_or(0)
        < 16
    {
        return Err("Connected injector does not support NGU weights (runtime.planVersion >= 16 required). Update the injector DLL and reload it, then read the plan again before saving or copying; command was not queued".into());
    }
    if written_plan
        .and_then(|plan| plan.get("Stages"))
        .and_then(Value::as_array)
        .is_some_and(|stages| {
            stages.iter().any(|stage| {
                stage
                    .get("DurationSeconds")
                    .and_then(Value::as_f64)
                    .is_some_and(|seconds| seconds > 0.0)
            })
        })
        && runtime
            .get("planVersion")
            .and_then(Value::as_u64)
            .unwrap_or(0)
            < 15
    {
        return Err("Connected injector does not support fixed stage durations (runtime.planVersion >= 15 required). Update the injector DLL and reload it, then read the plan again before saving or copying; command was not queued".into());
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
    let id = Uuid::new_v4().to_string();
    let now = Utc::now();
    let expires = now + chrono::Duration::milliseconds(COMMAND_TIMEOUT_MS as i64);
    let payload = json!({
        "id": id, "command": command, "args": args,
        "createdAt": now.to_rfc3339_opts(SecondsFormat::Millis, true),
        "expiresAt": expires.to_rfc3339_opts(SecondsFormat::Millis, true)
    });
    let bytes = serde_json::to_vec_pretty(&payload).map_err(|err| err.to_string())?;
    let limit = runtime
        .get("maxCommandBytes")
        .and_then(Value::as_u64)
        .unwrap_or(MAX_COMMAND_BYTES)
        .min(MAX_COMMAND_BYTES);
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
    let limit = if command == Some("readPlan") || command.is_none() {
        MAX_PLAN_RESULT_BYTES
    } else {
        MAX_RESULT_BYTES
    };
    let bytes = read_file_bytes(&path, limit, false)?.ok_or("Command result disappeared")?;
    let result = parse_json_object(&bytes, "Command result")?;
    let summary: CommandResultSummary = serde_json::from_value(result.clone())
        .map_err(|err| format!("Invalid command result: {err}"))?;
    if summary.command != "readPlan" && bytes.len() as u64 > MAX_RESULT_BYTES {
        return Err("Command result exceeds the 64 KiB limit".into());
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
        if !metadata.is_file() || metadata.len() > MAX_RESULT_BYTES {
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
        if let Ok(value) = read_json_object(&path, MAX_RESULT_BYTES, false) {
            if let Ok(summary) = serde_json::from_value::<CommandResultSummary>(value) {
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
            "DurationSeconds": 0, "DurationUnit": "%",
            "GearGoal": gear, "CompletionMode": 0, "BlackBarLookaheadSeconds": 5,
            "EnergyPriorities": [], "MagicPriorities": [],
            "TargetAdvancedTrainingPower": "0", "TargetAdvancedTrainingToughness": "0"
        })
    })
    .collect();
    json!({
        "Version": 16, "Name": "new-plan", "AutoRebirthEnabled": false,
        "TimeTriggerEnabled": true, "TimeTriggerSeconds": 3600,
        "NumberTriggerEnabled": false, "NumberMultiplierTarget": 100.0,
        "NumberClimbEnabled": true, "NumberClimbMinSeconds": 480, "NumberClimbMultiplierTarget": 100.0,
        "TriggerMode": 0, "StageSchedulerEnabled": true, "StageDurationSeconds": 1800,
        "StageLoopStartIndex": 5, "Stages": stages, "AutoActivateFruits": true,
        "EnergyNguWeights": [0, 0, 0, 0, 1, 0, 1, 0, 0], "MagicNguWeights": [1, 0, 0, 0, 0, 0, 0],
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
            save_settings_patch,
            read_plan,
            save_plan,
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
        let existing = json!({"Name": "old", "Future": {"keep": 9}, "RebirthHistory": [{"id": 123}], "Stages": [1]});
        let incoming = json!({"Name": "untrusted", "RebirthHistory": [], "Stages": [2]});
        assert_eq!(
            merge_plan(existing, &incoming, "safe-name").unwrap(),
            json!({
                "Name": "safe-name", "Future": {"keep": 9}, "RebirthHistory": [{"id": 123}], "Stages": [2]
            })
        );
        assert!(merge_plan(json!([]), &incoming, "safe-name").is_err());
        assert!(merge_plan(json!({}), &json!(null), "safe-name").is_err());
        assert_eq!(
            merge_plan(json!({}), &json!({"RebirthHistory": [1]}), "new").unwrap()
                ["RebirthHistory"],
            json!([])
        );
    }

    #[test]
    fn commands_require_allowlist_object_args_and_safe_plan_names() {
        assert!(validate_command("manualRebirth", &json!({})).is_ok());
        assert!(validate_command("unknown", &json!({})).is_err());
        assert!(validate_command("manualRebirth", &json!(null)).is_err());
        assert!(validate_command("manualRebirth", &json!({"unexpected": true})).is_err());
        assert!(validate_command("applySettingsPatch", &json!({"patch": {"future": 1}})).is_ok());
        assert!(validate_command("applySettingsPatch", &json!({"patch": []})).is_err());
        assert!(validate_command("savePlan", &json!({"name": "../escape", "plan": {}})).is_err());
        assert!(validate_command("savePlan", &json!({"name": "new", "plan": {}})).is_ok());
        assert!(validate_command_id(&Uuid::new_v4().to_string()).is_ok());
        assert!(validate_command_id("../escape").is_err());
        assert!(validate_command_id("00000000-0000-0000-0000-000000000000").is_err());
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
            assert!(validate_command("applySettingsPatch", &json!({"patch": patch})).is_err());
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
        result["message"] = json!("x".repeat(MAX_RESULT_BYTES as usize));
        write_json_atomic(&path, &result, true).unwrap();
        assert!(read_result(dir.path(), &id, None).is_err());
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
    fn copies_clear_history_and_default_plan_has_valid_enabled_stages() {
        let copy = prepare_plan_copy(
            json!({"Name": "old", "RebirthHistory": [{"old": true}], "Future": 42}),
            "copy",
        )
        .unwrap();
        assert_eq!(
            copy,
            json!({"Name": "copy", "RebirthHistory": [], "Future": 42})
        );
        assert!(prepare_plan_copy(json!([]), "copy").is_err());
        let mut plan = default_plan();
        assert_eq!(plan["Version"], 16);
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
    fn deletion_guards_builtins_last_plan_and_case_insensitive_running_selection() {
        for name in ["default", "DEFAULT", "24hr", "1h-3m-rebirth", "Infinite"] {
            assert!(validate_plan_deletion(name, &json!({}), 5).is_err());
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
        let settings_path = dir.path().join("settings.json");
        let settings = json!({"_customPlanFile": "copy", "_useCustomRebirthPlan": false, "Future": {"keep": 42}});
        write_json_atomic(&settings_path, &settings, true).unwrap();
        assert_eq!(delete_plan_offline(dir.path(), "copy").unwrap(), "default");
        assert!(!plan_path(dir.path(), "copy").unwrap().exists());
        assert!(plan_path(dir.path(), "other").unwrap().exists());
        let saved = read_json_object(&settings_path, MAX_JSON_BYTES, false).unwrap();
        assert_eq!(saved["_customPlanFile"], "default");
        assert_eq!(saved["_useCustomRebirthPlan"], false);
        assert_eq!(saved["Future"], settings["Future"]);
        let fallback = read_json_object(
            &plan_path(dir.path(), "default").unwrap(),
            MAX_JSON_BYTES,
            false,
        )
        .unwrap();
        assert_eq!(fallback["Name"], "default");
        assert_eq!(fallback["RebirthHistory"], json!([]));
        assert!(!fallback["Stages"].as_array().unwrap().is_empty());
        assert!(delete_plan_offline(dir.path(), "default").is_err());
    }

    #[test]
    fn corrupt_settings_or_fallback_never_delete_the_selected_plan() {
        let dir = tempfile::tempdir().unwrap();
        let _lock = lock_offline_writer(dir.path()).unwrap();
        let copy_path = plan_path(dir.path(), "copy").unwrap();
        let fallback_path = plan_path(dir.path(), "default").unwrap();
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
        let runtime = json!({"externalUiEnabled": true, "updatedAt": Utc::now().to_rfc3339(), "status": "running", "protocolVersion": 3, "supportedCommands": ["createPlan", "deletePlan"]});
        write_json_atomic(&dir.path().join("runtime.json"), &runtime, false).unwrap();
        let id = queue_command(
            dir.path(),
            "createPlan",
            json!({"requestedName": "copy", "source": {"RebirthHistory": [1], "Future": 42}}),
        )
        .unwrap();
        let payload = read_json_object(
            &dir.path().join("commands").join(format!("{id}.json")),
            MAX_COMMAND_BYTES,
            false,
        )
        .unwrap();
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
                json!({"name": "copy", "selectedPlan": "default"}),
                "selectedPlan",
                "default",
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
mod ngu_weight_tests {
    use super::*;

    #[test]
    fn weight_validation_accepts_zero_and_int32_boundaries_without_rewriting() {
        for version in [16, 17] {
            for weight in [0, 1, i32::MAX] {
                let plan = json!({"Version": version, "EnergyNguWeights": vec![weight; 9], "MagicNguWeights": vec![weight; 7]});
                let before = serde_json::to_string(&plan).unwrap();
                validate_plan_ngu_weights(&plan).unwrap();
                assert_eq!(serde_json::to_string(&plan).unwrap(), before);
            }
        }
    }

    #[test]
    fn weight_validation_accepts_exact_whole_decimal_and_exponent_tokens_losslessly() {
        let mut tokens: Vec<String> = [
            "0.0",
            "-0.000",
            "0e-400",
            "-0e999999999999999999999999",
            "1.0",
            "1e0",
            "1.000e0",
            "1.00E+00",
            "0.1e1",
            "1000e-3",
            "10e-1",
            "2147483647.0",
            "2.147483647e9",
            "214748364700e-2",
        ]
        .into_iter()
        .map(str::to_string)
        .collect();
        tokens.push(format!("1{}e-400", "0".repeat(400)));
        tokens.push(format!("0.{}1e401", "0".repeat(400)));
        for token in tokens {
            let weight: Value = serde_json::from_str(&token).unwrap();
            let mut plan = default_plan();
            plan["EnergyNguWeights"] = json!(vec![weight.clone(); 9]);
            plan["MagicNguWeights"] = json!(vec![weight; 7]);
            let before = serde_json::to_string(&plan).unwrap();
            validate_plan_ngu_weights(&plan).unwrap_or_else(|err| panic!("{token}: {err}"));
            for (command, args) in [
                ("savePlan", json!({"name": "valid", "plan": plan})),
                (
                    "createPlan",
                    json!({"requestedName": "valid", "source": plan}),
                ),
            ] {
                validate_command(command, &args).unwrap();
            }
            assert_eq!(serde_json::to_string(&plan).unwrap(), before);
        }
    }

    #[test]
    fn malformed_weights_are_rejected_before_queueing_or_offline_writing() {
        let dir = tempfile::tempdir().unwrap();
        let runtime_dir = dir.path().to_string_lossy().into_owned();
        let runtime = json!({"externalUiEnabled": true, "updatedAt": Utc::now().to_rfc3339(), "status": "running", "protocolVersion": 3, "planVersion": 16, "supportedCommands": ["readPlan", "savePlan", "createPlan"]});
        write_json_atomic(&dir.path().join("runtime.json"), &runtime, false).unwrap();
        for (field, length) in [("EnergyNguWeights", 9), ("MagicNguWeights", 7)] {
            let mut malformed = vec![
                Value::Null,
                json!(false),
                json!(1),
                json!("weights"),
                json!({}),
                json!([]),
                json!(vec![0; length - 1]),
                json!(vec![0; length + 1]),
            ];
            for token in [
                "-1",
                "2147483648",
                "18446744073709551616123456789",
                "1.5",
                "1e-400",
                "-1e-400",
                "1.0000000000000001",
                "0.999999999999999999",
                "1e1000",
                "1e9223372036854775808",
                "1e-9223372036854775808",
                "1e-9223372036854775809",
                "2147483647.000000001",
                "2147483647.00000000001",
                "2.147483648e9",
                "true",
                "null",
                "\"1\"",
                "[]",
                "{}",
            ] {
                let mut weights = json!(vec![0; length]);
                weights[length - 1] = serde_json::from_str(token).unwrap();
                malformed.push(weights);
            }
            let mut plans = Vec::new();
            let mut missing = default_plan();
            missing.as_object_mut().unwrap().remove(field);
            plans.push(missing);
            for weights in malformed {
                let mut plan = default_plan();
                plan[field] = weights;
                plans.push(plan);
            }
            for plan in plans {
                let err = validate_plan_ngu_weights(&plan).unwrap_err();
                assert!(err.contains(field), "{err}: {plan}");
                for (command, args) in [
                    ("savePlan", json!({"name": "bad", "plan": plan})),
                    (
                        "createPlan",
                        json!({"requestedName": "bad", "source": plan}),
                    ),
                ] {
                    assert_eq!(validate_command(command, &args).unwrap_err(), err);
                    assert_eq!(queue_command(dir.path(), command, args).unwrap_err(), err);
                    assert!(!dir.path().join("commands").exists());
                }
                assert_eq!(
                    save_plan_offline(dir.path(), "bad", &plan, None).unwrap_err(),
                    err
                );
                assert!(!plan_path(dir.path(), "bad").unwrap().exists());
            }
        }
        remove_if_present(&dir.path().join("runtime.json")).unwrap();
        let mut source = default_plan();
        source["MagicNguWeights"] = json!([0]);
        let err = tauri::async_runtime::block_on(create_plan(
            runtime_dir,
            "bad-copy".into(),
            Some(serde_json::to_string(&source).unwrap()),
        ))
        .unwrap_err();
        assert!(err.contains("MagicNguWeights"));
        assert!(!plan_path(dir.path(), "bad-copy").unwrap().exists());
    }

    #[test]
    fn weight_presence_requires_an_integer_version_at_least_sixteen() {
        for version in [
            json!(null),
            json!(14),
            json!(15),
            json!(16.0),
            json!("16"),
            json!(-1),
        ] {
            for field in ["EnergyNguWeights", "MagicNguWeights"] {
                let mut plan = json!({"Version": version});
                plan[field] = Value::Null;
                let err = validate_plan_ngu_weights(&plan).unwrap_err();
                assert!(err.contains("Version >= 16"), "{err}");
                assert!(err.contains("reload the legacy plan"), "{err}");
            }
        }
        assert!(validate_plan_ngu_weights(&json!({"EnergyNguWeights": []})).is_err());
        for version in [16, 17] {
            assert!(validate_plan_ngu_weights(&json!({"Version": version}))
                .unwrap_err()
                .contains("EnergyNguWeights"));
        }
        for version in [14, 15] {
            validate_plan_ngu_weights(&json!({"Version": version, "NguGoal": 2})).unwrap();
        }
    }

    #[test]
    fn weighted_saves_and_copies_require_runtime_v16_including_direct_queue_calls() {
        let dir = tempfile::tempdir().unwrap();
        let runtime_dir = dir.path().to_string_lossy().into_owned();
        let runtime_path = dir.path().join("runtime.json");
        let mut runtime = json!({"externalUiEnabled": true, "status": "running", "protocolVersion": 3, "supportedCommands": ["readPlan", "savePlan", "createPlan"]});
        for runtime_version in [
            None,
            Some(json!(null)),
            Some(json!("16")),
            Some(json!(15)),
            Some(json!(16)),
            Some(json!(17)),
        ] {
            if let Some(version) = &runtime_version {
                runtime["planVersion"] = version.clone();
            }
            runtime["updatedAt"] = json!(Utc::now().to_rfc3339());
            write_json_atomic(&runtime_path, &runtime, true).unwrap();
            for plan_version in [16, 17] {
                for zero in [false, true] {
                    let mut plan = default_plan();
                    plan["Version"] = json!(plan_version);
                    if zero {
                        plan["EnergyNguWeights"] = json!(vec![0; 9]);
                        plan["MagicNguWeights"] = json!(vec![0; 7]);
                    }
                    let supported = runtime_version
                        .as_ref()
                        .and_then(Value::as_u64)
                        .unwrap_or(0)
                        >= 16;
                    for (command, field, args) in [
                        ("savePlan", "plan", json!({"name": "copy", "plan": plan})),
                        (
                            "createPlan",
                            "source",
                            json!({"requestedName": "copy", "source": plan}),
                        ),
                    ] {
                        let result = queue_command(dir.path(), command, args);
                        if supported {
                            let path = dir
                                .path()
                                .join("commands")
                                .join(format!("{}.json", result.unwrap()));
                            let payload =
                                read_json_object(&path, MAX_COMMAND_BYTES, false).unwrap();
                            assert_eq!(payload["args"][field], plan);
                            remove_if_present(&path).unwrap();
                        } else {
                            let err = result.unwrap_err();
                            assert!(err.contains("runtime.planVersion >= 16"), "{err}");
                            assert!(err.contains("Update the injector DLL and reload"), "{err}");
                        }
                    }
                    if !supported {
                        let plan_json = serde_json::to_string(&plan).unwrap();
                        let err = tauri::async_runtime::block_on(save_plan(
                            runtime_dir.clone(),
                            "copy".into(),
                            plan_json.clone(),
                        ))
                        .unwrap_err();
                        assert!(err.contains("runtime.planVersion >= 16"), "{err}");
                        let err = tauri::async_runtime::block_on(create_plan(
                            runtime_dir.clone(),
                            "copy".into(),
                            Some(plan_json),
                        ))
                        .unwrap_err();
                        assert!(err.contains("runtime.planVersion >= 16"), "{err}");
                        assert!(!dir.path().join("commands").exists());
                    }
                }
            }
        }
    }

    #[test]
    fn legacy_offline_edits_and_copies_preserve_the_enum_without_faking_migration() {
        let dir = tempfile::tempdir().unwrap();
        let runtime_dir = dir.path().to_string_lossy().into_owned();
        let path = plan_path(dir.path(), "legacy").unwrap();
        for version in [14, 15] {
            let original =
                json!({"Version": version, "NguGoal": 2, "Future": 42, "RebirthHistory": [1]});
            write_json_atomic(&path, &original, true).unwrap();
            let transport = read_plan_json(&path).unwrap();
            let mut plan = parse_json_object(transport.as_bytes(), "legacy").unwrap();
            let revision = take_plan_revision(&mut plan).unwrap().unwrap();
            plan["TimeTriggerSeconds"] = json!(7200);
            save_plan_offline(dir.path(), "legacy", &plan, Some(&revision)).unwrap();
            let saved = read_json_object(&path, MAX_JSON_BYTES, false).unwrap();
            assert_eq!(saved["Version"], version);
            assert_eq!(saved["NguGoal"], 2);
            assert_eq!(saved["Future"], 42);
            assert_eq!(saved["TimeTriggerSeconds"], 7200);
            assert_eq!(saved["RebirthHistory"], json!([1]));
            assert!(saved.get("EnergyNguWeights").is_none());
            assert!(saved.get("MagicNguWeights").is_none());
            let name = tauri::async_runtime::block_on(create_plan(
                runtime_dir.clone(),
                format!("copy-{version}"),
                Some(serde_json::to_string(&saved).unwrap()),
            ))
            .unwrap();
            let copy = read_json_object(
                &plan_path(dir.path(), &name).unwrap(),
                MAX_JSON_BYTES,
                false,
            )
            .unwrap();
            assert_eq!(copy["Version"], version);
            assert_eq!(copy["NguGoal"], 2);
            assert_eq!(copy["RebirthHistory"], json!([]));
            assert!(copy.get("EnergyNguWeights").is_none());
            let before = fs::read(&path).unwrap();
            let revision = file_revision(&before);
            plan["EnergyNguWeights"] = default_plan()["EnergyNguWeights"].clone();
            plan["MagicNguWeights"] = default_plan()["MagicNguWeights"].clone();
            assert!(
                save_plan_offline(dir.path(), "legacy", &plan, Some(&revision))
                    .unwrap_err()
                    .contains("reload the legacy plan")
            );
            plan["Version"] = json!(16);
            assert!(
                save_plan_offline(dir.path(), "legacy", &plan, Some(&revision))
                    .unwrap_err()
                    .contains("reload the legacy plan")
            );
            assert_eq!(fs::read(&path).unwrap(), before);
        }
    }

    #[test]
    fn legacy_files_with_weights_require_backend_migration_not_offline_writes() {
        let dir = tempfile::tempdir().unwrap();
        let mut plan = default_plan();
        plan["Version"] = json!(15);
        plan["NguGoal"] = json!(2);
        let path = plan_path(dir.path(), "legacy").unwrap();
        write_json_atomic(&path, &plan, false).unwrap();
        let before = fs::read(&path).unwrap();
        let revision = file_revision(&before);
        let err = save_plan_offline(
            dir.path(),
            "legacy",
            &json!({"Version": 15, "TimeTriggerSeconds": 7200}),
            Some(&revision),
        )
        .unwrap_err();
        assert!(err.contains("reload the legacy plan"), "{err}");
        assert_eq!(fs::read(&path).unwrap(), before);
        let err = tauri::async_runtime::block_on(create_plan(
            dir.path().to_string_lossy().into_owned(),
            "bad-copy".into(),
            Some(serde_json::to_string(&plan).unwrap()),
        ))
        .unwrap_err();
        assert!(err.contains("reload the legacy plan"), "{err}");
        assert!(!plan_path(dir.path(), "bad-copy").unwrap().exists());
        // Read transport stays lossless; only the connected backend performs migrations.
        plan[REVISION_FIELD] = json!(revision);
        let response = json!({"result": {"data": {"plan": plan}}});
        let returned = connected_plan_json(&response).unwrap();
        assert_eq!(
            parse_json_object(returned.as_bytes(), "connected plan").unwrap(),
            plan
        );
    }

    #[test]
    fn offline_v16_saves_and_copies_preserve_weights_unknown_numbers_history_and_cas() {
        let dir = tempfile::tempdir().unwrap();
        let runtime_dir = dir.path().to_string_lossy().into_owned();
        let path = plan_path(dir.path(), "original").unwrap();
        let mut original = default_plan();
        original["NguGoal"] = json!(2);
        original["Future"] = serde_json::from_str("18446744073709551616123456789").unwrap();
        original["FutureDecimal"] = serde_json::from_str("1.2300e+42").unwrap();
        original["RebirthHistory"] = json!([{"keep": 1}]);
        write_json_atomic(&path, &original, false).unwrap();
        let mut plan =
            parse_json_object(read_plan_json(&path).unwrap().as_bytes(), "plan").unwrap();
        let stale = plan.clone();
        plan["EnergyNguWeights"] = json!(vec![0; 9]);
        plan["MagicNguWeights"][6] = json!(i32::MAX);
        plan["RebirthHistory"] = json!([]);
        let result = tauri::async_runtime::block_on(save_plan(
            runtime_dir.clone(),
            "original".into(),
            serde_json::to_string(&plan).unwrap(),
        ))
        .unwrap();
        assert_eq!(result["mode"], "offline");
        assert_eq!(result["protocol"]["planVersion"], 16);
        let saved = read_json_object(&path, MAX_JSON_BYTES, false).unwrap();
        assert_eq!(saved["EnergyNguWeights"], plan["EnergyNguWeights"]);
        assert_eq!(saved["MagicNguWeights"], plan["MagicNguWeights"]);
        assert_eq!(saved["RebirthHistory"], original["RebirthHistory"]);
        assert_eq!(saved["Future"].to_string(), "18446744073709551616123456789");
        assert_eq!(saved["FutureDecimal"].to_string(), "1.2300e+42");
        assert!(saved.get("NguGoal").is_none());
        assert!(saved.get(REVISION_FIELD).is_none());
        let before = fs::read(&path).unwrap();
        let err = tauri::async_runtime::block_on(save_plan(
            runtime_dir.clone(),
            "original".into(),
            serde_json::to_string(&stale).unwrap(),
        ))
        .unwrap_err();
        assert!(err.contains("conflict"), "{err}");
        let revision = file_revision(&before);
        let mut incomplete = saved.clone();
        incomplete
            .as_object_mut()
            .unwrap()
            .remove("MagicNguWeights");
        assert!(
            save_plan_offline(dir.path(), "original", &incomplete, Some(&revision))
                .unwrap_err()
                .contains("MagicNguWeights")
        );
        assert_eq!(fs::read(&path).unwrap(), before);
        plan["NguGoal"] = json!(2);
        let name = tauri::async_runtime::block_on(create_plan(
            runtime_dir,
            "copy".into(),
            Some(serde_json::to_string(&plan).unwrap()),
        ))
        .unwrap();
        let copy = read_json_object(
            &plan_path(dir.path(), &name).unwrap(),
            MAX_JSON_BYTES,
            false,
        )
        .unwrap();
        let mut expected = saved;
        expected["Name"] = json!(name);
        expected["RebirthHistory"] = json!([]);
        assert_eq!(copy, expected);
    }

    #[test]
    fn offline_existing_whole_decimal_weights_allow_normalized_edits_and_lossless_copies() {
        let dir = tempfile::tempdir().unwrap();
        let runtime_dir = dir.path().to_string_lossy().into_owned();
        let path = plan_path(dir.path(), "original").unwrap();
        let original = br#"{"Version":16,"EnergyNguWeights":[1.0,1e0,1.000e0,0.0,2.147483647e9,0,0,0,0],"MagicNguWeights":[10e-1,0.0,0,0,0,0,0],"RebirthHistory":[{"keep":1}],"Future":18446744073709551616123456789}"#;
        write_atomic(&path, original, false).unwrap();
        let transport = read_plan_json(&path).unwrap();
        let mut plan = parse_json_object(transport.as_bytes(), "plan").unwrap();
        assert_eq!(plan[REVISION_FIELD], file_revision(original));
        assert_eq!(plan["EnergyNguWeights"][0].to_string(), "1.0");
        assert_eq!(plan["EnergyNguWeights"][2].to_string(), "1.000e+0");
        let name = tauri::async_runtime::block_on(create_plan(
            runtime_dir.clone(),
            "copy".into(),
            Some(transport),
        ))
        .unwrap();
        let copy = read_json_object(
            &plan_path(dir.path(), &name).unwrap(),
            MAX_JSON_BYTES,
            false,
        )
        .unwrap();
        assert_eq!(copy["EnergyNguWeights"], plan["EnergyNguWeights"]);
        assert_eq!(copy["MagicNguWeights"], plan["MagicNguWeights"]);
        assert_eq!(copy["RebirthHistory"], json!([]));
        assert_eq!(copy["Future"], plan["Future"]);
        assert!(copy.get(REVISION_FIELD).is_none());
        assert_eq!(fs::read(&path).unwrap(), original);

        plan["EnergyNguWeights"] = json!([1, 1, 1, 0, i32::MAX, 0, 0, 0, 0]);
        plan["MagicNguWeights"] = json!([1, 0, 0, 0, 0, 0, 0]);
        plan["TimeTriggerSeconds"] = json!(7200);
        plan["RebirthHistory"] = json!([]);
        tauri::async_runtime::block_on(save_plan(
            runtime_dir,
            "original".into(),
            serde_json::to_string(&plan).unwrap(),
        ))
        .unwrap();
        let saved = read_json_object(&path, MAX_JSON_BYTES, false).unwrap();
        assert_eq!(saved["EnergyNguWeights"], plan["EnergyNguWeights"]);
        assert_eq!(saved["MagicNguWeights"], plan["MagicNguWeights"]);
        assert_eq!(saved["TimeTriggerSeconds"], 7200);
        assert_eq!(saved["RebirthHistory"], json!([{"keep": 1}]));
        assert_eq!(saved["Future"].to_string(), "18446744073709551616123456789");
        assert!(saved.get(REVISION_FIELD).is_none());
    }

    #[test]
    fn offline_new_plan_and_protocol_advertise_v16_weight_defaults() {
        let dir = tempfile::tempdir().unwrap();
        let name = tauri::async_runtime::block_on(create_plan(
            dir.path().to_string_lossy().into_owned(),
            "new".into(),
            None,
        ))
        .unwrap();
        let plan = read_json_object(
            &plan_path(dir.path(), &name).unwrap(),
            MAX_JSON_BYTES,
            false,
        )
        .unwrap();
        assert_eq!(plan["Version"], 16);
        assert_eq!(plan["EnergyNguWeights"], json!([0, 0, 0, 0, 1, 0, 1, 0, 0]));
        assert_eq!(plan["MagicNguWeights"], json!([1, 0, 0, 0, 0, 0, 0]));
        assert!(plan.get("NguGoal").is_none());
        validate_plan_ngu_weights(&plan).unwrap();
        validate_plan_durations(&plan).unwrap();
        let protocol = protocol_info();
        assert_eq!(protocol["planVersion"], 16);
        assert!(protocol["nguWeights"]["liveWrites"]
            .as_str()
            .unwrap()
            .contains("runtime.planVersion >= 16"));
        assert!(protocol["nguWeights"]["offlineMigration"]
            .as_str()
            .unwrap()
            .contains("reload the legacy plan"));
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
        assert!(validate_plan_durations(&plan)
            .unwrap_err()
            .contains("time remaining"));
        plan["StageDurationSeconds"] = json!(61);
        validate_plan_durations(&plan).unwrap();
        plan["Stages"][1]["Percent"] = json!(1e308);
        plan["Stages"][2]["Percent"] = json!(1e308);
        assert!(validate_plan_durations(&plan)
            .unwrap_err()
            .contains("finite total"));
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
                json!({"Version": 15, "StageDurationSeconds": 0, "Stages": [{"Percent": 1}]}),
                "StageDurationSeconds",
            ),
            (
                json!({"Version": 15, "StageDurationSeconds": 60, "Stages": [{"DurationSeconds": 60}, {"Percent": 1}]}),
                "time remaining",
            ),
            (
                json!({"Version": 15, "StageDurationSeconds": 60, "Stages": [{"DurationSeconds": 61}, {"Percent": 1}]}),
                "time remaining",
            ),
            (
                json!({"Version": 15, "Stages": [{"CompletionMode": 1, "Percent": 0}]}),
                "Percent",
            ),
            (json!({"Version": 15, "Stages": [null]}), "JSON object"),
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
    fn connected_write_validation_accepts_mixed_legacy_scales_and_at_sequences_losslessly() {
        let dir = tempfile::tempdir().unwrap();
        let runtime = json!({"externalUiEnabled": true, "updatedAt": Utc::now().to_rfc3339(), "status": "running", "protocolVersion": 3, "planVersion": 15, "supportedCommands": ["readPlan", "savePlan", "createPlan"]});
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
                let mut expected = plan.clone();
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
    fn mixed_durations_reserve_exact_seconds_and_require_remaining_weighted_budget() {
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
                assert!(validate_plan_durations(&plan)
                    .unwrap_err()
                    .contains("time remaining"));
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
            json!(0),
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
        assert_eq!(stored["Stages"], source["Stages"]);
        assert_eq!(stored["Future"].to_string(), "1.2300e+42");
        assert_eq!(stored["RebirthHistory"], json!([]));
        assert!(stored.get(REVISION_FIELD).is_none());
        stored["RebirthHistory"] = json!([{"id": 123}]);
        write_json_atomic(&path, &stored, true).unwrap();
        let original = fs::read(&path).unwrap();
        let revision = file_revision(&original);
        let mut patch = json!({"Version": 15, "StageDurationSeconds": 40, "RebirthHistory": []});
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
        source["StageDurationSeconds"] = json!(40);
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
        assert_eq!(default["Version"], 16);
        assert_eq!(default["Stages"], default_plan()["Stages"]);
    }

    #[test]
    fn queued_fixed_writes_require_runtime_v15_but_weighted_and_read_commands_do_not() {
        let dir = tempfile::tempdir().unwrap();
        let runtime_path = dir.path().join("runtime.json");
        let mut runtime = json!({"externalUiEnabled": true, "updatedAt": Utc::now().to_rfc3339(), "status": "running", "protocolVersion": 3, "supportedCommands": ["readPlan", "savePlan", "createPlan"]});
        let fixed = json!({"Version": 15, "StageDurationSeconds": 100, "Stages": [
            {"DurationSeconds": 40, "DurationUnit": "h", "Percent": 1, "Future": {"keep": true}},
            {"DurationSeconds": 0, "DurationUnit": "%", "Percent": 3}
        ]});
        for version in [None, Some(14), Some(15), Some(16)] {
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
                if version.unwrap_or(0) < 15 {
                    let err = outcome.unwrap_err();
                    assert!(err.contains("runtime.planVersion >= 15"));
                    assert!(err.contains("Update the injector DLL and reload"));
                    assert!(!dir.path().join("commands").exists());
                } else {
                    let id = outcome.unwrap();
                    let path = dir.path().join("commands").join(format!("{id}.json"));
                    let payload = read_json_object(&path, MAX_COMMAND_BYTES, false).unwrap();
                    let mut expected = fixed.clone();
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
            ("createPlan", json!({"requestedName": "default"})),
            ("readPlan", json!({"name": "copy"})),
        ] {
            queue_command(dir.path(), command, args).unwrap();
        }
        assert_eq!(
            fs::read_dir(dir.path().join("commands")).unwrap().count(),
            4
        );
        assert_eq!(protocol_info()["planVersion"], 16);
    }

    #[test]
    fn connected_plan_v15_and_runtime_result_data_are_not_filtered() {
        let dir = tempfile::tempdir().unwrap();
        let id = Uuid::new_v4().to_string();
        let mut plan = default_plan();
        plan["Version"] = json!(15);
        plan.as_object_mut().unwrap().remove("EnergyNguWeights");
        plan.as_object_mut().unwrap().remove("MagicNguWeights");
        plan["NguGoal"] = json!(0);
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
        let original = b"\xef\xbb\xbf{\r\n  \"Version\":14,\"Name\":\"copy\",\"Future\":42,\"RebirthHistory\":[1]\r\n}\r\n";
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
        let original = br#"{"Version":14,"FutureInteger":18446744073709551616123456789,"FutureNegative":-9223372036854775809123456789,"FutureDecimal":1.2345678901234567890123456789,"FutureExponent":1.2300e+42}"#;
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
        let runtime = json!({"externalUiEnabled": true, "updatedAt": Utc::now().to_rfc3339(), "status": "running", "protocolVersion": 3, "supportedCommands": ["readPlan", "savePlan", "createPlan"]});
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
        plan["FutureBlob"] = json!("x".repeat(MAX_RESULT_BYTES as usize));
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
            MAX_RESULT_BYTES as usize
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
