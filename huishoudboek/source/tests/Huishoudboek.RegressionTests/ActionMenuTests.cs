using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Huishoudboek.Core;
using Huishoudboek.Web;
using Huishoudboek.Web.Components.Pages;
using Huishoudboek.Web.Components.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

internal static class ActionMenuTests
{
    public static IEnumerable<(string Name, Action Run)> All()
    {
        yield return ("Desktop and mobile transactions share compact, labelled menus with unique targets in both languages", () => WithCultures(() =>
        {
            var (state, transaction) = Fixture();
            var html = Render<Transactions>(state);
            var menus = Menus(html); Equal(2, menus.Count);
            Equal(2, menus.Select(m => m.Groups["id"].Value).Distinct().Count());
            foreach (Match menu in menus)
            {
                True(html.Contains($"popovertarget=\"{menu.Groups["id"].Value}\""));
                True(menu.Value.Contains("popover=\"auto\" role=\"menu\""));
                True(menu.Value.Contains(WebUtility.HtmlEncode(UiCulture.Text("ActionsFor", transaction.Description))));
                Actions(menu.Value, "Edit", "RecognizeAs", "PurchaseNew", "TransactionHandling", "LinkToRecurringPayment", "Delete");
            }
            Equal(2, Regex.Matches(html, "class=\"row-actions-toggle\"").Count);
            True(html.Contains("aria-haspopup=\"menu\" aria-expanded=\"false\""));
            True(html.Contains(WebUtility.HtmlEncode(UiCulture.Text("MoreActions"))));
        }));
        yield return ("Transaction menus preserve action eligibility for income, mortgages, transfers, plans and splits", () => WithCultures(() =>
        {
            var (state, transaction) = Fixture();
            transaction.Amount = 25;
            Actions(TransactionMenu(state, transaction), "Edit", "RecognizeAs", "TransactionHandling", "LinkToRecurringPayment", "Delete");
            transaction.Kind = TransactionKind.Mortgage;
            Actions(TransactionMenu(state, transaction), "Edit", "RecognizeAs", "LinkToRecurringPayment", "Delete");
            transaction.Kind = TransactionKind.Transfer;
            Actions(TransactionMenu(state, transaction), "Edit", "Delete");
            transaction.Kind = TransactionKind.Normal; transaction.Amount = -25;
            state.Data.TransferMatches.Add(new() { DebitTransactionId = transaction.Id, CreditTransactionId = Guid.NewGuid(), IsConfirmed = true });
            // Receipt-link eligibility is deliberately unchanged, including linked bank transfers.
            Actions(TransactionMenu(state, transaction), "Edit", "PurchaseNew", "Delete");
            state.Data.TransferMatches.Clear(); transaction.RecurringPaymentId = Guid.NewGuid();
            Actions(TransactionMenu(state, transaction), "Edit", "RecognizeAs", "PurchaseNew", "TransactionHandling", "Delete");
            transaction.RecurringPaymentId = null;
            state.Data.TransactionAllocations.Add(new() { TransactionId = transaction.Id, Amount = transaction.Amount });
            Actions(TransactionMenu(state, transaction), "Edit", "RecognizeAs", "PurchaseNew", "TransactionHandling", "Delete");
        }));
        yield return ("Transaction menus retain receipt destinations and counts without exposing unavailable optional callbacks", () => WithCultures(() =>
        {
            var (state, transaction) = Fixture();
            state.Data.PurchaseReceipts.Add(new() { TransactionId = transaction.Id });
            state.Data.PurchaseReceipts.Add(new() { TransactionId = transaction.Id });
            var html = TransactionMenu(state, transaction);
            True(html.Contains($"href=\"aankopen?transaction={transaction.Id:D}\" role=\"menuitem\" tabindex=\"-1\""));
            True(WebUtility.HtmlDecode(html).Contains(UiCulture.Text("PurchaseViewForTransaction") + " (2)"));
            state.Data.PurchaseReceipts.Clear();
            html = Render<TransactionActions>(state, new Dictionary<string, object?> { [nameof(TransactionActions.Transaction)] = transaction });
            Actions(html, "Edit", "PurchaseNew", "TransactionHandling", "Delete");
        }));
        yield return ("Recognition menus retain edit and preview while deletion stays unavailable for shared plans in both languages", () => WithCultures(() =>
        {
            var (state, _) = Fixture();
            var plan = new RecurringPayment { Name = "Insurance", AccountId = state.Data.Accounts[0].Id };
            state.Data.RecurringPayments.Add(plan);
            state.Data.Rules.Add(new() { Name = "Shop", Contains = "SHOP" });
            state.Data.Rules.Add(new() { Name = "Insurance", Contains = "POLICY", RecurringPaymentId = plan.Id });
            var html = Render<Recognition>(state); var menus = Menus(html);
            Equal(2, menus.Count);
            Actions(menus[0].Value, "Edit", "PreviewRecognition", "Delete");
            Actions(menus[1].Value, "Edit", "PreviewRecognition");
            True(menus[0].Value.Contains("class=\"menu-delete\""));
            True(!menus[1].Value.Contains("class=\"menu-delete\""));
            True(html.Contains(WebUtility.HtmlEncode(UiCulture.Text("ActionsFor", "SHOP"))));
        }));
    }

    private static (WebState State, Transaction Transaction) Fixture()
    {
        var state = new WebState(null!); var account = new Account { Name = "Current" };
        state.Data.Accounts.Add(account);
        var transaction = new Transaction { AccountId = account.Id, Description = "Shop", Date = new(state.Year, state.Month, 1), Amount = -25 };
        state.Data.Transactions.Add(transaction);
        return (state, transaction);
    }
    private static string TransactionMenu(WebState state, Transaction transaction)
    {
        var callback = EventCallback.Factory.Create<Transaction>(state, (Transaction _) => { });
        return Render<TransactionActions>(state, new Dictionary<string, object?>
        {
            [nameof(TransactionActions.Transaction)] = transaction,
            [nameof(TransactionActions.Edit)] = callback, [nameof(TransactionActions.Recognize)] = callback,
            [nameof(TransactionActions.Handle)] = callback, [nameof(TransactionActions.Recurring)] = callback,
            [nameof(TransactionActions.Delete)] = callback
        });
    }
    private static MatchCollection Menus(string html) => Regex.Matches(html,
        "<div id=\"(?<id>row-actions-[^\"]+)\" class=\"row-actions-menu\"[^>]*>.*?</div>", RegexOptions.Singleline);
    private static void Actions(string html, params string[] keys)
    {
        var actions = Regex.Matches(html, "<(?:button|a)\\b[^>]*role=\"menuitem\"[^>]*>(.*?)</(?:button|a)>", RegexOptions.Singleline);
        Equal(string.Join("|", keys.Select(k => UiCulture.Text(k))),
            string.Join("|", actions.Select(a => WebUtility.HtmlDecode(a.Groups[1].Value).Trim())));
        True(actions.All(a => a.Value.Contains("tabindex=\"-1\"")));
    }
    private static string Render<T>(WebState state, IDictionary<string, object?>? parameters = null) where T : IComponent
    {
        var services = new ServiceCollection(); services.AddLogging(); services.AddLocalization(); services.AddSingleton(state); services.AddSingleton<IJSRuntime>(new NoJs());
        using var provider = services.BuildServiceProvider();
        return Run().GetAwaiter().GetResult();
        async Task<string> Run()
        {
            await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
            return await renderer.Dispatcher.InvokeAsync(async () =>
                (await renderer.RenderComponentAsync<T>(ParameterView.FromDictionary(parameters ?? new Dictionary<string, object?>()))).ToHtmlString());
        }
    }
    private sealed class NoJs : IJSRuntime
    {
        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) => ValueTask.FromResult(default(T)!);
        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken cancellationToken, object?[]? args) => ValueTask.FromResult(default(T)!);
    }
    private static void WithCultures(Action run)
    {
        var culture = CultureInfo.CurrentCulture; var ui = CultureInfo.CurrentUICulture;
        try { foreach (var name in new[] { "nl-NL", "en-GB" }) { CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(name); run(); } }
        finally { CultureInfo.CurrentCulture = culture; CultureInfo.CurrentUICulture = ui; }
    }
    private static void Equal<T>(T expected, T actual)
    { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}."); }
    private static void True(bool value) { if (!value) throw new Exception("Assertion failed."); }
}
