use super::*;

#[test]
fn stale_offline_settings_write_is_rejected() {
    let dir = tempfile::tempdir().unwrap();
    let runtime_dir = dir.path().to_string_lossy().into_owned();
    let path = dir.path().join("settings.json");
    fs::write(&path, b"{\"_moneyPitThreshold\":1}").unwrap();
    let read = get_ngui_state(Some(runtime_dir.clone())).unwrap();
    fs::write(&path, b"{\"_moneyPitThreshold\":2}").unwrap();
    let before = fs::read(&path).unwrap();
    let result = tauri::async_runtime::block_on(save_settings_patch_json(
        runtime_dir,
        json!({"_moneyPitThreshold":3}).to_string(),
        read.settings_revision,
    ));
    assert!(
        result.is_err(),
        "stale offline editor must not overwrite a newer value"
    );
    assert_eq!(fs::read(&path).unwrap(), before);
}

#[test]
fn shared_settings_contract_covers_validation_defaults_provenance_and_exact_tokens() {
    let fixture: Value =
        serde_json::from_str(include_str!("../../../checks/settings-contract.json")).unwrap();
    for row in fixture["patches"].as_array().unwrap() {
        let dir = tempfile::tempdir().unwrap();
        let path = dir.path().join("settings.json");
        fs::write(&path, fixture["sourceJson"].as_str().unwrap()).unwrap();
        let before = fs::read(&path).unwrap();
        let result = tauri::async_runtime::block_on(save_settings_patch_json(
            dir.path().to_string_lossy().into_owned(),
            row["json"].as_str().unwrap().into(),
            file_revision(&before),
        ));
        assert_eq!(
            result.is_ok(),
            row["accept"].as_bool().unwrap(),
            "{}: {result:?}",
            row["name"]
        );
        if result.is_err() {
            assert_eq!(fs::read(&path).unwrap(), before);
            continue;
        }
        let result: Value = serde_json::from_str(&result.unwrap()).unwrap();
        let source: Value = serde_json::from_str(fixture["sourceJson"].as_str().unwrap()).unwrap();
        let saved = &result["data"]["settings"];
        assert_eq!(
            result["data"]["revision"],
            file_revision(&fs::read(&path).unwrap())
        );
        assert_eq!(saved["Future"].to_string(), source["Future"].to_string());
        assert_eq!(
            saved["_customIronPillHistory"],
            source["_customIronPillHistory"]
        );
        let patch: Value = serde_json::from_str(row["json"].as_str().unwrap()).unwrap();
        for (key, value) in fixture["defaults"].as_object().unwrap() {
            if patch.get(key).is_none() {
                assert_eq!(&saved[key], value, "default {key}");
            }
        }
    }
}

#[test]
fn settings_absence_creation_corruption_and_unreadable_are_distinct() {
    let dir = tempfile::tempdir().unwrap();
    let runtime_dir = dir.path().to_string_lossy().into_owned();
    let state = get_ngui_state(Some(runtime_dir.clone())).unwrap();
    assert_eq!(state.settings_revision, "");
    assert_eq!(state.settings_json, "{}");
    let save = |revision: &str| {
        tauri::async_runtime::block_on(save_settings_patch_json(
            runtime_dir.clone(),
            "{}".into(),
            revision.into(),
        ))
    };
    save("").unwrap();
    let path = dir.path().join("settings.json");
    let before = fs::read(&path).unwrap();
    assert!(save("").unwrap_err().contains("revision conflict"));
    assert_eq!(fs::read(&path).unwrap(), before);
    fs::write(&path, b"{").unwrap();
    assert!(get_ngui_state(Some(runtime_dir.clone())).is_err());
    assert!(save("").is_err());
    assert_eq!(fs::read(&path).unwrap(), b"{");
    fs::remove_file(&path).unwrap();
    fs::create_dir(&path).unwrap();
    assert!(get_ngui_state(Some(runtime_dir)).is_err());
}

#[test]
fn connected_settings_forward_original_revision_and_lossless_tokens_without_offline_fallback() {
    for success in [true, false] {
        let dir = tempfile::tempdir().unwrap();
        let path = dir.path().join("settings.json");
        fs::write(&path, b"{\"_moneyPitThreshold\":1}").unwrap();
        let before = fs::read(&path).unwrap();
        write_json_atomic(&dir.path().join("runtime.json"), &json!({"externalUiEnabled":true,"status":"running","protocolVersion":3,"settingsCasVersion":1,"startedAt":"session","updatedAt":Utc::now().to_rfc3339(),"supportedCommands":["applySettingsPatch"]}), false).unwrap();
        let _ownership = lock_injector_ownership(dir.path()).unwrap();
        let server = dir.path().to_owned();
        let expected = "c".repeat(64);
        let patch = "{\"_moneyPitThreshold\":0.12345678901234567890123456789}";
        let bridge = std::thread::spawn(move || {
            let deadline = Instant::now() + Duration::from_secs(3);
            let id = loop {
                if let Some(id) = list_json_stems(&server.join("commands")).unwrap().first() {
                    break id.clone();
                }
                assert!(Instant::now() < deadline);
                std::thread::sleep(Duration::from_millis(5));
            };
            let request = read_json_object(
                &server.join("commands").join(format!("{id}.json")),
                MAX_COMMAND_BYTES,
                false,
            )
            .unwrap();
            assert_eq!(request["args"]["expectedRevision"], "c".repeat(64));
            assert_eq!(request["args"]["patch"].to_string(), patch);
            let _writer = lock_offline_writer(&server).unwrap();
            let settings = parse_json_object(b"{\"_moneyPitThreshold\":0.12345678901234567890123456789,\"Future\":9007199254740993}", "fixture").unwrap();
            let result = json!({"id":id,"command":"applySettingsPatch","success":success,"message":"Settings revision conflict","completedAt":Utc::now().to_rfc3339(),"data":{"revision":"d".repeat(64),"settings":settings}});
            write_json_atomic(
                &server.join("command-results").join(format!("{id}.json")),
                &result,
                false,
            )
            .unwrap();
        });
        let result = tauri::async_runtime::block_on(save_settings_patch_json(
            dir.path().to_string_lossy().into_owned(),
            patch.into(),
            expected,
        ));
        bridge.join().unwrap();
        if success {
            let result = result.unwrap();
            assert!(result.contains("0.12345678901234567890123456789"));
            assert!(result.contains("9007199254740993"));
        } else {
            assert!(result.unwrap_err().contains("Settings revision conflict"));
        }
        assert_eq!(fs::read(&path).unwrap(), before);
        assert!(list_json_stems(&dir.path().join("commands"))
            .unwrap()
            .is_empty());
    }
}

#[test]
fn settings_revisions_reject_missing_invalid_and_uppercase_tokens_before_queueing() {
    for args in [
        json!({"patch":{}}),
        json!({"patch":{},"expectedRevision":null}),
        json!({"patch":{},"expectedRevision":"A".repeat(64)}),
        json!({"patch":{},"expectedRevision":"a".repeat(63)}),
    ] {
        assert!(
            validate_command("applySettingsPatch", &args).is_err(),
            "{args}"
        );
    }
}

#[test]
fn settings_snapshot_hashes_exact_bom_and_whitespace_bytes_without_rewriting() {
    let dir = tempfile::tempdir().unwrap();
    let bytes = b"\xef\xbb\xbf{ \"Future\":9007199254740993, \"decimal\":1.2300e+004 }\r\n";
    let path = dir.path().join("settings.json");
    fs::write(&path, bytes).unwrap();
    let state = get_ngui_state(Some(dir.path().to_string_lossy().into_owned())).unwrap();
    assert_eq!(state.settings_revision, file_revision(bytes));
    assert_eq!(state.settings["Future"].to_string(), "9007199254740993");
    assert!(state.settings_json.contains("1.2300e+004"));
    assert_eq!(fs::read(&path).unwrap(), bytes);
    let transport = serde_json::to_value(state).unwrap();
    assert!(transport.get("settings").is_none());
    assert!(transport["settings_json"].is_string());
}

#[test]
fn settings_transport_refuses_old_injectors_before_they_can_ignore_the_revision() {
    let dir = tempfile::tempdir().unwrap();
    let path = dir.path().join("settings.json");
    fs::write(&path, b"{}").unwrap();
    let before = fs::read(&path).unwrap();
    for version in [
        Value::Null,
        json!(false),
        json!(true),
        json!(0),
        json!("1"),
        json!(2),
    ] {
        let runtime = json!({"externalUiEnabled":true,"status":"running","protocolVersion":3,"settingsCasVersion":version,"updatedAt":Utc::now().to_rfc3339(),"supportedCommands":["applySettingsPatch"]});
        write_json_atomic(&dir.path().join("runtime.json"), &runtime, true).unwrap();
        let error = tauri::async_runtime::block_on(save_settings_patch_json(
            dir.path().to_string_lossy().into_owned(),
            "{}".into(),
            file_revision(&before),
        ))
        .unwrap_err();
        assert!(error.contains("settingsCasVersion"), "{error}");
        assert!(list_json_stems(&dir.path().join("commands"))
            .unwrap()
            .is_empty());
        assert_eq!(fs::read(&path).unwrap(), before);
    }
}

#[test]
fn concurrent_offline_settings_creation_has_one_winner() {
    let dir = tempfile::tempdir().unwrap();
    let barrier = std::sync::Arc::new(std::sync::Barrier::new(2));
    let workers: Vec<_> = [1, 2]
        .into_iter()
        .map(|value| {
            let runtime = dir.path().to_string_lossy().into_owned();
            let barrier = barrier.clone();
            std::thread::spawn(move || {
                barrier.wait();
                tauri::async_runtime::block_on(save_settings_patch_json(
                    runtime,
                    json!({"_moneyPitThreshold":value}).to_string(),
                    String::new(),
                ))
            })
        })
        .collect();
    let results: Vec<_> = workers
        .into_iter()
        .map(|worker| worker.join().unwrap())
        .collect();
    assert_eq!(results.iter().filter(|result| result.is_ok()).count(), 1);
    let winner: Value = serde_json::from_str(
        results
            .iter()
            .find_map(|result| result.as_ref().ok())
            .unwrap(),
    )
    .unwrap();
    assert_eq!(
        winner["data"]["revision"],
        file_revision(&fs::read(dir.path().join("settings.json")).unwrap())
    );
}

fn sparse_plan() -> Value {
    serde_json::from_str(r#"{
        "Version": 17,
        "LoadoutPresets": [{
            "Id": "legacy", "Name": "Legacy", "Weights": [1,1,0,0,0,0],
            "HorizonSeconds": {"retained": true},
            "Future": {"integer": 9007199254740993, "decimal": 1.2300e+004,
                       "EnergyNguWeights": "not a retired field"}
        }],
        "GoalPresets": [{"Name": "Legacy", "GearGoal": 0, "Future": 12345678901234567890.123456789}],
        "ActiveGoalPresetIndex": 0,
        "RebirthHistory": [{"FutureHistory": 9007199254740993}],
        "FutureRoot": 9223372036854775807
    }"#).unwrap()
}

#[test]
fn sparse_historical_read_and_save_preserve_source_rows() {
    for rotation in [None, Some(json!(null)), Some(json!([])), Some(json!([2]))] {
        let dir = tempfile::tempdir().unwrap();
        let path = plan_path(dir.path(), "sparse").unwrap();
        let mut source = sparse_plan();
        if let Some(rotation) = rotation {
            source["LoadoutPresets"][0]["BeardRotation"] = rotation;
        }
        write_json_atomic(&path, &source, false).unwrap();
        let before = fs::read(&path).unwrap();
        let mut read: Value = serde_json::from_str(
            &tauri::async_runtime::block_on(read_plan(
                dir.path().to_string_lossy().into_owned(),
                "sparse".into(),
            ))
            .unwrap(),
        )
        .unwrap();
        assert_eq!(
            fs::read(&path).unwrap(),
            before,
            "reading must not rewrite provenance"
        );
        assert_eq!(
            read["LoadoutPresets"].to_string(),
            source["LoadoutPresets"].to_string()
        );
        read["TimeTriggerSeconds"] = json!(7200);
        let result = tauri::async_runtime::block_on(save_plan(
            dir.path().to_string_lossy().into_owned(),
            "sparse".into(),
            read.to_string(),
        ))
        .unwrap();
        assert_eq!(result["mode"], "offline");
        let saved = read_json_object(&path, MAX_JSON_BYTES, false).unwrap();
        for field in [
            "LoadoutPresets",
            "GoalPresets",
            "RebirthHistory",
            "FutureRoot",
        ] {
            assert_eq!(
                saved[field].to_string(),
                source[field].to_string(),
                "{field}"
            );
        }
        assert_eq!(saved["TimeTriggerSeconds"], 7200);
        assert_eq!(
            saved["LoadoutPresets"][0]["Weights"]
                .as_array()
                .unwrap()
                .len(),
            6
        );
        assert!(!dir.path().join("presets.json").exists());
    }
}

#[test]
fn sparse_historical_copy_preserves_rows_without_materializing_defaults() {
    let dir = tempfile::tempdir().unwrap();
    let path = plan_path(dir.path(), "source").unwrap();
    let mut source = sparse_plan();
    source["LoadoutPresets"][0]
        .as_object_mut()
        .unwrap()
        .remove("Weights");
    source["LoadoutPresets"][0]["AutoUpdate"] = json!(false);
    write_json_atomic(&path, &source, false).unwrap();
    let before = fs::read(&path).unwrap();
    let copy = tauri::async_runtime::block_on(create_plan(
        dir.path().to_string_lossy().into_owned(),
        "copy".into(),
        Some(source.to_string()),
    ))
    .unwrap();
    let copied = read_json_object(
        &plan_path(dir.path(), &copy).unwrap(),
        MAX_JSON_BYTES,
        false,
    )
    .unwrap();
    assert_eq!(
        copied["LoadoutPresets"].to_string(),
        source["LoadoutPresets"].to_string()
    );
    assert_eq!(
        copied["GoalPresets"].to_string(),
        source["GoalPresets"].to_string()
    );
    assert_eq!(copied["RebirthHistory"], json!([]));
    assert_eq!(fs::read(&path).unwrap(), before);
    assert!(!dir.path().join("presets.json").exists());
}

#[test]
fn sparse_historical_supplied_invalid_fields_are_not_defaulted() {
    for (field, value) in [
        ("AutoUpdate", json!(null)),
        ("SupportMode", json!(null)),
        ("Weights", json!(null)),
        ("MinimumPower", json!(null)),
        ("BeardPriority", json!(null)),
        ("BeardRotation", json!(true)),
        ("DiggerBudgetPercent", json!("75")),
        ("AdvancedTrainingWeights", json!([])),
        ("ResourcePrioritiesReviewed", json!(true)),
    ] {
        let dir = tempfile::tempdir().unwrap();
        let mut invalid = sparse_plan();
        invalid["LoadoutPresets"][0][field] = value;
        let error = validate_plan_presets(&invalid).unwrap_err();
        assert!(
            error.contains(field)
                || field == "ResourcePrioritiesReviewed" && error.contains("priorities"),
            "{field}: {error}"
        );
        let error = tauri::async_runtime::block_on(create_plan(
            dir.path().to_string_lossy().into_owned(),
            "invalid".into(),
            Some(invalid.to_string()),
        ))
        .unwrap_err();
        assert!(
            error.contains(field)
                || field == "ResourcePrioritiesReviewed" && error.contains("priorities"),
            "{field}: {error}"
        );
        assert!(!plan_path(dir.path(), "invalid").unwrap().exists());
    }
}

#[test]
fn sparse_historical_provenance_remains_protected() {
    let dir = tempfile::tempdir().unwrap();
    let source = sparse_plan();
    let path = plan_path(dir.path(), "sparse").unwrap();
    write_json_atomic(&path, &source, false).unwrap();
    let before = fs::read(&path).unwrap();
    let revision = file_revision(&before);
    let mut changed_row = source["LoadoutPresets"][0].clone();
    changed_row["Future"]["decimal"] = json!(12300);
    let mut defaulted_row = source["LoadoutPresets"][0].clone();
    defaulted_row["SupportMode"] = json!("planned");
    for (field, value) in [
        ("LoadoutPresets", json!([])),
        ("LoadoutPresets", json!([changed_row])),
        ("LoadoutPresets", json!([defaulted_row])),
        ("GoalPresets", json!([])),
        ("ActiveGoalPresetIndex", json!(1)),
    ] {
        let mut proposed = source.clone();
        proposed[field] = value;
        let error =
            save_plan_offline(dir.path(), "sparse", &proposed, Some(&revision)).unwrap_err();
        assert!(
            error.contains(&format!("{field} is historical provenance")),
            "{error}"
        );
        assert_eq!(fs::read(&path).unwrap(), before);
    }
    assert!(!dir.path().join("presets.json").exists());
}

#[test]
fn sparse_historical_retired_ngu_exception_preserves_every_other_token() {
    let dir = tempfile::tempdir().unwrap();
    write_json_atomic(
        &dir.path().join("settings.json"),
        &global_ngu_tests::globals(),
        false,
    )
    .unwrap();
    let library = json!({"Version": 1, "Presets": [], "MigrationReceipts": {}, "Future": 9007199254740993_u64});
    let library_path = dir.path().join("presets.json");
    write_json_atomic(&library_path, &library, false).unwrap();
    let library_before = fs::read(&library_path).unwrap();
    let path = plan_path(dir.path(), "sparse").unwrap();
    let mut source = sparse_plan();
    for field in ["LoadoutPresets", "GoalPresets"] {
        source[field][0]["EnergyNguWeights"] = json!([1]);
        source[field][0]["MagicNguWeights"] = json!([1]);
    }
    write_json_atomic(&path, &source, false).unwrap();
    let revision = file_revision(&fs::read(&path).unwrap());
    let mut expected = source.clone();
    strip_plan_ngu_weights(&mut expected);
    // A stale editor may submit retired fields again; only those fields may disappear.
    save_plan_offline(dir.path(), "sparse", &source, Some(&revision)).unwrap();
    let saved = read_json_object(&path, MAX_JSON_BYTES, false).unwrap();
    for field in ["LoadoutPresets", "GoalPresets"] {
        assert_eq!(
            saved[field].to_string(),
            expected[field].to_string(),
            "{field}"
        );
    }
    assert_eq!(fs::read(&library_path).unwrap(), library_before);
}
