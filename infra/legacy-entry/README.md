# Legacy entry

Talent Management administrators enter the historical rows of the mentorship tracker by hand, through reviewed SQL
scripts (ADR-008, CONTRACT-LEGACY-ENTRY). There is no importer, entry screen or API for it, and none is needed.

## Who enters

Only Talent Management administrators enter historical relationships (BR-022). Each one:

1. uses their own database account. The database operator makes that account a member of `mentor_legacy_entry` and
   holds no membership themselves;
2. runs `authorise-account.sql` once, as that account, with their own Entra object identifier. This maps the account to
   their employee record, and every row they enter is attributed to that record.

A second administrator checks that identifier before it is run, because the mapping is final and can never be changed.

The application's own account cannot enter legacy rows, and nobody can enter them through the application.

## Preparing a script

1. Copy `entry-script.template.sql` and fill in its header. Give the script an identifier and a version, for example
   `2026 tracker rows 8-22, v1, 12 Nov 2026`. Any revision after review is a new version.
2. Validate each tracker row before you write its call.
   - Resolve each person to their Entra object identifier. The person must already have an employee record, which the
     application creates when they first sign in.
   - Give `NULL` for anyone you cannot resolve, or who has no employee record yet. The row is then held as a review
     exception, and you can enter it later once the person is resolved.
   - Give `NULL` as the identifier of a person whose cell is empty. The database refuses an identity for a person the
     row does not name.
   - Never guess an identity.
3. Copy every cell exactly as it reads: Active, Duration, Start, End, and the names as they are written. Do not correct,
   convert or complete anything. The procedure decides what a cell may be read as (see below), and keeps the text
   either way.
4. Save the script as UTF-8. Keep every version, with its review, wherever Talent Management keeps the tracker. The
   script holds names and directory identifiers, so it does not belong in this repository.

## Review and run

A second administrator checks every call against the tracker and the directory. The reviewer refuses a script that
does anything but call the procedure, one row per call. Then run the script as your own account:

```sh
psql -X --set ON_ERROR_STOP=1 --file <script>
```

The script is one transaction: if any row is refused, nothing in it is entered. Each call answers one of the following,
with the id of the record written:

- `RELATIONSHIP`
- `UNRESOLVED_PERSON`
- `NO_MENTOR`

## What the procedure reads from the cells

| Cell | Read as | Otherwise |
| --- | --- | --- |
| Start, End | a day/month/year date, `D/M/YYYY` or `DD/MM/YYYY`, that exists: `10/1/2026` is 10 January 2026 | unknown; the text is kept (`ongoing` stays text) |
| Duration | `6 months`, `12 months` or `12+ months`: that duration category | unknown; the text is kept |
| Active | `Successfully complete`: State Complete. `put on hold`: State On hold. `No - needs reassigning`: Status Needs reassigning | State, Status and IsActive unresolved |

Spacing before and after a value is ignored, tabs and line breaks included. In Duration and Active, letter case is
ignored too, and any run of spacing inside the value counts as one space. Nothing else is: a date is read only when it
is written `D/M/YYYY` with no spaces inside, and `6months` is not `6 months`. A cell that is not read raises no review
exception, so the reviewer checks those cells against the tracker.

- **IsActive** is never read from Active.
- **`Yes` and an empty Active cell** resolve nothing.
- **An unresolved State, Status or IsActive** is recorded as a review exception of the relationship. An administrator
  resolves it later in the application, where the change is audited.

Two kinds of row are held whole as a review exception, instead of being entered as a relationship:

- a row whose Mentor cell is empty (`NO_MENTOR`);
- a row with a person not resolved to an employee, an empty Protégé cell included (`UNRESOLVED_PERSON`).

Nobody is created for an unresolved person.

## What is recorded

- **The relationship:**
  - provenance `LEGACY`;
  - the source file and row;
  - every cell as written;
  - the resolved people;
  - you, as the entering administrator;
  - the actual time of entry.
- **Never recorded:** no candidate, agreement, acceptance, approval or goal, and no historical event time.
- **Audit:** one entry for each record written. The action is `LEGACY_ENTRY`, with provenance `LEGACY_SOURCE` and
  identity scheme `DATABASE`. Each entry names you and the source file and row.

## Repeats and mistakes

- **A source row is entered once.** Entering it again is refused, and the whole script with it.
- **A row held for an unresolved person** can be entered as a relationship later, once the person it names is
  resolved. The exception stays as evidence. A row whose Protégé cell is empty names nobody to resolve, so it stays held.
- **A row held because it names no mentor stays held.** No mentor is ever given to it.
- **Entered rows are never deleted or rewritten.** Check the script before you run it.
