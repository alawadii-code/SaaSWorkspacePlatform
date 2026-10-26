using Workspace.Application.Common;
using Xunit;

namespace Workspace.UnitTests.Common;

/// <summary>Paging bounds are an input-validation concern: w/o them a client cud ask for evry row in a table.</summary>
public class PageRequestTests
{
    [Fact]
    public void Defaults_when_nothing_is_supplied()
    {
        var page = new PageRequest();

        Assert.Equal(1, page.Page);
        Assert.Equal(PageRequest.DefaultPageSize, page.PageSize);
        Assert.Equal(0, page.Skip);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Non_positive_page_numbers_fall_back_to_the_first_page(int requested) =>
        Assert.Equal(1, new PageRequest(requested).Page);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Non_positive_page_sizes_fall_back_to_the_default(int requested) =>
        Assert.Equal(PageRequest.DefaultPageSize, new PageRequest(1, requested).PageSize);

    [Fact]
    public void Page_size_is_capped() =>
        Assert.Equal(PageRequest.MaxPageSize, new PageRequest(1, 10_000).PageSize);

    [Fact]
    public void Skip_is_derived_from_page_and_size() =>
        Assert.Equal(60, new PageRequest(4, 20).Skip);

    [Fact]
    public void TotalPages_rounds_up()
    {
        var result = new PagedResult<string>(["a"], Page: 1, PageSize: 20, TotalCount: 41);

        Assert.Equal(3, result.TotalPages);
        Assert.True(result.HasNextPage);
    }

    [Fact]
    public void HasNextPage_is_false_on_the_last_page()
    {
        var result = new PagedResult<string>(["a"], Page: 3, PageSize: 20, TotalCount: 41);

        Assert.False(result.HasNextPage);
    }

    [Fact]
    public void Empty_results_have_no_pages()
    {
        var result = new PagedResult<string>([], Page: 1, PageSize: 20, TotalCount: 0);

        Assert.Equal(0, result.TotalPages);
        Assert.False(result.HasNextPage);
    }
}
