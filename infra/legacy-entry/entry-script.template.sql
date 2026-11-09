-- Legacy entry script (ADR-008, CONTRACT-LEGACY-ENTRY), made from infra/legacy-entry/entry-script.template.sql.
--
-- Script:       {{script_version}}
-- Source:       {{batch_source}}
-- Prepared by:  {{prepared_by}}
-- Reviewed by:  {{reviewed_by}}
--
-- Save the script as UTF-8 and run it once, as your own database account, after authorise-account.sql:
--     psql -X --set ON_ERROR_STOP=1 --file <this script>
-- The script is one transaction: if the database refuses any row, nothing in the script is entered. An entered row is
-- never removed or rewritten, so the review comes first (see README.md).
--
-- Write one call per source row, each between row:begin and row:end, and nothing else. Fill in each value as an SQL
-- literal:
--   - text in single quotes, copied exactly as the cell reads, with any quote inside written twice;
--   - an Entra object identifier as '<uuid>';
--   - NULL for an empty cell, for a person who could not be resolved to an employee, and as the identifier of a person
--     whose cell is empty.
-- The database reads what the recorded decisions allow and keeps the rest unresolved.

SET client_encoding = 'UTF8';

BEGIN;

-- row:begin
SELECT outcome, record_id FROM mentor.enter_legacy_row(
    source_file          => {{source_file}},
    source_row           => {{source_row}},
    protege_object_id    => {{protege_object_id}},
    mentor_object_id     => {{mentor_object_id}},
    source_protege_text  => {{source_protege_text}},
    source_mentor_text   => {{source_mentor_text}},
    source_active_text   => {{source_active_text}},
    source_duration_text => {{source_duration_text}},
    source_start_text    => {{source_start_text}},
    source_end_text      => {{source_end_text}});
-- row:end

COMMIT;
