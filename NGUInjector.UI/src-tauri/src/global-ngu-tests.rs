use super::*;

pub(super) fn globals() -> Value {
    json!({"_energyNguWeights": [0,0,0,0,1,0,1,0,0], "_magicNguWeights": [1,0,0,0,0,0,0]})
}

fn runtime() -> Value {
    json!({"externalUiEnabled": true, "status": "running", "updatedAt": Utc::now().to_rfc3339(),
        "protocolVersion": 3, "planVersion": 17, "stageTimingVersion": 1,
        "globalNguWeightsVersion": 1, "loadoutPresetsVersion": 2, "globalPresetsVersion": 2,
        "startedAt": "session", "settingsCasVersion": 1, "supportedCommands": ["readPlan", "savePlan", "createPlan", "applySettingsPatch", "previewLoadoutPreset", "applyLoadoutPreset", "previewGlobalPreset", "applyGlobalPreset", "equipGlobalPreset"]})
}

#[test]
fn global_settings_accept_zero_int32_boundaries_and_exact_whole_decimal_tokens() {
    for token in [
        "0",
        "1",
        "2147483647",
        "-0.000",
        "0e-999",
        "1.0",
        "1e0",
        "0.1e1",
        "214748364700e-2",
    ] {
        let weight: Value = serde_json::from_str(token).unwrap();
        let patch = json!({"_energyNguWeights": vec![weight.clone(); 9], "_magicNguWeights": vec![weight; 7]});
        let before = patch.to_string();
        validate_settings_patch(&patch).unwrap();
        validate_offline_settings_patch(&patch).unwrap();
        validate_command(
            "applySettingsPatch",
            &json!({"patch": patch, "expectedRevision":""}),
        )
        .unwrap();
        assert_eq!(patch.to_string(), before);
    }
}

#[test]
fn invalid_globals_fail_before_queueing_or_offline_writing() {
    let dir = tempfile::tempdir().unwrap();
    let settings_path = dir.path().join("settings.json");
    write_json_atomic(&settings_path, &globals(), false).unwrap();
    let before = fs::read(&settings_path).unwrap();
    for (key, count) in [("_energyNguWeights", 9), ("_magicNguWeights", 7)] {
        let mut invalid = vec![
            Value::Null,
            json!(false),
            json!(1),
            json!("weights"),
            json!({}),
            json!([]),
            json!(vec![0; count - 1]),
            json!(vec![0; count + 1]),
        ];
        for token in [
            "-1",
            "2147483648",
            "18446744073709551616123456789",
            "1.5",
            "1e-999",
            "-1e-999",
            "1.00000000000000001",
            "0.99999999999999999",
            "2147483647.000000001",
            "2.147483648e9",
            "1e999",
            "1e-9223372036854775809",
            "null",
            "true",
            "\"1\"",
            "[]",
            "{}",
        ] {
            let mut weights = json!(vec![0; count]);
            weights[count - 1] = serde_json::from_str(token).unwrap();
            invalid.push(weights);
        }
        for value in invalid {
            let patch = json!({key: value});
            let err = validate_settings_patch(&patch).unwrap_err();
            assert!(err.contains(key), "{err}");
            assert_eq!(
                queue_command(
                    dir.path(),
                    "applySettingsPatch",
                    json!({"patch": patch, "expectedRevision":""})
                )
                .unwrap_err(),
                err
            );
            assert_eq!(
                tauri::async_runtime::block_on(save_settings_patch(
                    dir.path().to_string_lossy().into_owned(),
                    patch
                ))
                .unwrap_err(),
                err
            );
            assert_eq!(fs::read(&settings_path).unwrap(), before);
            assert!(!dir.path().join("commands").exists());
        }
    }
}

#[test]
fn plans_retire_ratios_without_requiring_or_validating_them() {
    for version in [14, 15, 16, 17] {
        for retired in [
            json!(null),
            json!(false),
            json!([-1]),
            json!({"obsolete": true}),
        ] {
            let source = json!({"Version": version, "NguGoal": 1,
                "EnergyNguWeights": retired, "MagicNguWeights": retired,
                "GoalPresets": [{"Name": "Historical", "EnergyNguWeights": retired, "MagicNguWeights": retired, "Future": 42}],
                "Future": 18446744073709551615_u64});
            validate_command("savePlan", &json!({"name": "plan", "plan": source})).unwrap();
            let normalized = normalize_offline_plan(source.clone()).unwrap();
            for field in ["EnergyNguWeights", "MagicNguWeights", "NguGoal"] {
                assert!(normalized.get(field).is_none());
            }
            for field in ["EnergyNguWeights", "MagicNguWeights"] {
                assert!(normalized["GoalPresets"][0].get(field).is_none());
            }
            assert_eq!(normalized["GoalPresets"][0]["Future"], 42);
            assert_eq!(normalized["Future"], source["Future"]);
            assert_eq!(normalized["Version"], 17);
        }
        normalize_offline_plan(json!({"Version": version})).unwrap();
    }
    assert!(default_plan().get("EnergyNguWeights").is_none());
    assert!(default_plan().get("MagicNguWeights").is_none());
}

#[test]
fn offline_retirement_waits_for_durable_globals_and_preserves_legacy_source() {
    for version in [14, 15, 16, 17] {
        let dir = tempfile::tempdir().unwrap();
        let path = plan_path(dir.path(), "legacy").unwrap();
        let original = json!({"Version": version, "NguGoal": 1, "EnergyNguWeights": [9,8,7,6,5,4,3,2,1], "MagicNguWeights": [7,6,5,4,3,2,1], "RebirthHistory": [{"keep": true}], "Future": 42});
        write_json_atomic(&path, &original, false).unwrap();
        let bytes = fs::read(&path).unwrap();
        let revision = file_revision(&bytes);
        let mut plan: Value = serde_json::from_str(&read_plan_json(&path).unwrap()).unwrap();
        assert_eq!(plan[REVISION_FIELD], revision);
        assert!(plan.get("EnergyNguWeights").is_none());
        plan.as_object_mut().unwrap().remove(REVISION_FIELD);
        plan["Future"] = json!(43);
        for settings in [
            json!({"_customPlanFile": "legacy"}),
            json!({"_energyNguWeights": vec![0;9]}),
            json!({"_energyNguWeights": vec![0;9], "_magicNguWeights": [0]}),
        ] {
            write_json_atomic(&dir.path().join("settings.json"), &settings, true).unwrap();
            let settings_bytes = fs::read(dir.path().join("settings.json")).unwrap();
            assert!(
                save_plan_offline(dir.path(), "legacy", &plan, Some(&revision))
                    .unwrap_err()
                    .contains("durably migrate")
            );
            assert_eq!(fs::read(&path).unwrap(), bytes);
            assert_eq!(
                fs::read(dir.path().join("settings.json")).unwrap(),
                settings_bytes
            );
        }
        let settings = json!({"_energyNguWeights": vec![0;9], "_magicNguWeights": vec![0;7], "_customPlanFile": "legacy"});
        write_json_atomic(&dir.path().join("settings.json"), &settings, true).unwrap();
        let settings_bytes = fs::read(dir.path().join("settings.json")).unwrap();
        save_plan_offline(dir.path(), "legacy", &plan, Some(&revision)).unwrap();
        let saved = read_json_object(&path, MAX_JSON_BYTES, false).unwrap();
        assert_eq!(saved["Version"], 17);
        assert_eq!(saved["Future"], 43);
        assert_eq!(saved["RebirthHistory"], original["RebirthHistory"]);
        assert!(saved.get("EnergyNguWeights").is_none());
        assert!(saved.get("MagicNguWeights").is_none());
        assert!(saved.get("NguGoal").is_none());
        assert_eq!(
            fs::read(dir.path().join("settings.json")).unwrap(),
            settings_bytes
        );
        assert!(
            save_plan_offline(dir.path(), "legacy", &plan, Some(&revision))
                .unwrap_err()
                .contains("conflict")
        );
    }
}

#[test]
fn selection_and_deletion_cannot_bypass_selected_legacy_migration() {
    let dir = tempfile::tempdir().unwrap();
    let settings_path = dir.path().join("settings.json");
    let settings = json!({"_customPlanFile": "legacy"});
    write_json_atomic(&settings_path, &settings, false).unwrap();
    let path = plan_path(dir.path(), "legacy").unwrap();
    write_json_atomic(&path, &json!({"Version": 15, "NguGoal": 1}), false).unwrap();
    write_json_atomic(
        &plan_path(dir.path(), "other").unwrap(),
        &default_plan(),
        false,
    )
    .unwrap();
    let settings_bytes = fs::read(&settings_path).unwrap();
    let plan_bytes = fs::read(&path).unwrap();
    let error = tauri::async_runtime::block_on(save_settings_patch(
        dir.path().to_string_lossy().into_owned(),
        json!({"_customPlanFile": "other"}),
    ))
    .unwrap_err();
    assert!(error.contains("durably migrate"), "{error}");
    assert!(delete_plan_offline(dir.path(), "legacy")
        .unwrap_err()
        .contains("durably migrate"));
    assert_eq!(fs::read(&settings_path).unwrap(), settings_bytes);
    assert_eq!(fs::read(&path).unwrap(), plan_bytes);
}

#[test]
fn copying_and_selecting_plans_never_change_saved_globals() {
    let dir = tempfile::tempdir().unwrap();
    let settings_path = dir.path().join("settings.json");
    let settings = json!({"_energyNguWeights": vec![0;9], "_magicNguWeights": [0,0,0,0,0,0,17], "_customPlanFile": "legacy", "Future": 42});
    write_json_atomic(&settings_path, &settings, false).unwrap();
    let mut source = default_plan();
    source["EnergyNguWeights"] = json!(vec![99; 9]);
    source["MagicNguWeights"] = json!(vec![99; 7]);
    let source_path = plan_path(dir.path(), "legacy").unwrap();
    write_json_atomic(&source_path, &source, false).unwrap();
    let before = fs::read(&settings_path).unwrap();
    let source_bytes = fs::read(&source_path).unwrap();
    let name = tauri::async_runtime::block_on(create_plan(
        dir.path().to_string_lossy().into_owned(),
        "copy".into(),
        Some(source.to_string()),
    ))
    .unwrap();
    assert_eq!(fs::read(&settings_path).unwrap(), before);
    assert_eq!(fs::read(&source_path).unwrap(), source_bytes);
    let copy = read_json_object(
        &plan_path(dir.path(), &name).unwrap(),
        MAX_JSON_BYTES,
        false,
    )
    .unwrap();
    assert!(copy.get("EnergyNguWeights").is_none());
    assert!(copy.get("MagicNguWeights").is_none());
    tauri::async_runtime::block_on(save_settings_patch(
        dir.path().to_string_lossy().into_owned(),
        json!({"_customPlanFile": name}),
    ))
    .unwrap();
    let saved = read_json_object(&settings_path, MAX_JSON_BYTES, false).unwrap();
    assert_eq!(saved["_energyNguWeights"], settings["_energyNguWeights"]);
    assert_eq!(saved["_magicNguWeights"], settings["_magicNguWeights"]);
    assert_eq!(saved["Future"], 42);
}

#[test]
fn global_settings_writes_obey_both_existing_write_guards() {
    let dir = tempfile::tempdir().unwrap();
    let path = dir.path().join("settings.json");
    write_json_atomic(&path, &globals(), false).unwrap();
    let before = fs::read(&path).unwrap();
    for ownership in [false, true] {
        let _lock = if ownership {
            lock_injector_ownership(dir.path())
        } else {
            lock_offline_writer(dir.path())
        }
        .unwrap();
        assert!(tauri::async_runtime::block_on(save_settings_patch(
            dir.path().to_string_lossy().into_owned(),
            json!({"_energyNguWeights": vec![0;9]})
        ))
        .is_err());
        assert_eq!(fs::read(&path).unwrap(), before);
    }
    tauri::async_runtime::block_on(save_settings_patch(
        dir.path().to_string_lossy().into_owned(),
        json!({"_energyNguWeights": vec![0;9]}),
    ))
    .unwrap();
    let saved = read_json_object(&path, MAX_JSON_BYTES, false).unwrap();
    assert_eq!(saved["_energyNguWeights"], json!(vec![0; 9]));
    assert_eq!(saved["_magicNguWeights"], globals()["_magicNguWeights"]);
}

#[test]
fn global_only_writes_require_new_runtime_capability() {
    let dir = tempfile::tempdir().unwrap();
    for capability in [Value::Null, json!(0), json!(2), json!("1"), json!(true)] {
        let mut state = runtime();
        state["globalNguWeightsVersion"] = capability;
        write_json_atomic(&dir.path().join("runtime.json"), &state, true).unwrap();
        for (command, args) in [
            (
                "savePlan",
                json!({"name": "new", "plan": default_plan(), "expectedRevision": ""}),
            ),
            ("createPlan", json!({"requestedName": "new"})),
            (
                "applySettingsPatch",
                json!({"patch": globals(), "expectedRevision":""}),
            ),
            (
                "applySettingsPatch",
                json!({"patch": {"_customPlanFile": "other"}, "expectedRevision":""}),
            ),
            (
                "previewGlobalPreset",
                json!({"presetId": "energy", "libraryRevision": "a".repeat(64)}),
            ),
        ] {
            assert!(queue_command(dir.path(), command, args)
                .unwrap_err()
                .contains("globalNguWeightsVersion"));
            assert!(!dir.path().join("commands").exists());
        }
    }
    write_json_atomic(&dir.path().join("runtime.json"), &runtime(), true).unwrap();
    let id = queue_command(
        dir.path(),
        "applySettingsPatch",
        json!({"patch": globals(), "expectedRevision":""}),
    )
    .unwrap();
    let payload = read_json_object(
        &dir.path().join("commands").join(format!("{id}.json")),
        MAX_COMMAND_BYTES,
        false,
    )
    .unwrap();
    assert_eq!(payload["args"]["patch"], globals());
}

#[test]
fn protocol_advertises_global_only_contract_and_backend_defaults() {
    let protocol = protocol_info();
    assert_eq!(protocol["planVersion"], 17);
    assert_eq!(protocol["globalNguWeightsVersion"], 1);
    assert_eq!(protocol["loadoutPresetsVersion"], 2);
    assert_eq!(protocol["globalPresetsVersion"], 2);
    assert_eq!(
        protocol["loadoutPresets"]["requiredRuntimeFields"]["loadoutPresetsVersion"],
        2
    );
    assert_eq!(
        protocol["globalPresets"]["requiredRuntimeFields"]["globalPresetsVersion"],
        2
    );
    assert_eq!(
        protocol["nguWeights"]["energyDefault"],
        globals()["_energyNguWeights"]
    );
    assert_eq!(
        protocol["nguWeights"]["magicDefault"],
        globals()["_magicNguWeights"]
    );
    assert!(protocol["loadoutPresets"]["args"]
        .get("energyNguWeights")
        .is_none());
    assert!(protocol["loadoutPresets"]["args"]
        .get("magicNguWeights")
        .is_none());
}

#[test]
fn queued_plans_and_inline_presets_strip_retired_fields_and_require_v2_previews() {
    let dir = tempfile::tempdir().unwrap();
    let mut state = runtime();
    let mut row = normalize_preset_library(
        json!({"Version": 1, "Presets": [{"Id": "energy", "Name": "Energy"}]}),
    )
    .unwrap()["Presets"][0]
        .clone();
    row["EnergyNguWeights"] = json!({"retired": true});
    row["MagicNguWeights"] = json!(null);
    row["Future"] = json!(42);
    let source = json!({"Version": 17, "EnergyNguWeights": [-1], "MagicNguWeights": false, "LoadoutPresets": [row], "GoalPresets": [row]});
    for version in [Value::Null, json!(1), json!("2"), json!(true), json!(2)] {
        state["loadoutPresetsVersion"] = version;
        write_json_atomic(&dir.path().join("runtime.json"), &state, true).unwrap();
        for command in ["previewLoadoutPreset", "applyLoadoutPreset"] {
            let mut args = json!({"preset": row});
            if command == "applyLoadoutPreset" {
                args["candidateKey"] = json!("mixed");
                args["previewId"] = json!("a".repeat(32));
            }
            let result = queue_command(dir.path(), command, args);
            if state["loadoutPresetsVersion"].as_u64() == Some(2) {
                let id = result.unwrap();
                let payload = read_json_object(
                    &dir.path().join("commands").join(format!("{id}.json")),
                    MAX_COMMAND_BYTES,
                    false,
                )
                .unwrap();
                assert!(payload["args"]["preset"].get("EnergyNguWeights").is_none());
                assert!(payload["args"]["preset"].get("MagicNguWeights").is_none());
                assert_eq!(payload["args"]["preset"]["Future"], 42);
            } else {
                assert!(result.unwrap_err().contains("loadoutPresetsVersion 2"));
            }
        }
    }
    for (command, field, args) in [
        (
            "savePlan",
            "plan",
            json!({"name": "new", "expectedRevision": "", "plan": source}),
        ),
        (
            "createPlan",
            "source",
            json!({"requestedName": "new", "source": source}),
        ),
    ] {
        let id = queue_command(dir.path(), command, args).unwrap();
        let payload = read_json_object(
            &dir.path().join("commands").join(format!("{id}.json")),
            MAX_COMMAND_BYTES,
            false,
        )
        .unwrap();
        let plan = &payload["args"][field];
        assert!(plan.get("EnergyNguWeights").is_none());
        assert!(plan.get("MagicNguWeights").is_none());
        for key in ["LoadoutPresets", "GoalPresets"] {
            assert!(plan[key][0].get("EnergyNguWeights").is_none());
            assert!(plan[key][0].get("MagicNguWeights").is_none());
            assert_eq!(plan[key][0]["Future"], 42);
        }
    }
}

#[test]
fn offline_plan_retirement_cannot_destroy_historical_import_identity_inputs() {
    let dir = tempfile::tempdir().unwrap();
    write_json_atomic(&dir.path().join("settings.json"), &globals(), false).unwrap();
    let library = json!({"Version": 1, "Presets": [{"Id": "import_existing", "Name": "Edited import"}], "MigrationReceipts": {"historical_hash": {"Source": "LEGACY", "PresetId": "import_existing"}}});
    let library_path = dir.path().join("presets.json");
    write_json_atomic(&library_path, &library, false).unwrap();
    let library_bytes = fs::read(&library_path).unwrap();
    let path = plan_path(dir.path(), "legacy").unwrap();
    let source = json!({"Version": 17, "ActivePresetId": "import_existing", "GoalPresets": [{"EnergyNguWeights": [9], "MagicNguWeights": [7]}]});
    write_json_atomic(&path, &source, false).unwrap();
    let bytes = fs::read(&path).unwrap();
    let normalized = normalize_offline_plan(source).unwrap();
    assert!(save_plan_offline(
        dir.path(),
        "legacy",
        &normalized,
        Some(&file_revision(&bytes))
    )
    .unwrap_err()
    .contains("receipt identities"));
    assert_eq!(fs::read(&path).unwrap(), bytes);
    assert_eq!(fs::read(&library_path).unwrap(), library_bytes);
}
