use super::*;

#[test]
fn resource_lane_review_preserves_legacy_and_explicit_empty_arrays_without_read_writes() {
    for fields in [
        json!({}),
        json!({"EnergyPriorities": null}),
        json!({"MagicPriorities": null}),
        json!({"EnergyPriorities": null, "MagicPriorities": null, "ResourcePrioritiesReviewed": false}),
        json!({"EnergyPriorities": ["TM", "NGU"], "MagicPriorities": []}),
        json!({"EnergyPriorities": ["TM", "NGU"], "MagicPriorities": ["RIT", "NGU"], "ResourcePrioritiesReviewed": false}),
        json!({"EnergyPriorities": [], "MagicPriorities": [], "ResourcePrioritiesReviewed": true}),
        json!({"EnergyPriorities": ["NGU", "TM", "AUG", "WAN", "AT"], "MagicPriorities": ["RIT", "NGU", "WAN", "TM"], "ResourcePrioritiesReviewed": true}),
    ] {
        let dir = tempfile::tempdir().unwrap();
        let path = dir.path().join("presets.json");
        let mut source = library();
        source["Presets"][0]
            .as_object_mut()
            .unwrap()
            .extend(fields.as_object().unwrap().clone());
        source["Presets"][0]["AdventureDynamicWeight"] = json!(2);
        source["Presets"][0]["Future"] = serde_json::from_str("900719925474099312345").unwrap();
        write_json_atomic(&path, &source, false).unwrap();
        let before = fs::read(&path).unwrap();
        let loaded = read(dir.path());
        assert_eq!(fs::read(&path).unwrap(), before);
        for key in [
            "EnergyPriorities",
            "MagicPriorities",
            "ResourcePrioritiesReviewed",
        ] {
            assert_eq!(loaded["Presets"][0].get(key), source["Presets"][0].get(key));
        }
        save(dir.path(), &loaded).unwrap();
        assert_eq!(read(dir.path())["Presets"], loaded["Presets"]);
        assert_eq!(
            loaded["Presets"][0]["Future"],
            source["Presets"][0]["Future"]
        );
    }
}

#[test]
fn resource_lane_asymmetric_null_or_absent_pairs_reject_before_offline_save_or_inline_actions() {
    let dir = tempfile::tempdir().unwrap();
    let path = dir.path().join("presets.json");
    write_json_atomic(&path, &library(), false).unwrap();
    let loaded = read(dir.path());
    let before = fs::read(&path).unwrap();
    for reviewed in [None, Some(false), Some(true)] {
        for missing in [None, Some(Value::Null)] {
            for lane in ["EnergyPriorities", "MagicPriorities"] {
                let mut invalid = loaded.clone();
                let row = &mut invalid["Presets"][0];
                for key in [
                    "EnergyPriorities",
                    "MagicPriorities",
                    "ResourcePrioritiesReviewed",
                ] {
                    row.as_object_mut().unwrap().remove(key);
                }
                row[lane] = json!(["NGU"]);
                if let Some(value) = &missing {
                    row[if lane == "EnergyPriorities" {
                        "MagicPriorities"
                    } else {
                        "EnergyPriorities"
                    }] = value.clone();
                }
                if let Some(value) = reviewed {
                    row["ResourcePrioritiesReviewed"] = json!(value);
                }
                assert!(validate_loadout_preset(row)
                    .unwrap_err()
                    .contains("must both be arrays or both null"));
                for command in ["previewLoadoutPreset", "applyLoadoutPreset"] {
                    let mut args = json!({"preset": row});
                    if command == "applyLoadoutPreset" {
                        args["candidateKey"] = json!("mixed");
                        args["previewId"] = json!("a".repeat(32));
                    }
                    assert!(validate_command(command, &args)
                        .unwrap_err()
                        .contains("must both be arrays or both null"));
                }
                assert!(normalize_preset_library(invalid.clone())
                    .unwrap_err()
                    .contains("must both be arrays or both null"));
                assert!(save(dir.path(), &invalid)
                    .unwrap_err()
                    .contains("must both be arrays or both null"));
                assert_eq!(fs::read(&path).unwrap(), before);
            }
        }
    }
}

#[test]
fn resource_lane_validation_rejects_invalid_types_tokens_duplicates_and_incomplete_review() {
    let base = normalize_preset_library(library()).unwrap();
    for fields in [
        json!({"ResourcePrioritiesReviewed": null}),
        json!({"ResourcePrioritiesReviewed": "true"}),
        json!({"ResourcePrioritiesReviewed": 1}),
        json!({"ResourcePrioritiesReviewed": true}),
        json!({"ResourcePrioritiesReviewed": true, "EnergyPriorities": [], "MagicPriorities": null}),
        json!({"EnergyPriorities": ["RIT"]}),
        json!({"MagicPriorities": ["AUG"]}),
        json!({"MagicPriorities": ["AT"]}),
        json!({"EnergyPriorities": ["ngu"]}),
        json!({"EnergyPriorities": [" NGU"]}),
        json!({"EnergyPriorities": ["NGU-0"]}),
        json!({"EnergyPriorities": ["NGU", "NGU"]}),
        json!({"MagicPriorities": ["NGU", "RIT", "TM", "WAN", "NGU"]}),
        json!({"EnergyPriorities": [1]}),
        json!({"MagicPriorities": "NGU"}),
        json!({"EnergyPriorities": [null]}),
        json!({"MagicPriorities": [true]}),
    ] {
        let mut invalid = base.clone();
        invalid["Presets"][0]
            .as_object_mut()
            .unwrap()
            .extend(fields.as_object().unwrap().clone());
        assert!(validate_resource_lane_priorities(&invalid["Presets"][0])
            .unwrap_err()
            .contains("Priorities"));
        for command in ["previewLoadoutPreset", "applyLoadoutPreset"] {
            let mut args = json!({"preset": invalid["Presets"][0]});
            if command == "applyLoadoutPreset" {
                args["previewId"] = json!("a".repeat(32));
                args["candidateKey"] = json!("mixed");
            }
            assert!(validate_command(command, &args)
                .unwrap_err()
                .contains("Priorities"));
        }
        assert!(normalize_preset_library(invalid.clone())
            .unwrap_err()
            .contains("Priorities"));
        assert!(validate_global_preset_command(
            "savePresetLibrary",
            &json!({"library": invalid, "expectedRevision": "a".repeat(64)})
        )
        .unwrap_err()
        .contains("Priorities"));
        let dir = tempfile::tempdir().unwrap();
        let path = dir.path().join("presets.json");
        write_json_atomic(&path, &library(), false).unwrap();
        let loaded = read(dir.path());
        let before = fs::read(&path).unwrap();
        invalid[REVISION_FIELD] = loaded[REVISION_FIELD].clone();
        assert!(save(dir.path(), &invalid)
            .unwrap_err()
            .contains("Priorities"));
        assert_eq!(fs::read(&path).unwrap(), before);
    }
}

fn resource_lane_runtime() -> Value {
    json!({"externalUiEnabled": true, "status": "running", "updatedAt": Utc::now().to_rfc3339(),
        "protocolVersion": 3, "startedAt": "lane-session", "globalPresetsVersion": 2,
        "loadoutPresetsVersion": 2, "globalNguWeightsVersion": 1, "stageTimingVersion": 1,
        "planVersion": 17, "stageOverridesV1": true, "resourceLanePrioritiesVersion": 1, "settingsCasVersion": 1,
        "supportedCommands": ["previewLoadoutPreset", "applyLoadoutPreset", "previewGlobalPreset",
            "applyGlobalPreset", "equipGlobalPreset", "applySettingsPatch", "setPlanStage", "savePlan", "readPlan", "createPlan"]})
}

#[test]
fn resource_lane_queue_requires_exact_capability_fresh_session_and_advertised_commands() {
    let dir = tempfile::tempdir().unwrap();
    let library_path = dir.path().join("presets.json");
    let runtime_path = dir.path().join("runtime.json");
    let mut saved = normalize_preset_library(library()).unwrap();
    saved["Presets"][0]["ResourcePrioritiesReviewed"] = json!(true);
    saved["Presets"][0]["EnergyPriorities"] = json!(["TM", "NGU"]);
    saved["Presets"][0]["MagicPriorities"] = json!([]);
    write_json_atomic(&library_path, &saved, false).unwrap();
    let before = fs::read(&library_path).unwrap();
    let revision = file_revision(&before);
    for command in [
        "previewLoadoutPreset",
        "applyLoadoutPreset",
        "previewGlobalPreset",
        "applyGlobalPreset",
        "equipGlobalPreset",
    ] {
        let mut args = if command.contains("Global") {
            json!({"presetId": "energy", "libraryRevision": revision})
        } else {
            json!({"preset": saved["Presets"][0]})
        };
        if command.starts_with("apply") {
            args["previewId"] = json!("a".repeat(32));
            args["candidateKey"] = json!("mixed");
        }
        for version in [
            None,
            Some(Value::Null),
            Some(json!(0)),
            Some(json!(2)),
            Some(json!("1")),
            Some(json!(true)),
            Some(serde_json::from_str("1.00000000000000001").unwrap()),
        ] {
            let mut runtime = resource_lane_runtime();
            runtime
                .as_object_mut()
                .unwrap()
                .remove("resourceLanePrioritiesVersion");
            if let Some(version) = version {
                runtime["resourceLanePrioritiesVersion"] = version;
            }
            write_json_atomic(&runtime_path, &runtime, true).unwrap();
            assert!(queue_command(dir.path(), command, args.clone())
                .unwrap_err()
                .contains("resourceLanePrioritiesVersion"));
            assert!(list_json_stems(&dir.path().join("commands"))
                .unwrap()
                .is_empty());
        }
        for (field, value) in [
            ("startedAt", Value::Null),
            ("startedAt", json!(" ")),
            ("startedAt", json!(true)),
            ("supportedCommands", json!([])),
            ("supportedCommands", json!(command)),
            (
                "updatedAt",
                json!((Utc::now() - chrono::Duration::seconds(30)).to_rfc3339()),
            ),
            ("globalNguWeightsVersion", json!(0)),
        ] {
            let mut runtime = resource_lane_runtime();
            runtime[field] = value;
            write_json_atomic(&runtime_path, &runtime, true).unwrap();
            assert!(
                queue_command(dir.path(), command, args.clone()).is_err(),
                "{command}: {runtime}"
            );
            assert!(list_json_stems(&dir.path().join("commands"))
                .unwrap()
                .is_empty());
        }
        write_json_atomic(&runtime_path, &resource_lane_runtime(), true).unwrap();
        let id = queue_command(dir.path(), command, args.clone()).unwrap();
        let path = dir.path().join("commands").join(format!("{id}.json"));
        assert_eq!(
            read_json_object(&path, MAX_COMMAND_BYTES, false).unwrap()["args"],
            args
        );
        remove_if_present(&path).unwrap();
        assert_eq!(fs::read(&library_path).unwrap(), before);
    }
    let args = json!({"presetId": "energy", "libraryRevision": "a".repeat(64)});
    assert!(queue_command(dir.path(), "equipGlobalPreset", args)
        .unwrap_err()
        .contains("revision changed"));
}

#[test]
fn resource_lane_plan_execution_and_bindings_resolve_committed_literal_references() {
    for binding in ["active", "stage", "inherited"] {
        let dir = tempfile::tempdir().unwrap();
        let mut saved = library();
        saved["Presets"][0]["ResourcePrioritiesReviewed"] = json!(true);
        saved["Presets"][0]["EnergyPriorities"] = json!([]);
        saved["Presets"][0]["MagicPriorities"] = json!(["RIT"]);
        write_json_atomic(&dir.path().join("presets.json"), &saved, false).unwrap();
        let mut plan = default_plan();
        plan["ActivePresetId"] = json!(if binding == "stage" { "" } else { "energy" });
        plan["Stages"][0]["PresetId"] = json!(match binding {
            "stage" => "energy",
            "inherited" => "@active",
            _ => "",
        });
        let path = plan_path(dir.path(), "selected").unwrap();
        write_json_atomic(&path, &plan, false).unwrap();
        let plan_bytes = fs::read(&path).unwrap();
        let revision = file_revision(&plan_bytes);
        let mut runtime = resource_lane_runtime();
        runtime["settings"] = json!({"customPlanFile": "selected"});
        for (command, args) in [
            (
                "savePlan",
                json!({"name": "selected", "plan": plan, "expectedRevision": revision}),
            ),
            (
                "createPlan",
                json!({"requestedName": "copy", "source": plan}),
            ),
            (
                "setPlanStage",
                json!({"name": "selected", "expectedRevision": revision, "expectedEntry": "entry", "action": "set", "stageIndex": 0}),
            ),
            (
                "applySettingsPatch",
                json!({"patch": {"_customPlanFile": "selected"}, "expectedRevision":""}),
            ),
            (
                "applySettingsPatch",
                json!({"patch": {"_useCustomRebirthPlan": true}, "expectedRevision":""}),
            ),
            (
                "applySettingsPatch",
                json!({"patch": {"_autoRebirth": true}, "expectedRevision":""}),
            ),
        ] {
            for version in [Value::Null, json!(false), json!("1"), json!(2)] {
                runtime["resourceLanePrioritiesVersion"] = version;
                write_json_atomic(&dir.path().join("runtime.json"), &runtime, true).unwrap();
                assert!(
                    queue_command(dir.path(), command, args.clone())
                        .unwrap_err()
                        .contains("resourceLanePrioritiesVersion"),
                    "{binding}: {command}"
                );
                assert!(list_json_stems(&dir.path().join("commands"))
                    .unwrap()
                    .is_empty());
            }
            runtime["resourceLanePrioritiesVersion"] = json!(1);
            write_json_atomic(&dir.path().join("runtime.json"), &runtime, true).unwrap();
            let id = queue_command(dir.path(), command, args).unwrap();
            remove_if_present(&dir.path().join("commands").join(format!("{id}.json"))).unwrap();
        }
        let stale = json!({"name": "selected", "expectedRevision": "stale", "expectedEntry": "entry", "action": "restart", "stageIndex": 0});
        assert!(queue_command(dir.path(), "setPlanStage", stale)
            .unwrap_err()
            .contains("Plan revision changed"));
        runtime["resourceLanePrioritiesVersion"] = Value::Null;
        write_json_atomic(&dir.path().join("runtime.json"), &runtime, true).unwrap();
        queue_command(
            dir.path(),
            "applySettingsPatch",
            json!({"patch": {"_useCustomRebirthPlan": false, "_autoRebirth": false}, "expectedRevision":""}),
        )
        .unwrap();
        assert_eq!(fs::read(&path).unwrap(), plan_bytes);
    }
}

#[test]
fn resource_lane_legacy_actions_and_unknown_library_fields_remain_unchanged_without_new_capability()
{
    let dir = tempfile::tempdir().unwrap();
    let mut saved = normalize_preset_library(library()).unwrap();
    saved["Future"] = serde_json::from_str("900719925474099312345").unwrap();
    saved["Presets"][0]["Future"] = json!({"keep": true});
    saved["Presets"][0]["EnergyPriorities"] = Value::Null;
    saved["Presets"][0]["MagicPriorities"] = Value::Null;
    saved["Presets"][0]["ResourcePrioritiesReviewed"] = json!(false);
    let path = dir.path().join("presets.json");
    write_json_atomic(&path, &saved, false).unwrap();
    let before = fs::read(&path).unwrap();
    let mut runtime = resource_lane_runtime();
    runtime
        .as_object_mut()
        .unwrap()
        .remove("resourceLanePrioritiesVersion");
    runtime["settings"] = json!({"customPlanFile": "selected"});
    write_json_atomic(&dir.path().join("runtime.json"), &runtime, false).unwrap();
    let mut plan = default_plan();
    plan["ActivePresetId"] = json!("energy");
    write_json_atomic(&plan_path(dir.path(), "selected").unwrap(), &plan, false).unwrap();
    for (command, args) in [
        (
            "previewLoadoutPreset",
            json!({"preset": saved["Presets"][0]}),
        ),
        (
            "applyLoadoutPreset",
            json!({"preset": saved["Presets"][0], "previewId": "a".repeat(32), "candidateKey": "mixed"}),
        ),
        (
            "equipGlobalPreset",
            json!({"presetId": "energy", "libraryRevision": file_revision(&before)}),
        ),
        (
            "applySettingsPatch",
            json!({"patch": {"_useCustomRebirthPlan": true}, "expectedRevision":""}),
        ),
    ] {
        let id = queue_command(dir.path(), command, args.clone()).unwrap();
        assert_eq!(
            read_json_object(
                &dir.path().join("commands").join(format!("{id}.json")),
                MAX_COMMAND_BYTES,
                false
            )
            .unwrap()["args"],
            args
        );
    }
    assert_eq!(fs::read(&path).unwrap(), before);
}

#[test]
fn adventure_dynamic_weight_validates_optional_numeric_and_positive_goal_contract() {
    let base = normalize_preset_library(library()).unwrap()["Presets"][0].clone();
    assert!(base.get("AdventureDynamicWeight").is_none() || base["AdventureDynamicWeight"] == 0);
    validate_loadout_preset(&base).unwrap();
    for count in [6, 10, 12, 14] {
        let mut row = base.clone();
        row["Weights"] = json!(vec![0; count]);
        assert!(validate_loadout_preset(&row).is_err());
        for weight in [json!(0.25), json!(1), json!(1_000_000)] {
            row["AdventureDynamicWeight"] = weight;
            validate_loadout_preset(&row).unwrap();
            let normalized =
                normalize_preset_library(json!({"Version": 1, "Presets": [row]})).unwrap();
            assert_eq!(normalized["Presets"][0]["Weights"], json!(vec![0; 14]));
            assert_eq!(
                normalized["Presets"][0]["AdventureDynamicWeight"],
                row["AdventureDynamicWeight"]
            );
        }
        row["AdventureDynamicWeight"] = json!(0);
        assert!(validate_loadout_preset(&row)
            .unwrap_err()
            .contains("positive"));
        row["Weights"][0] = json!(3);
        validate_loadout_preset(&row).unwrap();
    }
    for weight in [
        json!(null),
        json!(true),
        json!("1"),
        json!({}),
        json!([]),
        json!(-0.1),
        json!(1_000_001),
        serde_json::from_str("1000000.00000000001").unwrap(),
    ] {
        let mut row = base.clone();
        row["AdventureDynamicWeight"] = weight;
        assert!(validate_loadout_preset(&row)
            .unwrap_err()
            .contains("AdventureDynamicWeight"));
        assert!(normalize_preset_library(json!({"Version": 1, "Presets": [row]})).is_err());
        assert!(validate_command("previewLoadoutPreset", &json!({"preset": row})).is_err());
    }
}

#[test]
fn adventure_dynamic_offline_roundtrip_preserves_existing_rows_ids_and_unknown_tokens() {
    let dir = tempfile::tempdir().unwrap();
    let mut original = library();
    original["Presets"][0] = json!({"Id": "builtin_pp", "Name": "User edited PP", "Weights": [0,0,0,7,0,0], "Future": serde_json::from_str::<Value>("900719925474099312345").unwrap()});
    original["Presets"][1]["AdventureDynamicWeight"] = json!(0.5);
    original["Presets"][1]["Weights"] = json!(vec![0; 12]);
    write_json_atomic(&dir.path().join("presets.json"), &original, false).unwrap();
    let loaded = read(dir.path());
    save(dir.path(), &loaded).unwrap();
    let reread = read(dir.path());
    assert_eq!(reread["Presets"], loaded["Presets"]);
    assert_eq!(reread["Presets"].as_array().unwrap().len(), 2);
    assert_eq!(reread["Presets"][0]["Id"], "builtin_pp");
    assert_eq!(reread["Presets"][0]["Name"], "User edited PP");
    assert_eq!(reread["Presets"][0]["Weights"][3], 7);
    assert_eq!(
        reread["Presets"][0]["Future"],
        original["Presets"][0]["Future"]
    );
    assert!(reread["Presets"][0].get("BeardRotation").is_none());
    assert_eq!(reread["Presets"][1]["AdventureDynamicWeight"], 0.5);
}

#[test]
fn adventure_dynamic_queue_gates_inline_and_committed_actions_without_changing_requests() {
    let dir = tempfile::tempdir().unwrap();
    let mut saved = normalize_preset_library(library()).unwrap();
    saved["Presets"][0]["AdventureDynamicWeight"] = json!(1);
    saved["Presets"][0]["Weights"] = json!(vec![0; 12]);
    write_json_atomic(&dir.path().join("presets.json"), &saved, false).unwrap();
    let runtime_path = dir.path().join("runtime.json");
    let mut runtime = json!({"externalUiEnabled": true, "status": "running", "updatedAt": Utc::now().to_rfc3339(), "protocolVersion": 3, "globalPresetsVersion": 2, "loadoutPresetsVersion": 2, "globalNguWeightsVersion": 1, "startedAt": "session", "supportedCommands": ["previewGlobalPreset", "applyGlobalPreset", "equipGlobalPreset", "previewLoadoutPreset", "applyLoadoutPreset"]});
    for command in [
        "previewGlobalPreset",
        "applyGlobalPreset",
        "equipGlobalPreset",
        "previewLoadoutPreset",
        "applyLoadoutPreset",
    ] {
        let mut args = if command.contains("Global") {
            action_args(command)
        } else {
            json!({"preset": saved["Presets"][0]})
        };
        if command.contains("Global") {
            args["presetId"] = saved["Presets"][0]["Id"].clone();
        }
        if command.starts_with("apply") {
            args["candidateKey"] = json!("mixed");
            args["previewId"] = json!("a".repeat(32));
        }
        for version in [Value::Null, json!(0), json!(2), json!("1"), json!(true)] {
            runtime["adventureDynamicPresetsVersion"] = version;
            write_json_atomic(&runtime_path, &runtime, true).unwrap();
            assert!(queue_command(dir.path(), command, args.clone())
                .unwrap_err()
                .contains("adventureDynamicPresetsVersion"));
            assert!(list_json_stems(&dir.path().join("commands"))
                .unwrap()
                .is_empty());
        }
        runtime["adventureDynamicPresetsVersion"] = json!(1);
        write_json_atomic(&runtime_path, &runtime, true).unwrap();
        let id = queue_command(dir.path(), command, args.clone()).unwrap();
        let path = dir.path().join("commands").join(format!("{id}.json"));
        let queued = read_json_object(&path, MAX_COMMAND_BYTES, false).unwrap();
        assert_eq!(queued["args"], args);
        remove_if_present(&path).unwrap();
    }
    runtime
        .as_object_mut()
        .unwrap()
        .remove("adventureDynamicPresetsVersion");
    write_json_atomic(&runtime_path, &runtime, true).unwrap();
    let args = json!({"presetId": "magic", "libraryRevision": "a".repeat(64)});
    queue_command(dir.path(), "equipGlobalPreset", args).unwrap();
}

#[test]
fn shared_definition_defaults_match_normalization_without_new_beard_fields() {
    let definitions: Value = serde_json::from_str(include_str!(
        "../../../NGUInjector/Data/PresetDefinitions.json"
    ))
    .unwrap();
    let defaults = definitions["defaults"].as_object().unwrap();
    assert!(!defaults.contains_key("EnergyNguWeights"));
    assert!(!defaults.contains_key("MagicNguWeights"));
    assert_eq!(defaults["SupportMode"], "planned");
    assert_eq!(defaults["BeardPriority"], json!([]));
    assert!(!defaults.contains_key("BeardRotation"));
    assert!(!defaults.contains_key("HorizonSeconds"));
    let normalized = normalize_preset_library(json!({
        "Version": 1, "Presets": [{"Id": "minimal", "Name": "Minimal"}]
    }))
    .unwrap();
    let row = normalized["Presets"][0].as_object().unwrap();
    assert_eq!(row.len(), defaults.len() + 2);
    for (key, value) in defaults {
        assert_eq!(&row[key], value, "shared default {key}");
    }
}

#[test]
fn shared_defaults_only_fill_missing_fields_and_do_not_reseed_existing_rows() {
    let future: Value =
        serde_json::from_str(r#"{"exact":18446744073709551616123456789.123456789}"#).unwrap();
    let original = json!({"Version": 1, "Presets": [{
        "Id": "builtin_allNgu", "Name": "User edited seed", "Weights": [0,1,0,0,0,0],
        "AutoUpdate": false, "SupportMode": "current", "BeardPriority": [4,0],
        "BeardRotation": [2], "DiggerWeights": [1,0,0,0,0,0,0,0,0,0,0,0],
        "HorizonSeconds": future.clone(), "Future": future.clone()
    }]});
    let normalized = normalize_preset_library(original.clone()).unwrap();
    assert_eq!(normalized["Presets"].as_array().unwrap().len(), 1);
    for (key, value) in original["Presets"][0].as_object().unwrap() {
        if key != "Weights" {
            assert_eq!(&normalized["Presets"][0][key], value, "preserve {key}");
        }
    }
    assert_eq!(
        normalized["Presets"][0]["Weights"],
        json!([0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0])
    );
    assert_eq!(
        normalize_preset_library(json!({"Version": 1, "Presets": []})).unwrap()["Presets"],
        json!([])
    );
    assert!(normalize_preset_library(json!({
        "Version": 1, "Presets": [{"Id": "null-mode", "Name": "Invalid", "SupportMode": null}]
    }))
    .is_err());
}

fn library() -> Value {
    json!({"Version": 1, "Presets": [{"Id": "energy", "Name": "Energy"}, {"Id": "magic", "Name": "Magic", "AutoUpdate": false}]})
}

fn read(dir: &Path) -> Value {
    serde_json::from_str(
        &tauri::async_runtime::block_on(read_preset_library(dir.to_string_lossy().into_owned()))
            .unwrap(),
    )
    .unwrap()
}

fn save(dir: &Path, library: &Value) -> Result<Value, String> {
    tauri::async_runtime::block_on(save_preset_library(
        dir.to_string_lossy().into_owned(),
        library.to_string(),
    ))
}

#[test]
fn beard_rotation_preserves_absent_null_empty_and_explicit_arrays_across_boundaries() {
    for priority in [json!([]), json!([1, 3, 5, 6])] {
        for rotation in [
            None,
            Some(json!(null)),
            Some(json!([])),
            Some(json!([4, 0, 2])),
            Some(serde_json::from_str("[4.0,0e0,2e0]").unwrap()),
        ] {
            let dir = tempfile::tempdir().unwrap();
            let path = dir.path().join("presets.json");
            let mut original = library();
            original["Presets"][0]["BeardPriority"] = priority.clone();
            if let Some(value) = &rotation {
                original["Presets"][0]["BeardRotation"] = value.clone();
            }
            write_json_atomic(&path, &original, false).unwrap();
            let before = fs::read(&path).unwrap();
            let loaded = read(dir.path());
            assert_eq!(loaded["Presets"][0].get("BeardRotation"), rotation.as_ref());
            assert_eq!(fs::read(&path).unwrap(), before);
            save(dir.path(), &loaded).unwrap();
            let saved: Value = serde_json::from_slice(&fs::read(&path).unwrap()).unwrap();
            assert_eq!(saved["Presets"][0].get("BeardRotation"), rotation.as_ref());
            assert_eq!(saved["Presets"][0]["BeardPriority"], priority);
            let reread = read(dir.path());
            assert_eq!(reread["Presets"], saved["Presets"]);
            assert!(reread["Presets"][1].get("BeardRotation").is_none());
            let response = json!({"result": {"data": {"library": reread}}});
            let connected: Value =
                serde_json::from_str(&connected_preset_library_json(&response).unwrap()).unwrap();
            assert_eq!(connected["Presets"], saved["Presets"]);
            let mut args = json!({"preset": reread["Presets"][0]});
            validate_command("previewLoadoutPreset", &args).unwrap();
            args["candidateKey"] = json!("mixed");
            args["previewId"] = json!("a".repeat(32));
            validate_command("applyLoadoutPreset", &args).unwrap();

            let mut edited = reread;
            edited["Presets"][0]
                .as_object_mut()
                .unwrap()
                .remove("BeardRotation");
            save(dir.path(), &edited).unwrap();
            assert!(read(dir.path())["Presets"][0]
                .get("BeardRotation")
                .is_none());
        }
    }
}

#[test]
fn beard_validation_rejects_invalid_recognized_fields_before_reads_saves_and_actions() {
    let dir = tempfile::tempdir().unwrap();
    let path = dir.path().join("presets.json");
    write_json_atomic(&path, &library(), false).unwrap();
    let loaded = read(dir.path());
    let before = fs::read(&path).unwrap();
    let mut invalid = Vec::new();
    for field in ["BeardPriority", "BeardRotation"] {
        for token in [
            "false",
            "0",
            "\"0\"",
            "{}",
            "[null]",
            "[true]",
            "[\"0\"]",
            "[[]]",
            "[-1]",
            "[7]",
            "[0.5]",
            "[0,0]",
            "[1,1.0]",
            "[0,-0.0]",
            "[1.00000000000000001]",
            "[6.00000000000000001]",
            "[1e-999]",
            "[1e999]",
        ] {
            invalid.push((field, serde_json::from_str::<Value>(token).unwrap()));
        }
    }
    invalid.push(("BeardPriority", json!(null)));
    invalid.push(("BeardRotation", json!([1])));
    invalid.push(("BeardRotation", serde_json::from_str("[1.0]").unwrap()));
    for (field, value) in invalid {
        let mut bad = loaded.clone();
        bad["Presets"][0]["BeardPriority"] = json!([1, 3, 5, 6]);
        bad["Presets"][0][field] = value;
        let error = normalize_preset_library(bad.clone()).unwrap_err();
        assert!(error.contains("Beard"), "{field}: {bad}: {error}");
        assert!(save(dir.path(), &bad).unwrap_err().contains("Beard"));
        assert_eq!(fs::read(&path).unwrap(), before);
        let response = json!({"result": {"data": {"library": bad}}});
        assert!(connected_preset_library_json(&response)
            .unwrap_err()
            .contains("Beard"));
        let mut body = bad.clone();
        body.as_object_mut().unwrap().remove(REVISION_FIELD);
        assert!(validate_command(
            "savePresetLibrary",
            &json!({"library": body, "expectedRevision": loaded[REVISION_FIELD]})
        )
        .unwrap_err()
        .contains("Beard"));
        let mut args = json!({"preset": bad["Presets"][0]});
        assert!(validate_command("previewLoadoutPreset", &args)
            .unwrap_err()
            .contains("Beard"));
        args["candidateKey"] = json!("mixed");
        args["previewId"] = json!("a".repeat(32));
        assert!(validate_command("applyLoadoutPreset", &args)
            .unwrap_err()
            .contains("Beard"));
        assert!(normalize_offline_plan(
            json!({"Version": 17, "LoadoutPresets": [bad["Presets"][0]]})
        )
        .unwrap_err()
        .contains("Beard"));
        write_json_atomic(&path, &bad, true).unwrap();
        let invalid_bytes = fs::read(&path).unwrap();
        assert!(read_preset_library_snapshot(dir.path())
            .unwrap_err()
            .contains("Beard"));
        assert_eq!(fs::read(&path).unwrap(), invalid_bytes);
        write_atomic(&path, &before, true).unwrap();
    }
}

#[test]
fn shared_seed_beards_come_only_from_activity_policy_not_raw_preset_overrides() {
    let definitions: Value = serde_json::from_str(include_str!(
        "../../../NGUInjector/Data/PresetDefinitions.json"
    ))
    .unwrap();
    let policy: Value = serde_json::from_str(include_str!(
        "../../../NGUInjector/Data/ActivityPolicies.json"
    ))
    .unwrap();
    let seeds = definitions["presets"].as_array().unwrap();
    assert_eq!(seeds.len(), 18);
    let dynamic = seeds
        .iter()
        .find(|row| row["Id"] == "builtin_adventureDynamic")
        .unwrap();
    assert_eq!(dynamic["Name"], "adventure-dynamic");
    assert_eq!(dynamic["AdventureDynamicWeight"], 1);
    assert_eq!(dynamic["Weights"], json!(vec![0; 12]));
    assert!(!seeds.iter().any(|row| matches!(
        row["Id"].as_str(),
        Some("builtin_pp" | "builtin_boosts" | "builtin_zone")
    )));
    let mut rows = Vec::new();
    for definition in seeds
        .iter()
        .chain(std::iter::once(&definitions["newPreset"]))
    {
        assert!(definition.get("BeardPriority").is_none());
        assert!(definition.get("BeardRotation").is_none());
        let mut row = definitions["defaults"].as_object().unwrap().clone();
        row.extend(definition.as_object().unwrap().clone());
        row.entry("Id").or_insert_with(|| json!("new_preset"));
        row.insert("BeardPriority".into(), policy["coreBeards"].clone());
        row.insert("BeardRotation".into(), policy["optionalBeards"].clone());
        row.get_mut("Weights")
            .unwrap()
            .as_array_mut()
            .unwrap()
            .resize(14, json!(0));
        rows.push(Value::Object(row));
    }
    let original = json!({"Version": 1, "Presets": rows});
    let normalized = normalize_preset_library(original.clone()).unwrap();
    assert_eq!(normalized, original);
    for row in normalized["Presets"].as_array().unwrap() {
        validate_command("previewLoadoutPreset", &json!({"preset": row})).unwrap();
        assert_eq!(row["BeardPriority"], policy["coreBeards"]);
        assert_eq!(row["BeardRotation"], policy["optionalBeards"]);
    }
}

#[test]
fn shared_cap_catalog_appends_objectives_and_seeds_without_changing_support_defaults() {
    let definitions: Value = serde_json::from_str(include_str!(
        "../../../NGUInjector/Data/PresetDefinitions.json"
    ))
    .unwrap();
    let keys: Vec<_> = definitions["objectives"]
        .as_array()
        .unwrap()
        .iter()
        .map(|row| row["key"].as_str().unwrap())
        .collect();
    assert_eq!(
        keys,
        [
            "energyNgu",
            "magicNgu",
            "blood",
            "pp",
            "boosts",
            "zone",
            "timeMachine",
            "augments",
            "wandoos",
            "advancedTraining",
            "adventurePower",
            "adventureToughness",
            "energyCap",
            "magicCap"
        ]
    );
    assert_eq!(
        definitions["defaults"]["Weights"],
        json!([1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0])
    );
    assert_eq!(definitions["defaults"]["SupportMode"], "planned");
    let diggers = definitions["diggers"].as_array().unwrap();
    assert_eq!(diggers.len(), 12);
    for (id, row) in diggers.iter().enumerate() {
        assert_eq!(row["id"], id);
        assert_eq!(row["objectiveWeights"].as_array().unwrap().len(), 14);
        assert_eq!(row["objectiveWeights"][12], 0);
        assert_eq!(row["objectiveWeights"][13], 0);
    }
    for (index, name) in [(12, "Energy Cap"), (13, "Magic Cap")] {
        let objective = &definitions["objectives"][index];
        assert_eq!(objective["label"], name);
        assert_eq!(objective["seedName"], name);
        let id = format!("builtin_{}", keys[index]);
        let seed = definitions["presets"]
            .as_array()
            .unwrap()
            .iter()
            .find(|row| row["Id"] == id)
            .unwrap();
        assert_eq!(seed["Name"], name);
        assert_eq!(seed["AutoUpdate"], true);
        assert_eq!(seed["SupportMode"], "current");
        let mut weights = vec![0; 14];
        weights[index] = 1;
        assert_eq!(seed["Weights"], json!(weights));
        assert!(seed.get("AdventureDynamicWeight").is_none());
        let normalized =
            normalize_preset_library(json!({"Version": 1, "Presets": [seed]})).unwrap();
        let row = &normalized["Presets"][0];
        assert_eq!(row["Weights"], seed["Weights"]);
        assert_eq!(row["DiggerWeights"], json!([]));
        validate_command("previewLoadoutPreset", &json!({"preset": row})).unwrap();
    }
    for seed in definitions["presets"]
        .as_array()
        .unwrap()
        .iter()
        .filter(|row| {
            !matches!(
                row["Id"].as_str(),
                Some("builtin_energyCap" | "builtin_magicCap")
            )
        })
    {
        let normalized =
            normalize_preset_library(json!({"Version": 1, "Presets": [seed]})).unwrap();
        assert_eq!(normalized["Presets"][0]["SupportMode"], "planned");
        assert_eq!(normalized["Presets"][0]["Weights"][12], 0);
        assert_eq!(normalized["Presets"][0]["Weights"][13], 0);
    }
    assert!(global_preset_protocol()["weights"]
        .as_str()
        .unwrap()
        .contains("energyCap=12, magicCap=13"));
    assert!(protocol_info()["loadoutPresets"]["args"]["preset"]
        .as_str()
        .unwrap()
        .contains("energyCap/magicCap at 12/13"));
}

#[test]
fn global_roundtrip_defaults_preserve_false_and_exact_unknown_tokens() {
    let dir = tempfile::tempdir().unwrap();
    let path = dir.path().join("presets.json");
    let mut original = library();
    let unknown: Value = serde_json::from_str(r#"{"integer":18446744073709551616123456789,"decimal":12345678901234567890.123456789,"tiny":1e-999}"#).unwrap();
    original["Future"] = unknown.clone();
    original["Presets"][0]["Future"] = unknown.clone();
    original["Presets"][0]["Weights"] = json!([1, 2, 3, 4, 5, 6]);
    original["Presets"][1]["SupportMode"] = json!("current");
    let bytes = format!("\u{feff}{}\n", original);
    write_atomic(&path, bytes.as_bytes(), false).unwrap();
    let mut loaded = read(dir.path());
    assert_eq!(loaded[REVISION_FIELD], file_revision(bytes.as_bytes()));
    assert_eq!(fs::read(&path).unwrap(), bytes.as_bytes());
    assert_eq!(loaded["Presets"][0]["AutoUpdate"], true);
    assert_eq!(loaded["Presets"][1]["AutoUpdate"], false);
    assert_eq!(loaded["Presets"][0]["SupportMode"], "planned");
    assert_eq!(loaded["Presets"][1]["SupportMode"], "current");
    assert_eq!(
        loaded["Presets"][0]["Weights"],
        json!([1, 2, 3, 4, 5, 6, 0, 0, 0, 0, 0, 0, 0, 0])
    );
    assert_eq!(
        loaded["Presets"][1]["Weights"],
        json!([1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0])
    );
    assert!(loaded["Presets"]
        .as_array()
        .unwrap()
        .iter()
        .all(|row| row.get("HorizonSeconds").is_none()));
    assert!(loaded["Presets"][0].get("EnergyNguWeights").is_none());
    assert!(loaded["Presets"][0].get("MagicNguWeights").is_none());
    assert_eq!(
        loaded["Presets"][0]["AdvancedTrainingWeights"],
        json!([1, 1, 0, 0, 0])
    );
    loaded["Presets"][0]["Name"] = json!("Edited");
    let response = save(dir.path(), &loaded).unwrap();
    let persisted = fs::read(&path).unwrap();
    assert_eq!(response["data"]["revision"], file_revision(&persisted));
    let reread = read(dir.path());
    assert_eq!(reread["Future"], unknown);
    assert_eq!(reread["Presets"][0]["Future"], unknown);
    assert_eq!(reread["Presets"][0]["Name"], "Edited");
    assert_eq!(reread["Presets"][1]["AutoUpdate"], false);
    assert_eq!(reread["Presets"][0]["SupportMode"], "planned");
    assert_eq!(reread["Presets"][1]["SupportMode"], "current");
    original[REVISION_FIELD] = json!("a".repeat(64));
    let response = json!({"result": {"data": {"library": original}}});
    let connected: Value =
        serde_json::from_str(&connected_preset_library_json(&response).unwrap()).unwrap();
    assert_eq!(connected["Presets"][0]["SupportMode"], "planned");
    assert_eq!(connected["Presets"][1]["SupportMode"], "current");
    assert!(!String::from_utf8(persisted)
        .unwrap()
        .contains(REVISION_FIELD));
    assert!(!dir.path().join("custom-plans").exists());
    assert!(!dir.path().join("settings.json").exists());
}

#[test]
fn legacy_weights_normalize_and_obsolete_horizons_survive_read_and_offline_save() {
    for count in [6, 10, 12, 14] {
        for horizon in [
            json!(3600),
            json!(0),
            json!(-1),
            json!(604801),
            json!(1.5),
            json!(null),
            json!("invalid"),
            json!(true),
            json!([]),
            json!({"old": true}),
            serde_json::from_str("1.2300e+42").unwrap(),
            serde_json::from_str("18446744073709551616123456789.123456789").unwrap(),
        ] {
            let dir = tempfile::tempdir().unwrap();
            let path = dir.path().join("presets.json");
            let weights: Vec<_> = (1..=count).map(|value| json!(value)).collect();
            let mut expected = weights.clone();
            expected.resize(14, json!(0));
            let mut original = library();
            original["Presets"][0]["Weights"] = json!(weights);
            original["Presets"][0]["HorizonSeconds"] = horizon.clone();
            original["Presets"][0]["Future"] = json!({"keep": true});
            original["Presets"][0]["AutoUpdate"] = json!(false);
            original["Presets"][0]["EnergyNguWeights"] = json!([9, 8, 7, 6, 5, 4, 3, 2, 1]);
            original["Presets"][0]["MagicNguWeights"] = json!([7, 6, 5, 4, 3, 2, 1]);
            original["Presets"][0]["AdvancedTrainingWeights"] = json!([0, 0, 0, 1, 1]);
            write_json_atomic(&path, &original, false).unwrap();
            let before = fs::read(&path).unwrap();
            let mut loaded = read(dir.path());
            assert_eq!(loaded["Presets"][0]["Weights"], json!(expected));
            assert_eq!(loaded["Presets"][0]["HorizonSeconds"], horizon);
            assert_eq!(
                loaded["Presets"][0]["HorizonSeconds"].to_string(),
                horizon.to_string()
            );
            assert_eq!(fs::read(&path).unwrap(), before);
            loaded["Presets"][0]
                .as_object_mut()
                .unwrap()
                .remove("HorizonSeconds");
            save(dir.path(), &loaded).unwrap();
            let saved: Value = serde_json::from_slice(&fs::read(&path).unwrap()).unwrap();
            assert_eq!(saved["Presets"][0]["Weights"], json!(expected));
            assert_eq!(saved["Presets"][0]["HorizonSeconds"], horizon);
            assert_eq!(
                saved["Presets"][0]["HorizonSeconds"].to_string(),
                horizon.to_string()
            );
            assert!(saved["Presets"][1].get("HorizonSeconds").is_none());
            for field in ["EnergyNguWeights", "MagicNguWeights"] {
                assert!(saved["Presets"][0].get(field).is_none());
            }
            for field in ["AutoUpdate", "AdvancedTrainingWeights"] {
                assert_eq!(saved["Presets"][0][field], original["Presets"][0][field]);
            }
            assert_eq!(
                saved["Presets"][0]["Future"],
                original["Presets"][0]["Future"]
            );
            assert_eq!(read(dir.path())["Presets"], saved["Presets"]);

            original[REVISION_FIELD] = json!("a".repeat(64));
            let response = json!({"result": {"data": {"library": original}}});
            let connected: Value =
                serde_json::from_str(&connected_preset_library_json(&response).unwrap()).unwrap();
            assert_eq!(connected["Presets"], saved["Presets"]);
            assert_eq!(connected[REVISION_FIELD], original[REVISION_FIELD]);
        }
    }
}

#[test]
fn missing_library_requires_injector_migration_without_initializing() {
    let dir = tempfile::tempdir().unwrap();
    let error = tauri::async_runtime::block_on(read_preset_library(
        dir.path().to_string_lossy().into_owned(),
    ))
    .unwrap_err();
    assert!(error.contains("Connect to the injector"), "{error}");
    let mut incoming = library();
    incoming[REVISION_FIELD] = json!("a".repeat(64));
    assert!(save(dir.path(), &incoming)
        .unwrap_err()
        .contains("Connect to the injector"));
    assert!(!dir.path().join("presets.json").exists());
}

#[test]
fn library_cas_rejects_missing_stale_uppercase_and_removed_revisions() {
    let dir = tempfile::tempdir().unwrap();
    let path = dir.path().join("presets.json");
    write_json_atomic(&path, &library(), false).unwrap();
    let loaded = read(dir.path());
    let before = fs::read(&path).unwrap();
    for revision in [
        None,
        Some(json!("")),
        Some(Value::Null),
        Some(json!("A".repeat(64))),
        Some(json!("a".repeat(64))),
    ] {
        let mut invalid = loaded.clone();
        invalid.as_object_mut().unwrap().remove(REVISION_FIELD);
        if let Some(revision) = revision {
            invalid[REVISION_FIELD] = revision;
        }
        assert!(save(dir.path(), &invalid).is_err());
        assert_eq!(fs::read(&path).unwrap(), before);
    }
    let response = save(dir.path(), &loaded).unwrap();
    assert_ne!(response["data"]["revision"], loaded[REVISION_FIELD]);
    assert!(save(dir.path(), &loaded)
        .unwrap_err()
        .contains("revision conflict"));
    fs::remove_file(&path).unwrap();
    assert!(save(dir.path(), &loaded)
        .unwrap_err()
        .contains("Connect to the injector"));
    assert!(!path.exists());
}

#[test]
fn library_offline_operations_obey_both_writer_and_injector_locks() {
    let dir = tempfile::tempdir().unwrap();
    let path = dir.path().join("presets.json");
    write_json_atomic(&path, &library(), false).unwrap();
    let loaded = read(dir.path());
    let before = fs::read(&path).unwrap();
    {
        let _writer = lock_offline_writer(dir.path()).unwrap();
        assert!(save(dir.path(), &loaded)
            .unwrap_err()
            .contains("Another UI writer"));
    }
    {
        let _ownership = lock_injector_ownership(dir.path()).unwrap();
        assert!(save(dir.path(), &loaded).unwrap_err().contains("ownership"));
        assert!(tauri::async_runtime::block_on(read_preset_library(
            dir.path().to_string_lossy().into_owned()
        ))
        .is_err());
    }
    assert_eq!(fs::read(&path).unwrap(), before);
    save(dir.path(), &loaded).unwrap();
}

#[test]
fn deletion_rejects_active_and_stage_references_without_modifying_either_file() {
    for reference in [
        json!({"ActivePresetId": "energy"}),
        json!({"Stages": [{"PresetId": "energy"}]}),
        json!({"ActivePresetId": "energy", "Stages": [{"PresetId": "@active"}]}),
    ] {
        let dir = tempfile::tempdir().unwrap();
        let path = dir.path().join("presets.json");
        write_json_atomic(&path, &library(), false).unwrap();
        let plan_path = plan_path(dir.path(), "reference").unwrap();
        write_json_atomic(&plan_path, &reference, false).unwrap();
        let before = fs::read(&path).unwrap();
        let plan_before = fs::read(&plan_path).unwrap();
        let mut incoming = read(dir.path());
        incoming["Presets"].as_array_mut().unwrap().remove(0);
        let error = save(dir.path(), &incoming).unwrap_err();
        assert!(
            error.contains("references global preset 'energy'"),
            "{error}"
        );
        assert_eq!(fs::read(&path).unwrap(), before);
        assert_eq!(fs::read(&plan_path).unwrap(), plan_before);
        fs::remove_file(&plan_path).unwrap();
        save(dir.path(), &incoming).unwrap();
        assert_eq!(read(dir.path())["Presets"].as_array().unwrap().len(), 1);
    }
}

#[test]
fn library_saves_fail_closed_for_malformed_reference_files() {
    for plan in [
        json!({"ActivePresetId": null}),
        json!({"Stages": [{"PresetId": 1}]}),
        json!({"Stages": {"PresetId": "energy"}}),
        json!({"Stages": [1]}),
    ] {
        let dir = tempfile::tempdir().unwrap();
        let path = dir.path().join("presets.json");
        write_json_atomic(&path, &library(), false).unwrap();
        write_json_atomic(&plan_path(dir.path(), "bad").unwrap(), &plan, false).unwrap();
        let before = fs::read(&path).unwrap();
        assert!(save(dir.path(), &read(dir.path())).is_err(), "{plan}");
        assert_eq!(fs::read(&path).unwrap(), before);
    }
}

#[test]
fn offline_plan_save_and_copy_validate_new_global_references() {
    let dir = tempfile::tempdir().unwrap();
    write_json_atomic(&dir.path().join("presets.json"), &library(), false).unwrap();
    for (active, stage, valid) in [
        ("", "", true),
        ("energy", "magic", true),
        ("unknown", "", false),
        ("", "unknown", false),
        ("../bad", "", false),
        ("energy", "@active", true),
        ("", "@active", false),
        ("unknown", "@active", false),
        ("@active", "@active", false),
    ] {
        let mut plan = default_plan();
        plan["ActivePresetId"] = json!(active);
        plan["Stages"][0]["PresetId"] = json!(stage);
        let name = format!("plan_{}", Uuid::new_v4());
        let outcome = save_plan_offline(dir.path(), &name, &plan, None);
        assert_eq!(outcome.is_ok(), valid, "{outcome:?}");
        assert_eq!(plan_path(dir.path(), &name).unwrap().exists(), valid);
        let copy = tauri::async_runtime::block_on(create_plan(
            dir.path().to_string_lossy().into_owned(),
            format!("copy_{name}"),
            Some(plan.to_string()),
        ));
        assert_eq!(copy.is_ok(), valid, "{copy:?}");
    }
}

#[test]
fn global_library_validation_is_strict_and_bounded() {
    let normalized = normalize_preset_library(library()).unwrap();
    for (key, value) in [
        ("SupportMode", json!(null)),
        ("SupportMode", json!(false)),
        ("SupportMode", json!(0)),
        ("SupportMode", json!("auto")),
        ("AutoUpdate", json!(null)),
        ("AutoUpdate", json!("false")),
        (
            "AdvancedTrainingWeights",
            json!([1, 1, 0, 0, 2147483648_u64]),
        ),
        ("Weights", json!(vec![0; 6])),
        ("Weights", json!(vec![0; 10])),
        ("Weights", json!(vec![0; 12])),
        ("Weights", json!(vec![0; 14])),
        ("Weights", json!(vec![1; 11])),
        ("Weights", json!(vec![1; 13])),
        ("Weights", json!(vec![1; 15])),
        ("DiggerWeights", json!(vec![1; 14])),
        ("Weights", json!([0, 0, 0, 0, 0, 0, 0, 0, 0, 0, -1, 1])),
        ("Weights", json!([0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1000001])),
    ] {
        let mut bad = normalized.clone();
        bad["Presets"][0][key] = value;
        assert!(normalize_preset_library(bad).is_err(), "{key}");
    }
    for field in ["AdvancedTrainingWeights"] {
        let mut bad = normalized.clone();
        bad["Presets"][0][field][0] = serde_json::from_str("1.00000000000000001").unwrap();
        assert!(normalize_preset_library(bad).is_err());
    }
    for bad in [
        json!({"Version": 2, "Presets": []}),
        json!({"Version": 1}),
        json!({"Version": 1, "Presets": [normalized["Presets"][0].clone(), normalized["Presets"][0].clone()]}),
    ] {
        assert!(normalize_preset_library(bad).is_err());
    }
    let rows: Vec<_> = (0..1024)
        .map(|id| json!({"Id": format!("p_{id}"), "Name": "Preset"}))
        .collect();
    let mut boundary = json!({"Version": 1, "Presets": rows});
    assert_eq!(
        normalize_preset_library(boundary.clone()).unwrap()["Presets"]
            .as_array()
            .unwrap()
            .len(),
        1024
    );
    boundary["Presets"]
        .as_array_mut()
        .unwrap()
        .push(json!({"Id": "too_many", "Name": "Extra"}));
    assert!(normalize_preset_library(boundary)
        .unwrap_err()
        .contains("1024"));
    let mut oversized = normalized;
    oversized["Future"] = json!("x".repeat(MAX_PRESET_LIBRARY_BYTES as usize));
    assert!(normalize_preset_library(oversized)
        .unwrap_err()
        .contains("2 MiB"));
}

fn action_args(command: &str) -> Value {
    let mut args = json!({"presetId": "builtin_energy_ngu", "libraryRevision": "a".repeat(64)});
    if command == "applyGlobalPreset" {
        args["previewId"] = json!("0123456789abcdef0123456789abcdef");
        args["candidateKey"] = json!("mixed_1-2");
    }
    args
}

#[test]
fn global_commands_reject_extras_missing_fields_and_invalid_tokens() {
    for command in [
        "previewGlobalPreset",
        "applyGlobalPreset",
        "equipGlobalPreset",
    ] {
        let args = action_args(command);
        validate_command(command, &args).unwrap();
        for key in args.as_object().unwrap().keys() {
            let mut missing = args.clone();
            missing.as_object_mut().unwrap().remove(key);
            assert!(validate_command(command, &missing).is_err());
            for invalid in [
                json!(null),
                json!(true),
                json!(1),
                json!(""),
                json!(" ../bad "),
                json!("g".repeat(65)),
            ] {
                let mut bad = args.clone();
                bad[key] = invalid;
                assert!(validate_command(command, &bad).is_err(), "{command}: {bad}");
            }
        }
        let mut extra = args.clone();
        extra["plan"] = json!("not allowed");
        assert!(validate_command(command, &extra).is_err());
        let mut uppercase = args;
        uppercase["libraryRevision"] = json!("A".repeat(64));
        assert!(validate_command(command, &uppercase).is_err());
    }
    validate_command("readPresetLibrary", &json!({})).unwrap();
    assert!(validate_command("readPresetLibrary", &json!({"name": "plan"})).is_err());
    let mut args = json!({"library": library(), "expectedRevision": "a".repeat(64)});
    validate_command("savePresetLibrary", &args).unwrap();
    args["library"][REVISION_FIELD] = json!("a".repeat(64));
    assert!(validate_command("savePresetLibrary", &args).is_err());
}

#[test]
fn global_queue_requires_fresh_protocol_capability_and_advertised_commands_not_plan_timing() {
    let dir = tempfile::tempdir().unwrap();
    write_json_atomic(&dir.path().join("presets.json"), &library(), false).unwrap();
    let runtime_path = dir.path().join("runtime.json");
    let commands = [
        "readPresetLibrary",
        "savePresetLibrary",
        "previewGlobalPreset",
        "applyGlobalPreset",
        "equipGlobalPreset",
    ];
    let runtime = json!({"externalUiEnabled": true, "status": "running", "updatedAt": Utc::now().to_rfc3339(), "protocolVersion": 3, "globalPresetsVersion": 2, "globalNguWeightsVersion": 1, "startedAt": "session", "supportedCommands": commands});
    for command in commands {
        let args = match command {
            "readPresetLibrary" => json!({}),
            "savePresetLibrary" => {
                json!({"library": library(), "expectedRevision": "a".repeat(64)})
            }
            _ => action_args(command),
        };
        for (field, value) in [
            ("globalPresetsVersion", Value::Null),
            ("globalPresetsVersion", json!("1")),
            ("globalPresetsVersion", json!(true)),
            ("globalPresetsVersion", json!(1)),
            ("globalNguWeightsVersion", Value::Null),
            ("globalNguWeightsVersion", json!(0)),
            ("globalNguWeightsVersion", json!("1")),
            ("protocolVersion", json!(2)),
            ("supportedCommands", json!([])),
            (
                "updatedAt",
                json!((Utc::now() - chrono::Duration::seconds(30)).to_rfc3339()),
            ),
        ] {
            let mut bad = runtime.clone();
            bad[field] = value;
            write_json_atomic(&runtime_path, &bad, true).unwrap();
            assert!(
                queue_command(dir.path(), command, args.clone()).is_err(),
                "{command}: {bad}"
            );
            assert!(list_json_stems(&dir.path().join("commands"))
                .unwrap()
                .is_empty());
        }
        write_json_atomic(&runtime_path, &runtime, true).unwrap();
        let id = queue_command(dir.path(), command, args.clone()).unwrap();
        let path = dir.path().join("commands").join(format!("{id}.json"));
        let queued = read_json_object(&path, MAX_PRESET_LIBRARY_RESULT_BYTES, false).unwrap();
        assert_eq!(queued["command"], command);
        assert_eq!(queued["args"]["expectedRevision"], args["expectedRevision"]);
        assert!(queued["expiresAt"].as_str().unwrap() > queued["createdAt"].as_str().unwrap());
        remove_if_present(&path).unwrap();
    }
}

#[test]
fn inline_commands_accept_legacy_and_fourteen_weights_without_preset_horizon() {
    let row = normalize_preset_library(library()).unwrap()["Presets"][0].clone();
    assert!(row.get("HorizonSeconds").is_none());
    for count in [6, 10, 12, 14] {
        let mut row = row.clone();
        row["Weights"] = json!(vec![1; count]);
        let mut args = json!({"preset": row});
        validate_command("previewLoadoutPreset", &args).unwrap();
        args["candidateKey"] = json!("mixed");
        args["previewId"] = json!("a".repeat(32));
        validate_command("applyLoadoutPreset", &args).unwrap();
    }
}

#[test]
fn offline_legacy_horizons_follow_stable_ids_after_reordering_and_cloning() {
    let dir = tempfile::tempdir().unwrap();
    let path = dir.path().join("presets.json");
    let mut original = library();
    original["Presets"][0]["HorizonSeconds"] = serde_json::from_str("1.2300e+42").unwrap();
    original["Presets"][1]["HorizonSeconds"] = json!({"obsolete": true});
    write_json_atomic(&path, &original, false).unwrap();
    let mut edited = read(dir.path());
    let rows = edited["Presets"].as_array_mut().unwrap();
    rows.reverse();
    for row in rows.iter_mut() {
        row.as_object_mut().unwrap().remove("HorizonSeconds");
    }
    let mut clone = rows[1].clone();
    clone["Id"] = json!("energy_copy");
    rows.push(clone);
    save(dir.path(), &edited).unwrap();
    let saved = read(dir.path());
    assert_eq!(saved["Presets"][0]["Id"], "magic");
    assert_eq!(
        saved["Presets"][0]["HorizonSeconds"],
        original["Presets"][1]["HorizonSeconds"]
    );
    assert_eq!(saved["Presets"][1]["Id"], "energy");
    assert_eq!(
        saved["Presets"][1]["HorizonSeconds"].to_string(),
        "1.2300e+42"
    );
    assert!(saved["Presets"][2].get("HorizonSeconds").is_none());
}

#[test]
fn adventure_and_cap_global_objectives_roundtrip_and_reject_invalid_tail_weights() {
    for index in [10, 11, 12, 13] {
        let dir = tempfile::tempdir().unwrap();
        let path = dir.path().join("presets.json");
        let mut original = library();
        original["Presets"][0]["Weights"] = json!(vec![0; 14]);
        original["Presets"][0]["DiggerWeights"] = json!(vec![1; 12]);
        original["Presets"][0]["Weights"][index] = json!(0.5);
        write_json_atomic(&path, &original, false).unwrap();
        let loaded = read(dir.path());
        assert_eq!(
            loaded["Presets"][0]["Weights"],
            original["Presets"][0]["Weights"]
        );
        save(dir.path(), &loaded).unwrap();
        assert_eq!(
            read(dir.path())["Presets"][0]["DiggerWeights"],
            json!(vec![1; 12])
        );
        let before = fs::read(&path).unwrap();
        let mut bad = read(dir.path());
        for token in [
            "-1",
            "1000000.0000000000000001",
            "1e999",
            "null",
            "true",
            "\"1\"",
        ] {
            bad["Presets"][0]["Weights"][index] = serde_json::from_str(token).unwrap();
            assert!(
                save(dir.path(), &bad).unwrap_err().contains("Weight"),
                "{index}: {token}"
            );
            assert_eq!(fs::read(&path).unwrap(), before);
        }
    }
}

#[test]
fn connected_library_results_preserve_revision_and_support_large_envelopes() {
    let dir = tempfile::tempdir().unwrap();
    let mut library = normalize_preset_library(library()).unwrap();
    library["Future"] = json!("x".repeat(ACTION_RESULT_MAX_BYTES as usize));
    library["Presets"][0]["HorizonSeconds"] =
        serde_json::from_str("18446744073709551616123456789.123456789").unwrap();
    library[REVISION_FIELD] = json!("a".repeat(64));
    let id = Uuid::new_v4().to_string();
    let result = json!({"id": id, "command": "readPresetLibrary", "success": true, "message": "ok", "completedAt": Utc::now().to_rfc3339(), "data": {"library": library}});
    write_json_atomic(
        &dir.path()
            .join("command-results")
            .join(format!("{id}.json")),
        &result,
        false,
    )
    .unwrap();
    assert_eq!(
        read_result(dir.path(), &id, Some("readPresetLibrary")).unwrap(),
        Some(result.clone())
    );
    assert_eq!(
        read_command_result(dir.path().to_string_lossy().into_owned(), id.clone()).unwrap(),
        Some(result)
    );
    let response = wait_for_command(dir.path(), &id, "readPresetLibrary", Instant::now()).unwrap();
    let parsed: Value =
        serde_json::from_str(&connected_preset_library_json(&response).unwrap()).unwrap();
    assert_eq!(parsed, library);
    assert!(list_command_results(&dir.path().join("command-results"))
        .unwrap()
        .iter()
        .any(|row| row.command == "readPresetLibrary"));
    let mut bad = response;
    bad["result"]["data"]["library"]
        .as_object_mut()
        .unwrap()
        .remove(REVISION_FIELD);
    assert!(connected_preset_library_json(&bad).is_err());
}

#[test]
fn offline_saves_preserve_injector_owned_migration_receipts() {
    let dir = tempfile::tempdir().unwrap();
    let path = dir.path().join("presets.json");
    let mut original = library();
    original["MigrationReceipts"] =
        json!({"import_hash": {"PresetId": "deleted_import", "Future": 9007199254740993_u64}});
    write_json_atomic(&path, &original, false).unwrap();
    let mut edited = read(dir.path());
    edited["MigrationReceipts"] = json!({"forged": {"PresetId": "energy"}});
    save(dir.path(), &edited).unwrap();
    assert_eq!(
        read(dir.path())["MigrationReceipts"],
        original["MigrationReceipts"]
    );
}

#[test]
fn offline_saves_preserve_cap_migration_marker_after_deletion() {
    for stored in [None, Some(false), Some(true)] {
        let dir = tempfile::tempdir().unwrap();
        let mut original = library();
        original["Presets"][0]["Id"] = json!("builtin_energyNgu");
        if let Some(value) = stored {
            original["CapPresetsAdded"] = json!(value);
        }
        write_json_atomic(&dir.path().join("presets.json"), &original, false).unwrap();
        for submitted in [None, Some(false), Some(true)] {
            let mut edited = read(dir.path());
            edited.as_object_mut().unwrap().remove("CapPresetsAdded");
            if let Some(value) = submitted {
                edited["CapPresetsAdded"] = json!(value);
            }
            save(dir.path(), &edited).unwrap();
            let reread = read(dir.path());
            assert_eq!(
                reread.get("CapPresetsAdded"),
                original.get("CapPresetsAdded")
            );
            assert_eq!(
                reread["Presets"],
                normalize_preset_library(original.clone()).unwrap()["Presets"]
            );
        }
    }
    for invalid in [json!(null), json!(0), json!("true"), json!([]), json!({})] {
        let mut original = library();
        original["CapPresetsAdded"] = invalid;
        assert!(normalize_preset_library(original)
            .unwrap_err()
            .contains("CapPresetsAdded"));
    }
}

#[test]
fn retiring_preset_ratios_preserves_import_ids_references_and_historical_receipt_aliases() {
    let dir = tempfile::tempdir().unwrap();
    let path = dir.path().join("presets.json");
    let original = json!({"Version": 1, "Presets": [{"Id": "import_existing", "Name": "User edited import", "EnergyNguWeights": [1], "MagicNguWeights": false}],
        "MigrationReceipts": {"historical_ratio_hash": {"PresetId": "import_existing", "Source": "legacy"}, "ratio_free_alias": {"PresetId": "import_existing", "Source": "legacy"}}});
    write_json_atomic(&path, &original, false).unwrap();
    let mut plan = default_plan();
    plan["ActivePresetId"] = json!("import_existing");
    plan["Stages"][0]["PresetId"] = json!("@active");
    let plan_path = plan_path(dir.path(), "legacy").unwrap();
    write_json_atomic(&plan_path, &plan, false).unwrap();
    let plan_bytes = fs::read(&plan_path).unwrap();
    let mut edited = read(dir.path());
    assert!(edited["Presets"][0].get("EnergyNguWeights").is_none());
    assert!(edited["Presets"][0].get("MagicNguWeights").is_none());
    edited["Presets"][0]["Name"] = json!("Still the same import");
    save(dir.path(), &edited).unwrap();
    let saved = read_json_object(&path, MAX_PRESET_LIBRARY_BYTES, false).unwrap();
    assert_eq!(saved["Presets"].as_array().unwrap().len(), 1);
    assert_eq!(saved["Presets"][0]["Id"], "import_existing");
    assert_eq!(saved["Presets"][0]["Name"], "Still the same import");
    assert!(saved["Presets"][0].get("EnergyNguWeights").is_none());
    assert!(saved["Presets"][0].get("MagicNguWeights").is_none());
    assert_eq!(saved["MigrationReceipts"], original["MigrationReceipts"]);
    assert_eq!(fs::read(&plan_path).unwrap(), plan_bytes);
}

#[test]
fn referenced_plans_require_valid_schema_offline_and_global_capability_when_queued() {
    let dir = tempfile::tempdir().unwrap();
    let path = dir.path().join("presets.json");
    write_json_atomic(&path, &library(), false).unwrap();
    let mut plan = default_plan();
    plan["ActivePresetId"] = json!("energy");
    plan["Stages"][0]["DurationSeconds"] = json!(-1);
    write_json_atomic(&plan_path(dir.path(), "invalid").unwrap(), &plan, false).unwrap();
    assert!(save(dir.path(), &read(dir.path()))
        .unwrap_err()
        .contains("DurationSeconds"));
    plan["Stages"][0]["DurationSeconds"] = json!(0);
    let mut runtime = json!({"externalUiEnabled": true, "status": "running", "updatedAt": Utc::now().to_rfc3339(), "protocolVersion": 3, "stageTimingVersion": 1, "globalNguWeightsVersion": 1, "planVersion": 17, "supportedCommands": ["readPlan", "savePlan", "createPlan"]});
    for (command, args) in [
        (
            "savePlan",
            json!({"name": "new", "plan": plan, "expectedRevision": ""}),
        ),
        (
            "createPlan",
            json!({"requestedName": "new", "source": plan}),
        ),
    ] {
        runtime
            .as_object_mut()
            .unwrap()
            .remove("globalPresetsVersion");
        write_json_atomic(&dir.path().join("runtime.json"), &runtime, true).unwrap();
        assert!(queue_command(dir.path(), command, args.clone())
            .unwrap_err()
            .contains("globalPresetsVersion"));
        runtime["globalPresetsVersion"] = json!(2);
        write_json_atomic(&dir.path().join("runtime.json"), &runtime, true).unwrap();
        let id = queue_command(dir.path(), command, args).unwrap();
        remove_if_present(&dir.path().join("commands").join(format!("{id}.json"))).unwrap();
    }
}

#[test]
fn library_file_limit_accepts_two_mib_and_rejects_one_extra_byte() {
    let dir = tempfile::tempdir().unwrap();
    let path = dir.path().join("presets.json");
    let native = concat!(
        "{\r\n",
        "    \"Future\" : {\r\n",
        "        \"array\" : [\r\n",
        "        ],\r\n",
        "        \"exact\" : 18446744073709551616123456789.123456789,\r\n",
        "        \"object\" : {\r\n",
        "        },\r\n",
        "        \"text\" : \"caf\u{e9} \\\"quoted\\\"\\n\\\\\"\r\n",
        "    },\r\n",
        "    \"Padding\" : \"\",\r\n",
        "    \"Presets\" : [\r\n",
        "    ],\r\n",
        "    \"Version\" : 1\r\n",
        "}"
    );
    let padding = MAX_PRESET_LIBRARY_BYTES as usize - native.len();
    let bytes = native
        .replacen(
            "\"Padding\" : \"\"",
            &format!("\"Padding\" : \"{}\"", "x".repeat(padding)),
            1,
        )
        .into_bytes();
    assert_eq!(bytes.len() as u64, MAX_PRESET_LIBRARY_BYTES);
    write_atomic(&path, &bytes, false).unwrap();
    let mut loaded = read(dir.path());
    let expected_unknown = loaded["Future"].clone();
    save(dir.path(), &loaded).unwrap();
    let saved = fs::read(&path).unwrap();
    let reread = read(dir.path());
    assert_eq!(reread["Future"], expected_unknown);
    assert_eq!(reread["Padding"], "x".repeat(padding));
    loaded["Padding"] = json!("x".repeat(padding + 1));
    loaded[REVISION_FIELD] = reread[REVISION_FIELD].clone();
    let mut body = loaded.clone();
    body.as_object_mut().unwrap().remove(REVISION_FIELD);
    assert!(serde_json::to_vec_pretty(&body).unwrap().len() < MAX_PRESET_LIBRARY_BYTES as usize);
    assert!(save(dir.path(), &loaded).unwrap_err().contains("2 MiB"));
    assert_eq!(fs::read(&path).unwrap(), saved);
    let oversized = [bytes.as_slice(), b" "].concat();
    write_atomic(&path, &oversized, true).unwrap();
    assert!(read_preset_library_snapshot(dir.path())
        .unwrap_err()
        .contains("2097152 byte JSON limit"));
}

#[test]
fn native_size_budget_rejects_exponent_integer_expansion_without_publication() {
    let dir = tempfile::tempdir().unwrap();
    let path = dir.path().join("presets.json");
    let native = concat!(
        "{\r\n",
        "    \"Future\" : 900719925474099312345.123456789,\r\n",
        "    \"Padding\" : \"\",\r\n",
        "    \"Presets\" : [\r\n",
        "        {\r\n",
        "            \"AdvancedTrainingWeights\" : [\r\n",
        "                1000000000,\r\n",
        "                1000000000,\r\n",
        "                0,\r\n",
        "                0,\r\n",
        "                0\r\n",
        "            ],\r\n",
        "            \"AutoUpdate\" : true,\r\n",
        "            \"BeardPriority\" : [\r\n",
        "                1\r\n",
        "            ],\r\n",
        "            \"BeardRotation\" : [\r\n",
        "                2\r\n",
        "            ],\r\n",
        "            \"DiggerBudgetPercent\" : 90,\r\n",
        "            \"DiggerWeights\" : [\r\n",
        "            ],\r\n",
        "            \"EnergyPriorities\" : [\r\n",
        "            ],\r\n",
        "            \"Id\" : \"integer-boundary\",\r\n",
        "            \"MagicBloodPercent\" : 100,\r\n",
        "            \"MagicPriorities\" : [\r\n",
        "            ],\r\n",
        "            \"MinimumPower\" : 0,\r\n",
        "            \"MinimumToughness\" : 0,\r\n",
        "            \"Name\" : \"Integer boundary\",\r\n",
        "            \"ResourcePrioritiesReviewed\" : false,\r\n",
        "            \"SupportMode\" : \"planned\",\r\n",
        "            \"SwapSeconds\" : 1000,\r\n",
        "            \"Weights\" : [\r\n",
        "                1,\r\n",
        "                1,\r\n",
        "                0,\r\n",
        "                0,\r\n",
        "                0,\r\n",
        "                0,\r\n",
        "                0,\r\n",
        "                0,\r\n",
        "                0,\r\n",
        "                0,\r\n",
        "                0,\r\n",
        "                0,\r\n",
        "                0,\r\n",
        "                0\r\n",
        "            ],\r\n",
        "            \"Zone\" : 10\r\n",
        "        }\r\n",
        "    ],\r\n",
        "    \"Version\" : 1\r\n",
        "}"
    );
    let padding = MAX_PRESET_LIBRARY_BYTES as usize - native.len();
    let mut source = parse_json_object(native.as_bytes(), "Native integer fixture").unwrap();
    source["Padding"] = json!("x".repeat(padding));
    assert_eq!(native.len() + padding, MAX_PRESET_LIBRARY_BYTES as usize);
    write_json_atomic(&path, &source, false).unwrap();
    save(dir.path(), &read(dir.path())).unwrap();
    let before = fs::read(&path).unwrap();
    let mut loaded = read(dir.path());
    let row = &mut loaded["Presets"][0];
    row["AdvancedTrainingWeights"] = serde_json::from_str("[1e9,1e9,0,0,0]").unwrap();
    row["BeardPriority"] = serde_json::from_str("[1e0]").unwrap();
    row["BeardRotation"] = serde_json::from_str("[2e0]").unwrap();
    row["MagicBloodPercent"] = serde_json::from_str("1e2").unwrap();
    row["DiggerBudgetPercent"] = serde_json::from_str("9e1").unwrap();
    row["SwapSeconds"] = serde_json::from_str("1e3").unwrap();
    row.as_object_mut()
        .unwrap()
        .remove("ResourcePrioritiesReviewed");
    loaded["Padding"] = json!("x".repeat(padding + 1));
    let mut body = loaded.clone();
    body.as_object_mut().unwrap().remove(REVISION_FIELD);
    let body = normalize_preset_library(body).unwrap();
    assert!(serde_json::to_vec_pretty(&body).unwrap().len() < MAX_PRESET_LIBRARY_BYTES as usize);
    assert!(save(dir.path(), &loaded).unwrap_err().contains("2 MiB"));
    assert_eq!(fs::read(&path).unwrap(), before);
    assert!(!dir.path().join("commands").exists());

    loaded["Padding"] = json!("");
    save(dir.path(), &loaded).unwrap();
    let reread = read(dir.path());
    assert_eq!(reread["Presets"], loaded["Presets"]);
    assert_eq!(
        reread["Future"].to_string(),
        "900719925474099312345.123456789"
    );
    assert_eq!(
        reread["Presets"][0]["AdvancedTrainingWeights"][0].to_string(),
        "1e+9"
    );
}

#[test]
fn offline_library_reads_allow_reducing_native_oversized_content() {
    let dir = tempfile::tempdir().unwrap();
    let path = dir.path().join("presets.json");
    let mut source = normalize_preset_library(library()).unwrap();
    source["Future"] = json!({"exact": serde_json::from_str::<Value>("900719925474099312345.123456789").unwrap(), "padding": ""});
    let padding =
        MAX_PRESET_LIBRARY_BYTES as usize - serde_json::to_vec_pretty(&source).unwrap().len();
    source["Future"]["padding"] = json!("x".repeat(padding));
    let before = serde_json::to_vec_pretty(&source).unwrap();
    assert_eq!(before.len() as u64, MAX_PRESET_LIBRARY_BYTES);
    write_atomic(&path, &before, false).unwrap();
    let mut loaded = read(dir.path());
    assert_eq!(loaded["Future"], source["Future"]);
    assert_eq!(fs::read(&path).unwrap(), before);
    assert!(save(dir.path(), &loaded).unwrap_err().contains("2 MiB"));
    assert_eq!(fs::read(&path).unwrap(), before);
    loaded["Future"]["padding"] = json!("");
    save(dir.path(), &loaded).unwrap();
    let reread = read(dir.path());
    assert_eq!(reread["Future"]["padding"], "");
    assert_eq!(
        reread["Future"]["exact"].to_string(),
        "900719925474099312345.123456789"
    );
}

#[test]
fn tauri_connected_read_and_save_use_global_queue_contracts_without_offline_writes() {
    let dir = tempfile::tempdir().unwrap();
    let path = dir.path().join("presets.json");
    write_json_atomic(&path, &library(), false).unwrap();
    let before = fs::read(&path).unwrap();
    write_json_atomic(&dir.path().join("runtime.json"), &json!({"externalUiEnabled": true, "status": "running", "updatedAt": Utc::now().to_rfc3339(), "protocolVersion": 3, "globalPresetsVersion": 2, "globalNguWeightsVersion": 1, "supportedCommands": ["readPresetLibrary", "savePresetLibrary"]}), false).unwrap();
    let _ownership = lock_injector_ownership(dir.path()).unwrap();
    let server_dir = dir.path().to_owned();
    let bridge = std::thread::spawn(move || {
        let mut requests = Vec::new();
        for command in ["readPresetLibrary", "savePresetLibrary"] {
            let deadline = Instant::now() + Duration::from_secs(3);
            let (id, request) = loop {
                let pending = list_json_stems(&server_dir.join("commands")).unwrap();
                if let Some(id) = pending.first() {
                    let bytes = read_file_bytes(
                        &server_dir.join("commands").join(format!("{id}.json")),
                        MAX_PRESET_LIBRARY_RESULT_BYTES,
                        true,
                    )
                    .unwrap();
                    // The waiter can remove the previous request between listing and opening it.
                    if let Some(bytes) = bytes {
                        let request = parse_json_object(&bytes, "Queued fixture command").unwrap();
                        if request["command"] == command {
                            break (id.clone(), request);
                        }
                    }
                }
                assert!(Instant::now() < deadline, "Timed out waiting for {command}");
                std::thread::sleep(Duration::from_millis(5));
            };
            // A connected waiter must release the UI lock before waiting for the bridge.
            let _writer = lock_offline_writer(&server_dir).unwrap();
            let data = if command == "readPresetLibrary" {
                let mut library = normalize_preset_library(library()).unwrap();
                library[REVISION_FIELD] = json!("a".repeat(64));
                json!({"library": library})
            } else {
                json!({"revision": "b".repeat(64)})
            };
            let response = json!({"id": id, "command": command, "success": true, "message": "ok", "completedAt": Utc::now().to_rfc3339(), "data": data});
            write_json_atomic(
                &server_dir
                    .join("command-results")
                    .join(format!("{id}.json")),
                &response,
                false,
            )
            .unwrap();
            requests.push(request);
        }
        requests
    });
    let loaded = read(dir.path());
    assert_eq!(loaded[REVISION_FIELD], "a".repeat(64));
    let result = save(dir.path(), &loaded).unwrap();
    assert_eq!(result["mode"], "connected");
    assert_eq!(result["result"]["data"]["revision"], "b".repeat(64));
    let requests = bridge.join().unwrap();
    assert_eq!(requests[0]["args"], json!({}));
    assert_eq!(
        requests[1]["args"]["expectedRevision"],
        loaded[REVISION_FIELD]
    );
    let mut expected = loaded;
    expected.as_object_mut().unwrap().remove(REVISION_FIELD);
    assert_eq!(requests[1]["args"]["library"], expected);
    assert_eq!(fs::read(&path).unwrap(), before);
    assert!(list_json_stems(&dir.path().join("commands"))
        .unwrap()
        .is_empty());
}

#[test]
fn active_stage_sentinel_resolves_only_the_validated_plan_selection() {
    assert_eq!(
        plan_preset_references(
            &json!({"ActivePresetId": "energy", "Stages": [{"PresetId": "@active"}]})
        )
        .unwrap(),
        vec!["energy"]
    );
    for active in [None, Some(json!(""))] {
        let mut plan = json!({"Stages": [{"PresetId": "@active"}]});
        if let Some(active) = active {
            plan["ActivePresetId"] = active;
        }
        assert!(plan_preset_references(&plan)
            .unwrap_err()
            .contains("requires a nonempty valid ActivePresetId"));
        assert!(validate_references(&plan, &normalize_preset_library(library()).unwrap()).is_err());
    }
    for active in [json!(null), json!(1), json!("@active"), json!("../bad")] {
        let plan = json!({"ActivePresetId": active, "Stages": [{"PresetId": "@active"}]});
        assert!(plan_preset_references(&plan)
            .unwrap_err()
            .contains("ActivePresetId"));
    }
    let unresolved = json!({"ActivePresetId": "missing", "Stages": [{"PresetId": "@active"}]});
    assert!(
        validate_references(&unresolved, &normalize_preset_library(library()).unwrap())
            .unwrap_err()
            .contains("references global preset 'missing'")
    );
    let dir = tempfile::tempdir().unwrap();
    let mut plan = default_plan();
    plan["ActivePresetId"] = json!("energy");
    plan["Stages"][0]["PresetId"] = json!("@active");
    write_json_atomic(&dir.path().join("presets.json"), &library(), false).unwrap();
    save_plan_offline(dir.path(), "selected", &plan, None).unwrap();
    let persisted = read_json_object(
        &plan_path(dir.path(), "selected").unwrap(),
        MAX_JSON_BYTES,
        false,
    )
    .unwrap();
    assert_eq!(persisted["Stages"][0]["PresetId"], "@active");
    assert_eq!(persisted["ActivePresetId"], "energy");
}

#[test]
fn inherited_stages_reject_missing_or_empty_active_selection_before_read_save_copy_or_queue() {
    let dir = tempfile::tempdir().unwrap();
    write_json_atomic(&dir.path().join("presets.json"), &library(), false).unwrap();
    let path = plan_path(dir.path(), "existing").unwrap();
    let mut original = default_plan();
    original["ActivePresetId"] = json!("energy");
    original["Stages"][0]["PresetId"] = json!("@active");
    write_json_atomic(&path, &original, false).unwrap();
    let before = fs::read(&path).unwrap();
    for active in [None, Some(json!(""))] {
        let mut invalid = original.clone();
        invalid.as_object_mut().unwrap().remove("ActivePresetId");
        if let Some(active) = active {
            invalid["ActivePresetId"] = active;
        }
        let error = save_plan_offline(
            dir.path(),
            "existing",
            &invalid,
            Some(&file_revision(&before)),
        )
        .unwrap_err();
        assert!(error.contains("ActivePresetId"), "{error}");
        assert_eq!(fs::read(&path).unwrap(), before);
        assert!(tauri::async_runtime::block_on(create_plan(
            dir.path().to_string_lossy().into_owned(),
            "invalid_copy".into(),
            Some(invalid.to_string())
        ))
        .unwrap_err()
        .contains("ActivePresetId"));
        assert!(!plan_path(dir.path(), "invalid_copy").unwrap().exists());
        for (command, args) in [
            (
                "savePlan",
                json!({"name": "new", "plan": invalid, "expectedRevision": ""}),
            ),
            (
                "createPlan",
                json!({"requestedName": "new", "source": invalid}),
            ),
        ] {
            assert!(queue_command(dir.path(), command, args)
                .unwrap_err()
                .contains("ActivePresetId"));
        }
        let unreadable = plan_path(dir.path(), "invalid_read").unwrap();
        write_json_atomic(&unreadable, &invalid, true).unwrap();
        let invalid_before = fs::read(&unreadable).unwrap();
        assert!(read_plan_json(&unreadable)
            .unwrap_err()
            .contains("ActivePresetId"));
        assert_eq!(fs::read(&unreadable).unwrap(), invalid_before);
    }
    assert!(list_json_stems(&dir.path().join("commands"))
        .unwrap()
        .is_empty());
}

#[test]
fn receipt_linked_unmigrated_sources_require_connected_deletion_but_allow_edits() {
    for kind in ["active", "loadout:legacy", "stage"] {
        let dir = tempfile::tempdir().unwrap();
        let path = dir.path().join("presets.json");
        let plan_path = plan_path(dir.path(), "legacy").unwrap();
        let mut original = library();
        original["MigrationReceipts"] =
            json!({"hash": {"PresetId": "energy", "Source": "LEGACY", "Kind": kind}});
        write_json_atomic(&path, &original, false).unwrap();
        let legacy = json!({"Version": 15, "GearGoal": 2, "ActiveGoalPresetIndex": 0, "GoalPresets": [{"Name": "Energy", "GearGoal": 2}], "Stages": [{"EnergyFocus": 1, "GearGoal": 2}]});
        write_json_atomic(&plan_path, &legacy, false).unwrap();
        let plan_before = fs::read(&plan_path).unwrap();
        let mut edit = read(dir.path());
        edit["Presets"][0]["Name"] = json!("Renamed");
        save(dir.path(), &edit).unwrap();
        let before = fs::read(&path).unwrap();
        let mut deletion = read(dir.path());
        deletion["Presets"].as_array_mut().unwrap().remove(0);
        // UI receipt edits cannot hide links held in the existing on-disk library.
        deletion["MigrationReceipts"] = json!({});
        let error = save(dir.path(), &deletion).unwrap_err();
        assert!(error.contains("implicit migration reference"), "{error}");
        assert!(error.contains("Connect to the injector"), "{error}");
        assert_eq!(fs::read(&path).unwrap(), before);
        assert_eq!(fs::read(&plan_path).unwrap(), plan_before);
        let mut migrated = legacy;
        migrated["ActivePresetId"] = json!("");
        write_json_atomic(&plan_path, &migrated, true).unwrap();
        let migrated_bytes = fs::read(&plan_path).unwrap();
        save(dir.path(), &deletion).unwrap();
        assert_eq!(fs::read(&plan_path).unwrap(), migrated_bytes);
        assert_eq!(
            read(dir.path())["MigrationReceipts"],
            original["MigrationReceipts"]
        );
    }
}

#[test]
fn receipt_sources_do_not_block_deletion_for_other_or_explicitly_migrated_plans() {
    for (source, active) in [
        ("other", None),
        ("legacy", Some("")),
        ("legacy", Some("magic")),
    ] {
        let dir = tempfile::tempdir().unwrap();
        let mut original = library();
        original["MigrationReceipts"] =
            json!({"hash": {"PresetId": "energy", "Source": source, "Kind": "active"}});
        write_json_atomic(&dir.path().join("presets.json"), &original, false).unwrap();
        let mut legacy = json!({"GoalPresets": [{"Name": "Energy", "GearGoal": 2}], "Stages": [{"GearGoal": 2}]});
        if let Some(active) = active {
            legacy["ActivePresetId"] = json!(active);
        }
        let plan_path = plan_path(dir.path(), "legacy").unwrap();
        write_json_atomic(&plan_path, &legacy, false).unwrap();
        let before = fs::read(&plan_path).unwrap();
        let mut deletion = read(dir.path());
        deletion["Presets"].as_array_mut().unwrap().remove(0);
        save(dir.path(), &deletion).unwrap();
        assert_eq!(read(dir.path())["Presets"].as_array().unwrap().len(), 1);
        assert_eq!(fs::read(&plan_path).unwrap(), before);
    }
}

#[test]
fn untraceable_imports_fail_closed_only_when_deleting_with_unmigrated_sources() {
    for receipts in [
        None,
        Some(json!({"hash": {"PresetId": "import_old"}})),
        Some(json!({"hash": {"Source": "legacy", "Kind": "active"}})),
    ] {
        let dir = tempfile::tempdir().unwrap();
        let path = dir.path().join("presets.json");
        let mut original = library();
        original["Presets"][0]["Id"] = json!("import_old");
        if let Some(receipts) = receipts {
            original["MigrationReceipts"] = receipts;
        }
        write_json_atomic(&path, &original, false).unwrap();
        let plan_path = plan_path(dir.path(), "legacy").unwrap();
        write_json_atomic(&plan_path, &json!({"GearGoal": 2}), false).unwrap();
        let mut deletion = read(dir.path());
        deletion["Presets"].as_array_mut().unwrap().remove(0);
        let before = fs::read(&path).unwrap();
        assert!(save(dir.path(), &deletion)
            .unwrap_err()
            .contains("Connect to the injector"));
        assert_eq!(fs::read(&path).unwrap(), before);
        fs::remove_file(&plan_path).unwrap();
        save(dir.path(), &deletion).unwrap();
        assert_eq!(read(dir.path())["Presets"].as_array().unwrap().len(), 1);
    }
}

#[test]
fn inherited_stages_require_global_capability_before_queueing() {
    let dir = tempfile::tempdir().unwrap();
    write_json_atomic(&dir.path().join("presets.json"), &library(), false).unwrap();
    let mut plan = default_plan();
    plan["ActivePresetId"] = json!("energy");
    plan["Stages"][0]["PresetId"] = json!("@active");
    let mut runtime = json!({"externalUiEnabled": true, "status": "running", "updatedAt": Utc::now().to_rfc3339(), "protocolVersion": 3, "stageTimingVersion": 1, "globalNguWeightsVersion": 1, "planVersion": 17, "supportedCommands": ["readPlan", "savePlan", "createPlan"]});
    for (command, args) in [
        (
            "savePlan",
            json!({"name": "new", "plan": plan, "expectedRevision": ""}),
        ),
        (
            "createPlan",
            json!({"requestedName": "new", "source": plan}),
        ),
    ] {
        runtime
            .as_object_mut()
            .unwrap()
            .remove("globalPresetsVersion");
        write_json_atomic(&dir.path().join("runtime.json"), &runtime, true).unwrap();
        assert!(queue_command(dir.path(), command, args.clone())
            .unwrap_err()
            .contains("globalPresetsVersion"));
        assert!(list_json_stems(&dir.path().join("commands"))
            .unwrap()
            .is_empty());
        runtime["globalPresetsVersion"] = json!(2);
        write_json_atomic(&dir.path().join("runtime.json"), &runtime, true).unwrap();
        let id = queue_command(dir.path(), command, args).unwrap();
        let queued_path = dir.path().join("commands").join(format!("{id}.json"));
        let queued = read_json_object(&queued_path, MAX_COMMAND_BYTES, false).unwrap();
        let body = if command == "savePlan" {
            &queued["args"]["plan"]
        } else {
            &queued["args"]["source"]
        };
        assert_eq!(body["Stages"][0]["PresetId"], "@active");
        remove_if_present(&queued_path).unwrap();
    }
}

#[test]
fn global_library_saves_honor_raised_advertised_command_limit() {
    let dir = tempfile::tempdir().unwrap();
    let mut body = normalize_preset_library(library()).unwrap();
    body["Future"] = json!("x".repeat(MAX_COMMAND_BYTES as usize));
    let args = json!({"library": body, "expectedRevision": "a".repeat(64)});
    let mut runtime = json!({"externalUiEnabled": true, "status": "running", "updatedAt": Utc::now().to_rfc3339(), "protocolVersion": 3, "globalPresetsVersion": 2, "globalNguWeightsVersion": 1, "maxCommandBytes": MAX_COMMAND_BYTES, "supportedCommands": ["readPresetLibrary", "savePresetLibrary"]});
    let runtime_path = dir.path().join("runtime.json");
    write_json_atomic(&runtime_path, &runtime, false).unwrap();
    assert!(queue_command(dir.path(), "savePresetLibrary", args.clone())
        .unwrap_err()
        .contains("bridge limit"));
    assert!(list_json_stems(&dir.path().join("commands"))
        .unwrap()
        .is_empty());
    runtime["maxCommandBytes"] = json!(MAX_PRESET_LIBRARY_COMMAND_BYTES);
    write_json_atomic(&runtime_path, &runtime, true).unwrap();
    let id = queue_command(dir.path(), "savePresetLibrary", args).unwrap();
    let path = dir.path().join("commands").join(format!("{id}.json"));
    assert!(fs::metadata(&path).unwrap().len() > MAX_COMMAND_BYTES);
    let queued = read_json_object(&path, MAX_PRESET_LIBRARY_COMMAND_BYTES, false).unwrap();
    assert_eq!(queued["args"]["library"]["Future"], body["Future"]);
    assert_eq!(
        global_preset_protocol()["maxCommandBytes"],
        MAX_PRESET_LIBRARY_COMMAND_BYTES
    );
}
