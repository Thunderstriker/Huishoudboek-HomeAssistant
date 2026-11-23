using System.Globalization;
using Huishoudboek.Core;
using Huishoudboek.Web;
using Huishoudboek.Web.Components.Widgets;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Huishoudboek.Service;

public static class DashboardCards
{
    public sealed record FrameEndpoint;

    public static void Map(WebApplication app, HomeAssistantOptions homeAssistant, WebsiteOptions website)
    {
        // No new public listener or machine API. The preview keeps its loopback-only guard.
        if (!homeAssistant.Enabled && !website.PreviewEnabled) return;
        if (website.PreviewEnabled)
            app.MapGet("/kaarten/preview", (HttpContext context, AdministrationCoordinator coordinator, WebsiteSessions sessions) =>
            {
                if (!sessions.Valid(context.User)) return (IResult)Results.Unauthorized();
                var data = coordinator.Read().Data;
                return new RazorComponentResult<CardGallery>(new
                {
                    Owners = data.Owners.ToArray(),
                    Categories = data.Categories.Where(c => c.Purpose == CategoryPurpose.Normal).OrderBy(c => c.Name).ToArray(),
                    Today = DateOnly.FromDateTime(DateTime.Today)
                });
            }).RequireAuthorization();
        app.MapGet("/kaarten/{widget}", (string widget, HttpContext context,
            AdministrationCoordinator coordinator, WebsiteSessions sessions) =>
        {
            if (!sessions.Valid(context.User)) return (IResult)Results.Unauthorized();
            if (widget is not ("month-overview" or "spending" or "freedom-map" or "budget-watch" or "upcoming" or "data-status" or "category-trend" or "import-status")) return Results.NotFound();
            var today = DateOnly.FromDateTime(DateTime.Today);
            var period = new DateOnly(today.Year, today.Month, 1);
            var query = context.Request.Query;
            if (query.Any(p => p.Value.Count != 1)) return Results.BadRequest("Geef elke kaartinstelling eenmaal op.");
            var requestedPeriod = query["period"].ToString();
            if (requestedPeriod is not ("" or "current") &&
                (!DateOnly.TryParseExact(requestedPeriod + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out period) || period.Year is < 1900 or > 2200))
                return Results.BadRequest("Gebruik period=current of een maand in de vorm jjjj-mm (1900–2200).");
            var theme = query["theme"].ToString();
            if (theme == "") theme = "light";
            if (theme is not ("light" or "dark")) return Results.BadRequest("Gebruik theme=light of theme=dark.");
            if (query["language"].ToString() is not ("" or "nl" or "en")) return Results.BadRequest("Gebruik language=nl of language=en.");
            Guid? ownerId = null;
            if (query.TryGetValue("owner", out var owner))
            {
                if (!Guid.TryParseExact(owner.ToString(), "D", out var id)) return Results.BadRequest("Gebruik een geldige eigenaar-id.");
                ownerId = id;
            }
            var data = coordinator.Read().Data;
            var selectedOwner = ownerId == null ? null : data.Owners.SingleOrDefault(o => o.Id == ownerId);
            // Never silently widen a stale/invalid owner selection to the whole household.
            if (ownerId != null && selectedOwner == null) return Results.BadRequest("De gekozen eigenaar bestaat niet meer.");
            CategoryHistoryOverview? history = null;
            string? categoryName = null;
            if (widget == "category-trend")
            {
                if (!Guid.TryParseExact(query["category"].ToString(), "D", out var categoryId))
                    return Results.BadRequest("Kies category met een categorie-id; gebruik de lege UUID voor ongecategoriseerd.");
                var category = data.Categories.SingleOrDefault(c => c.Id == categoryId && c.Purpose == CategoryPurpose.Normal);
                if (categoryId != Guid.Empty && category == null) return Results.BadRequest("De gekozen uitgavencategorie bestaat niet meer.");
                var months = query["history_months"].ToString() switch { "" or "12" => 12, "6" => 6, "24" => 24, _ => 0 };
                if (months == 0) return Results.BadRequest("Gebruik history_months=6, 12 of 24.");
                var current = new DateOnly(today.Year, today.Month, 1);
                if (period > current) return Results.BadRequest("Categorietrends tonen geen toekomstige maanden.");
                var end = period == current ? current.AddMonths(-1) : period;
                if (end.Year < 1900 || (end.Year - 1900) * 12 + end.Month < months)
                    return Results.BadRequest("De trendperiode moet volledig binnen 1900–2200 vallen.");
                history = CategoryHistory.Create(data, categoryId, end.AddMonths(1 - months), end, today, ownerId);
                categoryName = category?.Name;
            }
            var overview = HomeOverview.Create(data, period.Year, period.Month, today, ownerId);
            var insights = widget == "freedom-map" ? FinancialInsights.Create(data, today, ownerId) : null;
            var model = new DashboardCardModel(widget, context.Request.PathBase + "/", theme, period.Year,
                period.Month, overview, insights, selectedOwner?.Name) { CategoryHistory = history, CategoryName = categoryName, OwnerId = ownerId };
            return new RazorComponentResult<CardDocument>(new { Model = model });
        }).RequireAuthorization().WithMetadata(new FrameEndpoint());
    }
}
