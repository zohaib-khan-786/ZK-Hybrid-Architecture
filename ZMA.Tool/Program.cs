using System.Diagnostics;
using Spectre.Console;

namespace ZMA.Tool;

public static class Program
{
    private static readonly Dictionary<string, TierInfo> Tiers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Small"] = new TierInfo(
            ShortName: "zma-small",
            PackageId: "ZMA.Small.Template",
            Description: "Monolith - 4-layer Clean Architecture (Domain, Application, Infrastructure, Presentation)",
            Color: "blue"),
        ["Medium"] = new TierInfo(
            ShortName: "zma-med",
            PackageId: "ZMA.Medium.Template",
            Description: "Modular Monolith - Module-per-feature with separate DbContexts",
            Color: "orange3"),
        ["Large"] = new TierInfo(
            ShortName: "zma-large",
            PackageId: "ZMA.Large.Template",
            Description: "Microservices - Catalog, Order, Payment services + API Gateway + SharedKernel",
            Color: "green"),
    };

    public static async Task<int> Main(string[] args)
    {
        var parsed = ParseArgs(args);

        if (parsed.NonInteractive)
        {
            return await RunNonInteractive(parsed);
        }

        return await RunInteractive();
    }

    private static async Task<int> RunNonInteractive(Args parsed)
    {
        if (!Tiers.TryGetValue(parsed.Tier!, out var tierInfo))
        {
            Console.Error.WriteLine($"Unknown tier '{parsed.Tier}'. Valid: Small, Medium, Large");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(parsed.ProjectName))
        {
            Console.Error.WriteLine("--name is required in non-interactive mode.");
            return 1;
        }

        var parentDir = string.IsNullOrWhiteSpace(parsed.OutputDir)
            ? Environment.CurrentDirectory
            : Path.GetFullPath(parsed.OutputDir);

        var projectDir = Path.Combine(parentDir, parsed.ProjectName);
        var solutionPath = Path.Combine(projectDir, $"{parsed.ProjectName}.sln");

        Console.WriteLine($"Tier: {parsed.Tier}");
        Console.WriteLine($"Template: {tierInfo.ShortName}");
        Console.WriteLine($"Project: {parsed.ProjectName}");
        Console.WriteLine($"Output: {projectDir}");
        Console.WriteLine();

        var installed = await IsTemplateInstalled(tierInfo.ShortName);
        if (!installed)
        {
            Console.WriteLine($"Template '{tierInfo.ShortName}' not found. Installing...");
            var installResult = await RunCommandSilent("dotnet", $"new install {tierInfo.PackageId}");

            if (installResult != 0)
            {
                Console.Error.WriteLine("Failed to install template.");
                return 1;
            }

            Console.WriteLine("Template installed successfully.");
        }

        Console.WriteLine("Scaffolding project...");
        var scaffoldArgs = $"new {tierInfo.ShortName} -n {parsed.ProjectName} -o \"{projectDir}\"";
        var scaffoldResult = await RunCommandSilent("dotnet", scaffoldArgs);

        if (scaffoldResult != 0)
        {
            Console.Error.WriteLine("Scaffolding failed.");
            return 1;
        }

        Console.WriteLine();
        Console.WriteLine("Done! Your project is ready at:");
        Console.WriteLine(solutionPath);
        Console.WriteLine();
        Console.WriteLine("Run these commands to get started:");
        Console.WriteLine($"cd {parsed.ProjectName}");
        Console.WriteLine("dotnet restore");
        Console.WriteLine("dotnet build");
        Console.WriteLine($"dotnet run --project {GetRunProject(parsed.Tier!, parsed.ProjectName)}");

        return 0;
    }

    private static async Task<int> RunInteractive()
    {
        AnsiConsole.Write(new FigletText("ZMA Toolkit").Color(new Color(0, 136, 204)));
        AnsiConsole.MarkupLine("[grey]Zohaib Modular Architecture — [bold]Build once, scale forever[/][/]");
        AnsiConsole.WriteLine();

        var tier = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Which [bold]architecture tier[/] do you need?")
                .PageSize(10)
                .AddChoices(Tiers.Keys)
                .UseConverter(key => $"[{Tiers[key].Color}]{key}[/] — {Tiers[key].Description}"));

        var tierInfo = Tiers[tier];

        var projectName = AnsiConsole.Ask<string>("Enter your [green]project name[/]:");

        var outputDir = AnsiConsole.Prompt(
            new TextPrompt<string?>("Output directory [grey](Enter for current)[/]:")
                .AllowEmpty());

        var parentDir = string.IsNullOrWhiteSpace(outputDir)
            ? Environment.CurrentDirectory
            : Path.GetFullPath(outputDir);

        var projectDir = Path.Combine(parentDir, projectName);
        var solutionPath = Path.Combine(projectDir, $"{projectName}.sln");

        AnsiConsole.WriteLine();
        var summary = new Table();
        summary.AddColumn("Setting");
        summary.AddColumn("Value");
        summary.AddRow("Tier", $"[{tierInfo.Color}]{tier}[/]");
        summary.AddRow("Template", tierInfo.ShortName);
        summary.AddRow("Project Name", projectName);
        summary.AddRow("Output", projectDir);
        AnsiConsole.Write(summary);

        if (!AnsiConsole.Confirm("Scaffold this project?", defaultValue: true))
        {
            AnsiConsole.MarkupLine("[yellow]Cancelled.[/]");
            return 1;
        }

        AnsiConsole.WriteLine();

        var installed = await IsTemplateInstalled(tierInfo.ShortName);
        if (!installed)
        {
            AnsiConsole.MarkupLine($"[yellow]Template '{tierInfo.ShortName}' not found. Installing...[/]");
            var installResult = await RunCommand("dotnet", $"new install {tierInfo.PackageId}");

            if (installResult != 0)
            {
                AnsiConsole.MarkupLine("[red]Failed to install template. Try running:[/]");
                AnsiConsole.MarkupLine($"[grey]dotnet new install {tierInfo.PackageId}[/]");
                return 1;
            }

            AnsiConsole.MarkupLine("[green]Template installed successfully.[/]");
        }

        AnsiConsole.WriteLine();
        await AnsiConsole.Status()
            .StartAsync("Scaffolding project...", async _ =>
            {
                var args = $"new {tierInfo.ShortName} -n {projectName} -o \"{projectDir}\"";
                var result = await RunCommand("dotnet", args);

                if (result != 0)
                {
                    throw new InvalidOperationException("Scaffolding failed.");
                }
            });

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold green]Done![/] Your project is ready at:");
        AnsiConsole.MarkupLine($"[cyan]{solutionPath}[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("Run these commands to get started:");
        AnsiConsole.MarkupLine($"[grey]cd {projectName}[/]");
        AnsiConsole.MarkupLine("[grey]dotnet restore[/]");
        AnsiConsole.MarkupLine("[grey]dotnet build[/]");
        AnsiConsole.MarkupLine($"[grey]dotnet run --project {GetRunProject(tier, projectName)}[/]");

        return 0;
    }

    private static Args ParseArgs(string[] args)
    {
        var result = new Args();
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--tier":
                case "-t":
                    if (i + 1 < args.Length) result.Tier = args[++i];
                    break;
                case "--name":
                case "-n":
                    if (i + 1 < args.Length) result.ProjectName = args[++i];
                    break;
                case "--output":
                case "-o":
                    if (i + 1 < args.Length) result.OutputDir = args[++i];
                    break;
                case "--non-interactive":
                case "--auto":
                    result.NonInteractive = true;
                    break;
            }
        }
        return result;
    }

    private static string GetRunProject(string tier, string projectName) => tier.ToLowerInvariant() switch
    {
        "large" => $"{projectName}.ApiGateway",
        _ => $"{projectName}.Presentation"
    };

    private static async Task<bool> IsTemplateInstalled(string shortName)
    {
        var psi = new ProcessStartInfo("dotnet", "new list")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi);
        if (process is null) return false;

        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        return output.Contains(shortName, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<int> RunCommand(string program, string args)
    {
        AnsiConsole.MarkupLine($"[grey]> {program} {args}[/]");

        var psi = new ProcessStartInfo(program, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi);
        if (process is null) return -1;

        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (!string.IsNullOrWhiteSpace(output))
            AnsiConsole.MarkupLine($"[grey]{output.Trim()}[/]");
        if (!string.IsNullOrWhiteSpace(error))
            AnsiConsole.MarkupLine($"[red]{error.Trim()}[/]");

        return process.ExitCode;
    }

    private static async Task<int> RunCommandSilent(string program, string args)
    {
        var psi = new ProcessStartInfo(program, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi);
        if (process is null) return -1;

        await process.StandardOutput.ReadToEndAsync();
        await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return process.ExitCode;
    }

    private record TierInfo(string ShortName, string PackageId, string Description, string Color);

    private class Args
    {
        public string? Tier { get; set; }
        public string? ProjectName { get; set; }
        public string? OutputDir { get; set; }
        public bool NonInteractive { get; set; }
    }
}
