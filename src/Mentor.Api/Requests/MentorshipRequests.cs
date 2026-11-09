using System.Security.Claims;
using Mentor.Api.Authorisation;
using Mentor.Api.Identity;
using Mentor.Core.Requests;
using Mentor.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Mentor.Api.Requests;

/// <summary>
/// Mentorship requests (COMP-MENTORSHIP-REQUEST, DR-003): an employee submits their own, and it stays a record of its
/// own before and after any agreement made from it (FR-017). The protégé reads their requests, a mentor reads one only
/// while a candidate of it that has not ended names them, and Talent Management reads any (ADR-003). The matrix has no
/// action that edits a request, so nothing here changes one.
/// </summary>
public sealed class MentorshipRequests(MentorDbContext db, CallerResolver callers, IAuthorizationService authorization)
{
    /// <summary>
    /// The caller's own request, open until a relationship is formed from it. The protégé is the authenticated caller,
    /// whose employee record their own employee profile creates (COMP-EMPLOYEE-PROFILE); without one, nothing is
    /// submitted.
    /// </summary>
    public async Task<PairingOutcome<MentorshipRequestView>> SubmitAsync(ClaimsPrincipal user, RequestDetails details, CancellationToken cancellationToken)
    {
        if (await callers.ResolveAsync(user, cancellationToken) is not { } caller)
        {
            return PairingOutcome<MentorshipRequestView>.Refused(PairingRefusal.Forbidden);
        }

        if (caller.EmployeeId is not { } protege)
        {
            return PairingOutcome<MentorshipRequestView>.Refused(PairingRefusal.NoEmployeeRecord);
        }

        if (!await authorization.IsPermittedAsync(user, AuthorisationMatrix.MentorshipRequest.Create, new ProtectedRecord { Owner = protege }))
        {
            return PairingOutcome<MentorshipRequestView>.Refused(PairingRefusal.Forbidden);
        }

        var request = new MentorshipRequest
        {
            ProtegeEmployeeId = protege,
            Motivation = details.Motivation,
            DevelopmentGoals = details.DevelopmentGoals,
            NontechnicalExpectations = details.NontechnicalExpectations,
            Disposition = RequestDisposition.Open,
        };
        db.MentorshipRequests.Add(request);
        await db.SaveChangesAsync(cancellationToken);
        return PairingOutcome<MentorshipRequestView>.Of((await ViewsAsync(db.MentorshipRequests.Where(row => row.Id == request.Id), cancellationToken)).Single());
    }

    /// <summary>The caller's own requests, oldest first; none for a caller without an employee record.</summary>
    public async Task<IReadOnlyList<MentorshipRequestView>> ListOwnAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        if (await callers.ResolveAsync(user, cancellationToken) is not { EmployeeId: { } protege })
        {
            return [];
        }

        return await ViewsAsync(db.MentorshipRequests.Where(request => request.ProtegeEmployeeId == protege), cancellationToken);
    }

    public async Task<PairingOutcome<MentorshipRequestView>> ReadAsync(ClaimsPrincipal user, Guid id, CancellationToken cancellationToken)
    {
        var request = await db.MentorshipRequests.AsNoTracking().SingleOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (request is null)
        {
            return PairingOutcome<MentorshipRequestView>.Refused(PairingRefusal.NotFound);
        }

        if (!await IsReadableAsync(user, request, cancellationToken))
        {
            return PairingOutcome<MentorshipRequestView>.Refused(PairingRefusal.Forbidden);
        }

        return PairingOutcome<MentorshipRequestView>.Of((await ViewsAsync(db.MentorshipRequests.Where(row => row.Id == id), cancellationToken)).Single());
    }

    /// <summary>
    /// The candidates of a request the caller may read, oldest first: every one for its protégé and Talent Management,
    /// and only their own for a mentor, who learns nothing of the request's other pairings.
    /// </summary>
    public async Task<PairingOutcome<IReadOnlyList<CandidateView>>> CandidatesAsync(ClaimsPrincipal user, Guid id, CancellationToken cancellationToken)
    {
        var request = await db.MentorshipRequests.AsNoTracking().SingleOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (request is null)
        {
            return PairingOutcome<IReadOnlyList<CandidateView>>.Refused(PairingRefusal.NotFound);
        }

        if (!await IsReadableAsync(user, request, cancellationToken))
        {
            return PairingOutcome<IReadOnlyList<CandidateView>>.Refused(PairingRefusal.Forbidden);
        }

        return PairingOutcome<IReadOnlyList<CandidateView>>.Of(
            await CandidateQueries.ReadableAsync(db, authorization, user, db.MentorSelections.Where(candidate => candidate.RequestId == id), cancellationToken));
    }

    // mentorship-request.read admits a mentor while a candidate of the request names them (ADR-003); an ended candidate
    // names nobody, since its mentor declined or the pairing was withdrawn.
    private async Task<bool> IsReadableAsync(ClaimsPrincipal user, MentorshipRequest request, CancellationToken cancellationToken)
    {
        var namingMentors = await db.MentorSelections
            .Where(CandidateQueries.Standing)
            .Where(candidate => candidate.RequestId == request.Id)
            .Select(candidate => candidate.MentorEmployeeId)
            .ToListAsync(cancellationToken);
        return await authorization.IsPermittedAsync(user, AuthorisationMatrix.MentorshipRequest.Read, ProtectedRecord.Of(request, namingMentors));
    }

    private async Task<IReadOnlyList<MentorshipRequestView>> ViewsAsync(IQueryable<MentorshipRequest> requests, CancellationToken cancellationToken)
    {
        var rows = await requests.AsNoTracking()
            .Join(db.Employees, request => request.ProtegeEmployeeId, protege => protege.Id, (request, protege) => new { request, protege.DisplayName })
            .OrderBy(row => row.request.SubmittedAt).ThenBy(row => row.request.Id)
            .ToListAsync(cancellationToken);
        return [.. rows.Select(row => MentorshipRequestView.Of(row.request, row.DisplayName))];
    }
}
