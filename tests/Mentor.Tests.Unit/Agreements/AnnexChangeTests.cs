using Mentor.Core.Agreements;

namespace Mentor.Tests.Unit.Agreements;

// ADR-004, FR-068, BR-020, BR-023: the one no-reset change names a permission basis from the enumerated set, and each
// basis permits exactly one field of the administrative annex.
public sealed class AnnexChangeTests
{
    [Theory]
    [InlineData(PermittedChangeBasis.AdministrativeReference, AnnexField.AdminReference)]
    [InlineData(PermittedChangeBasis.AdministrativeRemark, AnnexField.AdminRemarks)]
    public void Each_basis_permits_one_annex_field(PermittedChangeBasis basis, AnnexField field)
    {
        Assert.True(AnnexChange.TryCreate(basis, "TM-2026-014", out var change, out var errors));

        Assert.Empty(errors);
        Assert.Equal((basis, field, "TM-2026-014"), (change.Basis, change.Field, change.Value));
    }

    [Fact]
    public void A_change_without_a_basis_of_the_enumerated_set_is_refused()
    {
        Assert.False(AnnexChange.TryCreate(null, "TM-2026-014", out var none, out var errors));
        Assert.Null(none);
        Assert.Equal(["basis"], errors.Keys);
        Assert.False(AnnexChange.TryCreate((PermittedChangeBasis)5, "TM-2026-014", out _, out errors));
        Assert.Equal(["basis"], errors.Keys);
        Assert.Throws<ArgumentOutOfRangeException>(() => AnnexChange.FieldOf((PermittedChangeBasis)5));
    }

    [Fact]
    public void A_reference_is_one_short_line_and_a_blank_clears_it()
    {
        Assert.True(AnnexChange.TryCreate(PermittedChangeBasis.AdministrativeReference, "  TM-2026-014 ", out var trimmed, out _));
        Assert.Equal("TM-2026-014", trimmed.Value);
        Assert.True(AnnexChange.TryCreate(PermittedChangeBasis.AdministrativeReference, "   ", out var cleared, out _));
        Assert.Null(cleared.Value);
        Assert.True(AnnexChange.TryCreate(PermittedChangeBasis.AdministrativeReference, null, out cleared, out _));
        Assert.Null(cleared.Value);

        Assert.False(AnnexChange.TryCreate(PermittedChangeBasis.AdministrativeReference, "TM-2026\n014", out _, out var errors));
        Assert.Equal(["value"], errors.Keys);
        Assert.False(AnnexChange.TryCreate(PermittedChangeBasis.AdministrativeReference, new string('x', AnnexChange.MaxReferenceLength + 1), out _, out errors));
        Assert.Equal(["value"], errors.Keys);
        Assert.True(AnnexChange.TryCreate(PermittedChangeBasis.AdministrativeReference, new string('x', AnnexChange.MaxReferenceLength), out _, out _));
    }

    [Fact]
    public void Remarks_may_span_lines_within_their_length()
    {
        Assert.True(AnnexChange.TryCreate(PermittedChangeBasis.AdministrativeRemark, "Filed with the register.\r\nCost centre 4410.", out var change, out _));
        Assert.Equal("Filed with the register.\nCost centre 4410.", change.Value);

        Assert.False(AnnexChange.TryCreate(PermittedChangeBasis.AdministrativeRemark, new string('x', AnnexChange.MaxRemarksLength + 1), out _, out var errors));
        Assert.Equal(["value"], errors.Keys);
        Assert.False(AnnexChange.TryCreate(PermittedChangeBasis.AdministrativeRemark, "Filed\u0007", out _, out errors));
        Assert.Equal(["value"], errors.Keys);
    }
}
