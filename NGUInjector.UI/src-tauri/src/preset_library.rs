use super::*;
use std::collections::HashSet;

pub(super) const MAX_PRESET_LIBRARY_BYTES: u64 = 2 * 1024 * 1024;
pub(super) const MAX_PRESET_LIBRARY_RESULT_BYTES: u64 = MAX_PRESET_LIBRARY_BYTES + MAX_RESULT_BYTES;
pub(super) const MAX_PRESET_LIBRARY_COMMAND_BYTES: u64 = 3 * 1024 * 1024;

pub(super) fn global_preset_protocol() -> Value {
    json!({
        "file": "presets.json", "root": "{Version:1,Presets:[]}",
        "maxPresets": 1024, "maxFileBytes": MAX_PRESET_LIBRARY_BYTES,
        "maxResultBytes": MAX_PRESET_LIBRARY_RESULT_BYTES,
        "maxCommandBytes": MAX_PRESET_LIBRARY_COMMAND_BYTES,
        "readCommand": "readPresetLibrary", "saveCommand": "savePresetLibrary",
        "saveArgs": "{library: object without __externalUiRevision, expectedRevision: lowercase SHA-256}",
        "readResult": "data.library object with __externalUiRevision", "saveResult": "data.revision lowercase SHA-256",
        "tauriRead": "read_preset_library(runtimeDir) returns JSON STRING with revision",
        "tauriSave": "save_preset_library(runtimeDir, libraryJson) accepts JSON STRING with revision",
        "requiredRuntimeFields": {"globalPresetsVersion": 2, "globalNguWeightsVersion": 1},
        "weights": "Weights accepts 6, 10, 12 or 14 entries; global library pads legacy arrays with zeroes to 14; adventurePower=10, adventureToughness=11, energyCap=12, magicCap=13; DiggerWeights remains empty or 12 entries",
        "adventureDynamic": {"field": "Optional numeric AdventureDynamicWeight, absent=0, range 0..1000000; any fixed or dynamic weight must be positive", "resolution": "Add the full weight to the current Adventure farming metric (PP=3, Boosts=4, Zone=5), without adding an independent power priority", "requiredRuntimeFields": {"adventureDynamicPresetsVersion": 1}, "result": "Preview and runtime loadoutPreset carry effectiveWeights (14 numbers), adventureTask (label), adventureTaskKey (string); requests remain committed presetId/libraryRevision"},
        "resourceLanePriorities": {"energy": ["NGU", "TM", "AUG", "WAN", "AT"], "magic": ["NGU", "RIT", "TM", "WAN"], "review": "ResourcePrioritiesReviewed absent=false; optional null arrays retain legacy allocation; true requires both unique uppercase arrays, empty means no activity", "allocation": "Reviewed arrays independently authorize funding included activities even when their setup goal weight is zero, without changing setup goals; unreviewed presets retain legacy allocation without this capability", "requiredRuntimeFields": {"resourceLanePrioritiesVersion": 2}},
        "legacyHorizon": "HorizonSeconds is ignored, not required or defaulted; Rust preserves existing tokens as legacy data, including when omitted by an offline editor",
        "defaults": "SupportMode:planned; AutoUpdate:true; AdvancedTrainingWeights:[1,1,0,0,0]; NGU ratios come exclusively from global settings",
        "offlineMigration": "Missing presets.json requires connecting to the injector for migration; UI never seeds a partial library",
        "nguRetirement": "Retired NGU fields are stripped without changing IDs or injector-owned migration receipts; receipt-linked legacy plan saves require connected migration so historical import hashes cannot be lost",
        "offlineDeletion": "Requires no explicit references and no receipt-linked unmigrated source plans; untraceable imports require connected deletion",
        "stageReferences": "PresetId @active requires a nonempty valid ActivePresetId resolving to an existing global preset; @active is never a library ID",
        "actions": {"previewGlobalPreset": "{presetId,libraryRevision}", "applyGlobalPreset": "{presetId,libraryRevision,previewId,candidateKey}", "equipGlobalPreset": "{presetId,libraryRevision}"}
    })
}

pub(super) fn preset_library_revision(value: &Value) -> Result<String, String> {
    value
        .as_str()
        .filter(|text| {
            text.len() == 64
                && text
                    .bytes()
                    .all(|byte| byte.is_ascii_digit() || (b'a'..=b'f').contains(&byte))
        })
        .map(str::to_owned)
        .ok_or_else(|| {
            "Preset library revision must be a 64-character lowercase SHA-256; reload the library"
                .into()
        })
}

pub(super) fn normalize_preset_library(mut library: Value) -> Result<Value, String> {
    require_object(&library, "Preset library")?;
    if library["Version"].as_u64() != Some(1) {
        return Err(
            "Preset library Version must be 1; connect to an updated injector for migration".into(),
        );
    }
    library.as_object_mut().unwrap().remove(REVISION_FIELD);
    if library
        .get("MigrationReceipts")
        .is_some_and(|value| !value.is_object())
    {
        return Err("MigrationReceipts must be an object".into());
    }
    if library
        .get("CapPresetsAdded")
        .is_some_and(|value| !value.is_boolean())
    {
        return Err("CapPresetsAdded must be a boolean".into());
    }
    let rows = library["Presets"]
        .as_array_mut()
        .filter(|rows| rows.len() <= 1024)
        .ok_or("Presets must be an array of at most 1024 presets")?;
    let definitions: Value = serde_json::from_str(include_str!(
        "../../../NGUInjector/Data/PresetDefinitions.json"
    ))
    .expect("Embedded preset definitions must be valid JSON");
    let defaults = definitions["defaults"]
        .as_object()
        .expect("Embedded preset definitions must contain defaults");
    let mut ids = HashSet::new();
    for row in rows {
        require_object(row, "Global preset")?;
        let object = row.as_object_mut().unwrap();
        for (key, default) in defaults {
            object.entry(key.clone()).or_insert_with(|| default.clone());
        }
        strip_ngu_weights(row);
        validate_loadout_preset(row)?;
        let weights = row["Weights"].as_array_mut().unwrap();
        weights.resize(14, json!(0));
        if !ids.insert(row["Id"].as_str().unwrap().to_owned()) {
            return Err("Global preset IDs must be unique".into());
        }
    }
    preset_library_bytes(&library, false)?;
    Ok(library)
}

pub(super) fn validate_resource_lane_priorities(row: &Value) -> Result<(), String> {
    let reviewed = match row.get("ResourcePrioritiesReviewed") {
        None => false,
        Some(value) => value
            .as_bool()
            .ok_or("ResourcePrioritiesReviewed must be boolean")?,
    };
    if row["EnergyPriorities"].is_null() != row["MagicPriorities"].is_null()
        || reviewed && row["EnergyPriorities"].is_null()
    {
        return Err("EnergyPriorities and MagicPriorities must both be arrays or both null; reviewed priorities require both arrays".into());
    }
    for (key, tokens) in [
        ("EnergyPriorities", &["NGU", "TM", "AUG", "WAN", "AT"][..]),
        ("MagicPriorities", &["NGU", "RIT", "TM", "WAN"][..]),
    ] {
        let value = row.get(key).unwrap_or(&Value::Null);
        if value.is_null() && !reviewed {
            continue;
        }
        let values = value
            .as_array()
            .filter(|values| values.len() <= tokens.len())
            .ok_or_else(|| {
                format!(
                    "{key} must be an activity array{}",
                    if reviewed {
                        " (reviewed lanes require both arrays)"
                    } else {
                        " or null"
                    }
                )
            })?;
        let mut seen = HashSet::new();
        for value in values {
            let token = value
                .as_str()
                .filter(|token| tokens.contains(token))
                .ok_or_else(|| format!("{key} must contain exact uppercase activity tokens"))?;
            if !seen.insert(token) {
                return Err(format!("{key} activity tokens must be unique"));
            }
        }
    }
    Ok(())
}

fn preset_library_bytes(library: &Value, check_injector_size: bool) -> Result<Vec<u8>, String> {
    // Windows SimpleJSON.ToString(4) uses CRLF, " : ", and a newline even in empty containers.
    fn injector_size(value: &Value, depth: usize) -> Result<usize, String> {
        match value {
            Value::Array(values) => {
                let mut size = 4 + depth * 4 + values.len().saturating_sub(1);
                for value in values {
                    size += 2 + (depth + 1) * 4 + injector_size(value, depth + 1)?;
                }
                Ok(size)
            }
            Value::Object(values) => {
                let mut size = 4 + depth * 4 + values.len().saturating_sub(1);
                for (key, value) in values {
                    size += 2
                        + (depth + 1) * 4
                        + serde_json::to_vec(key)
                            .map_err(|err| err.to_string())?
                            .len()
                        + 3
                        + injector_size(value, depth + 1)?;
                }
                Ok(size)
            }
            _ => serde_json::to_vec(value)
                .map(|bytes| bytes.len())
                .map_err(|err| err.to_string()),
        }
    }

    let bytes = serde_json::to_vec_pretty(library).map_err(|err| err.to_string())?;
    if bytes.len() as u64 > MAX_PRESET_LIBRARY_BYTES {
        return Err("Preset library exceeds the 2 MiB file limit; file was not modified".into());
    }
    if !check_injector_size {
        return Ok(bytes);
    }
    let mut injector_bytes = injector_size(library, 0)?;
    for row in library["Presets"].as_array().unwrap() {
        if row["EnergyPriorities"].is_array() && row.get("ResourcePrioritiesReviewed").is_none() {
            injector_bytes += "            \"ResourcePrioritiesReviewed\" : false,\r\n".len();
        }
        // Known doubles are re-emitted by .NET; reserve their round-trip upper bound
        // where formatting may grow. Stored values and unknown numeric tokens stay exact.
        for value in row["Weights"]
            .as_array()
            .unwrap()
            .iter()
            .chain(row["DiggerWeights"].as_array().unwrap())
            .chain(
                ["AdventureDynamicWeight", "MinimumPower", "MinimumToughness"]
                    .iter()
                    .filter_map(|key| row.get(*key)),
            )
        {
            let number = value.as_f64().unwrap();
            let length = if number.fract() == 0.0 && number <= 1_000_000.0 {
                format!("{number:.0}").len()
            } else {
                24
            };
            injector_bytes += length.saturating_sub(value.to_string().len());
        }
        // Known Int32 values are re-emitted as decimal integers, not source exponents.
        for value in ["AdvancedTrainingWeights", "BeardPriority", "BeardRotation"]
            .iter()
            .filter_map(|key| row.get(*key).and_then(Value::as_array))
            .flatten()
            .chain(
                [
                    "MagicBloodPercent",
                    "DiggerBudgetPercent",
                    "Zone",
                    "SwapSeconds",
                ]
                .iter()
                .filter_map(|key| row.get(*key)),
            )
        {
            let length = format!("{:.0}", value.as_f64().unwrap()).len();
            injector_bytes += length.saturating_sub(value.to_string().len());
        }
    }
    if injector_bytes as u64 > MAX_PRESET_LIBRARY_BYTES {
        return Err("Preset library exceeds the 2 MiB file limit; file was not modified".into());
    }
    Ok(bytes)
}

pub(super) fn read_preset_library_snapshot(dir: &Path) -> Result<(Value, String), String> {
    let path = dir.join("presets.json");
    let bytes = read_file_bytes(&path, MAX_PRESET_LIBRARY_BYTES, true)?
        .ok_or("Global preset library is missing. Connect to the injector to migrate and initialize presets.json before editing offline")?;
    let library = normalize_preset_library(parse_json_object(&bytes, "Preset library")?)?;
    Ok((library, file_revision(&bytes)))
}

pub(super) fn connected_preset_library_json(response: &Value) -> Result<String, String> {
    let library = &response["result"]["data"]["library"];
    let revision = preset_library_revision(&library[REVISION_FIELD])?;
    let mut library = normalize_preset_library(library.clone())?;
    library[REVISION_FIELD] = json!(revision);
    serde_json::to_string(&library).map_err(|err| err.to_string())
}

// Stage @active inherits the separately validated plan selection, not a library ID.
pub(super) fn plan_preset_references(plan: &Value) -> Result<Vec<&str>, String> {
    require_object(plan, "Plan")?;
    let mut references = Vec::new();
    fn add<'a>(value: &'a Value, label: &str, references: &mut Vec<&'a str>) -> Result<(), String> {
        if value.as_str() == Some("") {
            return Ok(());
        }
        if !loadout_safe_key(value) {
            return Err(format!(
                "{label} must be empty or a 1..64 safe ASCII preset ID"
            ));
        }
        references.push(value.as_str().unwrap());
        Ok(())
    }
    if let Some(value) = plan.get("ActivePresetId") {
        add(value, "ActivePresetId", &mut references)?;
    }
    if let Some(stages) = plan.get("Stages").and_then(Value::as_array) {
        for stage in stages {
            if let Some(value) = stage.get("PresetId") {
                if value.as_str() == Some("@active") {
                    if !loadout_safe_key(&plan["ActivePresetId"]) {
                        return Err(
                            "Stage PresetId '@active' requires a nonempty valid ActivePresetId"
                                .into(),
                        );
                    }
                    continue;
                }
                add(value, "Stage PresetId", &mut references)?;
            }
        }
    }
    Ok(references)
}

fn validate_references(plan: &Value, library: &Value) -> Result<(), String> {
    if plan.get("ActivePresetId").is_some()
        || plan
            .get("Stages")
            .and_then(Value::as_array)
            .is_some_and(|stages| stages.iter().any(|stage| stage.get("PresetId").is_some()))
    {
        validate_plan_presets(plan)?;
        validate_plan_durations(plan)?;
    }
    let ids: HashSet<_> = library["Presets"]
        .as_array()
        .unwrap()
        .iter()
        .map(|row| row["Id"].as_str().unwrap())
        .collect();
    for id in plan_preset_references(plan)? {
        if !ids.contains(id) {
            return Err(format!("Plan references global preset '{id}'; referenced presets cannot be deleted or left missing"));
        }
    }
    Ok(())
}

pub(super) fn validate_offline_plan_preset_references(
    dir: &Path,
    plan: &Value,
) -> Result<(), String> {
    if !plan_preset_references(plan)?.is_empty() {
        let (library, _) = read_preset_library_snapshot(dir)?;
        validate_references(plan, &library)?;
    }
    Ok(())
}

// The caller holds the UI writer lock, matching offline plan save discipline.
pub(super) fn save_preset_library_offline(
    dir: &Path,
    library: Value,
    expected: &str,
) -> Result<String, String> {
    let _ownership = lock_injector_ownership(dir)?;
    let (previous, actual) = read_preset_library_snapshot(dir)?;
    if actual != expected {
        return Err("Preset library revision conflict: file changed after reading; reload before saving. File was not modified".into());
    }
    let mut library = normalize_preset_library(library)?;
    // Preserve injector-owned migration state so deleted presets cannot be resurrected.
    for key in ["MigrationReceipts", "CapPresetsAdded"] {
        match previous.get(key) {
            Some(value) => library[key] = value.clone(),
            None => {
                library.as_object_mut().unwrap().remove(key);
            }
        }
    }
    // Current editors omit the obsolete horizon; do not erase persisted legacy data.
    for row in library["Presets"].as_array_mut().unwrap() {
        if row.get("HorizonSeconds").is_none() {
            if let Some(horizon) = previous["Presets"]
                .as_array()
                .unwrap()
                .iter()
                .find(|existing| existing["Id"] == row["Id"])
                .and_then(|existing| existing.get("HorizonSeconds"))
            {
                row["HorizonSeconds"] = horizon.clone();
            }
        }
    }
    let next_ids: HashSet<_> = library["Presets"]
        .as_array()
        .unwrap()
        .iter()
        .map(|row| row["Id"].as_str().unwrap())
        .collect();
    let removed: HashSet<_> = previous["Presets"]
        .as_array()
        .unwrap()
        .iter()
        .map(|row| row["Id"].as_str().unwrap())
        .filter(|id| !next_ids.contains(id))
        .collect();
    let mut removed_sources = Vec::new();
    let mut untraceable_import = false;
    for id in &removed {
        let mut linked = false;
        if let Some(receipts) = previous.get("MigrationReceipts").and_then(Value::as_object) {
            for receipt in receipts.values() {
                let Some(receipt_id) = receipt.get("PresetId").and_then(Value::as_str) else {
                    untraceable_import = true;
                    continue;
                };
                if receipt_id == *id {
                    linked = true;
                    match receipt
                        .get("Source")
                        .and_then(Value::as_str)
                        .filter(|source| !source.is_empty())
                    {
                        Some(source) => removed_sources.push(source),
                        None => untraceable_import = true,
                    }
                }
            }
        }
        untraceable_import |= id.starts_with("import_") && !linked;
    }
    let plans = dir.join("custom-plans");
    match fs::read_dir(&plans) {
        Ok(entries) => {
            for entry in entries {
                let path = entry.map_err(|err| err.to_string())?.path();
                if path
                    .extension()
                    .and_then(|ext| ext.to_str())
                    .is_some_and(|ext| ext.eq_ignore_ascii_case("json"))
                {
                    let plan = read_json_object(&path, MAX_JSON_BYTES, false)?;
                    if let Some(stages) = plan.get("Stages") {
                        let stages = stages
                            .as_array()
                            .ok_or("Referencing plan Stages must be an array")?;
                        for stage in stages {
                            require_object(stage, "Referencing plan stage")?;
                        }
                    }
                    // Library migration can create an active reference without persisting
                    // it in the source plan. Do not emulate backend canonical import hashes.
                    if plan.get("ActivePresetId").is_none() {
                        let source = path.file_stem().and_then(|stem| stem.to_str());
                        let linked_source = removed_sources.iter().any(|receipt_source| {
                            source.is_some_and(|source| {
                                receipt_source
                                    .chars()
                                    .flat_map(char::to_lowercase)
                                    .eq(source.chars().flat_map(char::to_lowercase))
                            })
                        });
                        if linked_source || untraceable_import {
                            return Err(format!("Cannot safely delete imported presets offline: {} has no persisted ActivePresetId and may gain an implicit migration reference. Connect to the injector to delete these presets; files were not modified", path.display()));
                        }
                    }
                    validate_references(&plan, &library)
                        .map_err(|err| format!("{}: {err}", path.display()))?;
                }
            }
        }
        Err(err) if err.kind() == std::io::ErrorKind::NotFound => {}
        Err(err) => return Err(format!("Cannot scan referencing plans: {err}")),
    }
    let bytes = preset_library_bytes(&library, true)?;
    write_atomic(&dir.join("presets.json"), &bytes, true)?;
    Ok(file_revision(&bytes))
}

pub(super) fn validate_global_preset_command(command: &str, args: &Value) -> Result<(), String> {
    let keys: &[&str] = match command {
        "readPresetLibrary" => &[],
        "savePresetLibrary" => &["library", "expectedRevision"],
        "applyGlobalPreset" => &["presetId", "libraryRevision", "previewId", "candidateKey"],
        _ => &["presetId", "libraryRevision"],
    };
    let fields = args.as_object().ok_or("Command args must be an object")?;
    if fields.len() != keys.len() || fields.keys().any(|key| !keys.contains(&key.as_str())) {
        return Err(format!("{command} accepts exactly {}", keys.join(", ")));
    }
    if command == "readPresetLibrary" {
        return Ok(());
    }
    if command == "savePresetLibrary" {
        preset_library_revision(&args["expectedRevision"])?;
        if args["library"].get(REVISION_FIELD).is_some() {
            return Err("Queue library must not contain transport __externalUiRevision".into());
        }
        normalize_preset_library(args["library"].clone())?;
    } else {
        if !loadout_safe_key(&args["presetId"]) {
            return Err("presetId must be 1..64 safe ASCII characters".into());
        }
        preset_library_revision(&args["libraryRevision"])?;
        if command == "applyGlobalPreset" {
            if !loadout_safe_key(&args["candidateKey"]) {
                return Err("candidateKey must be 1..64 safe ASCII characters".into());
            }
            if !args["previewId"]
                .as_str()
                .is_some_and(|id| id.len() == 32 && id.bytes().all(|byte| byte.is_ascii_hexdigit()))
            {
                return Err("previewId must be a server-owned 32-hex token".into());
            }
        }
    }
    Ok(())
}

#[cfg(test)]
#[path = "preset-library-tests.rs"]
mod tests;
