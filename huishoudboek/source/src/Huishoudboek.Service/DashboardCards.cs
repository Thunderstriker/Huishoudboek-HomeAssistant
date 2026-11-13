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
                return new RazorComponentResult<CardGallery>(new
                {
                    Owners = coordinator.Read().Data.Owners.ToArray(), Today = DateOnly.FromDateTime(DateTime.Today)
                });
            }).RequireAuthorization();
        app.MapGet("/kaarten/{widget}", (string widget, HttpContext context,
            AdministrationCoordinator coordinator, WebsiteSessions sessions) =>
        {
            if (!sessions.Valid(context.User)) return (IResult)Results.Unauthorized();
            if (widget is not ("month-overview" or "spending" or "freedom-map")) return Results.NotFound();
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
            var overview = HomeOverview.Create(data, period.Year, period.Month, today, ownerId);
            var insights = widget == "freedom-map" ? FinancialInsights.Create(data, today, ownerId) : null;
            var model = new DashboardCardModel(widget, context.Request.PathBase + "/", theme, period.Year,
                period.Month, overview, insights, selectedOwner?.Name);
            return new RazorComponentResult<CardDocument>(new { Model = model });
        }).RequireAuthorization().WithMetadata(new FrameEndpoint());
    }
}
