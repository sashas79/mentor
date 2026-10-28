using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mentor.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "mentor");

            migrationBuilder.CreateTable(
                name: "employee",
                schema: "mentor",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    entra_object_id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name = table.Column<string>(type: "text", nullable: false),
                    level_title = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "legacy_entry_authorisation",
                schema: "mentor",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    database_account = table.Column<string>(type: "text", nullable: false),
                    authorised_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_legacy_entry_authorisation", x => x.id);
                    table.ForeignKey(
                        name: "fk_legacy_entry_authorisation_employee_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "mentor",
                        principalTable: "employee",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "mentor_profile",
                schema: "mentor",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    biography = table.Column<string>(type: "text", nullable: false),
                    role_title = table.Column<string>(type: "text", nullable: true),
                    industry_experience = table.Column<string>(type: "text", nullable: true),
                    mentoring_experience = table.Column<string>(type: "text", nullable: true),
                    approval_status = table.Column<string>(type: "text", nullable: false),
                    approval_decided_by_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    approval_decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mentor_profile", x => x.id);
                    table.CheckConstraint("ck_mentor_profile_approval_decision", "CASE approval_status\n    WHEN 'PENDING' THEN approval_decided_by_employee_id IS NULL AND approval_decided_at IS NULL\n    ELSE approval_decided_by_employee_id IS NOT NULL AND approval_decided_at IS NOT NULL\nEND");
                    table.CheckConstraint("ck_mentor_profile_approval_status", "approval_status IN ('PENDING', 'APPROVED', 'DECLINED')");
                    table.ForeignKey(
                        name: "fk_mentor_profile_employee_approval_decided_by_employee_id",
                        column: x => x.approval_decided_by_employee_id,
                        principalSchema: "mentor",
                        principalTable: "employee",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_mentor_profile_employee_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "mentor",
                        principalTable: "employee",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "mentorship_request",
                schema: "mentor",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    protege_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    motivation = table.Column<string>(type: "text", nullable: false),
                    development_goals = table.Column<string>(type: "text", nullable: false),
                    nontechnical_expectations = table.Column<string>(type: "text", nullable: false),
                    disposition = table.Column<string>(type: "text", nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mentorship_request", x => x.id);
                    table.UniqueConstraint("ak_mentorship_request_id_protege_employee_id", x => new { x.id, x.protege_employee_id });
                    table.CheckConstraint("ck_mentorship_request_disposition", "disposition IN ('OPEN', 'FORMED', 'WITHDRAWN')");
                    table.ForeignKey(
                        name: "fk_mentorship_request_employee_protege_employee_id",
                        column: x => x.protege_employee_id,
                        principalSchema: "mentor",
                        principalTable: "employee",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "notification_event",
                schema: "mentor",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "text", nullable: false),
                    subject_type = table.Column<string>(type: "text", nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient_role = table.Column<string>(type: "text", nullable: false),
                    required_action = table.Column<string>(type: "text", nullable: false),
                    delivery = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    delivered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification_event", x => x.id);
                    table.CheckConstraint("ck_notification_event_delivered_at", "CASE delivery\n    WHEN 'SIMULATED' THEN delivered_at = created_at\n    ELSE delivered_at IS NULL\nEND");
                    table.CheckConstraint("ck_notification_event_delivery", "delivery IN ('NONE', 'SIMULATED')");
                    table.CheckConstraint("ck_notification_event_event_type", "event_type IN ('MENTOR_APPLICATION_DECIDED', 'INVITATION_SENT', 'MENTOR_RESPONDED', 'WILLINGNESS_RECORDED', 'REVISION_CREATED', 'ACCEPTANCE_RECORDED', 'FORMATION_DECIDED', 'GOAL_COMPLETION_REQUESTED', 'GOAL_COMPLETION_DECIDED')");
                    table.CheckConstraint("ck_notification_event_recipient_role", "recipient_role IN ('PROTEGE', 'MENTOR', 'APPLICANT', 'TALENT_MANAGEMENT')");
                    table.ForeignKey(
                        name: "fk_notification_event_employee_recipient_employee_id",
                        column: x => x.recipient_employee_id,
                        principalSchema: "mentor",
                        principalTable: "employee",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "agreement_acceptance",
                schema: "mentor",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content_hash = table.Column<string>(type: "text", nullable: false),
                    participant_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agreement_acceptance", x => x.id);
                    table.ForeignKey(
                        name: "fk_agreement_acceptance_employee_participant_employee_id",
                        column: x => x.participant_employee_id,
                        principalSchema: "mentor",
                        principalTable: "employee",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "agreement_revision",
                schema: "mentor",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    candidate_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    parent_revision_id = table.Column<Guid>(type: "uuid", nullable: true),
                    content = table.Column<string>(type: "text", nullable: false),
                    content_hash = table.Column<string>(type: "text", nullable: false),
                    created_by_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agreement_revision", x => x.id);
                    table.UniqueConstraint("ak_agreement_revision_id_candidate_id", x => new { x.id, x.candidate_id });
                    table.UniqueConstraint("ak_agreement_revision_id_content_hash", x => new { x.id, x.content_hash });
                    table.CheckConstraint("ck_agreement_revision_content_hash", "content_hash = encode(sha256(convert_to(content, 'UTF8')), 'hex')");
                    table.CheckConstraint("ck_agreement_revision_content_json", "content IS JSON OBJECT");
                    table.CheckConstraint("ck_agreement_revision_number", "revision_number >= 1");
                    table.CheckConstraint("ck_agreement_revision_parent", "(revision_number = 1) = (parent_revision_id IS NULL)");
                    table.ForeignKey(
                        name: "fk_agreement_revision_employee_created_by_employee_id",
                        column: x => x.created_by_employee_id,
                        principalSchema: "mentor",
                        principalTable: "employee",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_agreement_revision_parent_of_candidate",
                        columns: x => new { x.parent_revision_id, x.candidate_id },
                        principalSchema: "mentor",
                        principalTable: "agreement_revision",
                        principalColumns: new[] { "id", "candidate_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "mentor_selection",
                schema: "mentor",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    protege_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mentor_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    mentor_response = table.Column<string>(type: "text", nullable: false),
                    mentor_responded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    protege_willing_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    mentor_willing_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    withdrawn_by_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    withdrawn_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    current_revision_id = table.Column<Guid>(type: "uuid", nullable: true),
                    admin_reference = table.Column<string>(type: "text", nullable: true),
                    admin_remarks = table.Column<string>(type: "text", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    invited_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mentor_selection", x => x.id);
                    table.UniqueConstraint("ak_mentor_selection_id_protege_employee_id_mentor_employee_id", x => new { x.id, x.protege_employee_id, x.mentor_employee_id });
                    table.CheckConstraint("ck_mentor_selection_current_revision", "(current_revision_id IS NULL OR status IN ('DRAFTING', 'FORMED', 'WITHDRAWN'))\nAND (status <> 'FORMED' OR current_revision_id IS NOT NULL)");
                    table.CheckConstraint("ck_mentor_selection_distinct_participants", "mentor_employee_id <> protege_employee_id");
                    table.CheckConstraint("ck_mentor_selection_mentor_response", "mentor_response IN ('PENDING', 'ACCEPTED', 'DECLINED')");
                    table.CheckConstraint("ck_mentor_selection_mutual_acceptance", "status NOT IN ('DRAFTING', 'FORMED')\nOR (mentor_response = 'ACCEPTED' AND protege_willing_at IS NOT NULL AND mentor_willing_at IS NOT NULL AND withdrawn_at IS NULL)");
                    table.CheckConstraint("ck_mentor_selection_response_time", "(mentor_response = 'PENDING') = (mentor_responded_at IS NULL)");
                    table.CheckConstraint("ck_mentor_selection_status", "status IN ('INVITED', 'DECLINED', 'ACCEPTED', 'DRAFTING', 'FORMED', 'WITHDRAWN')");
                    table.CheckConstraint("ck_mentor_selection_status_response", "CASE status\n    WHEN 'INVITED' THEN mentor_response = 'PENDING'\n    WHEN 'DECLINED' THEN mentor_response = 'DECLINED'\n    WHEN 'ACCEPTED' THEN mentor_response = 'ACCEPTED' AND (protege_willing_at IS NULL OR mentor_willing_at IS NULL)\n    WHEN 'WITHDRAWN' THEN mentor_response <> 'DECLINED'\n    ELSE true\nEND");
                    table.CheckConstraint("ck_mentor_selection_version", "version >= 1");
                    table.CheckConstraint("ck_mentor_selection_willingness", "mentor_response = 'ACCEPTED' OR (protege_willing_at IS NULL AND mentor_willing_at IS NULL)");
                    table.CheckConstraint("ck_mentor_selection_withdrawal", "(status = 'WITHDRAWN') = (withdrawn_at IS NOT NULL)\nAND (withdrawn_at IS NULL) = (withdrawn_by_employee_id IS NULL)\nAND (withdrawn_by_employee_id IS NULL OR withdrawn_by_employee_id IN (protege_employee_id, mentor_employee_id))");
                    table.ForeignKey(
                        name: "fk_mentor_selection_agreement_revision_current_revision_id",
                        column: x => x.current_revision_id,
                        principalSchema: "mentor",
                        principalTable: "agreement_revision",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_mentor_selection_employee_mentor_employee_id",
                        column: x => x.mentor_employee_id,
                        principalSchema: "mentor",
                        principalTable: "employee",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_mentor_selection_employee_protege_employee_id",
                        column: x => x.protege_employee_id,
                        principalSchema: "mentor",
                        principalTable: "employee",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_mentor_selection_employee_withdrawn_by_employee_id",
                        column: x => x.withdrawn_by_employee_id,
                        principalSchema: "mentor",
                        principalTable: "employee",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_mentor_selection_request_of_protege",
                        columns: x => new { x.request_id, x.protege_employee_id },
                        principalSchema: "mentor",
                        principalTable: "mentorship_request",
                        principalColumns: new[] { "id", "protege_employee_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "audit_entry",
                schema: "mentor",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    identity_scheme = table.Column<string>(type: "text", nullable: false),
                    action = table.Column<string>(type: "text", nullable: false),
                    record_type = table.Column<string>(type: "text", nullable: false),
                    record_id = table.Column<Guid>(type: "uuid", nullable: true),
                    prior_values = table.Column<string>(type: "jsonb", nullable: true),
                    new_values = table.Column<string>(type: "jsonb", nullable: true),
                    candidate_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revision_id = table.Column<Guid>(type: "uuid", nullable: true),
                    content_hash = table.Column<string>(type: "text", nullable: true),
                    outcome = table.Column<string>(type: "text", nullable: true),
                    invalidated_acceptance_ids = table.Column<Guid[]>(type: "uuid[]", nullable: true),
                    invalidated_decision_ids = table.Column<Guid[]>(type: "uuid[]", nullable: true),
                    provenance = table.Column<string>(type: "text", nullable: false),
                    source_file = table.Column<string>(type: "text", nullable: true),
                    source_row = table.Column<int>(type: "integer", nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_entry", x => x.id);
                    table.CheckConstraint("ck_audit_entry_action", "action IN ('MENTOR_APPLICATION_DECIDED', 'INVITATION_SENT', 'MENTOR_RESPONDED', 'WILLINGNESS_RECORDED', 'CANDIDATE_WITHDRAWN', 'REVISION_CREATED', 'ACCEPTANCE_RECORDED', 'ANNEX_CHANGED', 'FORMATION_DECIDED', 'ATTRIBUTE_CHANGED', 'GOAL_PROGRESS_SUBMITTED', 'GOAL_COMPLETION_DECIDED', 'LEGACY_ENTRY', 'REGISTER_EXPORTED')");
                    table.CheckConstraint("ck_audit_entry_decision_context", "(revision_id IS NULL) = (content_hash IS NULL)");
                    table.CheckConstraint("ck_audit_entry_identity_scheme", "identity_scheme IN ('ENTRA', 'SIMULATED', 'DATABASE')");
                    table.CheckConstraint("ck_audit_entry_origin", "CASE provenance\n    WHEN 'APPLICATION' THEN identity_scheme IN ('ENTRA', 'SIMULATED') AND source_file IS NULL AND source_row IS NULL\n    ELSE identity_scheme = 'DATABASE' AND source_file IS NOT NULL AND source_row IS NOT NULL\nEND");
                    table.CheckConstraint("ck_audit_entry_provenance", "provenance IN ('APPLICATION', 'LEGACY_SOURCE')");
                    table.ForeignKey(
                        name: "fk_audit_entry_employee_actor_employee_id",
                        column: x => x.actor_employee_id,
                        principalSchema: "mentor",
                        principalTable: "employee",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_audit_entry_mentor_selection_candidate_id",
                        column: x => x.candidate_id,
                        principalSchema: "mentor",
                        principalTable: "mentor_selection",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_audit_entry_revision_content",
                        columns: x => new { x.revision_id, x.content_hash },
                        principalSchema: "mentor",
                        principalTable: "agreement_revision",
                        principalColumns: new[] { "id", "content_hash" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "formation_decision",
                schema: "mentor",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    candidate_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content_hash = table.Column<string>(type: "text", nullable: false),
                    outcome = table.Column<string>(type: "text", nullable: false),
                    administrator_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_formation_decision", x => x.id);
                    table.CheckConstraint("ck_formation_decision_outcome", "outcome IN ('APPROVED', 'REJECTED')");
                    table.ForeignKey(
                        name: "fk_formation_decision_employee_administrator_employee_id",
                        column: x => x.administrator_employee_id,
                        principalSchema: "mentor",
                        principalTable: "employee",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_formation_decision_mentor_selection_candidate_id",
                        column: x => x.candidate_id,
                        principalSchema: "mentor",
                        principalTable: "mentor_selection",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_formation_decision_revision_content",
                        columns: x => new { x.revision_id, x.content_hash },
                        principalSchema: "mentor",
                        principalTable: "agreement_revision",
                        principalColumns: new[] { "id", "content_hash" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_formation_decision_revision_of_candidate",
                        columns: x => new { x.revision_id, x.candidate_id },
                        principalSchema: "mentor",
                        principalTable: "agreement_revision",
                        principalColumns: new[] { "id", "candidate_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "mentorship_relationship",
                schema: "mentor",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provenance = table.Column<string>(type: "text", nullable: false),
                    candidate_id = table.Column<Guid>(type: "uuid", nullable: true),
                    protege_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mentor_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "text", nullable: true),
                    is_active = table.Column<string>(type: "text", nullable: true),
                    duration_category = table.Column<string>(type: "text", nullable: true),
                    start_date = table.Column<DateOnly>(type: "date", nullable: true),
                    end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    source_file = table.Column<string>(type: "text", nullable: true),
                    source_row = table.Column<int>(type: "integer", nullable: true),
                    source_mentor_text = table.Column<string>(type: "text", nullable: true),
                    source_protege_text = table.Column<string>(type: "text", nullable: true),
                    source_active_text = table.Column<string>(type: "text", nullable: true),
                    source_duration_text = table.Column<string>(type: "text", nullable: true),
                    source_start_text = table.Column<string>(type: "text", nullable: true),
                    source_end_text = table.Column<string>(type: "text", nullable: true),
                    entered_by_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mentorship_relationship", x => x.id);
                    table.UniqueConstraint("ak_mentorship_relationship_id_mentor_employee_id", x => new { x.id, x.mentor_employee_id });
                    table.UniqueConstraint("ak_mentorship_relationship_id_protege_employee_id", x => new { x.id, x.protege_employee_id });
                    table.CheckConstraint("ck_mentorship_relationship_digital_attributes", "provenance <> 'DIGITAL' OR (state IS NOT NULL AND status IS NOT NULL AND is_active IS NOT NULL)");
                    table.CheckConstraint("ck_mentorship_relationship_distinct_participants", "mentor_employee_id <> protege_employee_id");
                    table.CheckConstraint("ck_mentorship_relationship_duration_category", "duration_category IN ('SIX_MONTHS', 'TWELVE_MONTHS', 'TWELVE_PLUS_MONTHS')");
                    table.CheckConstraint("ck_mentorship_relationship_is_active", "is_active IN ('Y', 'N')");
                    table.CheckConstraint("ck_mentorship_relationship_origin", "CASE provenance\n    WHEN 'DIGITAL' THEN candidate_id IS NOT NULL AND entered_by_employee_id IS NULL\n        AND source_file IS NULL AND source_row IS NULL\n        AND source_mentor_text IS NULL AND source_protege_text IS NULL AND source_active_text IS NULL\n        AND source_duration_text IS NULL AND source_start_text IS NULL AND source_end_text IS NULL\n    ELSE candidate_id IS NULL AND entered_by_employee_id IS NOT NULL\n        AND source_file IS NOT NULL AND source_row IS NOT NULL\nEND");
                    table.CheckConstraint("ck_mentorship_relationship_provenance", "provenance IN ('DIGITAL', 'LEGACY')");
                    table.CheckConstraint("ck_mentorship_relationship_state", "state IN ('IN_PROGRESS', 'ON_HOLD', 'COMPLETE')");
                    table.CheckConstraint("ck_mentorship_relationship_status", "status IN ('BLANK', 'NEEDS_REASSIGNING')");
                    table.CheckConstraint("ck_mentorship_relationship_version", "version >= 1");
                    table.ForeignKey(
                        name: "fk_mentorship_relationship_candidate_participants",
                        columns: x => new { x.candidate_id, x.protege_employee_id, x.mentor_employee_id },
                        principalSchema: "mentor",
                        principalTable: "mentor_selection",
                        principalColumns: new[] { "id", "protege_employee_id", "mentor_employee_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_mentorship_relationship_employee_entered_by_employee_id",
                        column: x => x.entered_by_employee_id,
                        principalSchema: "mentor",
                        principalTable: "employee",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_mentorship_relationship_employee_mentor_employee_id",
                        column: x => x.mentor_employee_id,
                        principalSchema: "mentor",
                        principalTable: "employee",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_mentorship_relationship_employee_protege_employee_id",
                        column: x => x.protege_employee_id,
                        principalSchema: "mentor",
                        principalTable: "employee",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "permitted_change",
                schema: "mentor",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    candidate_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    basis = table.Column<string>(type: "text", nullable: false),
                    field = table.Column<string>(type: "text", nullable: false),
                    prior_value = table.Column<string>(type: "text", nullable: true),
                    new_value = table.Column<string>(type: "text", nullable: true),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_permitted_change", x => x.id);
                    table.CheckConstraint("ck_permitted_change_basis", "basis IN ('ADMINISTRATIVE_REFERENCE', 'ADMINISTRATIVE_REMARK')");
                    table.CheckConstraint("ck_permitted_change_basis_field", "(basis = 'ADMINISTRATIVE_REFERENCE') = (field = 'ADMIN_REFERENCE')");
                    table.CheckConstraint("ck_permitted_change_field", "field IN ('ADMIN_REFERENCE', 'ADMIN_REMARKS')");
                    table.ForeignKey(
                        name: "fk_permitted_change_employee_actor_employee_id",
                        column: x => x.actor_employee_id,
                        principalSchema: "mentor",
                        principalTable: "employee",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_permitted_change_mentor_selection_candidate_id",
                        column: x => x.candidate_id,
                        principalSchema: "mentor",
                        principalTable: "mentor_selection",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "formed_agreement",
                schema: "mentor",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    relationship_id = table.Column<Guid>(type: "uuid", nullable: false),
                    candidate_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    content_hash = table.Column<string>(type: "text", nullable: false),
                    admin_reference = table.Column<string>(type: "text", nullable: true),
                    admin_remarks = table.Column<string>(type: "text", nullable: true),
                    formed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_formed_agreement", x => x.id);
                    table.UniqueConstraint("ak_formed_agreement_id_relationship_id", x => new { x.id, x.relationship_id });
                    table.CheckConstraint("ck_formed_agreement_content_hash", "content_hash = encode(sha256(convert_to(content, 'UTF8')), 'hex')");
                    table.ForeignKey(
                        name: "fk_formed_agreement_mentorship_relationship_relationship_id",
                        column: x => x.relationship_id,
                        principalSchema: "mentor",
                        principalTable: "mentorship_relationship",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_formed_agreement_revision_content",
                        columns: x => new { x.revision_id, x.content_hash },
                        principalSchema: "mentor",
                        principalTable: "agreement_revision",
                        principalColumns: new[] { "id", "content_hash" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_formed_agreement_revision_of_candidate",
                        columns: x => new { x.revision_id, x.candidate_id },
                        principalSchema: "mentor",
                        principalTable: "agreement_revision",
                        principalColumns: new[] { "id", "candidate_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "legacy_review_exception",
                schema: "mentor",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_file = table.Column<string>(type: "text", nullable: false),
                    source_row = table.Column<int>(type: "integer", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: false),
                    relationship_id = table.Column<Guid>(type: "uuid", nullable: true),
                    attribute = table.Column<string>(type: "text", nullable: true),
                    source_mentor_text = table.Column<string>(type: "text", nullable: true),
                    source_protege_text = table.Column<string>(type: "text", nullable: true),
                    source_active_text = table.Column<string>(type: "text", nullable: true),
                    source_duration_text = table.Column<string>(type: "text", nullable: true),
                    source_start_text = table.Column<string>(type: "text", nullable: true),
                    source_end_text = table.Column<string>(type: "text", nullable: true),
                    entered_by_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_legacy_review_exception", x => x.id);
                    table.CheckConstraint("ck_legacy_review_exception_attribute", "attribute IN ('STATE', 'STATUS', 'IS_ACTIVE')");
                    table.CheckConstraint("ck_legacy_review_exception_reason", "reason IN ('UNRESOLVED_PERSON', 'NO_MENTOR', 'UNRESOLVED_VALUE')");
                    table.CheckConstraint("ck_legacy_review_exception_subject", "CASE reason\n    WHEN 'UNRESOLVED_VALUE' THEN relationship_id IS NOT NULL AND attribute IS NOT NULL\n    ELSE relationship_id IS NULL AND attribute IS NULL\nEND");
                    table.ForeignKey(
                        name: "fk_legacy_review_exception_employee_entered_by_employee_id",
                        column: x => x.entered_by_employee_id,
                        principalSchema: "mentor",
                        principalTable: "employee",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_legacy_review_exception_relationship",
                        column: x => x.relationship_id,
                        principalSchema: "mentor",
                        principalTable: "mentorship_relationship",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "meeting",
                schema: "mentor",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    candidate_id = table.Column<Guid>(type: "uuid", nullable: true),
                    relationship_id = table.Column<Guid>(type: "uuid", nullable: true),
                    scheduled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    arranged_outside_application = table.Column<bool>(type: "boolean", nullable: false),
                    frequency_weeks = table.Column<int>(type: "integer", nullable: true),
                    duration_minutes = table.Column<int>(type: "integer", nullable: true),
                    created_by_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_meeting", x => x.id);
                    table.CheckConstraint("ck_meeting_duration_minutes", "duration_minutes > 0");
                    table.CheckConstraint("ck_meeting_frequency_weeks", "frequency_weeks > 0");
                    table.CheckConstraint("ck_meeting_kind", "kind IN ('INTRODUCTORY', 'RECURRING_SESSION')");
                    table.CheckConstraint("ck_meeting_owner", "CASE kind\n    WHEN 'INTRODUCTORY' THEN candidate_id IS NOT NULL AND relationship_id IS NULL\n    ELSE relationship_id IS NOT NULL AND candidate_id IS NULL\nEND");
                    table.ForeignKey(
                        name: "fk_meeting_employee_created_by_employee_id",
                        column: x => x.created_by_employee_id,
                        principalSchema: "mentor",
                        principalTable: "employee",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_meeting_mentor_selection_candidate_id",
                        column: x => x.candidate_id,
                        principalSchema: "mentor",
                        principalTable: "mentor_selection",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_meeting_mentorship_relationship_relationship_id",
                        column: x => x.relationship_id,
                        principalSchema: "mentor",
                        principalTable: "mentorship_relationship",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "development_goal",
                schema: "mentor",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    relationship_id = table.Column<Guid>(type: "uuid", nullable: false),
                    formed_agreement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    agreed_description = table.Column<string>(type: "text", nullable: false),
                    completion_status = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_development_goal", x => x.id);
                    table.UniqueConstraint("ak_development_goal_id_relationship_id", x => new { x.id, x.relationship_id });
                    table.CheckConstraint("ck_development_goal_completion_status", "completion_status IN ('INCOMPLETE', 'COMPLETE')");
                    table.CheckConstraint("ck_development_goal_position", "position >= 1");
                    table.ForeignKey(
                        name: "fk_development_goal_agreement_of_relationship",
                        columns: x => new { x.formed_agreement_id, x.relationship_id },
                        principalSchema: "mentor",
                        principalTable: "formed_agreement",
                        principalColumns: new[] { "id", "relationship_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_development_goal_mentorship_relationship_relationship_id",
                        column: x => x.relationship_id,
                        principalSchema: "mentor",
                        principalTable: "mentorship_relationship",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "goal_progress",
                schema: "mentor",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    goal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    relationship_id = table.Column<Guid>(type: "uuid", nullable: false),
                    protege_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    progress_update = table.Column<string>(type: "text", nullable: false),
                    completion_requested = table.Column<bool>(type: "boolean", nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_goal_progress", x => x.id);
                    table.UniqueConstraint("ak_goal_progress_id_relationship_id_completion_requested", x => new { x.id, x.relationship_id, x.completion_requested });
                    table.ForeignKey(
                        name: "fk_goal_progress_employee_protege_employee_id",
                        column: x => x.protege_employee_id,
                        principalSchema: "mentor",
                        principalTable: "employee",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_goal_progress_goal_of_relationship",
                        columns: x => new { x.goal_id, x.relationship_id },
                        principalSchema: "mentor",
                        principalTable: "development_goal",
                        principalColumns: new[] { "id", "relationship_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_goal_progress_relationship_protege",
                        columns: x => new { x.relationship_id, x.protege_employee_id },
                        principalSchema: "mentor",
                        principalTable: "mentorship_relationship",
                        principalColumns: new[] { "id", "protege_employee_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "goal_approval",
                schema: "mentor",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    progress_id = table.Column<Guid>(type: "uuid", nullable: false),
                    relationship_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mentor_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    decision = table.Column<string>(type: "text", nullable: false),
                    completion_requested = table.Column<bool>(type: "boolean", nullable: false),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_goal_approval", x => x.id);
                    table.CheckConstraint("ck_goal_approval_completion_requested", "completion_requested");
                    table.CheckConstraint("ck_goal_approval_decision", "decision IN ('APPROVED', 'REJECTED')");
                    table.ForeignKey(
                        name: "fk_goal_approval_completion_request",
                        columns: x => new { x.progress_id, x.relationship_id, x.completion_requested },
                        principalSchema: "mentor",
                        principalTable: "goal_progress",
                        principalColumns: new[] { "id", "relationship_id", "completion_requested" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_goal_approval_employee_mentor_employee_id",
                        column: x => x.mentor_employee_id,
                        principalSchema: "mentor",
                        principalTable: "employee",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_goal_approval_relationship_mentor",
                        columns: x => new { x.relationship_id, x.mentor_employee_id },
                        principalSchema: "mentor",
                        principalTable: "mentorship_relationship",
                        principalColumns: new[] { "id", "mentor_employee_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_agreement_acceptance_participant_employee_id",
                schema: "mentor",
                table: "agreement_acceptance",
                column: "participant_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_agreement_acceptance_revision_id_content_hash",
                schema: "mentor",
                table: "agreement_acceptance",
                columns: new[] { "revision_id", "content_hash" });

            migrationBuilder.CreateIndex(
                name: "ix_agreement_acceptance_revision_id_participant_employee_id",
                schema: "mentor",
                table: "agreement_acceptance",
                columns: new[] { "revision_id", "participant_employee_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_agreement_revision_candidate_id_revision_number",
                schema: "mentor",
                table: "agreement_revision",
                columns: new[] { "candidate_id", "revision_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_agreement_revision_created_by_employee_id",
                schema: "mentor",
                table: "agreement_revision",
                column: "created_by_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_agreement_revision_parent_revision_id_candidate_id",
                schema: "mentor",
                table: "agreement_revision",
                columns: new[] { "parent_revision_id", "candidate_id" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_entry_actor_employee_id",
                schema: "mentor",
                table: "audit_entry",
                column: "actor_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_entry_candidate_id",
                schema: "mentor",
                table: "audit_entry",
                column: "candidate_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_entry_record_type_record_id",
                schema: "mentor",
                table: "audit_entry",
                columns: new[] { "record_type", "record_id" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_entry_revision_id_content_hash",
                schema: "mentor",
                table: "audit_entry",
                columns: new[] { "revision_id", "content_hash" });

            migrationBuilder.CreateIndex(
                name: "ix_development_goal_formed_agreement_id_position",
                schema: "mentor",
                table: "development_goal",
                columns: new[] { "formed_agreement_id", "position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_development_goal_formed_agreement_id_relationship_id",
                schema: "mentor",
                table: "development_goal",
                columns: new[] { "formed_agreement_id", "relationship_id" });

            migrationBuilder.CreateIndex(
                name: "ix_development_goal_relationship_id",
                schema: "mentor",
                table: "development_goal",
                column: "relationship_id");

            migrationBuilder.CreateIndex(
                name: "ix_employee_entra_object_id",
                schema: "mentor",
                table: "employee",
                column: "entra_object_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_formation_decision_administrator_employee_id",
                schema: "mentor",
                table: "formation_decision",
                column: "administrator_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_formation_decision_revision_id_candidate_id",
                schema: "mentor",
                table: "formation_decision",
                columns: new[] { "revision_id", "candidate_id" });

            migrationBuilder.CreateIndex(
                name: "ix_formation_decision_revision_id_content_hash",
                schema: "mentor",
                table: "formation_decision",
                columns: new[] { "revision_id", "content_hash" });

            migrationBuilder.CreateIndex(
                name: "ux_formation_decision_one_approval_per_candidate",
                schema: "mentor",
                table: "formation_decision",
                column: "candidate_id",
                unique: true,
                filter: "outcome = 'APPROVED'");

            migrationBuilder.CreateIndex(
                name: "ix_formed_agreement_relationship_id",
                schema: "mentor",
                table: "formed_agreement",
                column: "relationship_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_formed_agreement_revision_id_candidate_id",
                schema: "mentor",
                table: "formed_agreement",
                columns: new[] { "revision_id", "candidate_id" });

            migrationBuilder.CreateIndex(
                name: "ix_formed_agreement_revision_id_content_hash",
                schema: "mentor",
                table: "formed_agreement",
                columns: new[] { "revision_id", "content_hash" });

            migrationBuilder.CreateIndex(
                name: "ix_goal_approval_mentor_employee_id",
                schema: "mentor",
                table: "goal_approval",
                column: "mentor_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_goal_approval_progress_id",
                schema: "mentor",
                table: "goal_approval",
                column: "progress_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_goal_approval_progress_id_relationship_id_completion_reques",
                schema: "mentor",
                table: "goal_approval",
                columns: new[] { "progress_id", "relationship_id", "completion_requested" });

            migrationBuilder.CreateIndex(
                name: "ix_goal_approval_relationship_id_mentor_employee_id",
                schema: "mentor",
                table: "goal_approval",
                columns: new[] { "relationship_id", "mentor_employee_id" });

            migrationBuilder.CreateIndex(
                name: "ix_goal_progress_goal_id_relationship_id",
                schema: "mentor",
                table: "goal_progress",
                columns: new[] { "goal_id", "relationship_id" });

            migrationBuilder.CreateIndex(
                name: "ix_goal_progress_protege_employee_id",
                schema: "mentor",
                table: "goal_progress",
                column: "protege_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_goal_progress_relationship_id_protege_employee_id",
                schema: "mentor",
                table: "goal_progress",
                columns: new[] { "relationship_id", "protege_employee_id" });

            migrationBuilder.CreateIndex(
                name: "ix_legacy_entry_authorisation_database_account",
                schema: "mentor",
                table: "legacy_entry_authorisation",
                column: "database_account",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_legacy_entry_authorisation_employee_id",
                schema: "mentor",
                table: "legacy_entry_authorisation",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_legacy_review_exception_entered_by_employee_id",
                schema: "mentor",
                table: "legacy_review_exception",
                column: "entered_by_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_legacy_review_exception_relationship_id_attribute",
                schema: "mentor",
                table: "legacy_review_exception",
                columns: new[] { "relationship_id", "attribute" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_meeting_candidate_id",
                schema: "mentor",
                table: "meeting",
                column: "candidate_id");

            migrationBuilder.CreateIndex(
                name: "ix_meeting_created_by_employee_id",
                schema: "mentor",
                table: "meeting",
                column: "created_by_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_meeting_relationship_id",
                schema: "mentor",
                table: "meeting",
                column: "relationship_id");

            migrationBuilder.CreateIndex(
                name: "ix_mentor_profile_approval_decided_by_employee_id",
                schema: "mentor",
                table: "mentor_profile",
                column: "approval_decided_by_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_mentor_profile_employee_id",
                schema: "mentor",
                table: "mentor_profile",
                column: "employee_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_mentor_selection_current_revision_id",
                schema: "mentor",
                table: "mentor_selection",
                column: "current_revision_id");

            migrationBuilder.CreateIndex(
                name: "ix_mentor_selection_mentor_employee_id",
                schema: "mentor",
                table: "mentor_selection",
                column: "mentor_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_mentor_selection_protege_employee_id",
                schema: "mentor",
                table: "mentor_selection",
                column: "protege_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_mentor_selection_request_id_protege_employee_id",
                schema: "mentor",
                table: "mentor_selection",
                columns: new[] { "request_id", "protege_employee_id" });

            migrationBuilder.CreateIndex(
                name: "ix_mentor_selection_withdrawn_by_employee_id",
                schema: "mentor",
                table: "mentor_selection",
                column: "withdrawn_by_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_mentorship_relationship_candidate_id",
                schema: "mentor",
                table: "mentorship_relationship",
                column: "candidate_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_mentorship_relationship_candidate_id_protege_employee_id_me",
                schema: "mentor",
                table: "mentorship_relationship",
                columns: new[] { "candidate_id", "protege_employee_id", "mentor_employee_id" });

            migrationBuilder.CreateIndex(
                name: "ix_mentorship_relationship_entered_by_employee_id",
                schema: "mentor",
                table: "mentorship_relationship",
                column: "entered_by_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_mentorship_relationship_mentor_employee_id",
                schema: "mentor",
                table: "mentorship_relationship",
                column: "mentor_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_mentorship_relationship_protege_employee_id",
                schema: "mentor",
                table: "mentorship_relationship",
                column: "protege_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_mentorship_relationship_source_file_source_row",
                schema: "mentor",
                table: "mentorship_relationship",
                columns: new[] { "source_file", "source_row" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_mentorship_request_protege_employee_id",
                schema: "mentor",
                table: "mentorship_request",
                column: "protege_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_notification_event_recipient_employee_id",
                schema: "mentor",
                table: "notification_event",
                column: "recipient_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_permitted_change_actor_employee_id",
                schema: "mentor",
                table: "permitted_change",
                column: "actor_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_permitted_change_candidate_id",
                schema: "mentor",
                table: "permitted_change",
                column: "candidate_id");

            migrationBuilder.AddForeignKey(
                name: "fk_agreement_acceptance_revision_content",
                schema: "mentor",
                table: "agreement_acceptance",
                columns: new[] { "revision_id", "content_hash" },
                principalSchema: "mentor",
                principalTable: "agreement_revision",
                principalColumns: new[] { "id", "content_hash" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_agreement_revision_mentor_selection_candidate_id",
                schema: "mentor",
                table: "agreement_revision",
                column: "candidate_id",
                principalSchema: "mentor",
                principalTable: "mentor_selection",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            ApplyDatabaseRules(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            RemoveDatabaseRules(migrationBuilder);

            migrationBuilder.DropForeignKey(
                name: "fk_agreement_revision_employee_created_by_employee_id",
                schema: "mentor",
                table: "agreement_revision");

            migrationBuilder.DropForeignKey(
                name: "fk_mentor_selection_employee_mentor_employee_id",
                schema: "mentor",
                table: "mentor_selection");

            migrationBuilder.DropForeignKey(
                name: "fk_mentor_selection_employee_protege_employee_id",
                schema: "mentor",
                table: "mentor_selection");

            migrationBuilder.DropForeignKey(
                name: "fk_mentor_selection_employee_withdrawn_by_employee_id",
                schema: "mentor",
                table: "mentor_selection");

            migrationBuilder.DropForeignKey(
                name: "fk_mentorship_request_employee_protege_employee_id",
                schema: "mentor",
                table: "mentorship_request");

            migrationBuilder.DropForeignKey(
                name: "fk_mentor_selection_agreement_revision_current_revision_id",
                schema: "mentor",
                table: "mentor_selection");

            migrationBuilder.DropTable(
                name: "agreement_acceptance",
                schema: "mentor");

            migrationBuilder.DropTable(
                name: "audit_entry",
                schema: "mentor");

            migrationBuilder.DropTable(
                name: "formation_decision",
                schema: "mentor");

            migrationBuilder.DropTable(
                name: "goal_approval",
                schema: "mentor");

            migrationBuilder.DropTable(
                name: "legacy_entry_authorisation",
                schema: "mentor");

            migrationBuilder.DropTable(
                name: "legacy_review_exception",
                schema: "mentor");

            migrationBuilder.DropTable(
                name: "meeting",
                schema: "mentor");

            migrationBuilder.DropTable(
                name: "mentor_profile",
                schema: "mentor");

            migrationBuilder.DropTable(
                name: "notification_event",
                schema: "mentor");

            migrationBuilder.DropTable(
                name: "permitted_change",
                schema: "mentor");

            migrationBuilder.DropTable(
                name: "goal_progress",
                schema: "mentor");

            migrationBuilder.DropTable(
                name: "development_goal",
                schema: "mentor");

            migrationBuilder.DropTable(
                name: "formed_agreement",
                schema: "mentor");

            migrationBuilder.DropTable(
                name: "mentorship_relationship",
                schema: "mentor");

            migrationBuilder.DropTable(
                name: "employee",
                schema: "mentor");

            migrationBuilder.DropTable(
                name: "agreement_revision",
                schema: "mentor");

            migrationBuilder.DropTable(
                name: "mentor_selection",
                schema: "mentor");

            migrationBuilder.DropTable(
                name: "mentorship_request",
                schema: "mentor");
        }
    }
}
