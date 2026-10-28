using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mentor.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MeetingsNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_notification_event_event_type",
                schema: "mentor",
                table: "notification_event");

            migrationBuilder.AddCheckConstraint(
                name: "ck_notification_event_event_type",
                schema: "mentor",
                table: "notification_event",
                sql: "event_type IN ('MENTOR_APPLICATION_DECIDED', 'INVITATION_SENT', 'MENTOR_RESPONDED', 'WILLINGNESS_RECORDED', 'CANDIDATE_WITHDRAWN', 'REVISION_CREATED', 'ACCEPTANCE_RECORDED', 'FORMATION_DECIDED', 'GOAL_COMPLETION_REQUESTED', 'GOAL_COMPLETION_DECIDED')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_notification_event_required_action",
                schema: "mentor",
                table: "notification_event",
                sql: "required_action IN ('NONE', 'RESPOND_TO_INVITATION', 'SELECT_ANOTHER_MENTOR', 'RECORD_WILLINGNESS', 'PREPARE_AGREEMENT_DRAFT', 'ACCEPT_CURRENT_REVISION', 'REVISE_DRAFT', 'ARRANGE_FIRST_SESSION', 'DECIDE_GOAL_COMPLETION', 'SUBMIT_PROGRESS')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_notification_event_event_type",
                schema: "mentor",
                table: "notification_event");

            migrationBuilder.DropCheckConstraint(
                name: "ck_notification_event_required_action",
                schema: "mentor",
                table: "notification_event");

            migrationBuilder.AddCheckConstraint(
                name: "ck_notification_event_event_type",
                schema: "mentor",
                table: "notification_event",
                sql: "event_type IN ('MENTOR_APPLICATION_DECIDED', 'INVITATION_SENT', 'MENTOR_RESPONDED', 'WILLINGNESS_RECORDED', 'REVISION_CREATED', 'ACCEPTANCE_RECORDED', 'FORMATION_DECIDED', 'GOAL_COMPLETION_REQUESTED', 'GOAL_COMPLETION_DECIDED')");
        }
    }
}
