using System.Diagnostics.CodeAnalysis;

namespace Mentor.Core.Agreements;

/// <summary>
/// A Talent Management administrator's change to the administrative annex of a draft (ADR-004, FR-068, BR-020,
/// BR-023): the one change that does not reset the workflow, because it touches no revision, no content hash and no
/// acceptance. It names a permission basis from the enumerated set, and each basis permits exactly one annex field:
/// ADMINISTRATIVE_REFERENCE the reference, ADMINISTRATIVE_REMARK the remarks. Nothing else is permitted without a reset:
/// every change to content is a replacement revision, so no change is classified as non-substantive by inference.
/// </summary>
public sealed record AnnexChange(PermittedChangeBasis Basis, string? Value)
{
    public const int MaxReferenceLength = 100;
    public const int MaxRemarksLength = 500;

    /// <summary>The one annex field <see cref="Basis"/> permits.</summary>
    public AnnexField Field => FieldOf(Basis);

    public static AnnexField FieldOf(PermittedChangeBasis basis) => basis switch
    {
        PermittedChangeBasis.AdministrativeReference => AnnexField.AdminReference,
        PermittedChangeBasis.AdministrativeRemark => AnnexField.AdminRemarks,
        _ => throw new ArgumentOutOfRangeException(nameof(basis), basis, "Not a permission basis of the enumerated set."),
    };

    /// <summary>
    /// Validates the basis and normalises the value (<see cref="AgreementText"/>): a reference is one line of at most
    /// <see cref="MaxReferenceLength"/> characters, remarks at most <see cref="MaxRemarksLength"/>. A blank value clears
    /// the field. The errors are keyed by member and never repeat what was submitted.
    /// </summary>
    public static bool TryCreate(PermittedChangeBasis? basis, string? value, [NotNullWhen(true)] out AnnexChange? change, out IReadOnlyDictionary<string, string[]> errors)
    {
        var failures = new Dictionary<string, string[]>();
        change = null;
        if (basis is not { } permitted || !Enum.IsDefined(permitted))
        {
            failures["basis"] = ["A permission basis is required: an administrative reference or an administrative remark."];
            errors = failures;
            return false;
        }

        var reference = permitted == PermittedChangeBasis.AdministrativeReference;
        var maxLength = reference ? MaxReferenceLength : MaxRemarksLength;
        switch (AgreementText.Normalize(value, maxLength, lineBreaks: !reference, out var normalized))
        {
            case AgreementText.Problem.TooLong:
                failures["value"] = [$"The value must be at most {maxLength} characters."];
                break;
            case AgreementText.Problem.NotOneLine:
                failures["value"] = ["An administrative reference is one line."];
                break;
            case AgreementText.Problem.NotText:
                failures["value"] = ["The value holds characters that are not text."];
                break;
            default:
                change = new AnnexChange(permitted, normalized);
                break;
        }

        errors = failures;
        return change is not null;
    }
}
