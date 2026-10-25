-- Makes the audit trail append-only in the database.
--
-- The application already refuses to modify or delete audit rows (SaveChanges
-- throws). This is the other half: without it, "append-only" would be a
-- convention that any bug, migration script or compromised database session
-- could quietly break. With it, the trail is evidence rather than a log.
--
-- The exception is raised with SQLSTATE 23514 (check_violation) so clients get
-- a recognizable, non-generic error.

CREATE OR REPLACE FUNCTION audit_logs_reject_mutation()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION 'audit_logs is append-only; % is not permitted on this table', TG_OP
        USING ERRCODE = 'check_violation';
END;
$$;

CREATE TRIGGER audit_logs_append_only
    BEFORE UPDATE OR DELETE ON audit_logs
    FOR EACH ROW
    EXECUTE FUNCTION audit_logs_reject_mutation();

-- A row-level trigger does not fire for TRUNCATE, which would otherwise empty
-- the table in one statement.
CREATE TRIGGER audit_logs_no_truncate
    BEFORE TRUNCATE ON audit_logs
    FOR EACH STATEMENT
    EXECUTE FUNCTION audit_logs_reject_mutation();
