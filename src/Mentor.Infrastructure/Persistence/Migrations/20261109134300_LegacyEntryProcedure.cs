using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mentor.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// The controlled entry of historical tracker rows (ADR-008, CONTRACT-LEGACY-ENTRY, COMP-LEGACY-RECORDS). A reviewed
    /// entry script made from infra/legacy-entry/entry-script.template.sql calls <c>mentor.enter_legacy_row</c> once per
    /// source row, giving the cells verbatim and the Entra object identifiers the people were resolved to. The procedure
    /// derives only what the recorded decisions allow and keeps every other value unresolved. It enters each row once,
    /// either as a LEGACY relationship or as a review exception, attributes it to the database account's authorised
    /// administrator and writes its LEGACY_SOURCE audit entries. It writes no employee and no digital evidence. Only
    /// mentor_legacy_entry may run it. The model is unchanged: these are functions and privileges only.
    /// </summary>
    public partial class LegacyEntryProcedure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(Readings);
            migrationBuilder.Sql(EntryProcedure);
            migrationBuilder.Sql(Privileges);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql("""
                DROP FUNCTION mentor.enter_legacy_row(text, integer, uuid, uuid, text, text, text, text, text, text),
                    mentor.legacy_active_reading(text), mentor.legacy_duration_category(text), mentor.legacy_source_date(text);
                """);

        // How a tracker cell is read (FR-062, ADR-006, ADR-008; the legacy tracker mapping and the product_domain decisions
        // of 27 Oct 2026 recorded in the specification). A cell is read only as those decisions allow; any other cell gives
        // NULL, which means unknown, and its text is kept by the caller.
        private const string Readings = """
            -- A tracker date is written day/month/year and read so: D/M/YYYY or DD/MM/YYYY, with a four-digit year, naming a
            -- real calendar date. Anything else is no date. That includes "ongoing", a two-digit year, another order or
            -- separator, and an impossible day.
            CREATE FUNCTION mentor.legacy_source_date(source text) RETURNS date
            LANGUAGE plpgsql IMMUTABLE
            SET search_path = pg_catalog, pg_temp
            AS $$
            DECLARE
                parts text[] := regexp_match(source, '^\s*([0-9]{1,2})/([0-9]{1,2})/([0-9]{4})\s*$');
            BEGIN
                IF parts IS NULL THEN
                    RETURN NULL;
                END IF;

                RETURN make_date(parts[3]::integer, parts[2]::integer, parts[1]::integer);
            EXCEPTION WHEN datetime_field_overflow THEN
                RETURN NULL;
            END
            $$;

            -- A Duration cell names one of the duration categories of BR-010 exactly, ignoring letter case and spacing:
            -- "6 months", "12 months" or "12+ months". Anything else, "ongoing" included, leaves the category unknown.
            CREATE FUNCTION mentor.legacy_duration_category(source text) RETURNS text
            LANGUAGE sql IMMUTABLE
            SET search_path = pg_catalog, pg_temp
            AS $$
                SELECT CASE btrim(regexp_replace(lower(source), '\s+', ' ', 'g'))
                    WHEN '6 months' THEN 'SIX_MONTHS'
                    WHEN '12 months' THEN 'TWELVE_MONTHS'
                    WHEN '12+ months' THEN 'TWELVE_PLUS_MONTHS'
                END
            $$;

            -- State, Status and IsActive as the recorded mapping decisions read an Active cell, ignoring letter case and
            -- spacing:
            --   "Successfully complete"  gives State COMPLETE;
            --   "put on hold"            gives State ON_HOLD;
            --   "No - needs reassigning" gives Status NEEDS_REASSIGNING (IsActive N is not authorised from it).
            -- "Yes" is a proposal only, and an empty cell is unknown. No cell ever gives IsActive, and every other value
            -- leaves all three unresolved.
            CREATE FUNCTION mentor.legacy_active_reading(source text, OUT state text, OUT status text, OUT is_active text)
            LANGUAGE sql IMMUTABLE
            SET search_path = pg_catalog, pg_temp
            AS $$
                SELECT CASE cell.reading WHEN 'successfully complete' THEN 'COMPLETE' WHEN 'put on hold' THEN 'ON_HOLD' END,
                       CASE cell.reading WHEN 'no - needs reassigning' THEN 'NEEDS_REASSIGNING' END,
                       NULL::text
                FROM (SELECT btrim(regexp_replace(lower(source), '\s+', ' ', 'g')) AS reading) AS cell
            $$;
            """;

        // One reviewed source row, entered by the administrator the session's database account is authorised as. The
        // relationship, the review exceptions and the audit entries it writes go through the legacy_entry_rule trigger,
        // which checks that same attribution again (ADR-008).
        private const string EntryProcedure = """
            CREATE FUNCTION mentor.enter_legacy_row(
                source_file text,
                source_row integer,
                protege_object_id uuid,
                mentor_object_id uuid,
                source_protege_text text,
                source_mentor_text text,
                source_active_text text,
                source_duration_text text,
                source_start_text text,
                source_end_text text)
            RETURNS TABLE (outcome text, record_id uuid)
            LANGUAGE plpgsql
            SET search_path = pg_catalog, pg_temp
            AS $$
            DECLARE
                entrant_id uuid;
                protege_id uuid;
                mentor_id uuid;
                held_reason text;
                held_id uuid;
                active_reading record;
                duration_value text;
                start_value date;
                end_value date;
                entered_id uuid;
                unresolved record;
            BEGIN
                SELECT authorisation.employee_id INTO entrant_id
                FROM mentor.legacy_entry_authorisation AS authorisation
                WHERE authorisation.database_account = session_user;
                IF entrant_id IS NULL THEN
                    RAISE EXCEPTION 'database account % is not authorised for legacy entry', session_user
                        USING ERRCODE = 'insufficient_privilege';
                END IF;

                -- The per-row lock below orders scripts only if each check reads what committed before it, as at READ COMMITTED
                -- (ADR-013); in an older snapshot a second script would not see the first one's row.
                IF current_setting('transaction_isolation') <> 'read committed' THEN
                    RAISE EXCEPTION 'legacy entry runs at READ COMMITTED, as the entry template begins it'
                        USING ERRCODE = 'invalid_transaction_state';
                END IF;

                IF coalesce(enter_legacy_row.source_file, '') ~ '^\s*$' OR enter_legacy_row.source_file ~ '^\s|\s$'
                    OR enter_legacy_row.source_row IS NULL OR enter_legacy_row.source_row < 1 THEN
                    RAISE EXCEPTION 'a legacy row names its source file, without surrounding spaces, and its row number'
                        USING ERRCODE = 'invalid_parameter_value';
                END IF;

                -- An identity is given only for a person the row's cells name: a cell that names nobody is never given someone.
                IF (enter_legacy_row.protege_object_id IS NOT NULL AND coalesce(enter_legacy_row.source_protege_text, '') ~ '^\s*$')
                    OR (enter_legacy_row.mentor_object_id IS NOT NULL AND coalesce(enter_legacy_row.source_mentor_text, '') ~ '^\s*$') THEN
                    RAISE EXCEPTION 'row % of % gives an identity for a person its cells do not name', enter_legacy_row.source_row, enter_legacy_row.source_file
                        USING ERRCODE = 'invalid_parameter_value';
                END IF;

                -- Scripts entering the same source row at once run one after the other, so each check below sees the other's row.
                PERFORM pg_advisory_xact_lock(hashtext(enter_legacy_row.source_file), enter_legacy_row.source_row);

                -- A source row is entered once. A row whose source names no mentor stays held: no mentor is ever given to it.
                IF EXISTS (SELECT FROM mentor.mentorship_relationship AS relationship
                           WHERE relationship.source_file = enter_legacy_row.source_file AND relationship.source_row = enter_legacy_row.source_row) THEN
                    RAISE EXCEPTION 'row % of % is already entered as a relationship', enter_legacy_row.source_row, enter_legacy_row.source_file
                        USING ERRCODE = 'unique_violation';
                END IF;

                IF EXISTS (SELECT FROM mentor.legacy_review_exception AS exception
                           WHERE exception.source_file = enter_legacy_row.source_file AND exception.source_row = enter_legacy_row.source_row
                             AND exception.reason = 'NO_MENTOR') THEN
                    RAISE EXCEPTION 'row % of % is held: its source names no mentor', enter_legacy_row.source_row, enter_legacy_row.source_file
                        USING ERRCODE = 'unique_violation';
                END IF;

                -- The people are the employees the reviewer resolved them to; nobody is invented for anyone else (DR-016).
                IF coalesce(enter_legacy_row.source_mentor_text, '') ~ '^\s*$' THEN
                    held_reason := 'NO_MENTOR';
                ELSE
                    SELECT employee.id INTO protege_id FROM mentor.employee AS employee WHERE employee.entra_object_id = enter_legacy_row.protege_object_id;
                    SELECT employee.id INTO mentor_id FROM mentor.employee AS employee WHERE employee.entra_object_id = enter_legacy_row.mentor_object_id;
                    IF protege_id IS NULL OR mentor_id IS NULL THEN
                        held_reason := 'UNRESOLVED_PERSON';
                    END IF;
                END IF;

                IF held_reason IS NOT NULL THEN
                    IF EXISTS (SELECT FROM mentor.legacy_review_exception AS exception
                               WHERE exception.source_file = enter_legacy_row.source_file AND exception.source_row = enter_legacy_row.source_row
                                 AND exception.relationship_id IS NULL) THEN
                        RAISE EXCEPTION 'row % of % is already held as a review exception', enter_legacy_row.source_row, enter_legacy_row.source_file
                            USING ERRCODE = 'unique_violation';
                    END IF;

                    INSERT INTO mentor.legacy_review_exception (id, source_file, source_row, reason, source_mentor_text, source_protege_text,
                        source_active_text, source_duration_text, source_start_text, source_end_text, entered_by_employee_id)
                    VALUES (gen_random_uuid(), enter_legacy_row.source_file, enter_legacy_row.source_row, held_reason,
                        enter_legacy_row.source_mentor_text, enter_legacy_row.source_protege_text, enter_legacy_row.source_active_text,
                        enter_legacy_row.source_duration_text, enter_legacy_row.source_start_text, enter_legacy_row.source_end_text, entrant_id)
                    RETURNING id INTO held_id;
                    INSERT INTO mentor.audit_entry (id, actor_employee_id, identity_scheme, action, record_type, record_id, new_values,
                        provenance, source_file, source_row)
                    VALUES (gen_random_uuid(), entrant_id, 'DATABASE', 'LEGACY_ENTRY', 'LEGACY_REVIEW_EXCEPTION', held_id,
                        jsonb_build_object('reason', held_reason), 'LEGACY_SOURCE', enter_legacy_row.source_file, enter_legacy_row.source_row);

                    outcome := held_reason;
                    record_id := held_id;
                    RETURN NEXT;
                    RETURN;
                END IF;

                SELECT * INTO active_reading FROM mentor.legacy_active_reading(enter_legacy_row.source_active_text);
                duration_value := mentor.legacy_duration_category(enter_legacy_row.source_duration_text);
                start_value := mentor.legacy_source_date(enter_legacy_row.source_start_text);
                end_value := mentor.legacy_source_date(enter_legacy_row.source_end_text);

                INSERT INTO mentor.mentorship_relationship (id, provenance, protege_employee_id, mentor_employee_id, state, status, is_active,
                    duration_category, start_date, end_date, source_file, source_row, source_mentor_text, source_protege_text,
                    source_active_text, source_duration_text, source_start_text, source_end_text, entered_by_employee_id)
                VALUES (gen_random_uuid(), 'LEGACY', protege_id, mentor_id, active_reading.state, active_reading.status, active_reading.is_active,
                    duration_value, start_value, end_value, enter_legacy_row.source_file, enter_legacy_row.source_row,
                    enter_legacy_row.source_mentor_text, enter_legacy_row.source_protege_text, enter_legacy_row.source_active_text,
                    enter_legacy_row.source_duration_text, enter_legacy_row.source_start_text, enter_legacy_row.source_end_text, entrant_id)
                RETURNING id INTO entered_id;
                INSERT INTO mentor.audit_entry (id, actor_employee_id, identity_scheme, action, record_type, record_id, new_values,
                    provenance, source_file, source_row)
                VALUES (gen_random_uuid(), entrant_id, 'DATABASE', 'LEGACY_ENTRY', 'RELATIONSHIP', entered_id,
                    jsonb_build_object(
                        'provenance', 'LEGACY',
                        'protegeEmployeeId', protege_id,
                        'mentorEmployeeId', mentor_id,
                        'state', active_reading.state,
                        'status', active_reading.status,
                        'isActive', active_reading.is_active,
                        'durationCategory', duration_value,
                        'startDate', start_value,
                        'endDate', end_value),
                    'LEGACY_SOURCE', enter_legacy_row.source_file, enter_legacy_row.source_row);

                -- Each value the source left unresolved is a review exception of the relationship (ADR-006).
                FOR unresolved IN
                    SELECT value.attribute
                    FROM (VALUES ('STATE', active_reading.state), ('STATUS', active_reading.status), ('IS_ACTIVE', active_reading.is_active))
                        AS value (attribute, resolved)
                    WHERE value.resolved IS NULL
                LOOP
                    INSERT INTO mentor.legacy_review_exception (id, source_file, source_row, reason, relationship_id, attribute,
                        source_mentor_text, source_protege_text, source_active_text, source_duration_text, source_start_text, source_end_text,
                        entered_by_employee_id)
                    VALUES (gen_random_uuid(), enter_legacy_row.source_file, enter_legacy_row.source_row, 'UNRESOLVED_VALUE', entered_id,
                        unresolved.attribute, enter_legacy_row.source_mentor_text, enter_legacy_row.source_protege_text,
                        enter_legacy_row.source_active_text, enter_legacy_row.source_duration_text, enter_legacy_row.source_start_text,
                        enter_legacy_row.source_end_text, entrant_id)
                    RETURNING id INTO held_id;
                    INSERT INTO mentor.audit_entry (id, actor_employee_id, identity_scheme, action, record_type, record_id, new_values,
                        provenance, source_file, source_row)
                    VALUES (gen_random_uuid(), entrant_id, 'DATABASE', 'LEGACY_ENTRY', 'LEGACY_REVIEW_EXCEPTION', held_id,
                        jsonb_build_object('reason', 'UNRESOLVED_VALUE', 'relationshipId', entered_id, 'attribute', unresolved.attribute),
                        'LEGACY_SOURCE', enter_legacy_row.source_file, enter_legacy_row.source_row);
                END LOOP;

                outcome := 'RELATIONSHIP';
                record_id := entered_id;
                RETURN NEXT;
            END
            $$;
            """;

        // Functions are executable by PUBLIC unless revoked. Only the legacy entry role runs these; the application cannot.
        private const string Privileges = """
            REVOKE ALL ON FUNCTION mentor.legacy_source_date(text), mentor.legacy_duration_category(text), mentor.legacy_active_reading(text),
                mentor.enter_legacy_row(text, integer, uuid, uuid, text, text, text, text, text, text) FROM PUBLIC;
            GRANT EXECUTE ON FUNCTION mentor.legacy_source_date(text), mentor.legacy_duration_category(text), mentor.legacy_active_reading(text),
                mentor.enter_legacy_row(text, integer, uuid, uuid, text, text, text, text, text, text) TO mentor_legacy_entry;
            """;
    }
}
