-- Authorises your database account for legacy entry (ADR-008). Run it once, as that account, before your first entry
-- script:
--     psql -X --set ON_ERROR_STOP=1 --file authorise-account.sql
-- Replace {{administrator_object_id}} with your own Entra object identifier, as '<uuid>'. A second administrator checks
-- the identifier before you run it: the mapping is final and can never be changed.
--
-- It maps the account to your own employee record, so that every row you enter is attributed to you. You need an
-- employee record first; the application creates it when you first sign in. The database lets an account authorise only
-- itself, and only once. It refuses an identifier without an employee record.
INSERT INTO mentor.legacy_entry_authorisation (id, employee_id, database_account)
VALUES (gen_random_uuid(),
        (SELECT employee.id FROM mentor.employee AS employee WHERE employee.entra_object_id = {{administrator_object_id}}),
        session_user);
