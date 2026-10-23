use super::*;

#[test]
fn manual_adventure_bonus_vectors_validate_and_preserve_base_presets() {
    let dir = tempfile::tempdir().unwrap();
    let runtime_dir = dir.path().to_string_lossy().into_owned();
    let path = dir.path().join("settings.json");
    let preset_path = dir.path().join("presets.json");
    let preset_bytes = br#"{"Version":1,"Presets":[{"Id":"base","Weights":[1,2,3],"BeardPriority":[5],"DiggerWeights":[1,2]}]}"#;
    fs::write(&preset_path, preset_bytes).unwrap();
    let original = json!({"_manualAdventureEnabled":false, "_manualAdventureLoadout":2, "_customAdventureZone":12,
        "Future": {"exact":serde_json::from_str::<Value>("1.234567890123456789").unwrap()}});
    write_json_atomic(&path, &original, false).unwrap();
    let before = fs::read(&path).unwrap();
    assert_eq!(
        get_ngui_state(Some(runtime_dir.clone())).unwrap().settings,
        original
    );
    assert_eq!(fs::read(&path).unwrap(), before);
    tauri::async_runtime::block_on(save_settings_patch(runtime_dir.clone(), json!({}))).unwrap();
    for (key, count) in [
        ("_manualAdventureGearWeights", 5),
        ("_manualAdventureBeardWeights", 7),
        ("_manualAdventureDiggerWeights", 12),
    ] {
        assert_eq!(
            read_json_object(&path, MAX_JSON_BYTES, false).unwrap()[key],
            json!(vec![0; count])
        );
        for value in [0.0, 0.125, 100.0, 1_000_000.0] {
            let patch = json!({key:vec![value;count]});
            validate_settings_patch(&patch).unwrap();
            validate_offline_settings_patch(&patch).unwrap();
            validate_command(
                "applySettingsPatch",
                &json!({"patch":patch, "expectedRevision":""}),
            )
            .unwrap();
            tauri::async_runtime::block_on(save_settings_patch(runtime_dir.clone(), patch.clone()))
                .unwrap();
            assert_eq!(
                get_ngui_state(Some(runtime_dir.clone())).unwrap().settings[key],
                patch[key]
            );
        }
        for invalid in [
            Value::Null,
            json!(0),
            json!({}),
            json!(vec![0; count - 1]),
            json!(vec![0; count + 1]),
            json!(vec![json!("100"); count]),
            json!(vec![Value::Null; count]),
            json!(vec![json!(true); count]),
            json!(vec![json!(-1); count]),
            json!(vec![json!(1_000_001); count]),
            json!(vec![serde_json::from_str::<Value>("1e400").unwrap(); count]),
        ] {
            let patch = json!({key:invalid});
            assert!(validate_settings_patch(&patch).is_err(), "accepted {patch}");
            assert!(validate_offline_settings_patch(&patch).is_err());
            assert!(validate_command(
                "applySettingsPatch",
                &json!({"patch":patch, "expectedRevision":""})
            )
            .is_err());
            let before = fs::read(&path).unwrap();
            assert!(tauri::async_runtime::block_on(save_settings_patch(
                runtime_dir.clone(),
                patch
            ))
            .is_err());
            assert_eq!(fs::read(&path).unwrap(), before);
        }
    }
    let before = get_ngui_state(Some(runtime_dir.clone())).unwrap().settings;
    tauri::async_runtime::block_on(save_settings_patch(
        runtime_dir.clone(),
        json!({"_manageEnergy":true}),
    ))
    .unwrap();
    let saved = get_ngui_state(Some(runtime_dir)).unwrap().settings;
    for (key, value) in before.as_object().unwrap() {
        assert_eq!(&saved[key], value, "changed {key}");
    }
    for (key, value) in original.as_object().unwrap() {
        assert_eq!(&saved[key], value, "changed {key}");
    }
    assert_eq!(fs::read(preset_path).unwrap(), preset_bytes);
}

#[test]
fn manual_combat_settings_validate_at_live_and_offline_boundaries() {
    for (key, valid, invalid) in [
        ("_manualAdventureLoadout", vec![-1, 0, 1, 2], vec![-2, 3]),
        ("_manualTitanLoadout", vec![-1, 0, 1, 2], vec![-2, 3]),
        ("_manualTitanMaxDeaths", vec![1, 3, 1000], vec![-1, 0, 1001]),
        (
            "_manualTitanZone",
            vec![6, 8, 11, 14, 16, 19, 23, 26, 30, 34, 38, 42],
            vec![-1, 0, 7, 15, 20, 43, 1000],
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
        for value in invalid.into_iter().map(|v| json!(v)).chain([
            Value::Null,
            json!(false),
            json!(true),
            json!("1"),
            json!([]),
            json!({}),
            json!(1.5),
            json!(2147483648_i64),
            serde_json::from_str("1.0000000000000001").unwrap(),
            serde_json::from_str("1e-400").unwrap(),
        ]) {
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
    for key in ["_manualTitanEnabled", "_manualTitanRequireAT"] {
        for value in [json!(false), json!(true)] {
            let patch = json!({key: value});
            validate_settings_patch(&patch).unwrap();
            validate_offline_settings_patch(&patch).unwrap();
            validate_command(
                "applySettingsPatch",
                &json!({"patch": patch, "expectedRevision":""}),
            )
            .unwrap();
        }
        for value in [
            Value::Null,
            json!(0),
            json!(1),
            json!("true"),
            json!([]),
            json!({}),
        ] {
            let patch = json!({key: value});
            assert!(validate_settings_patch(&patch).is_err());
            assert!(validate_offline_settings_patch(&patch).is_err());
            assert!(validate_command(
                "applySettingsPatch",
                &json!({"patch": patch, "expectedRevision":""})
            )
            .is_err());
        }
    }
}

#[test]
fn manual_combat_offline_defaults_roundtrip_without_touching_unrelated_data() {
    let dir = tempfile::tempdir().unwrap();
    let runtime_dir = dir.path().to_string_lossy().into_owned();
    let path = dir.path().join("settings.json");
    let original = json!({"_manualAdventureEnabled": false, "_customAdventureZone": 12,
        "Future": {"exact": serde_json::from_str::<Value>("1.234567890123456789").unwrap()}});
    write_json_atomic(&path, &original, false).unwrap();
    let before = fs::read(&path).unwrap();
    assert_eq!(
        get_ngui_state(Some(runtime_dir.clone())).unwrap().settings,
        original
    );
    assert_eq!(fs::read(&path).unwrap(), before);
    tauri::async_runtime::block_on(save_settings_patch(runtime_dir.clone(), json!({}))).unwrap();
    let saved = read_json_object(&path, MAX_JSON_BYTES, false).unwrap();
    let defaults = json!({"_manualAdventureLoadout": -1, "_manualTitanEnabled": false,
        "_manualTitanZone": 16, "_manualTitanRequireAT": true, "_manualTitanLoadout": 1, "_manualTitanMaxDeaths": 3});
    for (key, value) in defaults.as_object().unwrap() {
        assert_eq!(&saved[key], value);
    }
    let patch = json!({"_manualAdventureLoadout": 0, "_manualTitanEnabled": true,
        "_manualTitanZone": 19, "_manualTitanRequireAT": false, "_manualTitanLoadout": -1, "_manualTitanMaxDeaths": 1000});
    tauri::async_runtime::block_on(save_settings_patch(runtime_dir.clone(), patch.clone()))
        .unwrap();
    tauri::async_runtime::block_on(save_settings_patch(
        runtime_dir.clone(),
        json!({"_manageEnergy": false}),
    ))
    .unwrap();
    let saved = read_json_object(&path, MAX_JSON_BYTES, false).unwrap();
    for (key, value) in original
        .as_object()
        .unwrap()
        .iter()
        .chain(patch.as_object().unwrap())
    {
        assert_eq!(&saved[key], value, "changed {key}");
    }
    assert!(
        saved.get("manualTitan").is_none(),
        "runtime budget must not be persisted"
    );
    let before = fs::read(&path).unwrap();
    assert!(tauri::async_runtime::block_on(save_settings_patch(
        runtime_dir,
        json!({"_manualTitanEnabled": false, "_manualTitanZone": 7})
    ))
    .is_err());
    assert_eq!(fs::read(&path).unwrap(), before);
}
