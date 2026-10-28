use super::*;

fn blood_plan(enabled: bool, goal: u32, seconds: u32, worth: f64) -> Value {
    let mut plan = default_plan();
    plan["AutoBloodSpellsEnabled"] = json!(enabled);
    plan["BloodSpaghettiGoalPercent"] = json!(goal);
    plan["BloodCounterfeitGoalPercent"] = json!(goal);
    plan["IronPillSaveSeconds"] = json!(seconds);
    plan["IronPillMinWorth"] = json!(worth);
    plan
}

fn read_blood_plan(dir: &Path, name: &str) -> Value {
    let text =
        tauri::async_runtime::block_on(read_plan(dir.to_string_lossy().into_owned(), name.into()))
            .unwrap();
    serde_json::from_str(&text).unwrap()
}

fn save_blood_plan(dir: &Path, name: &str, plan: &Value) {
    let result = tauri::async_runtime::block_on(save_plan(
        dir.to_string_lossy().into_owned(),
        name.into(),
        plan.to_string(),
    ))
    .unwrap();
    assert_eq!(result["mode"], "offline");
}

#[test]
fn missing_and_null_overrides_inherit_without_read_migration_writes() {
    for version in [14, 17] {
        for explicit_null in [false, true] {
            let dir = tempfile::tempdir().unwrap();
            let settings_path = dir.path().join("settings.json");
            write_json_atomic(&settings_path, &global_ngu_tests::globals(), false).unwrap();
            let settings_before = fs::read(&settings_path).unwrap();
            let mut plan = default_plan();
            plan["Version"] = json!(version);
            plan.as_object_mut().unwrap().remove("AutoIronPillEnabled");
            for field in PLAN_BLOOD_OVERRIDE_FIELDS {
                if explicit_null {
                    plan[field] = Value::Null;
                }
            }
            let path = plan_path(dir.path(), "legacy").unwrap();
            write_json_atomic(&path, &plan, false).unwrap();
            let before = fs::read(&path).unwrap();
            let normalized = read_blood_plan(dir.path(), "legacy");
            for field in PLAN_BLOOD_OVERRIDE_FIELDS {
                assert!(
                    normalized.get(field).is_none(),
                    "{field}, v{version}, null={explicit_null}"
                );
            }
            assert_eq!(normalized["Version"], 18);
            assert_eq!(normalized["AutoIronPillEnabled"], true);
            assert_eq!(fs::read(&path).unwrap(), before);
            assert_eq!(normalized[REVISION_FIELD], file_revision(&before));
            save_blood_plan(dir.path(), "legacy", &normalized);
            let saved = read_blood_plan(dir.path(), "legacy");
            for field in PLAN_BLOOD_OVERRIDE_FIELDS {
                assert!(saved.get(field).is_none());
            }
            let copied = tauri::async_runtime::block_on(create_plan(
                dir.path().to_string_lossy().into_owned(),
                "copy".into(),
                Some(saved.to_string()),
            ))
            .unwrap();
            let copy = read_blood_plan(dir.path(), &copied);
            for field in PLAN_BLOOD_OVERRIDE_FIELDS {
                assert!(copy.get(field).is_none());
            }
            assert_eq!(fs::read(&settings_path).unwrap(), settings_before);
        }
    }
}

#[test]
fn offline_plans_and_copies_preserve_false_zero_bounds_and_unknown_source_data() {
    let dir = tempfile::tempdir().unwrap();
    let settings = dir.path().join("settings.json");
    let settings_bytes = br#"{"_customAutoBloodSpellsEnabled":true,"_customBloodSpaghettiGoalPercent":987,"future":42}"#;
    fs::write(&settings, settings_bytes).unwrap();
    let mut first = blood_plan(false, 0, 0, 0.0);
    first["AutoIronPillEnabled"] = json!(false);
    first["futureBlood"] = serde_json::from_str::<Value>(
        r#"{"precise":9007199254740993,"rows":[null,false,{"x":1.234567890123456789}]}"#,
    )
    .unwrap();
    first["Stages"][0]["futureBloodStage"] = json!({"keep": [null, 1]});
    save_blood_plan(dir.path(), "first", &first);
    let second = blood_plan(true, 100000, 604800, 1e8);
    save_blood_plan(dir.path(), "second", &second);
    let first_path = plan_path(dir.path(), "first").unwrap();
    let second_path = plan_path(dir.path(), "second").unwrap();
    let first_bytes = fs::read(&first_path).unwrap();
    let second_bytes = fs::read(&second_path).unwrap();
    for name in ["first", "second"] {
        let read = read_blood_plan(dir.path(), name);
        let source = if name == "first" { &first } else { &second };
        for field in PLAN_BLOOD_OVERRIDE_FIELDS {
            assert_eq!(read[field], source[field]);
        }
        let copy_name = tauri::async_runtime::block_on(create_plan(
            dir.path().to_string_lossy().into_owned(),
            format!("{name}-copy"),
            Some(read.to_string()),
        ))
        .unwrap();
        let mut copy = read_blood_plan(dir.path(), &copy_name);
        for field in PLAN_BLOOD_OVERRIDE_FIELDS {
            assert_eq!(copy[field], source[field]);
        }
        assert_eq!(copy["AutoIronPillEnabled"], source["AutoIronPillEnabled"]);
        assert_eq!(copy["futureBlood"], source["futureBlood"]);
        assert_eq!(
            copy["Stages"][0]["futureBloodStage"],
            source["Stages"][0]["futureBloodStage"]
        );
        copy["BloodSpaghettiGoalPercent"] = json!(123);
        copy["IronPillMinWorth"] = json!(0.125);
        save_blood_plan(dir.path(), &copy_name, &copy);
        assert_eq!(
            read_blood_plan(dir.path(), &copy_name)["IronPillMinWorth"],
            0.125
        );
    }
    assert_eq!(fs::read(&first_path).unwrap(), first_bytes);
    assert_eq!(fs::read(&second_path).unwrap(), second_bytes);
    let mut clear = read_blood_plan(dir.path(), "first");
    for field in PLAN_BLOOD_OVERRIDE_FIELDS {
        clear[field] = Value::Null;
    }
    save_blood_plan(dir.path(), "first", &clear);
    let cleared = read_blood_plan(dir.path(), "first");
    for field in PLAN_BLOOD_OVERRIDE_FIELDS {
        assert!(cleared.get(field).is_none());
    }
    assert_eq!(cleared["AutoIronPillEnabled"], false);
    assert_eq!(cleared["futureBlood"], first["futureBlood"]);
    assert_eq!(fs::read(&second_path).unwrap(), second_bytes);
    let mut omitted = read_blood_plan(dir.path(), "second");
    for field in PLAN_BLOOD_OVERRIDE_FIELDS {
        omitted.as_object_mut().unwrap().remove(field);
    }
    save_blood_plan(dir.path(), "second", &omitted);
    let cleared = read_blood_plan(dir.path(), "second");
    for field in PLAN_BLOOD_OVERRIDE_FIELDS {
        assert!(cleared.get(field).is_none());
    }
    assert_eq!(fs::read(&settings).unwrap(), settings_bytes);
}

#[test]
fn exact_integer_decimal_and_exponent_tokens_match_native_acceptance() {
    for field in [
        "BloodSpaghettiGoalPercent",
        "BloodCounterfeitGoalPercent",
        "IronPillSaveSeconds",
    ] {
        for token in ["0.0", "-0.0", "1e2", "1000e-1", "1.000e0"] {
            let mut plan = default_plan();
            plan[field] = serde_json::from_str(token).unwrap();
            validate_plan_blood_overrides(&plan).unwrap();
            validate_command("savePlan", &json!({"name": "valid", "plan": plan})).unwrap();
            validate_command(
                "createPlan",
                &json!({"requestedName": "valid", "source": plan}),
            )
            .unwrap();
            assert_eq!(
                prepare_plan_copy(plan.clone(), "copy").unwrap()[field],
                plan[field]
            );
        }
    }
}

#[test]
fn invalid_values_are_rejected_on_read_save_copy_and_command_validation_without_writes() {
    let dir = tempfile::tempdir().unwrap();
    let runtime_dir = dir.path().to_string_lossy().into_owned();
    save_blood_plan(dir.path(), "source", &blood_plan(false, 0, 0, 0.0));
    let source_path = plan_path(dir.path(), "source").unwrap();
    let before = fs::read(&source_path).unwrap();
    let valid = read_blood_plan(dir.path(), "source");
    for field in PLAN_BLOOD_OVERRIDE_FIELDS
        .into_iter()
        .chain(["AutoIronPillEnabled"])
    {
        let mut invalid = match field {
            "AutoBloodSpellsEnabled" => vec!["0", "1", "\"false\"", "\"true\"", "{}", "[]"],
            "AutoIronPillEnabled" => vec!["null", "0", "1", "\"false\"", "{}", "[]"],
            "IronPillMinWorth" => vec!["-1", "100000000.01", "1e100", "\"0\"", "false", "{}", "[]"],
            _ => vec![
                "-1",
                "1.5",
                "1e-400",
                "-1e-400",
                "1.0000000000000001",
                "2147483648",
                "1e100",
                "\"0\"",
                "false",
                "{}",
                "[]",
            ],
        };
        if field == "IronPillSaveSeconds" {
            invalid.push("604801");
        }
        if field.contains("GoalPercent") {
            invalid.push("100001");
        }
        for token in invalid {
            let mut bad = valid.clone();
            bad[field] = serde_json::from_str(token).unwrap();
            assert!(
                validate_plan_blood_overrides(&bad).is_err(),
                "{field}={token}"
            );
            assert!(
                normalize_offline_plan(bad.clone()).is_err(),
                "{field}={token}"
            );
            assert!(prepare_plan_copy(bad.clone(), "invalid").is_err());
            assert!(validate_command("savePlan", &json!({"name": "source", "plan": bad})).is_err());
            assert!(validate_command(
                "createPlan",
                &json!({"requestedName": "invalid", "source": bad})
            )
            .is_err());
            assert!(tauri::async_runtime::block_on(save_plan(
                runtime_dir.clone(),
                "source".into(),
                bad.to_string()
            ))
            .is_err());
            assert!(tauri::async_runtime::block_on(create_plan(
                runtime_dir.clone(),
                "invalid".into(),
                Some(bad.to_string())
            ))
            .is_err());
            assert!(!plan_path(dir.path(), "invalid").unwrap().exists());
            let bad_path = plan_path(dir.path(), "bad-read").unwrap();
            write_json_atomic(&bad_path, &bad, true).unwrap();
            let bad_bytes = fs::read(&bad_path).unwrap();
            assert!(tauri::async_runtime::block_on(read_plan(
                runtime_dir.clone(),
                "bad-read".into()
            ))
            .is_err());
            assert_eq!(fs::read(&bad_path).unwrap(), bad_bytes);
            assert_eq!(fs::read(&source_path).unwrap(), before);
        }
    }
    assert!(!dir.path().join("commands").exists());
}

#[test]
fn nonfinite_worth_json_is_rejected_before_offline_mutation() {
    let dir = tempfile::tempdir().unwrap();
    for token in ["NaN", "Infinity", "-Infinity", "1e999"] {
        let plan = default_plan().to_string();
        let text = format!(
            r#"{},"IronPillMinWorth":{token}}}"#,
            plan.strip_suffix('}').unwrap()
        );
        let parsed = parse_json_object(text.as_bytes(), "plan");
        assert!(parsed.is_err() || validate_plan_blood_overrides(&parsed.unwrap()).is_err());
        assert!(tauri::async_runtime::block_on(save_plan(
            dir.path().to_string_lossy().into_owned(),
            "invalid".into(),
            text.clone(),
        ))
        .is_err());
        assert!(tauri::async_runtime::block_on(create_plan(
            dir.path().to_string_lossy().into_owned(),
            "invalid".into(),
            Some(text),
        ))
        .is_err());
        assert!(!plan_path(dir.path(), "invalid").unwrap().exists());
    }
}
