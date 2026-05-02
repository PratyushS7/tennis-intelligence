using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TennisIntelligence.Models;
using TennisIntelligence.Services;

namespace TennisIntelligence.Filters;

/// <summary>
/// Automatically logs page views for all Razor Pages.
/// Only logs when the result is a PageResult (not redirects or JSON responses).
/// </summary>
public sealed class InteractionLoggingFilter : IAsyncPageFilter
{
    private static readonly Dictionary<string, string> PageRouteMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["/Index"] = PageNames.Home,
        ["/LogSession"] = PageNames.LogSession,
        ["/History"] = PageNames.History,
        ["/Insights"] = PageNames.Insights,
        ["/Coach"] = PageNames.Coach,
        ["/Goals"] = PageNames.Goals,
        ["/GoalDetail"] = PageNames.GoalDetail,
    };

    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

    public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        var result = await next();

        // Only log actual page renders, not redirects or API responses
        if (result.Result is not PageResult)
            return;

        // Only log GET requests (page views), not POSTs that render pages
        if (!string.Equals(context.HttpContext.Request.Method, "GET", StringComparison.OrdinalIgnoreCase))
            return;

        var pagePath = (context.ActionDescriptor as CompiledPageActionDescriptor)?.ViewEnginePath;
        if (pagePath == null || !PageRouteMap.TryGetValue(pagePath, out var pageName))
            return;

        var interactionService = context.HttpContext.RequestServices.GetRequiredService<InteractionService>();
        await interactionService.LogAsync(pageName, InteractionActions.PageView);
    }
}
