use super::*;

#[test]
fn execution_rebind_requires_exact_tokens_and_explicit_apply() {
    assert!(validate_command("previewExecutionRebind", &json!({})).is_ok());
    assert!(validate_command("previewExecutionRebind", &json!({"token": "x"})).is_err());
    let valid = json!({"token": "a".repeat(32), "checkpointRevision": "b".repeat(64)});
    assert!(validate_command("applyExecutionRebind", &valid).is_ok());
    for invalid in [
        json!({}),
        json!({"token": "a".repeat(32)}),
        json!({"token": "a".repeat(31), "checkpointRevision": "b".repeat(64)}),
        json!({"token": "A".repeat(32), "checkpointRevision": "b".repeat(64)}),
        json!({"token": "a".repeat(32), "checkpointRevision": "b".repeat(63)}),
        json!({"token": "a".repeat(32), "checkpointRevision": "b".repeat(64), "restart": true}),
    ] {
        assert!(
            validate_command("applyExecutionRebind", &invalid).is_err(),
            "{invalid}"
        );
    }
    assert!(validate_command("unknownExecutionRecovery", &json!({})).is_err());
}
