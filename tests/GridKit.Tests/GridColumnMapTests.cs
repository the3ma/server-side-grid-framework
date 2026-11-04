namespace GridKit.Tests;

using GridKit.Columns;
using GridKit.Contract;

public class GridColumnMapTests
{
    [Theory]
    [InlineData("RegionId")]
    [InlineData("region_id")]
    [InlineData("PI_NAME")]
    [InlineData("1abc")]
    [InlineData("")]
    [InlineData("name\n")]
    [InlineData("naïve")]
    public void Field_ids_must_be_camelCase(string field)
    {
        var act = () => GridColumnMap<TestRow>.Create("bad", "g.Id").Text(field, "g.Name");

        act.Should().Throw<ArgumentException>().WithMessage("*camelCase*");
    }

    [Fact]
    public void Field_id_pattern_rejects_a_trailing_newline_that_dollar_would_accept()
    {
        GridFieldNames.IsValid("regionId").Should().BeTrue();
        GridFieldNames.IsValid("regionId\n").Should().BeFalse();
        GridFieldNames.IsValid(new string('a', 64)).Should().BeTrue();
        GridFieldNames.IsValid(new string('a', 65)).Should().BeFalse();
    }

    [Fact]
    public void A_field_cannot_be_declared_twice()
    {
        var act = () => GridColumnMap<TestRow>.Create("bad", "g.Id").Text("name", "g.A").Text("name", "g.B");

        act.Should().Throw<ArgumentException>().WithMessage("*twice*");
    }

    [Fact]
    public void A_restricted_column_cannot_be_a_default_sort_key()
    {
        var act = () => GridColumnMap<TestRow>.Create("bad", "g.Id")
            .Text("tier", "g.Tier", c => c.Sortable().Restricted())
            .DefaultSortBy("tier");

        act.Should().Throw<ArgumentException>().WithMessage("*restricted*");
    }

    [Fact]
    public void A_default_sort_key_must_be_declared_and_sortable()
    {
        var unknown = () => GridColumnMap<TestRow>.Create("bad", "g.Id").DefaultSortBy("name");
        var notSortable = () => GridColumnMap<TestRow>.Create("bad", "g.Id").Text("name", "g.Name").DefaultSortBy("name");

        unknown.Should().Throw<ArgumentException>().WithMessage("*not declared*");
        notSortable.Should().Throw<ArgumentException>().WithMessage("*not sortable*");
    }

    [Fact]
    public void Flags_only_apply_to_the_types_they_make_sense_for()
    {
        var searchableNumber = () => GridColumnMap<TestRow>.Create("bad", "g.Id").Number("n", "g.N", c => c.Searchable());
        var integerText = () => GridColumnMap<TestRow>.Create("bad", "g.Id").Text("t", "g.T", c => c.IntegerValues());
        var upperSet = () => GridColumnMap<TestRow>.Create("bad", "g.Id").Set("s", "g.S", c => c.StoredUpperCase());

        searchableNumber.Should().Throw<ArgumentException>();
        integerText.Should().Throw<ArgumentException>();
        upperSet.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Operators_can_be_narrowed_but_not_widened()
    {
        var map = GridColumnMap<TestRow>.Create("ok", "g.Id")
            .Text("name", "g.Name", c => c.Filterable().Ops(GridOperator.Equals, GridOperator.StartsWith));
        var widened = () => GridColumnMap<TestRow>.Create("bad", "g.Id")
            .Text("name", "g.Name", c => c.Ops(GridOperator.InRange));

        map.Find("name")!.Operators.Should().Equal(GridOperator.Equals, GridOperator.StartsWith);
        widened.Should().Throw<ArgumentException>().WithMessage("*subset*");
    }

    [Fact]
    public void Everything_is_opt_in()
    {
        var column = G.Map.Find("hidden")!;

        column.Sortable.Should().BeFalse();
        column.Filterable.Should().BeFalse();
        column.Searchable.Should().BeFalse();
        column.Indexed.Should().BeFalse();
    }
}
