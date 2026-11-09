using System.Linq.Expressions;
using System.Security.Claims;
using Mentor.Api.Authorisation;
using Mentor.Core.Requests;
using Mentor.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Mentor.Api.Requests;

internal static class CandidateQueries
{
    /// <summary>
    /// A candidate that has not ended: invited, accepted, drafting or formed. A declined or withdrawn candidate has ended
    /// for good (ADR-015): it holds its request no longer, so the protégé may select again, and it names its mentor no
    /// longer, so that mentor no longer reads the request (ADR-003).
    /// </summary>
    public static readonly Expression<Func<MentorSelection, bool>> Standing =
        candidate => candidate.Status != CandidateStatus.Declined && candidate.Status != CandidateStatus.Withdrawn;

    /// <summary>The candidates <paramref name="candidates"/> selects, oldest first, each with its participants' display names.</summary>
    public static async Task<IReadOnlyList<(MentorSelection Candidate, CandidateView View)>> WithNamesAsync(
        MentorDbContext db, IQueryable<MentorSelection> candidates, CancellationToken cancellationToken)
    {
        var rows = await candidates.AsNoTracking()
            .Join(db.Employees, candidate => candidate.ProtegeEmployeeId, protege => protege.Id,
                (candidate, protege) => new { candidate, ProtegeName = protege.DisplayName })
            .Join(db.Employees, row => row.candidate.MentorEmployeeId, mentor => mentor.Id,
                (row, mentor) => new { row.candidate, row.ProtegeName, MentorName = mentor.DisplayName })
            .OrderBy(row => row.candidate.InvitedAt).ThenBy(row => row.candidate.Id)
            .ToListAsync(cancellationToken);
        return [.. rows.Select(row => (row.candidate, CandidateView.Of(row.candidate, row.ProtegeName, row.MentorName)))];
    }

    /// <summary>Of the candidates <paramref name="candidates"/> selects, those the caller may read (candidate.read).</summary>
    public static async Task<IReadOnlyList<CandidateView>> ReadableAsync(
        MentorDbContext db, IAuthorizationService authorization, ClaimsPrincipal user, IQueryable<MentorSelection> candidates, CancellationToken cancellationToken)
    {
        var readable = new List<CandidateView>();
        foreach (var (candidate, view) in await WithNamesAsync(db, candidates, cancellationToken))
        {
            if (await authorization.IsPermittedAsync(user, AuthorisationMatrix.Candidate.Read, ProtectedRecord.Of(candidate)))
            {
                readable.Add(view);
            }
        }

        return readable;
    }
}
