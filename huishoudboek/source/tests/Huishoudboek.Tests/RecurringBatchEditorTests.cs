using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text.Json;
using Huishoudboek.Core;
using Huishoudboek.Infrastructure;
using Huishoudboek.Service;
using Huishoudboek.Web;
using Huishoudboek.Web.Components.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Xunit;

namespace Huishoudboek.Tests;

public sealed class RecurringBatchEditorTests
{
    [Theory]
    [InlineData("nl-NL", true, "3 passende bankboekingen in de historie gevonden", "Opslaan en 3 boekingen koppelen")]
    [InlineData("nl-NL", false, "3 passende bankboekingen in de historie gevonden", "Opslaan zonder koppelen")]
    [InlineData("en-GB", true, "3 matching historical bank entries found", "Save and link 3 entries")]
    [InlineData("en-GB", false, "3 matching historical bank entries found", "Save without linking")]
    public async Task EditorShowsCountBeforeSavingAndSupportsAcceptingOrDecliningInBothLanguages(
        string culture, bool accept, string question, string button)
    {
        var previous = CultureInfo.CurrentCulture;
        var previousUi = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            var owner = new Owner { Name = "Tine" };
            var account = new Account { Name = "Zichtrekening AXA", OwnerIds = [owner.Id], OpeningDate = today.AddYears(-1) };
            var data = new HouseholdData { Owners = [owner], Accounts = [account] };
            for (var i = 0; i < 3; i++) data.Transactions.Add(new Transaction { AccountId = account.Id,
                Date = today.AddMonths(-i), Amount = -6, Description = "Beheren rek. Excellence P. (coop)" });
            var plan = new RecurringPayment { Name = "Beheer rekening", AccountId = account.Id, Amount = -6,
                StartDate = today.AddMonths(-3), DayOfMonth = today.Day, MatchText = "Beheren rek. Excellence P. (coop)",
                MatchTolerance = 0, MatchWindowDays = 2 };
            var backend = new Backend(data);
            var services = new ServiceCollection();
            services.AddLogging(); services.AddLocalization();
            services.AddSingleton<IJSRuntime>(new StaticJsRuntime());
            services.AddSingleton<IWebAdministration>(backend); services.AddSingleton<WebState>();
            var capture = new ProbeCapture(); services.AddSingleton(capture);
            using var provider = services.BuildServiceProvider();
            var state = provider.GetRequiredService<WebState>(); await state.LoadAsync();
            capture.Draft = EditorDraft.Recurring(state, plan);
            await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
            var root = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<EditorProbe>());
            await renderer.Dispatcher.InvokeAsync(() => Invoke(capture.Editor, "Save"));
            var html = await renderer.Dispatcher.InvokeAsync(() => WebUtility.HtmlDecode(root.ToHtmlString()));
            Assert.Contains(question, html);
            Assert.Contains(button, html);
            Assert.Equal(0, backend.SaveCount);
            Assert.Empty(backend.Data.RecurringPayments);
            Assert.False(capture.Closed);

            await renderer.Dispatcher.InvokeAsync(() => Invoke(capture.Editor, "Commit", accept));

            Assert.Equal(1, backend.SaveCount);
            Assert.True(capture.Closed);
            Assert.Single(backend.Data.RecurringPayments);
            Assert.All(backend.Data.Transactions, t => Assert.Equal(accept ? plan.Id : (Guid?)null, t.RecurringPaymentId));
            Assert.Contains(accept ? "3" : culture == "nl-NL" ? "opgeslagen" : "saved", state.Message);
            Validation.Validate(backend.Data);
        }
        finally { CultureInfo.CurrentCulture = previous; CultureInfo.CurrentUICulture = previousUi; }
    }

    private static async Task Invoke(Editor editor, string method, params object[] args)
    {
        await (Task)typeof(Editor).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(editor, args)!;
        typeof(ComponentBase).GetMethod("StateHasChanged", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(editor, null);
    }

    private sealed class StaticJsRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => throw new NotSupportedException("Static rendering must not execute JavaScript.");
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => throw new NotSupportedException("Static rendering must not execute JavaScript.");
    }

    public sealed class ProbeCapture
    {
        public EditorDraft Draft { get; set; } = null!;
        public Editor Editor { get; set; } = null!;
        public bool Closed { get; set; }
    }

    public sealed class EditorProbe : ComponentBase
    {
        [Inject] public ProbeCapture Capture { get; set; } = null!;
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<Editor>(0);
            builder.AddAttribute(1, nameof(Editor.Draft), Capture.Draft);
            builder.AddAttribute(2, nameof(Editor.Closed), EventCallback.Factory.Create(this, () => { Capture.Closed = true; }));
            builder.AddComponentReferenceCapture(3, instance => Capture.Editor = (Editor)instance);
            builder.CloseComponent();
        }
    }

    private sealed class Backend(HouseholdData data) : IWebAdministration
    {
        public HouseholdData Data { get; private set; } = data;
        public int SaveCount { get; private set; }
        private string _revision = "initial";
        public Task<AdministrationSnapshot> ReadAsync() => Task.FromResult(new AdministrationSnapshot(_revision, Clone(Data)));
        public Task<AdministrationSnapshot> UpdateAsync(string revision, Action<HouseholdData> change)
        {
            if (revision != _revision) throw new AdministrationConflictException();
            var candidate = Clone(Data); change(candidate); Validation.Validate(candidate);
            Data = candidate; SaveCount++; _revision = "saved";
            return ReadAsync();
        }
        private static HouseholdData Clone(HouseholdData source) => JsonSerializer.Deserialize<HouseholdData>(JsonSerializer.Serialize(source))!;
        public Task<UploadResponse> UploadAsync(Stream source, string name, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PreviewResponse> PreviewAsync(PreviewRequest request, string revision) => throw new NotSupportedException();
        public Task<PreviewRow> AdjustAsync(AdjustImportRequest request) => throw new NotSupportedException();
        public Task<CommitImportResponse> CommitAsync(CommitImportRequest request, string revision) => throw new NotSupportedException();
        public Task<string> RestoreAsync(Stream source, string revision, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
