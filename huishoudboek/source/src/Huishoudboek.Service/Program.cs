using Huishoudboek.Service;
using Microsoft.AspNetCore.Identity;

if (args.Contains("--home-assistant"))
{
    if (args.Length != 1) throw new InvalidOperationException("--home-assistant kan niet met andere startopties worden gecombineerd.");
    Environment.SetEnvironmentVariable("TZ", HomeAssistantBootstrap.ReadTimeZone("/data"));
    TimeZoneInfo.ClearCachedData();
    args = ["--config", HomeAssistantBootstrap.Prepare("/data")];
}

if (args.Contains("--hash-password"))
{
    Console.Write("Nieuw websitewachtwoord (minstens 12 tekens): ");
    var password = new System.Text.StringBuilder();
    while (true)
    {
        var key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Enter) break;
        if (key.Key == ConsoleKey.Backspace) { if (password.Length > 0) password.Length--; }
        else if (!char.IsControl(key.KeyChar) && password.Length < 1024) password.Append(key.KeyChar);
    }
    Console.WriteLine();
    if (password.Length < 12) { Console.Error.WriteLine("Kies minstens 12 tekens."); Environment.ExitCode = 1; return; }
    Console.WriteLine(new PasswordHasher<string>().HashPassword("huishouden", password.ToString()));
    return;
}

await using var app = ServiceApplication.Build(args);
await app.RunAsync();
