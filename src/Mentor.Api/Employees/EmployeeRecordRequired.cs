using Microsoft.AspNetCore.Http.HttpResults;

namespace Mentor.Api.Employees;

/// <summary>
/// The refusal of an action that would be attributed to the caller's employee record when they have none yet. The
/// record is created by the caller's own employee profile on first access (COMP-EMPLOYEE-PROFILE) and by nothing else,
/// so no application, request or decision is attributed to a record made up on the spot (DR-001).
/// </summary>
public static class EmployeeRecordRequired
{
    public const string Title = "No employee profile yet";

    public static ProblemHttpResult Problem() => TypedResults.Problem(
        statusCode: StatusCodes.Status409Conflict,
        title: Title,
        detail: "The action is attributed to the caller's employee record, which their own employee profile creates on first access; open or save that profile first.");
}
