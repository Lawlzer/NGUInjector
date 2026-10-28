use super::*;

fn schedule() -> Value {
    json!({"Id": "stable-id_1", "Anchor": 1, "DelaySeconds": 180, "IntervalSeconds": 180})
}

#[test]
fn migration_is_absence_only_and_identity_survives_reorder_copy_and_save() {
    let source = json!({"Version": 17, "Future": 42, "Stages": [
        {"FightOnEntry": true, "FutureStage": 12},
        {"FightOnEntry": true, "FightSchedule": null},
        {"FightOnEntry": false}, {}
    ]});
    let mut plan = normalize_offline_plan(source.clone()).unwrap();
    assert_eq!(plan["Version"], 18);
    assert_eq!(
        plan["Stages"][0]["FightSchedule"],
        json!({"Id": "legacy-entry-v1-0", "Anchor": 0, "DelaySeconds": 0, "IntervalSeconds": 0})
    );
    for index in 1..4 {
        assert_eq!(plan["Stages"][index]["FightSchedule"], Value::Null);
    }
    assert!(!plan.to_string().contains("FightOnEntry"));
    plan["Stages"].as_array_mut().unwrap().reverse();
    let copy = prepare_plan_copy(plan.clone(), "copy").unwrap();
    assert_eq!(
        copy["Stages"][3]["FightSchedule"]["Id"],
        "legacy-entry-v1-0"
    );
    assert_eq!(copy["Stages"], plan["Stages"]);
    let dir = tempfile::tempdir().unwrap();
    save_plan_offline(dir.path(), "copy", &copy, None).unwrap();
    let read: Value =
        serde_json::from_str(&read_plan_json(&plan_path(dir.path(), "copy").unwrap()).unwrap())
            .unwrap();
    assert_eq!(read["Stages"], copy["Stages"]);
    assert_eq!(read["Future"], 42);
    assert_eq!(source["Stages"][0]["FightOnEntry"], true);
}

#[test]
fn recurring_schedules_and_unknown_tokens_roundtrip_without_reinterpretation() {
    let mut plan = json!({"Version": 18, "AutoIronPillEnabled": true, "Stages": [{"FightSchedule": schedule()}]});
    let future: Value = serde_json::from_str("1.2345678901234567890123456789").unwrap();
    plan["Stages"][0]["FightSchedule"]["Future"] = future.clone();
    let normalized = normalize_offline_plan(plan.clone()).unwrap();
    assert_eq!(normalized, plan);
    assert_eq!(
        normalized["Stages"][0]["FightSchedule"]["DelaySeconds"],
        180
    );
    assert_eq!(
        normalized["Stages"][0]["FightSchedule"]["IntervalSeconds"],
        180
    );
    assert_eq!(
        normalized["Stages"][0]["FightSchedule"]["Future"].to_string(),
        future.to_string()
    );
    plan["Stages"][0]["FightSchedule"] = Value::Null;
    plan["Stages"][0]["FightOnEntry"] = json!(true);
    assert_eq!(
        normalize_offline_plan(plan).unwrap()["Stages"][0]["FightSchedule"],
        Value::Null
    );
}

#[test]
fn invalid_and_future_schemas_reject_before_writing_or_normalizing() {
    for field in ["Anchor", "DelaySeconds", "IntervalSeconds"] {
        for value in [
            Value::Null,
            json!(true),
            json!("0"),
            json!(-1),
            json!(0.5),
            json!(2147483648_u64),
            json!({}),
            json!([]),
        ] {
            let mut bad = schedule();
            bad[field] = value;
            assert!(normalize_offline_plan(
                json!({"Version": 18, "Stages": [{"FightSchedule": bad}]})
            )
            .unwrap_err()
            .contains(field));
        }
        let mut bad = schedule();
        bad.as_object_mut().unwrap().remove(field);
        assert!(
            normalize_offline_plan(json!({"Version": 18, "Stages": [{"FightSchedule": bad}]}))
                .is_err()
        );
    }
    for id in [
        "".to_string(),
        "bad.id".into(),
        "a".repeat(129),
        "non ascii\u{00e9}".into(),
    ] {
        let mut bad = schedule();
        bad["Id"] = json!(id);
        assert!(
            normalize_offline_plan(json!({"Version": 18, "Stages": [{"FightSchedule": bad}]}))
                .is_err()
        );
    }
    assert!(normalize_offline_plan(json!({"Version": 18, "Stages": [{"FightSchedule": schedule()}, {"FightSchedule": schedule()}]})).is_err());
    assert!(normalize_offline_plan(
        json!({"Version": 18, "Stages": [{"FightSchedule": null, "FightOnEntry": "true"}]})
    )
    .is_err());
    let future = json!({"Version": 19, "Stages": [{"FightOnEntry": true}]});
    assert!(normalize_offline_plan(future.clone())
        .unwrap_err()
        .contains("Future plan"));
    let dir = tempfile::tempdir().unwrap();
    assert!(save_plan_offline(dir.path(), "future", &future, None).is_err());
    assert!(!plan_path(dir.path(), "future").unwrap().exists());
}

#[test]
fn old_capabilities_queue_no_plan_reads_writes_copies_or_execution() {
    for (field, values) in [
        (
            "fightScheduleVersion",
            vec![Value::Null, json!(0), json!(2), json!("1")],
        ),
        ("planVersion", vec![Value::Null, json!(17), json!("18")]),
    ] {
        for value in values {
            let dir = tempfile::tempdir().unwrap();
            let mut runtime = json!({"externalUiEnabled": true, "status": "running", "protocolVersion": 3,
                "updatedAt": Utc::now().to_rfc3339(), "stageTimingVersion": 1, "fightScheduleVersion": 1,
                "planVersion": 18, "globalNguWeightsVersion": 1, "stageOverridesV1": true, "settingsCasVersion": 1,
                "supportedCommands": ["readPlan", "savePlan", "createPlan", "applySettingsPatch", "setPlanStage"]});
            runtime[field] = value;
            write_json_atomic(&dir.path().join("runtime.json"), &runtime, false).unwrap();
            let plan = json!({"Version": 18, "Stages": [{"FightSchedule": schedule()}]});
            for (command, args) in [
                ("readPlan", json!({"name": "plan"})),
                (
                    "savePlan",
                    json!({"name": "plan", "plan": plan, "expectedRevision": ""}),
                ),
                ("createPlan", json!({"requestedName": "new"})),
                (
                    "createPlan",
                    json!({"requestedName": "copy", "source": plan}),
                ),
                (
                    "applySettingsPatch",
                    json!({"patch": {"_customPlanFile": "plan"}, "expectedRevision": ""}),
                ),
                (
                    "applySettingsPatch",
                    json!({"patch": {"_useCustomRebirthPlan": true}, "expectedRevision": ""}),
                ),
                (
                    "applySettingsPatch",
                    json!({"patch": {"_autoRebirth": true}, "expectedRevision": ""}),
                ),
                (
                    "setPlanStage",
                    json!({"name": "plan", "expectedRevision": "a".repeat(64), "expectedEntry": "entry", "action": "restart", "stageIndex": 0}),
                ),
            ] {
                let error = queue_command(dir.path(), command, args).unwrap_err();
                assert!(error.contains("fightScheduleVersion"), "{command}: {error}");
                assert!(!dir.path().join("commands").exists());
            }
            queue_command(dir.path(), "applySettingsPatch", json!({"patch": {"_useCustomRebirthPlan": false, "_autoRebirth": false}, "expectedRevision": ""})).unwrap();
        }
    }
}

#[test]
fn downgraded_or_missing_schedule_readback_is_rejected() {
    for version in [17, 18, 19] {
        let plan =
            json!({"Version": version, "Stages": [{}], "__externalUiRevision": "a".repeat(64)});
        assert!(connected_plan_json(&json!({"result": {"data": {"plan": plan}}})).is_err());
    }
    let plan = json!({"Version": 18, "Stages": [{"FightSchedule": schedule()}], "__externalUiRevision": "a".repeat(64)});
    let read = connected_plan_json(&json!({"result": {"data": {"plan": plan}}})).unwrap();
    assert_eq!(serde_json::from_str::<Value>(&read).unwrap(), plan);
}

#[test]
fn malformed_schedule_commands_never_create_queue_files() {
    let dir = tempfile::tempdir().unwrap();
    let runtime = json!({"externalUiEnabled": true, "status": "running", "protocolVersion": 3,
        "updatedAt": Utc::now().to_rfc3339(), "stageTimingVersion": 1, "fightScheduleVersion": 1,
        "planVersion": 18, "globalNguWeightsVersion": 1, "supportedCommands": ["readPlan", "savePlan", "createPlan"]});
    write_json_atomic(&dir.path().join("runtime.json"), &runtime, false).unwrap();
    for plan in [
        json!({"Version": 19, "Stages": [{"FightSchedule": schedule()}]}),
        json!({"Version": 18, "Stages": [{"FightSchedule": true}]}),
        json!({"Version": 18, "Stages": [{"FightSchedule": {"Id": "bad", "Anchor": 2, "DelaySeconds": 0, "IntervalSeconds": 0}}]}),
        json!({"Version": 18, "Stages": [{"FightSchedule": schedule()}, {"FightSchedule": schedule()}]}),
    ] {
        for (command, args) in [
            (
                "savePlan",
                json!({"name": "plan", "plan": plan, "expectedRevision": ""}),
            ),
            (
                "createPlan",
                json!({"requestedName": "copy", "source": plan}),
            ),
        ] {
            assert!(queue_command(dir.path(), command, args).is_err());
            assert!(!dir.path().join("commands").exists());
        }
    }
}
