using Microsoft.EntityFrameworkCore.Migrations;

namespace Mentor.Infrastructure.Persistence.Migrations;

// The rules of the initial schema that the EF Core model cannot express: the roles and their column-level privileges
// (ADR-013); triggers that keep evidence insert-only and operational rows undeletable whatever role runs the
// statement; the whitelist of updatable columns, enforced for the schema owner as well; server-assigned times; the
// goal completion rule; the legacy entry rule (ADR-008); and two composite foreign keys. Like the rest of a migration,
// this file is never edited once applied: a later change is a new migration.
public partial class InitialSchema
{
    private static void ApplyDatabaseRules(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(Roles);
        migrationBuilder.Sql(CompositeForeignKeys);
        migrationBuilder.Sql(TriggerFunctions);
        migrationBuilder.Sql(Triggers);
        migrationBuilder.Sql(Privileges);
    }

    private static void RemoveDatabaseRules(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            ALTER TABLE mentor.formed_agreement DROP CONSTRAINT fk_formed_agreement_relationship_of_candidate;
            ALTER TABLE mentor.mentorship_relationship DROP CONSTRAINT ak_mentorship_relationship_id_candidate_id;
            ALTER TABLE mentor.mentor_selection DROP CONSTRAINT fk_mentor_selection_current_revision_of_candidate;
            DROP FUNCTION mentor.reject_change(), mentor.stamp_insert_time(), mentor.stamp_notification_times(),
                mentor.guard_update(), mentor.require_completion_approval(), mentor.enforce_legacy_entry() CASCADE;
            """);

    // Roles are cluster-wide and may already exist: a deployment can create them before migrating. Login accounts are
    // granted membership of them by the platform configuration. The migration never drops them.
    private const string Roles = """
        DO $$
        BEGIN
            IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'mentor_app') THEN
                CREATE ROLE mentor_app NOLOGIN;
            END IF;
            IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'mentor_legacy_entry') THEN
                CREATE ROLE mentor_legacy_entry NOLOGIN;
            END IF;
        END
        $$;
        """;

    private const string CompositeForeignKeys = """
        -- A candidate's current revision is one of its own revisions.
        ALTER TABLE mentor.mentor_selection
            ADD CONSTRAINT fk_mentor_selection_current_revision_of_candidate
            FOREIGN KEY (current_revision_id, id) REFERENCES mentor.agreement_revision (id, candidate_id);

        -- A formed agreement belongs to the relationship formed from the same candidate; a legacy relationship has no
        -- candidate, so it can have no formed agreement (ADR-008).
        ALTER TABLE mentor.mentorship_relationship
            ADD CONSTRAINT ak_mentorship_relationship_id_candidate_id UNIQUE (id, candidate_id);
        ALTER TABLE mentor.formed_agreement
            ADD CONSTRAINT fk_formed_agreement_relationship_of_candidate
            FOREIGN KEY (relationship_id, candidate_id) REFERENCES mentor.mentorship_relationship (id, candidate_id);
        """;

    private const string TriggerFunctions = """
        -- Evidence is insert-only and operational rows are never deleted (ADR-013).
        CREATE FUNCTION mentor.reject_change() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
            RAISE EXCEPTION '% of a row of %.% is not permitted', TG_OP, TG_TABLE_SCHEMA, TG_TABLE_NAME
                USING ERRCODE = 'restrict_violation';
        END
        $$;

        -- The listed columns take the transaction time on insert, whatever the statement supplied; no client or
        -- script can record an invented time (NFR-004).
        CREATE FUNCTION mentor.stamp_insert_time() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
            NEW := jsonb_populate_record(NEW, (SELECT jsonb_object_agg(column_name, now()) FROM unnest(TG_ARGV) AS column_name));
            RETURN NEW;
        END
        $$;

        -- A notification event takes the transaction time, and a simulated delivery happens at that moment (ADR-009).
        CREATE FUNCTION mentor.stamp_notification_times() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
            NEW.created_at := now();
            NEW.delivered_at := CASE WHEN NEW.delivery = 'SIMULATED' THEN NEW.created_at END;
            RETURN NEW;
        END
        $$;

        -- Only the listed columns of an operational row may change, and updated_at takes the transaction time.
        CREATE FUNCTION mentor.guard_update() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE
            fixed_columns text;
        BEGIN
            SELECT string_agg(changed.key, ', ' ORDER BY changed.key) INTO fixed_columns
            FROM jsonb_each(to_jsonb(NEW)) AS changed
            WHERE changed.key <> ALL (TG_ARGV)
              AND changed.key <> 'updated_at'
              AND changed.value IS DISTINCT FROM to_jsonb(OLD) -> changed.key;
            IF fixed_columns IS NOT NULL THEN
                RAISE EXCEPTION 'column(s) % of %.% cannot change', fixed_columns, TG_TABLE_SCHEMA, TG_TABLE_NAME
                    USING ERRCODE = 'restrict_violation';
            END IF;
            IF to_jsonb(NEW) ? 'updated_at' THEN
                NEW := jsonb_populate_record(NEW, jsonb_build_object('updated_at', now()));
            END IF;
            RETURN NEW;
        END
        $$;

        -- A goal is Complete only with its mentor's approval of a completion request (BR-005, DR-011).
        CREATE FUNCTION mentor.require_completion_approval() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
            IF NEW.completion_status = 'COMPLETE' AND NOT EXISTS (
                SELECT FROM mentor.goal_approval AS approval
                JOIN mentor.goal_progress AS progress ON progress.id = approval.progress_id
                WHERE progress.goal_id = NEW.id AND approval.decision = 'APPROVED')
            THEN
                RAISE EXCEPTION 'goal % has no approved completion request', NEW.id USING ERRCODE = 'check_violation';
            END IF;
            RETURN NEW;
        END
        $$;

        -- Legacy rows are written only under mentor_legacy_entry and attributed to the employee authorised for the
        -- session's database account; that role writes nothing else (ADR-008, ADR-013).
        CREATE FUNCTION mentor.enforce_legacy_entry() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE
            entry jsonb := to_jsonb(NEW);
            legacy boolean;
            authorised_employee uuid;
        BEGIN
            legacy := CASE TG_TABLE_NAME
                WHEN 'mentorship_relationship' THEN entry ->> 'provenance' = 'LEGACY'
                WHEN 'audit_entry' THEN entry ->> 'provenance' = 'LEGACY_SOURCE'
                ELSE true
            END;

            IF NOT legacy THEN
                IF pg_has_role(current_user, 'mentor_legacy_entry', 'MEMBER') AND NOT pg_has_role(current_user, 'mentor_app', 'MEMBER') THEN
                    RAISE EXCEPTION 'mentor_legacy_entry writes legacy rows only' USING ERRCODE = 'insufficient_privilege';
                END IF;
                RETURN NEW;
            END IF;

            IF NOT pg_has_role(current_user, 'mentor_legacy_entry', 'MEMBER') THEN
                RAISE EXCEPTION 'legacy rows are written only under mentor_legacy_entry' USING ERRCODE = 'insufficient_privilege';
            END IF;

            IF TG_TABLE_NAME = 'legacy_entry_authorisation' THEN
                IF entry ->> 'database_account' IS DISTINCT FROM session_user THEN
                    RAISE EXCEPTION 'a database account can authorise only itself' USING ERRCODE = 'insufficient_privilege';
                END IF;
                RETURN NEW;
            END IF;

            SELECT employee_id INTO authorised_employee
            FROM mentor.legacy_entry_authorisation
            WHERE database_account = session_user;
            IF authorised_employee IS NULL OR authorised_employee IS DISTINCT FROM
                (entry ->> CASE TG_TABLE_NAME WHEN 'audit_entry' THEN 'actor_employee_id' ELSE 'entered_by_employee_id' END)::uuid
            THEN
                RAISE EXCEPTION 'a legacy row must be attributed to the employee authorised for database account %', session_user
                    USING ERRCODE = 'insufficient_privilege';
            END IF;
            RETURN NEW;
        END
        $$;
        """;

    private const string Triggers = """
        -- Insert-only evidence.
        CREATE TRIGGER evidence_is_insert_only BEFORE UPDATE OR DELETE ON mentor.agreement_revision FOR EACH ROW EXECUTE FUNCTION mentor.reject_change();
        CREATE TRIGGER evidence_is_insert_only BEFORE UPDATE OR DELETE ON mentor.agreement_acceptance FOR EACH ROW EXECUTE FUNCTION mentor.reject_change();
        CREATE TRIGGER evidence_is_insert_only BEFORE UPDATE OR DELETE ON mentor.permitted_change FOR EACH ROW EXECUTE FUNCTION mentor.reject_change();
        CREATE TRIGGER evidence_is_insert_only BEFORE UPDATE OR DELETE ON mentor.formation_decision FOR EACH ROW EXECUTE FUNCTION mentor.reject_change();
        CREATE TRIGGER evidence_is_insert_only BEFORE UPDATE OR DELETE ON mentor.formed_agreement FOR EACH ROW EXECUTE FUNCTION mentor.reject_change();
        CREATE TRIGGER evidence_is_insert_only BEFORE UPDATE OR DELETE ON mentor.goal_progress FOR EACH ROW EXECUTE FUNCTION mentor.reject_change();
        CREATE TRIGGER evidence_is_insert_only BEFORE UPDATE OR DELETE ON mentor.goal_approval FOR EACH ROW EXECUTE FUNCTION mentor.reject_change();
        CREATE TRIGGER evidence_is_insert_only BEFORE UPDATE OR DELETE ON mentor.audit_entry FOR EACH ROW EXECUTE FUNCTION mentor.reject_change();
        CREATE TRIGGER evidence_is_insert_only BEFORE UPDATE OR DELETE ON mentor.legacy_entry_authorisation FOR EACH ROW EXECUTE FUNCTION mentor.reject_change();
        CREATE TRIGGER evidence_is_insert_only BEFORE UPDATE OR DELETE ON mentor.legacy_review_exception FOR EACH ROW EXECUTE FUNCTION mentor.reject_change();

        -- Operational rows: never deleted, and only their mutable columns change. The column lists match the grants below.
        CREATE TRIGGER rows_are_never_deleted BEFORE DELETE ON mentor.employee FOR EACH ROW EXECUTE FUNCTION mentor.reject_change();
        CREATE TRIGGER rows_are_never_deleted BEFORE DELETE ON mentor.mentor_profile FOR EACH ROW EXECUTE FUNCTION mentor.reject_change();
        CREATE TRIGGER rows_are_never_deleted BEFORE DELETE ON mentor.mentorship_request FOR EACH ROW EXECUTE FUNCTION mentor.reject_change();
        CREATE TRIGGER rows_are_never_deleted BEFORE DELETE ON mentor.mentor_selection FOR EACH ROW EXECUTE FUNCTION mentor.reject_change();
        CREATE TRIGGER rows_are_never_deleted BEFORE DELETE ON mentor.mentorship_relationship FOR EACH ROW EXECUTE FUNCTION mentor.reject_change();
        CREATE TRIGGER rows_are_never_deleted BEFORE DELETE ON mentor.development_goal FOR EACH ROW EXECUTE FUNCTION mentor.reject_change();
        CREATE TRIGGER rows_are_never_deleted BEFORE DELETE ON mentor.meeting FOR EACH ROW EXECUTE FUNCTION mentor.reject_change();
        CREATE TRIGGER rows_are_never_deleted BEFORE DELETE ON mentor.notification_event FOR EACH ROW EXECUTE FUNCTION mentor.reject_change();

        CREATE TRIGGER only_mutable_columns_change BEFORE UPDATE ON mentor.employee FOR EACH ROW
            EXECUTE FUNCTION mentor.guard_update('display_name', 'level_title');
        CREATE TRIGGER only_mutable_columns_change BEFORE UPDATE ON mentor.mentor_profile FOR EACH ROW
            EXECUTE FUNCTION mentor.guard_update('biography', 'role_title', 'industry_experience', 'mentoring_experience',
                'approval_status', 'approval_decided_by_employee_id', 'approval_decided_at');
        CREATE TRIGGER only_mutable_columns_change BEFORE UPDATE ON mentor.mentorship_request FOR EACH ROW
            EXECUTE FUNCTION mentor.guard_update('motivation', 'development_goals', 'nontechnical_expectations', 'disposition');
        CREATE TRIGGER only_mutable_columns_change BEFORE UPDATE ON mentor.mentor_selection FOR EACH ROW
            EXECUTE FUNCTION mentor.guard_update('status', 'mentor_response', 'mentor_responded_at', 'protege_willing_at',
                'mentor_willing_at', 'withdrawn_by_employee_id', 'withdrawn_at', 'current_revision_id', 'admin_reference',
                'admin_remarks', 'version');
        CREATE TRIGGER only_mutable_columns_change BEFORE UPDATE ON mentor.mentorship_relationship FOR EACH ROW
            EXECUTE FUNCTION mentor.guard_update('state', 'status', 'is_active', 'version');
        CREATE TRIGGER only_mutable_columns_change BEFORE UPDATE ON mentor.development_goal FOR EACH ROW
            EXECUTE FUNCTION mentor.guard_update('completion_status');
        CREATE TRIGGER only_mutable_columns_change BEFORE UPDATE ON mentor.meeting FOR EACH ROW
            EXECUTE FUNCTION mentor.guard_update('scheduled_at', 'arranged_outside_application');
        CREATE TRIGGER only_mutable_columns_change BEFORE UPDATE ON mentor.notification_event FOR EACH ROW
            EXECUTE FUNCTION mentor.guard_update('read_at');

        -- Server-assigned times.
        CREATE TRIGGER stamp_insert_time BEFORE INSERT ON mentor.employee FOR EACH ROW EXECUTE FUNCTION mentor.stamp_insert_time('created_at', 'updated_at');
        CREATE TRIGGER stamp_insert_time BEFORE INSERT ON mentor.mentor_profile FOR EACH ROW EXECUTE FUNCTION mentor.stamp_insert_time('created_at', 'updated_at');
        CREATE TRIGGER stamp_insert_time BEFORE INSERT ON mentor.mentorship_request FOR EACH ROW EXECUTE FUNCTION mentor.stamp_insert_time('submitted_at', 'updated_at');
        CREATE TRIGGER stamp_insert_time BEFORE INSERT ON mentor.mentor_selection FOR EACH ROW EXECUTE FUNCTION mentor.stamp_insert_time('invited_at', 'updated_at');
        CREATE TRIGGER stamp_insert_time BEFORE INSERT ON mentor.agreement_revision FOR EACH ROW EXECUTE FUNCTION mentor.stamp_insert_time('created_at');
        CREATE TRIGGER stamp_insert_time BEFORE INSERT ON mentor.agreement_acceptance FOR EACH ROW EXECUTE FUNCTION mentor.stamp_insert_time('accepted_at');
        CREATE TRIGGER stamp_insert_time BEFORE INSERT ON mentor.permitted_change FOR EACH ROW EXECUTE FUNCTION mentor.stamp_insert_time('changed_at');
        CREATE TRIGGER stamp_insert_time BEFORE INSERT ON mentor.formation_decision FOR EACH ROW EXECUTE FUNCTION mentor.stamp_insert_time('decided_at');
        CREATE TRIGGER stamp_insert_time BEFORE INSERT ON mentor.formed_agreement FOR EACH ROW EXECUTE FUNCTION mentor.stamp_insert_time('formed_at');
        CREATE TRIGGER stamp_insert_time BEFORE INSERT ON mentor.mentorship_relationship FOR EACH ROW EXECUTE FUNCTION mentor.stamp_insert_time('created_at', 'updated_at');
        CREATE TRIGGER stamp_insert_time BEFORE INSERT ON mentor.development_goal FOR EACH ROW EXECUTE FUNCTION mentor.stamp_insert_time('created_at', 'updated_at');
        CREATE TRIGGER stamp_insert_time BEFORE INSERT ON mentor.goal_progress FOR EACH ROW EXECUTE FUNCTION mentor.stamp_insert_time('submitted_at');
        CREATE TRIGGER stamp_insert_time BEFORE INSERT ON mentor.goal_approval FOR EACH ROW EXECUTE FUNCTION mentor.stamp_insert_time('decided_at');
        CREATE TRIGGER stamp_insert_time BEFORE INSERT ON mentor.meeting FOR EACH ROW EXECUTE FUNCTION mentor.stamp_insert_time('created_at', 'updated_at');
        CREATE TRIGGER stamp_insert_time BEFORE INSERT ON mentor.notification_event FOR EACH ROW EXECUTE FUNCTION mentor.stamp_notification_times();
        CREATE TRIGGER stamp_insert_time BEFORE INSERT ON mentor.audit_entry FOR EACH ROW EXECUTE FUNCTION mentor.stamp_insert_time('occurred_at');
        CREATE TRIGGER stamp_insert_time BEFORE INSERT ON mentor.legacy_entry_authorisation FOR EACH ROW EXECUTE FUNCTION mentor.stamp_insert_time('authorised_at');
        CREATE TRIGGER stamp_insert_time BEFORE INSERT ON mentor.legacy_review_exception FOR EACH ROW EXECUTE FUNCTION mentor.stamp_insert_time('created_at');

        CREATE TRIGGER completion_requires_approval BEFORE INSERT OR UPDATE OF completion_status ON mentor.development_goal
            FOR EACH ROW EXECUTE FUNCTION mentor.require_completion_approval();

        CREATE TRIGGER legacy_entry_rule BEFORE INSERT ON mentor.mentorship_relationship FOR EACH ROW EXECUTE FUNCTION mentor.enforce_legacy_entry();
        CREATE TRIGGER legacy_entry_rule BEFORE INSERT ON mentor.audit_entry FOR EACH ROW EXECUTE FUNCTION mentor.enforce_legacy_entry();
        CREATE TRIGGER legacy_entry_rule BEFORE INSERT ON mentor.legacy_entry_authorisation FOR EACH ROW EXECUTE FUNCTION mentor.enforce_legacy_entry();
        CREATE TRIGGER legacy_entry_rule BEFORE INSERT ON mentor.legacy_review_exception FOR EACH ROW EXECUTE FUNCTION mentor.enforce_legacy_entry();
        """;

    private const string Privileges = """
        REVOKE ALL ON SCHEMA mentor FROM PUBLIC;
        GRANT USAGE ON SCHEMA mentor TO mentor_app, mentor_legacy_entry;

        -- mentor_app reads everything, inserts evidence and operational rows, updates only the mutable columns of
        -- operational rows and deletes nothing. It cannot write the legacy tables.
        GRANT SELECT ON
            mentor.employee, mentor.mentor_profile, mentor.mentorship_request, mentor.mentor_selection,
            mentor.agreement_revision, mentor.agreement_acceptance, mentor.permitted_change, mentor.formation_decision,
            mentor.formed_agreement, mentor.mentorship_relationship, mentor.development_goal, mentor.goal_progress,
            mentor.goal_approval, mentor.meeting, mentor.notification_event, mentor.audit_entry,
            mentor.legacy_entry_authorisation, mentor.legacy_review_exception
            TO mentor_app;
        GRANT INSERT ON
            mentor.employee, mentor.mentor_profile, mentor.mentorship_request, mentor.mentor_selection,
            mentor.agreement_revision, mentor.agreement_acceptance, mentor.permitted_change, mentor.formation_decision,
            mentor.formed_agreement, mentor.mentorship_relationship, mentor.development_goal, mentor.goal_progress,
            mentor.goal_approval, mentor.meeting, mentor.notification_event, mentor.audit_entry
            TO mentor_app;
        GRANT UPDATE (display_name, level_title) ON mentor.employee TO mentor_app;
        GRANT UPDATE (biography, role_title, industry_experience, mentoring_experience, approval_status,
            approval_decided_by_employee_id, approval_decided_at) ON mentor.mentor_profile TO mentor_app;
        GRANT UPDATE (motivation, development_goals, nontechnical_expectations, disposition) ON mentor.mentorship_request TO mentor_app;
        GRANT UPDATE (status, mentor_response, mentor_responded_at, protege_willing_at, mentor_willing_at,
            withdrawn_by_employee_id, withdrawn_at, current_revision_id, admin_reference, admin_remarks, version)
            ON mentor.mentor_selection TO mentor_app;
        GRANT UPDATE (state, status, is_active, version) ON mentor.mentorship_relationship TO mentor_app;
        GRANT UPDATE (completion_status) ON mentor.development_goal TO mentor_app;
        GRANT UPDATE (scheduled_at, arranged_outside_application) ON mentor.meeting TO mentor_app;
        GRANT UPDATE (read_at) ON mentor.notification_event TO mentor_app;

        -- mentor_legacy_entry reads the employees it resolves participants against and the rows it writes, and inserts
        -- legacy rows only; it creates no employee (DR-016) and updates or deletes nothing (ADR-008).
        GRANT SELECT ON
            mentor.employee, mentor.mentorship_relationship, mentor.audit_entry,
            mentor.legacy_entry_authorisation, mentor.legacy_review_exception
            TO mentor_legacy_entry;
        GRANT INSERT ON
            mentor.mentorship_relationship, mentor.audit_entry,
            mentor.legacy_entry_authorisation, mentor.legacy_review_exception
            TO mentor_legacy_entry;
        """;
}
