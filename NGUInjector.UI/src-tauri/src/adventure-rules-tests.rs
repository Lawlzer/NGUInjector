use super::*;

fn rule() -> Value {
    json!({"Enabled": true, "MinPower": 0, "MinToughness": 0, "MinRegen": 0, "Mode": 0, "Zone": 0})
}

fn assert_rejected(rules: Value) {
    let patch = json!({"_customAdventureRules": rules});
    assert!(validate_settings_patch(&patch).is_err(), "accepted {patch}");
    assert!(
        validate_offline_settings_patch(&patch).is_err(),
        "accepted offline {patch}"
    );
    assert!(validate_command(
        "applySettingsPatch",
        &json!({"patch": patch, "expectedRevision": ""})
    )
    .is_err());
}

#[test]
fn adventure_rules_accept_empty_defaults_and_32_ordered_records() {
    let mut rules = Vec::new();
    for mode in [0, 1, 2, 4] {
        for zone in [-1, 0, 43, 1000] {
            for enabled in [false, true] {
                rules.push(
                    json!({"Enabled": enabled, "MinPower": 0.125, "MinToughness": 1e100,
                    "MinRegen": f64::MAX, "Mode": mode, "Zone": zone}),
                );
            }
        }
    }
    for rules in [json!([]), json!([rule()]), json!(rules)] {
        let patch = json!({"_customAdventureRules": rules});
        validate_settings_patch(&patch).unwrap();
        validate_offline_settings_patch(&patch).unwrap();
        validate_command(
            "applySettingsPatch",
            &json!({"patch": patch, "expectedRevision": ""}),
        )
        .unwrap();
        assert_eq!(patch["_customAdventureRules"], rules);
    }
}

#[test]
fn adventure_rules_require_an_array_and_exact_record_keys() {
    for rules in [
        Value::Null,
        json!(false),
        json!(0),
        json!(""),
        json!({}),
        json!([null]),
        json!([[]]),
        json!(vec![rule(); 33]),
    ] {
        assert_rejected(rules);
    }
    for key in [
        "Enabled",
        "MinPower",
        "MinToughness",
        "MinRegen",
        "Mode",
        "Zone",
    ] {
        let mut missing = rule();
        missing["Enabled"] = json!(false);
        let value = missing.as_object_mut().unwrap().remove(key).unwrap();
        assert_rejected(json!([missing]));
        missing[key.to_lowercase()] = value;
        assert_rejected(json!([missing]));
    }
    let mut extra = rule();
    extra["Future"] = json!(42);
    assert_rejected(json!([extra]));
}

#[test]
fn adventure_rules_reject_invalid_types_ranges_and_nonfinite_minimums() {
    for key in [
        "Enabled",
        "MinPower",
        "MinToughness",
        "MinRegen",
        "Mode",
        "Zone",
    ] {
        let mut invalid = vec![Value::Null, json!(""), json!("1"), json!([]), json!({})];
        match key {
            "Enabled" => invalid.extend([json!(0), json!(1), json!("true")]),
            "Mode" => invalid.extend([
                json!(true),
                json!(-1),
                json!(3),
                json!(5),
                json!(0.5),
                serde_json::from_str("1.0000000000000001").unwrap(),
                serde_json::from_str("1e-400").unwrap(),
            ]),
            "Zone" => invalid.extend([
                json!(true),
                json!(-2),
                json!(44),
                json!(999),
                json!(1001),
                json!(0.5),
                serde_json::from_str("43.0000000000000001").unwrap(),
            ]),
            _ => invalid.extend([
                json!(true),
                json!(-1),
                json!(-0.001),
                serde_json::from_str("1e9999").unwrap(),
            ]),
        }
        for value in invalid {
            let mut row = rule();
            row[key] = value;
            assert_rejected(json!([row]));
        }
    }
}

#[test]
fn adventure_rules_offline_saves_fill_defaults_and_preserve_unknown_settings() {
    let dir = tempfile::tempdir().unwrap();
    let runtime_dir = dir.path().to_string_lossy().into_owned();
    let path = dir.path().join("settings.json");
    let original = json!({"_customAdventureMode": 3, "_customAdventureZone": 12, "_manualAdventureEnabled": false,
        "Future": {"counter": 9007199254740993_u64, "exact": serde_json::from_str::<Value>("1.234567890123456789").unwrap()}});
    write_json_atomic(&path, &original, false).unwrap();
    let before = fs::read(&path).unwrap();
    assert_eq!(
        get_ngui_state(Some(runtime_dir.clone())).unwrap().settings,
        original
    );
    assert_eq!(fs::read(&path).unwrap(), before);
    tauri::async_runtime::block_on(save_settings_patch(runtime_dir.clone(), json!({}))).unwrap();
    assert_eq!(
        read_json_object(&path, MAX_JSON_BYTES, false).unwrap()["_customAdventureRules"],
        json!([])
    );

    let mut manual = rule();
    manual["Mode"] = json!(4);
    manual["Zone"] = json!(43);
    let rules = json!([manual, rule()]);
    let result = tauri::async_runtime::block_on(save_settings_patch(
        runtime_dir.clone(),
        json!({"_customAdventureRules": rules}),
    ))
    .unwrap();
    assert_eq!(result["mode"], "offline");
    tauri::async_runtime::block_on(save_settings_patch(
        runtime_dir.clone(),
        json!({"_manageEnergy": false}),
    ))
    .unwrap();
    let saved = get_ngui_state(Some(runtime_dir.clone())).unwrap().settings;
    assert_eq!(saved["_customAdventureRules"], rules);
    for (key, value) in original.as_object().unwrap() {
        assert_eq!(&saved[key], value, "changed {key}");
    }
    let before = fs::read(&path).unwrap();
    let mut invalid = rule();
    invalid["Mode"] = json!(3);
    assert!(tauri::async_runtime::block_on(save_settings_patch(
        runtime_dir,
        json!({"_customAdventureRules": [invalid]})
    ))
    .is_err());
    assert_eq!(fs::read(&path).unwrap(), before);
}
