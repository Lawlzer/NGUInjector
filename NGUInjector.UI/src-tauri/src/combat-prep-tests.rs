use super::*;

fn preview_args() -> Value {
    json!({"durationSeconds": 7200, "backgroundEnergyPercent": 3, "backgroundMagicPercent": 3})
}

#[test]
fn combat_prep_accepts_only_exact_bounded_contracts() {
    for seconds in [1, 7200, 86400] {
        for percent in [0.0, 3.5, 100.0] {
            validate_command("previewCombatPrep", &json!({"durationSeconds": seconds, "backgroundEnergyPercent": percent, "backgroundMagicPercent": percent})).unwrap();
        }
    }
    for value in [
        json!(0),
        json!(-1),
        json!(86401),
        json!(1.5),
        json!("7200"),
        Value::Null,
        json!(true),
        json!([]),
        json!({}),
    ] {
        let mut args = preview_args();
        args["durationSeconds"] = value;
        assert!(
            validate_command("previewCombatPrep", &args).is_err(),
            "accepted {args}"
        );
    }
    for key in ["backgroundEnergyPercent", "backgroundMagicPercent"] {
        for value in [
            json!(-1),
            json!(100.1),
            json!("3"),
            Value::Null,
            json!(true),
            json!([]),
            json!({}),
        ] {
            let mut args = preview_args();
            args[key] = value;
            assert!(
                validate_command("previewCombatPrep", &args).is_err(),
                "accepted {args}"
            );
        }
    }
    for (command, args) in [
        ("previewCombatPrep", preview_args()),
        (
            "startCombatPrep",
            json!({"previewId": "p", "candidateId": "c"}),
        ),
        ("cancelCombatPrep", json!({"runId": "r"})),
    ] {
        validate_command(command, &args).unwrap();
        let mut extra = args.clone();
        extra["extra"] = json!(true);
        assert!(validate_command(command, &extra).is_err());
        for key in args.as_object().unwrap().keys() {
            let mut missing = args.clone();
            missing.as_object_mut().unwrap().remove(key);
            assert!(validate_command(command, &missing).is_err());
        }
        assert!(validate_command(command, &Value::Null).is_err());
    }
    // serde_json cannot represent NaN/Infinity as JSON numbers.
    for text in ["NaN", "Infinity", "-Infinity", "1e9999"] {
        let value = serde_json::from_str::<Value>(text);
        assert!(value.is_err() || value.unwrap().as_f64().is_none());
    }
}

#[test]
fn combat_prep_ids_are_strings_of_at_most_128_utf16_units() {
    for (command, key, base) in [
        (
            "startCombatPrep",
            "previewId",
            json!({"previewId": "p", "candidateId": "c"}),
        ),
        (
            "startCombatPrep",
            "candidateId",
            json!({"previewId": "p", "candidateId": "c"}),
        ),
        ("cancelCombatPrep", "runId", json!({"runId": "r"})),
    ] {
        let mut args = base.clone();
        args[key] = json!("x".repeat(128));
        validate_command(command, &args).unwrap();
        for value in [
            json!(""),
            json!("   "),
            json!("x".repeat(129)),
            json!(0),
            Value::Null,
            json!(true),
            json!([]),
            json!({}),
        ] {
            let mut args = base.clone();
            args[key] = value;
            assert!(validate_command(command, &args).is_err(), "accepted {args}");
        }
    }
}

#[test]
fn combat_prep_percent_bounds_do_not_round_arbitrary_precision_tokens() {
    for token in ["100.0000000000000000000001", "-1e-9999", "1e9999"] {
        let mut args = preview_args();
        args["backgroundEnergyPercent"] = serde_json::from_str(token).unwrap();
        assert!(
            validate_command("previewCombatPrep", &args).is_err(),
            "accepted {args}"
        );
    }
    for token in [
        "99.9999999999999999999999",
        "1e-9999",
        "-0.0",
        "1e2",
        "100.000",
    ] {
        let mut args = preview_args();
        args["backgroundEnergyPercent"] = serde_json::from_str(token).unwrap();
        validate_command("previewCombatPrep", &args).unwrap();
    }
}

#[test]
fn combat_prep_queue_requires_fresh_advertised_identified_session() {
    let dir = tempfile::tempdir().unwrap();
    let ready = json!({"externalUiEnabled": true, "status": "running", "updatedAt": Utc::now().to_rfc3339(),
        "protocolVersion": 3, "startedAt": "session", "supportedCommands": ["previewCombatPrep", "startCombatPrep", "cancelCombatPrep"]});
    for (command, args) in [
        ("previewCombatPrep", preview_args()),
        (
            "startCombatPrep",
            json!({"previewId": "p", "candidateId": "c"}),
        ),
        ("cancelCombatPrep", json!({"runId": "runtime-run"})),
    ] {
        for (key, value) in [
            ("updatedAt", json!("2000-01-01T00:00:00Z")),
            ("supportedCommands", json!([])),
            ("startedAt", json!("")),
            ("externalUiEnabled", json!(false)),
        ] {
            let mut runtime = ready.clone();
            runtime[key] = value;
            write_json_atomic(&dir.path().join("runtime.json"), &runtime, true).unwrap();
            assert!(queue_command(dir.path(), command, args.clone()).is_err());
            assert!(!dir.path().join("commands").exists());
        }
    }
    write_json_atomic(&dir.path().join("runtime.json"), &ready, true).unwrap();
    let settings = b"{\"untouched\":true}";
    fs::write(dir.path().join("settings.json"), settings).unwrap();
    let id = queue_command(dir.path(), "previewCombatPrep", preview_args()).unwrap();
    let envelope = read_json_object(
        &dir.path().join("commands").join(format!("{id}.json")),
        MAX_COMMAND_BYTES,
        false,
    )
    .unwrap();
    assert_eq!(envelope["args"], preview_args());
    assert_eq!(envelope["command"], "previewCombatPrep");
    assert_eq!(
        fs::read(dir.path().join("settings.json")).unwrap(),
        settings
    );
    assert_eq!(MAX_RESULT_BYTES, 65536);
}

#[test]
fn combat_prep_advertises_the_existing_action_result_ceiling() {
    let protocol = protocol_info();
    assert_eq!(ACTION_RESULT_MAX_BYTES, 256 * 1024);
    assert_eq!(protocol["maxResultBytes"], ACTION_RESULT_MAX_BYTES);
    assert_eq!(
        protocol["combatPrep"]["maxPreviewBytes"],
        ACTION_RESULT_MAX_BYTES
    );
    assert_eq!(
        protocol["combatPrep"]["maxResultBytes"],
        ACTION_RESULT_MAX_BYTES
    );
    assert_eq!(MAX_RESULT_BYTES, 64 * 1024);
}
