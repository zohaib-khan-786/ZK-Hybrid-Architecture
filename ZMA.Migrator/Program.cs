using System.Text.RegularExpressions;
using ZMA.Licensing;

var helpText = """
ZMA.Migrator - Migrate ZMA projects between architectural tiers

USAGE:
  zma-migrate --source <path> [--output <path>] [--tier <tier>] [--dry-run]

OPTIONS:
  --source <path>   Path to the source project (project root or src/ folder)
  --output <path>   Output path (default: <source>-<TargetTier>)
  --tier <tier>     Target tier: small, medium, large (default: medium)
  --dry-run         Preview changes without writing files
  --help            Show this help message

TIERS:
  small    Flat 4-layer architecture (Domain, Application, Infrastructure, Presentation)
           Application: DTOs/, Interfaces/, Services/, Exceptions/, Validators/
           Infrastructure: single AppDbContext
           Presentation: Controllers/ directly

  medium   Module-separated 4-layer architecture
           Application: CatalogModule/, OrdersModule/, Shared/
           Infrastructure: separate CatalogDbContext + OrdersDbContext
           Presentation: API/Controllers/

  large    Microservices architecture with SharedKernel, API Gateway, Auth Service
           Each entity gets its own 4-layer service (Domain/Application/Infrastructure/Presentation)
           Application: Commands/, Queries/, DTOs/, Services/, Interfaces/
           Infrastructure: per-service DbContext, Repositories/, Services/
           SharedKernel: Common/, Events/, Interfaces/

EXAMPLES:
  zma-migrate --source ./MyProject
  zma-migrate --source ./MyProject --tier small
  zma-migrate --source ./MyProject/src --output ./MyProject-Migrated --tier medium
  zma-migrate --source ./MyProject --tier medium --dry-run
  zma-migrate --source ./MyProject --tier large --dry-run
""";

if (args.Length == 0 || args.Contains("--help") || args.Contains("-h") || args.Contains("/?"))
{
    Console.WriteLine(helpText);
    return 0;
}

var sourceDir = ParseArg(args, "--source");
var outputDir = ParseArg(args, "--output");
var targetTier = (ParseArg(args, "--tier") ?? "medium").ToLowerInvariant();
var dryRun = args.Contains("--dry-run");

if (sourceDir is null)
{
    Console.Error.WriteLine("--source is required. Use --help for usage.");
    return 1;
}

if (targetTier is not "small" and not "medium" and not "large")
{
    Console.Error.WriteLine($"Unsupported target tier '{targetTier}'. Supported: small, medium, large.");
    return 1;
}

sourceDir = Path.GetFullPath(sourceDir);
var srcDir = LocateSourceDir(sourceDir);

if (srcDir is null)
{
    Console.Error.WriteLine("Source directory not found. Pass the project root (containing src/) or the src/ directory directly.");
    return 1;
}

var projectName = DetectProjectName(srcDir);
if (projectName is null)
{
    Console.Error.WriteLine("Could not detect project name from solution or csproj files.");
    return 1;
}

var sourceTier = DetectTier(srcDir, projectName);
Console.WriteLine($"Detected: {projectName} ({sourceTier})  ->  Target: {targetTier}");
Console.WriteLine($"Source: {sourceDir}");
Console.WriteLine($"Output: {outputDir ?? $"<source>-{Capitalize(targetTier)}"}");

outputDir ??= sourceDir.Replace(Path.GetFileName(sourceDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
    $"{projectName}-{Capitalize(targetTier)}");
outputDir = Path.GetFullPath(outputDir);

var entities = ScanEntities(srcDir, projectName);
Console.WriteLine($"Entities: {(entities.Count > 0 ? string.Join(", ", entities) : "none found")}");

// Detect project type from source controllers
var isMvc = DetectMvcProject(srcDir, projectName);
if (isMvc) Console.WriteLine("Project type: MVC (with Views)");

// License check
var licenseValidator = new LicenseValidator();
if (!licenseValidator.CanMigrateEntityCount(entities.Count))
{
    var c = licenseValidator.Cached;
    Console.Error.WriteLine($"ERROR: This project has {entities.Count} entities but your license only supports {c.MaxEntities}.");
    Console.Error.WriteLine("Purchase a license or remove entities to continue.");
    Console.Error.WriteLine($"Run 'zma --register --key <key>' to activate a license.");
    return 1;
}

var outSrc = outputDir;

var skipReasons = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
var warnings = new List<string>();

if (!dryRun)
    BuildTargetStructure(outSrc, projectName, targetTier, entities);

var fileCount = 0;
var skipped = 0;

var dryRunFileCount = 0;
var dryRunSkipped = 0;

var skipMessageMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
{
    ["skip-shallow-path"] = "File path is too shallow (< 2 segments) - root-level file",
    ["skip-domain-unknown"] = "Not in Domain/Entities|Enums|ValueObjects|Exceptions|Interfaces|Events",
    ["skip-medium-unknown-module"] = "Medium source with unrecognized module directory",
    ["skip-no-entity-match"] = "Does not match any known entity name - add entity to Domain/Entities/ or rename file",
    ["skip-app-unknown"] = "Not in Application/DTOs|Interfaces|Services|Exceptions|Validators",
    ["split-dbcontext"] = "AppDbContext split into per-module DbContexts (Small→Medium)",
    ["skip-dbcontext-direction"] = "AppDbContext only split in Small→Medium migration",
    ["skip-split-contexts"] = "Already-split DbContexts removed in Medium→Small (will regenerate single AppDbContext)",
    ["skip-already-split"] = "Already-split DbContext not handled for this migration direction",
    ["skip-infra-unknown"] = "Not in Infrastructure/Persistence|Repositories|ExternalServices",
    ["skip-ctrl-no-entity"] = "Controller name does not match any entity - add entity or rename file to <Entity>Controller.cs",
    ["skip-already-api-controllers"] = "Already in API/Controllers/ (Medium target, no change needed)",
    ["skip-pres-resource"] = "Non-code file in Presentation (Properties/Models/Views is not migrated)",
    ["skip-pres-unknown"] = "Not in Presentation/Controllers|API|Properties|Models|Views",
    ["split-large-dbcontexts"] = "AppDbContext split into per-service DbContexts (Large tier)",
    ["skip-pres-program-large"] = "Program.cs not migrated in Large tier (generates per-service + gateway Program.cs)",
    ["skip-unknown-layer"] = "File in unrecognized layer directory (expected: Domain|Application|Infrastructure|Presentation)",
};

foreach (var file in Directory.EnumerateFiles(srcDir, "*.cs", SearchOption.AllDirectories))
{
    var relPath = Path.GetRelativePath(srcDir, file);

    // Skip build artifacts silently
    if (relPath.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
     || relPath.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
     || relPath.StartsWith($"obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
     || relPath.StartsWith($"bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
        continue;

    var (destRelPath, module) = ClassifyFile(relPath, projectName, sourceTier, targetTier, entities);

    if (destRelPath is null)
    {
        var reasonKey = module ?? "skip-unknown";
        if (reasonKey.StartsWith("skip-") || reasonKey.StartsWith("split-"))
        {
            skipReasons.TryGetValue(reasonKey, out var c);
            skipReasons[reasonKey] = c + 1;

            if (c < 3 && !dryRun)
            {
                var reason = skipMessageMap.TryGetValue(reasonKey, out var msg) ? msg : reasonKey;
                Console.Error.WriteLine($"  \u2716 {relPath}");
                Console.Error.WriteLine($"    -> {reason}");
            }
        }

        if (dryRun) dryRunSkipped++;
        else skipped++;
        continue;
    }

    if (dryRun)
    {
        dryRunFileCount++;
        if (dryRunFileCount <= 20)
            Console.WriteLine($"  {relPath}  ->  {destRelPath}  [{module}]");
        continue;
    }

    var destPath = Path.Combine(outSrc, destRelPath);
    Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

    var content = File.ReadAllText(file);
    content = TransformContent(content, relPath, projectName, sourceTier, targetTier, module, entities, isMvc);
    File.WriteAllText(destPath, content);
    fileCount++;
}

if (dryRun)
{
    fileCount = dryRunFileCount;
    skipped = dryRunSkipped;
}

Console.WriteLine($"Inventory: {fileCount} source files would be migrated, {skipped} skipped.");

if (skipReasons.Count > 0 && !dryRun)
{
    Console.WriteLine();
    Console.WriteLine("=== SKIPPED FILE REASONS ===");
    foreach (var kv in skipReasons.OrderByDescending(k => k.Value))
    {
        var msg = skipMessageMap.TryGetValue(kv.Key, out var m) ? m : kv.Key;
        Console.WriteLine($"  {kv.Value}x  {msg}");
    }
}

if (dryRun)
{
    Console.WriteLine();
    Console.WriteLine("=== DRY-RUN SUMMARY ===");
    Console.WriteLine($"Project:  {projectName}");
    Console.WriteLine($"From:     {sourceTier}");
    Console.WriteLine($"To:       {targetTier}");
    Console.WriteLine($"Output:   {outputDir}");
    Console.WriteLine($"Entities: {(entities.Count > 0 ? string.Join(", ", entities) : "none found")}");
    Console.WriteLine($"Files to migrate: {fileCount}");
    Console.WriteLine($"Files to skip:    {skipped}");
    Console.WriteLine();
    Console.WriteLine("Run without --dry-run to apply these changes.");
    return 0;
}

WriteTierFiles(outSrc, projectName, targetTier, entities, isMvc);
WriteProjectFiles(outSrc, projectName, targetTier);
WriteSolutionFile(outSrc, projectName);
WriteAppSettings(srcDir, outSrc, projectName);
WriteLaunchSettings(srcDir, outSrc);

Console.WriteLine($"Done. Migrated {fileCount} files, skipped {skipped}.");
return 0;

// ========== HELPERS ==========

static string? ParseArg(string[] args, string key)
{
    var idx = Array.IndexOf(args, key);
    return idx >= 0 && idx + 1 < args.Length ? args[idx + 1] : null;
}

static string Capitalize(string s) => char.ToUpper(s[0]) + s[1..];

static string? LocateSourceDir(string sourceDir)
{
    if (Directory.Exists(Path.Combine(sourceDir, "src")))
        return Path.Combine(sourceDir, "src");
    if (Directory.GetFiles(sourceDir, "*.sln").Length > 0)
        return sourceDir;
    return null;
}

static string? DetectProjectName(string srcDir)
{
    var slnFiles = Directory.GetFiles(srcDir, "*.sln");
    if (slnFiles.Length > 0)
    {
        var slnContent = File.ReadAllText(slnFiles[0]);
        var match = Regex.Match(slnContent, "\"([^\"]+)\\.Application\"");
        if (match.Success)
            return match.Groups[1].Value;
    }

    foreach (var csproj in Directory.GetFiles(srcDir, "*.csproj", SearchOption.AllDirectories))
    {
        var name = Path.GetFileNameWithoutExtension(csproj);
        if (name.EndsWith(".Application")) return name[..^12];
        if (name.EndsWith(".Presentation")) return name[..^13];
    }
    return null;
}

static string DetectTier(string srcDir, string projectName)
{
    // Large tier: has Services/, SharedKernel/, and Gateways/ directories
    if (Directory.Exists(Path.Combine(srcDir, "Services"))
        && Directory.Exists(Path.Combine(srcDir, "SharedKernel"))
        && Directory.Exists(Path.Combine(srcDir, "Gateways")))
        return "large";

    var appDir = Path.Combine(srcDir, $"{projectName}.Application");
    if (!Directory.Exists(appDir)) return "unknown";

    var hasModules = Directory.GetDirectories(appDir)
        .Any(d => Path.GetFileName(d).EndsWith("Module", StringComparison.Ordinal));

    if (hasModules) return "medium";

    var hasFlatDirs = Directory.Exists(Path.Combine(appDir, "DTOs"))
                   && Directory.Exists(Path.Combine(appDir, "Interfaces"));
    if (hasFlatDirs) return "small";

    // Check presentation structure
    var presDir = Path.Combine(srcDir, $"{projectName}.Presentation");
    if (Directory.Exists(presDir))
    {
        var hasApiControllers = Directory.Exists(Path.Combine(presDir, "API", "Controllers"));
        if (hasApiControllers) return "medium";
        var hasControllers = Directory.Exists(Path.Combine(presDir, "Controllers"));
        if (hasControllers) return "small";
    }

    return "unknown";
}

static List<string> ScanEntities(string srcDir, string projectName)
{
    var entities = new List<string>();
    var entityDir = Path.Combine(srcDir, $"{projectName}.Domain", "Entities");
    if (!Directory.Exists(entityDir)) return entities;

    foreach (var file in Directory.GetFiles(entityDir, "*.cs"))
    {
        var content = File.ReadAllText(file);
        var match = Regex.Match(content, @"(?:public\s+)?(?:class|record)\s+(\w+)");
        if (match.Success)
            entities.Add(match.Groups[1].Value);
    }
    return entities;
}

static bool DetectMvcProject(string srcDir, string projectName)
{
    var presDir = Path.Combine(srcDir, $"{projectName}.Presentation");
    if (!Directory.Exists(presDir)) return false;

    var ctrlDirs = new[] { "Controllers", "API/Controllers" }
        .Select(d => Path.Combine(presDir, d))
        .Where(Directory.Exists);

    foreach (var ctrlDir in ctrlDirs)
    {
        foreach (var file in Directory.GetFiles(ctrlDir, "*.cs"))
        {
            var content = File.ReadAllText(file);
            // Controller (MVC) inherits from Controller, not ControllerBase
            if (Regex.IsMatch(content, @":\s*Controller\b") && !Regex.IsMatch(content, @":\s*ControllerBase\b"))
                return true;
        }
    }
    return false;
}

// ========== FILE CLASSIFICATION ==========

static (string? destRelPath, string module) ClassifyFile(
    string relPath, string projectName, string sourceTier, string targetTier, List<string> entities)
{
    var parts = relPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    if (parts.Length < 2) return (null, "skip-shallow-path");

    var dir = parts[0];
    var category = parts.Length > 1 ? parts[1] : "";
    var fileName = Path.GetFileNameWithoutExtension(parts[^1]);

    var dotIdx = dir.IndexOf('.');
    var prefix = dotIdx > 0 && dotIdx < dir.Length - 1 ? dir[..(dotIdx + 1)] : "";
    var layer = dotIdx > 0 && dotIdx < dir.Length - 1 ? dir[(dotIdx + 1)..] : dir;
    string P(string path) => prefix + path;

    // Domain layer - copy as-is for most files
    if (layer == "Domain")
    {
        if (category is "Entities" or "Enums" or "ValueObjects")
        {
            if (targetTier == "large")
            {
                var entity = FindEntity(fileName, entities) ?? FindEntityFromFileName(fileName, entities);
                if (entity is null)
                    return ($"SharedKernel/Common/{fileName}.cs", "large-sharedkernel"); // Cross-cutting domain files
                var svc = EntityToService(entity);
                return ($"Services/{svc}/{svc}.Domain/{category}/{fileName}.cs", $"large-{svc}");
            }
            return (relPath, "copy");
        }
        if (category is "Exceptions" or "Interfaces" or "Events")
            return (relPath, "copy");
        return (null, "skip-domain-unknown");
    }

    // Application layer
    if (layer == "Application")
    {
        // Medium source with nested module structure
        if (sourceTier == "medium" && parts.Length >= 4)
        {
            var moduleDir = parts[1];
            var appCategory = parts[2];

            if (appCategory is "DTOs" or "Interfaces" or "Services" or "Exceptions" or "Validators")
            {
                if (targetTier == "medium")
                    return (relPath, "copy");

                if (targetTier == "small")
                {
                    var flatPath = $"{dir}/{appCategory}/{fileName}.cs";
                    return (flatPath, $"flatten-{moduleDir}");
                }

                if (targetTier == "large")
                {
                    if (moduleDir == "Shared")
                        return ($"SharedKernel/Common/{fileName}.cs", "large-sharedkernel");
                    var svc = ModuleToService(moduleDir);
                    return ($"Services/{svc}/{svc}.Application/{appCategory}/{fileName}.cs", $"large-{svc}");
                }
            }
            return (null, "skip-medium-unknown-module");
        }

        // Small source: classify by entity name
        if (category is "Exceptions" or "Validators")
        {
            if (targetTier == "small")
                return (relPath, "copy");
            if (targetTier == "large")
                return ($"SharedKernel/Common/{fileName}.cs", "large-sharedkernel");
            return (P($"Application/Shared/{category}/{fileName}.cs"), "shared");
        }

        if (category is "DTOs" or "Interfaces" or "Services")
        {
            if (targetTier == "small")
                return (relPath, "copy");

            if (targetTier == "large")
            {
                var entityName = FindEntity(fileName, entities)
                    ?? FindEntityFromFileName(fileName, entities)
                    ?? FindEntityFromFileName(Path.GetFileNameWithoutExtension(fileName.Replace("Dto", "")), entities);
                var svc = entityName is not null ? EntityToService(entityName) : null;
                if (svc is null) return (null, "skip-no-entity-match");
                return ($"Services/{svc}/{svc}.Application/{category}/{fileName}.cs", $"large-{svc}");
            }

            var entity = FindEntity(fileName, entities)
                ?? FindEntityFromFileName(fileName, entities)
                ?? FindEntityFromFileName(Path.GetFileNameWithoutExtension(fileName.Replace("Dto", "")), entities);

            var module = EntityToModule(entity);
            if (module is null)
                return (null, "skip-no-entity-match");

            return (P($"Application/{module}/{category}/{fileName}.cs"), $"app-{module}");
        }

        return (null, "skip-app-unknown");
    }

    // Infrastructure layer
    if (layer == "Infrastructure")
    {
        if (category == "Persistence" && fileName == "AppDbContext")
        {
            if (targetTier == "large")
                return (null, "split-large-dbcontexts");
            return (null, sourceTier == "small" && targetTier == "medium" ? "split-dbcontext" : "skip-dbcontext-direction");
        }

        if (category == "Persistence" && fileName.EndsWith("DbContext"))
        {
            if (sourceTier == "medium" && targetTier == "small")
                return (null, "skip-split-contexts");
            return (null, "skip-already-split");
        }

        if (category == "Repositories")
        {
            if (sourceTier == "medium" && targetTier == "small")
                return (relPath, "flatten-repo"); // Need using & DbContext updates

            if (targetTier == "small")
                return (relPath, "copy");

            if (targetTier == "large")
            {
                var repoEntityName = FindEntityFromRepo(fileName, entities)
                    ?? FindEntityFromFileName(fileName, entities);
                var svc = repoEntityName is not null ? EntityToService(repoEntityName)
                    : entities.Count > 0 ? EntityToService(entities[0])
                    : null;
                if (svc is null) return (null, "skip-no-entity-match");
                return ($"Services/{svc}/{svc}.Infrastructure/Repositories/{fileName}.cs", $"large-{svc}");
            }

            var repoEntity = FindEntityFromRepo(fileName, entities)
                ?? FindEntityFromFileName(fileName, entities);

            var repoModule = EntityToModule(repoEntity);
            if (repoModule is null)
                return (relPath, "copy"); // fallback: keep in original location
            return (relPath, $"repo-{repoModule}");
        }

        if (category == "ExternalServices")
        {
            if (targetTier == "large")
            {
                var firstSvc = entities.Count > 0 ? EntityToService(entities[0]) : null;
                if (firstSvc is null) return (null, "skip-no-entities");
                return ($"Services/{firstSvc}/{firstSvc}.Infrastructure/Services/{fileName}.cs", $"large-{firstSvc}");
            }
            return (relPath, "copy");
        }

        return (null, "skip-infra-unknown");
    }

    // Presentation layer
    if (layer == "Presentation")
    {
        if (category == "Controllers")
        {
            if (targetTier == "small")
                return (relPath, "copy");

            if (targetTier == "large")
            {
                var ctrlEntityName = FindEntityFromController(fileName, entities)
                    ?? FindEntityFromFileName(fileName, entities);
                var svc = ctrlEntityName is not null ? EntityToService(ctrlEntityName) : null;
                if (svc is null) return (null, "skip-ctrl-no-entity");
                var ctrlName = EntityToLargeControllerName(ctrlEntityName, fileName);
                return ($"Services/{svc}/{svc}.Presentation/Controllers/{ctrlName}.cs", $"large-ctrl-{svc}");
            }

            var ctrlEntity = FindEntityFromController(fileName, entities)
                ?? FindEntityFromFileName(fileName, entities);

            var ctrlModule = EntityToModule(ctrlEntity);
            if (ctrlModule is null)
                return (null, "skip-ctrl-no-entity");

            return (P($"Presentation/API/Controllers/{fileName}.cs"), $"ctrl-{ctrlModule}");
        }

        if (category == "API" && parts.Length > 2 && parts[2] == "Controllers")
        {
            if (sourceTier == "medium" && targetTier == "small")
            {
                var flatPath = $"{dir}/Controllers/{fileName}.cs";
                return (flatPath, "flatten-ctrl");
            }

            if (sourceTier == "medium" && targetTier == "large")
            {
                var moduleName = fileName.EndsWith("Controller")
                    ? $"{fileName.Replace("Controller", "")}Module"
                    : "UnknownModule";
                var svc = ModuleToService(moduleName);
                var ctrlEntity = entities.FirstOrDefault(e =>
                    string.Equals(EntityToService(e), svc, StringComparison.OrdinalIgnoreCase));
                var newCtrlName = ctrlEntity is not null
                    ? EntityToLargeControllerName(ctrlEntity, fileName)
                    : $"{svc.Replace("Service", "")}Controller";
                return ($"Services/{svc}/{svc}.Presentation/Controllers/{newCtrlName}.cs", $"large-ctrl-{svc}");
            }

            return (null, "skip-already-api-controllers");
        }

        if (category is "Properties" or "Models" or "Views")
            return (null, "skip-pres-resource");

        if (fileName == "Program" && parts.Length == 2)
        {
            if (targetTier == "large")
                return (null, "skip-pres-program-large"); // Will generate per-service Program.cs
            return (relPath, "program");
        }

        return (null, "skip-pres-unknown");
    }

    return (null, "skip-unknown-layer");
}

static string? FindEntity(string fileName, List<string> entities)
{
    foreach (var entity in entities)
    {
        if (fileName.Equals(entity, StringComparison.OrdinalIgnoreCase)
         || fileName.Equals($"I{entity}", StringComparison.OrdinalIgnoreCase)
         || fileName.Equals($"I{entity}Repository", StringComparison.OrdinalIgnoreCase)
         || fileName.Equals($"I{entity}Service", StringComparison.OrdinalIgnoreCase)
         || fileName.Equals($"{entity}Service", StringComparison.OrdinalIgnoreCase)
         || fileName.Equals($"Create{entity}Dto", StringComparison.OrdinalIgnoreCase)
         || fileName.Equals($"Update{entity}Dto", StringComparison.OrdinalIgnoreCase)
         || fileName.Equals($"{entity}Dto", StringComparison.OrdinalIgnoreCase))
            return entity;
    }
    return null;
}

static string? FindEntityFromRepo(string fileName, List<string> entities)
{
    foreach (var entity in entities)
    {
        if (fileName.Equals($"{entity}Repository", StringComparison.OrdinalIgnoreCase))
            return entity;
    }
    return null;
}

static string? FindEntityFromController(string fileName, List<string> entities)
{
    foreach (var entity in entities)
    {
        if (fileName.Equals($"{entity}Controller", StringComparison.OrdinalIgnoreCase))
            return entity;
    }
    return null;
}

static string? EntityToModule(string? entity) => entity is not null ? $"{entity}Module" : null;

static string EntityToService(string entity) => $"{entity}Service";

static string EntityToLargeControllerName(string entity, string _) => $"{entity}sController";

static string ToPlural(string word)
{
    if (word.EndsWith("y", StringComparison.OrdinalIgnoreCase) && word.Length > 2
        && !"aeiou".Contains(char.ToLower(word[^2])))
        return word[..^1] + "ies";
    if (word.EndsWith("s", StringComparison.OrdinalIgnoreCase)
        || word.EndsWith("sh", StringComparison.OrdinalIgnoreCase)
        || word.EndsWith("ch", StringComparison.OrdinalIgnoreCase)
        || word.EndsWith("x", StringComparison.OrdinalIgnoreCase)
        || word.EndsWith("z", StringComparison.OrdinalIgnoreCase))
        return word + "es";
    return word + "s";
}

static string? FindEntityFromFileName(string fileName, List<string> entities)
{
    foreach (var entity in entities)
    {
        if (fileName.StartsWith(entity, StringComparison.OrdinalIgnoreCase))
            return entity;
    }
    return null;
}

static string ModuleToService(string module)
{
    if (module == "Shared") return "SharedKernel";
    return module.EndsWith("Module") ? $"{module[..^6]}Service" : module;
}

// ========== CONTENT TRANSFORMATION ==========

static string TransformContent(string content, string relPath, string projectName,
    string sourceTier, string targetTier, string module, List<string> entities, bool isMvc = false)
{
    var result = content;

    // Flatten repository (Medium -> Small): reverse module using statements + merge DbContext
    // Must come before generic flatten- prefix check since "flatten-repo" also starts with "flatten-"
    if (module == "flatten-repo")
    {
        result = Regex.Replace(result,
            $@"using\s+{Regex.Escape(projectName)}\.Application\.(\w+Module)\.(DTOs|Interfaces|Services)\b(\.[^;]+)?;",
            $"using {projectName}.Application.$2;");

        result = Regex.Replace(result,
            $@"using\s+{Regex.Escape(projectName)}\.Application\.(\w+Module)\.Exceptions\b.*;",
            $"using {projectName}.Application.Exceptions;");

        // Replace any *DbContext with AppDbContext (merge)
        result = Regex.Replace(result, @"\b\w+DbContext\b", "AppDbContext");
        return result;
    }

    // Flatten controller (Medium -> Small): strip API.Controllers back to Controllers
    // Must come before generic flatten- prefix check since "flatten-ctrl" also starts with "flatten-"
    if (module == "flatten-ctrl")
    {
        result = result.Replace(
            $"namespace {projectName}.Presentation.API.Controllers",
            $"namespace {projectName}.Presentation.Controllers");

        // Strip entity suffix after DTOs/Interfaces/Services (e.g., .Courier)
        result = Regex.Replace(result,
            $@"using\s+{Regex.Escape(projectName)}\.Application\.(\w+Module)\.(DTOs|Interfaces|Services)\b(\.[^;]+)?;",
            $"using {projectName}.Application.$2;");

        result = Regex.Replace(result,
            $@"using\s+{Regex.Escape(projectName)}\.Application\.(\w+Module)\.Exceptions\b.*;",
            $"using {projectName}.Application.Exceptions;");

        result = Regex.Replace(result,
            $@"using\s+{Regex.Escape(projectName)}\.Application\.Shared\.Exceptions\b.*;",
            $"using {projectName}.Application.Exceptions;");

        return result;
    }

    // Flatten from module structure (Medium -> Small) — fully generic, handles any module name
    if (module?.StartsWith("flatten-") == true)
    {
        var modToFlatten = module["flatten-".Length..];

        result = Regex.Replace(result,
            $@"namespace\s+{Regex.Escape(projectName)}\.Application\.{Regex.Escape(modToFlatten)}\.(\w+)\b.*",
            $"namespace {projectName}.Application.$1");

        result = Regex.Replace(result,
            $@"using\s+{Regex.Escape(projectName)}\.Application\.{Regex.Escape(modToFlatten)}\.(\w+)\b(\.[^;]+)?;",
            $"using {projectName}.Application.$1;");

        // Also flatten Shared references in non-Shared files (e.g., ProductModule file using Shared.Exceptions)
        result = Regex.Replace(result,
            $@"using\s+{Regex.Escape(projectName)}\.Application\.Shared\.Exceptions\b",
            $"using {projectName}.Application.Exceptions");
        result = Regex.Replace(result,
            $@"namespace\s+{Regex.Escape(projectName)}\.Application\.Shared\.(\w+)\b",
            $"namespace {projectName}.Application.$1");

        return result;
    }

    // Large tier: route to SharedKernel
    if (module == "large-sharedkernel")
    {
        result = Regex.Replace(result,
            $@"namespace\s+{Regex.Escape(projectName)}\.\w+(\.\w+)*",
            "namespace SharedKernel.Common");
        result = Regex.Replace(result,
            $@"using\s+{Regex.Escape(projectName)}\.\w+(\.\w+)*",
            "using SharedKernel.Common");
        return result;
    }

    // Large tier: service-specific files
    if (module?.StartsWith("large-") == true)
    {
        var svc = module.StartsWith("large-ctrl-") ? module["large-ctrl-".Length..]
                : module["large-".Length..];

        // Redirect Exceptions/Validators to SharedKernel
        result = result.Replace(
            $"using {projectName}.Application.Exceptions;",
            "using SharedKernel.Common;");
        result = result.Replace(
            $"using {projectName}.Application.Validators;",
            "using SharedKernel.Common;");

        // Replace project name with service name in namespaces and usings
        result = result.Replace($"namespace {projectName}.", $"namespace {svc}.");
        result = result.Replace($"using {projectName}.", $"using {svc}.");

        // Replace AppDbContext with service-specific DbContext
        var dbCtxName = $"{svc.Replace("Service", "")}DbContext";
        result = result.Replace("AppDbContext", dbCtxName);

        // Add cross-service using directives for navigation properties
        foreach (var otherEntity in entities)
        {
            var otherSvc = EntityToService(otherEntity);
            if (string.Equals(otherSvc, svc, StringComparison.OrdinalIgnoreCase)) continue;
            if (result.Contains($" {otherEntity}", StringComparison.Ordinal) 
             || result.Contains($"({otherEntity}", StringComparison.Ordinal)
             || result.Contains($"<{otherEntity}", StringComparison.Ordinal)
             || result.Contains($".{otherEntity}?", StringComparison.Ordinal))
            {
                var usingStmt = $"using {otherSvc}.Domain.Entities;";
                if (!result.Contains(usingStmt))
                    result = usingStmt + "\r\n" + result;
            }
        }

        // Controller renaming for Large
        if (module.StartsWith("large-ctrl-"))
        {
            foreach (var entity in entities)
            {
                var s = EntityToService(entity);
                if (!string.Equals(s, svc, StringComparison.OrdinalIgnoreCase)) continue;
                var oldCtrlName = $"{entity}Controller";
                var newCtrlName = EntityToLargeControllerName(entity, oldCtrlName);
                if (oldCtrlName != newCtrlName)
                {
                    result = result.Replace($"class {oldCtrlName}", $"class {newCtrlName}");
                    result = result.Replace($"public {oldCtrlName}(", $"public {newCtrlName}(");
                    // For constructors that might not match exactly
                    result = result.Replace($"({oldCtrlName} ", $"({newCtrlName} ");
                }
            }
        }

        // Medium→Large: handle module-based controller names and module segment in namespaces
        if (sourceTier == "medium")
        {
            // Fix module-specific DbContext names (e.g., OrdersDbContext → OrderDbContext)
            var entityForSvc = entities.FirstOrDefault(e =>
                string.Equals(EntityToService(e), svc, StringComparison.OrdinalIgnoreCase));
            if (entityForSvc is not null)
            {
                var moduleName = EntityToModule(entityForSvc);
                var oldDbCtxName = moduleName?.EndsWith("Module") == true
                    ? $"{moduleName[..^6]}DbContext"
                    : $"{moduleName}DbContext";
                if (oldDbCtxName is not null)
                    result = result.Replace(oldDbCtxName, dbCtxName);
            }

            // Redirect Shared module usings for Exceptions/Validators (don't include ; in replacement – original ; stays)
            result = Regex.Replace(result,
                $@"using\s+{Regex.Escape(svc)}\.Application\.(\w+Module|Shared)\.(Exceptions|Validators)\b",
                "using SharedKernel.Common");

            // Strip module segment from namespaces/usings (e.g., ProductService.Application.ProductModule.DTOs -> ProductService.Application.DTOs)
            result = Regex.Replace(result,
                $@"namespace\s+{Regex.Escape(svc)}\.Application\.\w+Module\.",
                $"namespace {svc}.Application.");
            result = Regex.Replace(result,
                $@"using\s+{Regex.Escape(svc)}\.Application\.\w+Module\.",
                $"using {svc}.Application.");

            // Fix Presentation.API.Controllers namespace
            result = result.Replace(
                $"namespace {svc}.Presentation.API.Controllers",
                $"namespace {svc}.Presentation.Controllers");

            // Rename module-based controllers (e.g., CatalogController -> ProductsController)
            if (module.StartsWith("large-ctrl-"))
            {
                foreach (var entity in entities)
                {
                    var s = EntityToService(entity);
                    if (!string.Equals(s, svc, StringComparison.OrdinalIgnoreCase)) continue;
                    var entityModule = EntityToModule(entity);
                    if (entityModule is null) continue;
                    var newCtrlName = EntityToLargeControllerName(entity, $"{entity}Controller");
                    var oldNameFromModule = entityModule.EndsWith("Module")
                        ? $"{entityModule[..^6]}Controller"
                        : $"{entityModule}Controller";
                    if (result.Contains($"class {oldNameFromModule}"))
                    {
                        result = result.Replace($"class {oldNameFromModule}", $"class {newCtrlName}");
                        result = result.Replace($"public {oldNameFromModule}(", $"public {newCtrlName}(");
                        result = result.Replace($"({oldNameFromModule} ", $"({newCtrlName} ");
                    }
                }
            }
        }
        return result;
    }

    // Program.cs - rewrite entirely
    if (module == "program")
        return GenerateProgramCs(projectName, targetTier, entities, isMvc);

    // No transformation needed for Medium source staying as-is or copy
    if (module == "copy" || sourceTier == targetTier)
        return result;

    if (targetTier == "small")
        return result; // Already handled by flatten logic

    // Small -> Medium: module transformations
    string? ExtractMod(string tag) => tag switch
    {
        "shared" => "Shared",
        _ when tag?.StartsWith("app-") == true && tag.Length > 4 => tag[4..],
        _ when tag?.StartsWith("repo-") == true && tag.Length > 5 => tag[5..],
        _ when tag?.StartsWith("ctrl-") == true && tag.Length > 5 => tag[5..],
        _ => null
    };

    var modName = ExtractMod(module);

    if (modName is not null)
    {
        // Use regex to also strip any entity suffix (e.g., .Courier after DTOs)
        result = Regex.Replace(result,
            $@"using\s+{Regex.Escape(projectName)}\.Application\.DTOs(\..*)?;",
            $"using {projectName}.Application.{modName}.DTOs;");
        result = Regex.Replace(result,
            $@"using\s+{Regex.Escape(projectName)}\.Application\.Interfaces(\..*)?;",
            $"using {projectName}.Application.{modName}.Interfaces;");
        result = Regex.Replace(result,
            $@"using\s+{Regex.Escape(projectName)}\.Application\.Services(\..*)?;",
            $"using {projectName}.Application.{modName}.Services;");
    }

    if (module is not "copy" and not "program" and not "split-dbcontext" and not "ignore")
    {
        result = result.Replace(
            $"using {projectName}.Application.Exceptions;",
            $"using {projectName}.Application.Shared.Exceptions;");
        result = result.Replace(
            $"using {projectName}.Application.Validators;",
            $"using {projectName}.Application.Shared.Validators;");
    }

    if (modName is not null)
    {
        // Strip any entity suffix after DTOs/Interfaces/Services (e.g., .Courier)
        result = Regex.Replace(result,
            $@"namespace\s+{Regex.Escape(projectName)}\.Application\.(DTOs|Interfaces|Services)\b.*",
            $"namespace {projectName}.Application.{modName}.$1");
        result = Regex.Replace(result,
            $@"namespace\s+{Regex.Escape(projectName)}\.Application\.Exceptions\b.*",
            $"namespace {projectName}.Application.Shared.Exceptions");
        result = Regex.Replace(result,
            $@"namespace\s+{Regex.Escape(projectName)}\.Application\.Validators\b.*",
            $"namespace {projectName}.Application.Shared.Validators");
    }

    // Repository: replace AppDbContext with module-specific DbContext
    if (module?.StartsWith("repo-") == true && modName is not null)
    {
        var dbCtxName = modName.EndsWith("Module") ? modName[..^6] : modName;
        result = result.Replace("AppDbContext", $"{dbCtxName}DbContext");
    }

    // Controller: namespace move (no class rename — entity matches module generically)
    if (module?.StartsWith("ctrl-") == true)
    {
        result = Regex.Replace(result,
            $@"namespace\s+{Regex.Escape(projectName)}\.Presentation\.Controllers\b",
            $"namespace {projectName}.Presentation.API.Controllers");

        // Add missing cross-module usings (e.g., ShipmentController using ICourierService)
        var ctrlEntityName = module["ctrl-".Length..]; // e.g., "ShipmentModule"
        foreach (var entity in entities)
        {
            var otherModule = EntityToModule(entity);
            if (otherModule is null || string.Equals(otherModule, ctrlEntityName, StringComparison.OrdinalIgnoreCase))
                continue;

            // Check if this file references the other module's service interface
            var svcInterface = $"I{EntityToService(entity)?.Replace("Service", "")}";
            if (!result.Contains(svcInterface, StringComparison.Ordinal))
                continue;

            var usingStmt = $"using {projectName}.Application.{otherModule}.Interfaces;";
            if (!result.Contains(usingStmt))
            {
                // Add after the first using block
                var firstUsing = result.IndexOf("using ", StringComparison.Ordinal);
                if (firstUsing >= 0)
                {
                    var afterFirst = result.IndexOf(';', firstUsing);
                    if (afterFirst >= 0)
                        result = result.Insert(afterFirst + 1, $"\r\n{usingStmt}");
                }
            }
        }
    }

    return result;
}

static string GenerateProgramCs(string projectName, string targetTier, List<string> entities, bool isMvc = false)
{
    var sb = new System.Text.StringBuilder();

    sb.AppendLine("using Microsoft.EntityFrameworkCore;");

    var controllersLine = isMvc ? "builder.Services.AddControllersWithViews();" : "builder.Services.AddControllers();";

    if (targetTier == "small")
    {
        sb.AppendLine($"using {projectName}.Application.Interfaces;");
        sb.AppendLine($"using {projectName}.Application.Services;");
        sb.AppendLine($"using {projectName}.Infrastructure.Persistence;");
        sb.AppendLine($"using {projectName}.Infrastructure.Repositories;");
        sb.AppendLine();
        sb.AppendLine("var builder = WebApplication.CreateBuilder(args);");
        sb.AppendLine();
        sb.AppendLine(controllersLine);
        sb.AppendLine("builder.Services.AddOpenApi();");
        sb.AppendLine();
        sb.AppendLine($"builder.Services.AddDbContext<AppDbContext>(options =>");
        sb.AppendLine($"    options.UseInMemoryDatabase(\"{projectName}\"));");
        sb.AppendLine();
        foreach (var entity in entities)
        {
            sb.AppendLine($"builder.Services.AddScoped<I{entity}Repository, {entity}Repository>();");
            sb.AppendLine($"builder.Services.AddScoped<I{entity}Service, {entity}Service>();");
        }
    }
    else // Medium
    {
        foreach (var entity in entities)
        {
            var module = EntityToModule(entity);
            sb.AppendLine($"using {projectName}.Application.{module}.Interfaces;");
            sb.AppendLine($"using {projectName}.Application.{module}.Services;");
        }
        sb.AppendLine($"using {projectName}.Infrastructure.Persistence;");
        sb.AppendLine($"using {projectName}.Infrastructure.Repositories;");
        sb.AppendLine();
        sb.AppendLine("var builder = WebApplication.CreateBuilder(args);");
        sb.AppendLine();
        sb.AppendLine(controllersLine);
        sb.AppendLine("builder.Services.AddOpenApi();");
        sb.AppendLine();
        foreach (var entity in entities)
        {
            var dbCtxName = $"{entity}DbContext";
            sb.AppendLine($"builder.Services.AddDbContext<{dbCtxName}>(options =>");
            sb.AppendLine($"    options.UseInMemoryDatabase(\"{projectName}-{entity}\"));");
        }
        sb.AppendLine();
        foreach (var entity in entities)
        {
            sb.AppendLine($"builder.Services.AddScoped<I{entity}Repository, {entity}Repository>();");
            sb.AppendLine($"builder.Services.AddScoped<I{entity}Service, {entity}Service>();");
        }
    }

    sb.AppendLine();
    sb.AppendLine("var app = builder.Build();");
    sb.AppendLine();
    sb.AppendLine("if (app.Environment.IsDevelopment())");
    sb.AppendLine("{");
    sb.AppendLine("    app.MapOpenApi();");
    sb.AppendLine("}");
    sb.AppendLine();

    if (isMvc)
    {
        sb.AppendLine("app.UseStaticFiles();");
        sb.AppendLine();
        sb.AppendLine("app.UseRouting();");
        sb.AppendLine();
        sb.AppendLine("app.UseHttpsRedirection();");
        sb.AppendLine();
        sb.AppendLine("app.MapControllerRoute(");
        sb.AppendLine("    name: \"default\",");
        sb.AppendLine("    pattern: \"{controller=Home}/{action=Index}/{id?}\");");
    }
    else
    {
        sb.AppendLine("app.UseHttpsRedirection();");
        sb.AppendLine("app.MapControllers();");
    }

    sb.AppendLine("app.Run();");
    return sb.ToString();
}

// ========== OUTPUT GENERATION ==========

static void BuildTargetStructure(string outSrc, string projectName, string targetTier, List<string> entities)
{
    if (targetTier == "small")
    {
        var dirs = new[]
        {
            $"{projectName}.Domain/Entities",
            $"{projectName}.Domain/Enums",
            $"{projectName}.Domain/ValueObjects",
            $"{projectName}.Application/DTOs",
            $"{projectName}.Application/Interfaces",
            $"{projectName}.Application/Services",
            $"{projectName}.Application/Exceptions",
            $"{projectName}.Application/Validators",
            $"{projectName}.Infrastructure/Persistence",
            $"{projectName}.Infrastructure/Repositories",
            $"{projectName}.Infrastructure/ExternalServices",
            $"{projectName}.Presentation/Controllers",
            $"{projectName}.Presentation/Properties",
        };
        foreach (var dir in dirs)
            Directory.CreateDirectory(Path.Combine(outSrc, dir));
        return;
    }

    // Medium target structure — per entity module directories
    if (targetTier == "medium")
    {
        var medDirs = new List<string>
        {
            $"{projectName}.Domain/Entities",
            $"{projectName}.Domain/Enums",
            $"{projectName}.Domain/ValueObjects",
            $"{projectName}.Application/Shared/Exceptions",
            $"{projectName}.Application/Shared/Validators",
            $"{projectName}.Infrastructure/Persistence",
            $"{projectName}.Infrastructure/Repositories",
            $"{projectName}.Infrastructure/ExternalServices",
            $"{projectName}.Presentation/API/Controllers",
            $"{projectName}.Presentation/Models",
            $"{projectName}.Presentation/Views",
            $"{projectName}.Presentation/Properties",
        };
        foreach (var entity in entities)
        {
            var module = EntityToModule(entity);
            medDirs.Add($"{projectName}.Application/{module}/DTOs");
            medDirs.Add($"{projectName}.Application/{module}/Interfaces");
            medDirs.Add($"{projectName}.Application/{module}/Services");
        }
        foreach (var dir in medDirs)
            Directory.CreateDirectory(Path.Combine(outSrc, dir));
        return;
    }

    // Large target structure
    var largeDirs = new List<string>
    {
        "SharedKernel/Common",
        "SharedKernel/Events",
        "SharedKernel/Interfaces",
        "Gateways/API Gateway/Controllers",
        "Gateways/API Gateway/Middleware",
        "Gateways/API Gateway/Properties",
        "Gateways/Auth Service/Controllers",
        "Gateways/Auth Service/Services",
        "Gateways/Auth Service/Persistence",
        "Gateways/Auth Service/Properties",
    };

    foreach (var entity in entities)
    {
        var svc = EntityToService(entity);
        largeDirs.AddRange(new[]
        {
            $"Services/{svc}/{svc}.Domain/Entities",
            $"Services/{svc}/{svc}.Domain/Enums",
            $"Services/{svc}/{svc}.Domain/ValueObjects",
            $"Services/{svc}/{svc}.Domain/Exceptions",
            $"Services/{svc}/{svc}.Domain/Interfaces",
            $"Services/{svc}/{svc}.Domain/Events",
            $"Services/{svc}/{svc}.Application/DTOs",
            $"Services/{svc}/{svc}.Application/Commands",
            $"Services/{svc}/{svc}.Application/Queries",
            $"Services/{svc}/{svc}.Application/Interfaces",
            $"Services/{svc}/{svc}.Application/Services",
            $"Services/{svc}/{svc}.Infrastructure/Persistence",
            $"Services/{svc}/{svc}.Infrastructure/Persistence/Configurations",
            $"Services/{svc}/{svc}.Infrastructure/Repositories",
            $"Services/{svc}/{svc}.Infrastructure/Services",
            $"Services/{svc}/{svc}.Presentation/Controllers",
            $"Services/{svc}/{svc}.Presentation/DTOs",
            $"Services/{svc}/{svc}.Presentation/Filters",
            $"Services/{svc}/{svc}.Presentation/Middleware",
            $"Services/{svc}/{svc}.Presentation/Properties",
        });
    }

    foreach (var dir in largeDirs)
        Directory.CreateDirectory(Path.Combine(outSrc, dir));
}

static void WriteTierFiles(string outSrc, string projectName, string targetTier, List<string> entities, bool isMvc = false)
{
    if (targetTier == "small")
    {
        // Re-merge AppDbContext from split contexts (if they exist)
        WriteSmallDbContext(outSrc, projectName, entities);
        return;
    }

    if (targetTier == "large")
    {
        WriteLargeTierFiles(outSrc, projectName, entities);
        return;
    }

    // Medium: write split DbContexts + ValueObject
    foreach (var entity in entities)
        WriteEntityDbContext(outSrc, projectName, entity);
    WriteValueObject(outSrc, projectName);

    // MVC-only files — Views, ViewImports, ViewStart
    if (isMvc)
    {
        WriteProductViewModel(outSrc, projectName);
        WriteIndexView(outSrc, projectName);
        WriteViewImports(outSrc, projectName);
        WriteViewStart(outSrc, projectName);
    }
}

static void WriteLargeTierFiles(string outSrc, string projectName, List<string> entities)
{
    // Write per-service DbContexts
    foreach (var entity in entities)
    {
        var svc = EntityToService(entity);
        WriteLargeDbContext(outSrc, svc, entity);
    }

    // Write SharedKernel files
    WriteSharedKernelResult(outSrc);
    WriteSharedKernelIntegrationEvent(outSrc);
    WriteSharedKernelRepository(outSrc);

    // Write gateway files
    WriteApiGatewayProgram(outSrc);
    WriteApiGatewayController(outSrc);
    WriteApiGatewayMiddleware(outSrc);
    WriteAuthServiceProgram(outSrc);
    WriteAuthController(outSrc);
    WriteTokenService(outSrc);
    WriteAuthDbContext(outSrc);

    // Write per-service infrastructure files
    foreach (var entity in entities)
    {
        var svc = EntityToService(entity);
        WriteLargeServiceProgram(outSrc, svc, entity, entities);
        WriteProductConfiguration(outSrc, svc, entity);
        WriteDomainEvent(outSrc, svc, entity);
        WriteDomainException(outSrc, svc);
        WriteAggregateRoot(outSrc, svc);
        WriteMoneyValueObject(outSrc, svc);
        WriteCreateCommand(outSrc, svc, entity);
        WriteGetByIdQuery(outSrc, svc, entity);
        WriteCreateRequestDto(outSrc, svc, entity);
        WriteValidationFilter(outSrc, svc);
        WriteExceptionMiddleware(outSrc, svc);
    }
}

static void WriteLargeDbContext(string outSrc, string svc, string entity)
{
    var path = Path.Combine(outSrc, $"Services/{svc}/{svc}.Infrastructure", "Persistence", $"{svc.Replace("Service", "")}DbContext.cs");
    if (File.Exists(path)) return;

    var dbCtxName = $"{svc.Replace("Service", "")}DbContext";
    var content = $$"""
using Microsoft.EntityFrameworkCore;
using {{svc}}.Domain.Entities;

namespace {{svc}}.Infrastructure.Persistence
{
    public class {{dbCtxName}} : DbContext
    {
        public {{dbCtxName}}(DbContextOptions<{{dbCtxName}}> options) : base(options) { }

        public DbSet<{{entity}}> {{ToPlural(entity)}} => Set<{{entity}}>();
    }
}
""";
    File.WriteAllText(path, content);
}

static void WriteSharedKernelResult(string outSrc)
{
    var path = Path.Combine(outSrc, "SharedKernel/Common", "Result.cs");
    if (File.Exists(path)) return;

    var content = """
namespace SharedKernel.Common
{
    public class Result
    {
        public bool IsSuccess { get; }
        public bool IsFailure => !IsSuccess;
        public string Error { get; }

        protected Result(bool isSuccess, string error)
        {
            IsSuccess = isSuccess;
            Error = error;
        }

        public static Result Success() => new(true, null!);
        public static Result Failure(string error) => new(false, error);
        public static Result<T> Success<T>(T value) => new(value, true, null!);
        public static Result<T> Failure<T>(string error) => new(default, false, error);
    }

    public class Result<T> : Result
    {
        public T? Value { get; }

        public Result(T? value, bool isSuccess, string error) : base(isSuccess, error)
        {
            Value = value;
        }
    }
}
""";
    File.WriteAllText(path, content);
}

static void WriteSharedKernelIntegrationEvent(string outSrc)
{
    var path = Path.Combine(outSrc, "SharedKernel/Events", "IntegrationEvent.cs");
    if (File.Exists(path)) return;

    var content = """
namespace SharedKernel.Events
{
    public abstract class IntegrationEvent
    {
        public Guid Id { get; } = Guid.NewGuid();
        public DateTime OccurredOn { get; } = DateTime.UtcNow;
    }
}
""";
    File.WriteAllText(path, content);
}

static void WriteSharedKernelRepository(string outSrc)
{
    var path = Path.Combine(outSrc, "SharedKernel/Interfaces", "IRepository.cs");
    if (File.Exists(path)) return;

    var content = """
namespace SharedKernel.Interfaces
{
    public interface IRepository<T> where T : class
    {
        Task<T?> GetByIdAsync(int id);
        Task<IReadOnlyList<T>> ListAsync();
        Task AddAsync(T entity);
        Task UpdateAsync(T entity);
        Task DeleteAsync(T entity);
    }
}
""";
    File.WriteAllText(path, content);
}

static void WriteApiGatewayProgram(string outSrc)
{
    var path = Path.Combine(outSrc, "Gateways/API Gateway", "Program.cs");
    if (File.Exists(path)) return;

    var content = """
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();
builder.Services.AddControllers();
var app = builder.Build();
if (app.Environment.IsDevelopment()) app.MapOpenApi();
app.UseHttpsRedirection();
app.MapControllers();
app.Run();
""";
    File.WriteAllText(path, content);
}

static void WriteApiGatewayController(string outSrc)
{
    var path = Path.Combine(outSrc, "Gateways/API Gateway/Controllers", "GatewayController.cs");
    if (File.Exists(path)) return;

    var content = """
using Microsoft.AspNetCore.Mvc;

namespace ApiGateway.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class GatewayController : ControllerBase
    {
    }
}
""";
    File.WriteAllText(path, content);
}

static void WriteApiGatewayMiddleware(string outSrc)
{
    var path = Path.Combine(outSrc, "Gateways/API Gateway/Middleware", "RoutingMiddleware.cs");
    if (File.Exists(path)) return;

    var content = """
namespace ApiGateway.Middleware
{
    public class RoutingMiddleware
    {
        private readonly RequestDelegate _next;
        public RoutingMiddleware(RequestDelegate next) => _next = next;
        public async Task InvokeAsync(HttpContext context) => await _next(context);
    }
}
""";
    File.WriteAllText(path, content);
}

static void WriteAuthServiceProgram(string outSrc)
{
    var path = Path.Combine(outSrc, "Gateways/Auth Service", "Program.cs");
    if (File.Exists(path)) return;

    var content = """
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();
builder.Services.AddControllers();
var app = builder.Build();
if (app.Environment.IsDevelopment()) app.MapOpenApi();
app.UseHttpsRedirection();
app.MapControllers();
app.Run();
""";
    File.WriteAllText(path, content);
}

static void WriteAuthController(string outSrc)
{
    var path = Path.Combine(outSrc, "Gateways/Auth Service/Controllers", "AuthController.cs");
    if (File.Exists(path)) return;

    var content = """
using Microsoft.AspNetCore.Mvc;

namespace AuthService.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
    }
}
""";
    File.WriteAllText(path, content);
}

static void WriteTokenService(string outSrc)
{
    var path = Path.Combine(outSrc, "Gateways/Auth Service/Services", "TokenService.cs");
    if (File.Exists(path)) return;

    var content = """
namespace AuthService.Services
{
    public class TokenService
    {
    }
}
""";
    File.WriteAllText(path, content);
}

static void WriteAuthDbContext(string outSrc)
{
    var path = Path.Combine(outSrc, "Gateways/Auth Service/Persistence", "AuthDbContext.cs");
    if (File.Exists(path)) return;

    var content = """
using Microsoft.EntityFrameworkCore;

namespace AuthService.Persistence
{
    public class AuthDbContext : DbContext
    {
        public AuthDbContext(DbContextOptions<AuthDbContext> options) : base(options) { }
    }
}
""";
    File.WriteAllText(path, content);
}

static void WriteLargeServiceProgram(string outSrc, string svc, string entity, List<string> entities)
{
    var path = Path.Combine(outSrc, $"Services/{svc}/{svc}.Presentation", "Program.cs");
    if (File.Exists(path)) return;

    var dbCtxName = $"{svc.Replace("Service", "")}DbContext";
    var repoInterface = $"I{entity}Repository";
    var repoClass = $"{entity}Repository";
    var svcInterface = $"I{entity}Service";
    var svcClass = $"{entity}Service";
    var dbName = $"ZMA-{svc.Replace("Service", "")}";

    var content = $$"""
using {{svc}}.Application.Interfaces;
using {{svc}}.Application.Services;
using {{svc}}.Infrastructure.Persistence;
using {{svc}}.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();
builder.Services.AddControllers();
builder.Services.AddDbContext<{{dbCtxName}}>(options =>
    options.UseInMemoryDatabase("{{dbName}}"));
builder.Services.AddScoped<{{repoInterface}}, {{repoClass}}>();
builder.Services.AddScoped<{{svcInterface}}, {{svc}}.Application.Services.{{svcClass}}>();
var app = builder.Build();
if (app.Environment.IsDevelopment()) app.MapOpenApi();
app.UseHttpsRedirection();
app.MapControllers();
app.Run();
""";
    File.WriteAllText(path, content);
}

static void WriteProductConfiguration(string outSrc, string svc, string entity)
{
    var path = Path.Combine(outSrc, $"Services/{svc}/{svc}.Infrastructure/Persistence/Configurations", $"{entity}Configuration.cs");
    if (File.Exists(path)) return;

    var content = $$"""
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using {{svc}}.Domain.Entities;

namespace {{svc}}.Infrastructure.Persistence.Configurations
{
    public class {{entity}}Configuration : IEntityTypeConfiguration<{{entity}}>
    {
        public void Configure(EntityTypeBuilder<{{entity}}> builder)
        {
            builder.HasKey(e => e.Id);
        }
    }
}
""";
    File.WriteAllText(path, content);
}

static void WriteDomainEvent(string outSrc, string svc, string entity)
{
    var path = Path.Combine(outSrc, $"Services/{svc}/{svc}.Domain/Events", $"{entity}CreatedEvent.cs");
    if (File.Exists(path)) return;

    var content = $$"""
using SharedKernel.Events;

namespace {{svc}}.Domain.Events
{
    public class {{entity}}CreatedEvent : IntegrationEvent
    {
        public int {{entity}}Id { get; }
        public string {{entity}}Name { get; }

        public {{entity}}CreatedEvent(int id, string name)
        {
            {{entity}}Id = id;
            {{entity}}Name = name;
        }
    }
}
""";
    File.WriteAllText(path, content);
}

static void WriteDomainException(string outSrc, string svc)
{
    var path = Path.Combine(outSrc, $"Services/{svc}/{svc}.Domain/Exceptions", "DomainException.cs");
    if (File.Exists(path)) return;

    var content = $$"""
namespace {{svc}}.Domain.Exceptions
{
    public class DomainException : Exception
    {
        public DomainException(string message) : base(message) { }
    }
}
""";
    File.WriteAllText(path, content);
}

static void WriteAggregateRoot(string outSrc, string svc)
{
    var path = Path.Combine(outSrc, $"Services/{svc}/{svc}.Domain/Interfaces", "IAggregateRoot.cs");
    if (File.Exists(path)) return;

    var content = $$"""
namespace {{svc}}.Domain.Interfaces
{
    public interface IAggregateRoot { }
}
""";
    File.WriteAllText(path, content);
}

static void WriteMoneyValueObject(string outSrc, string svc)
{
    var path = Path.Combine(outSrc, $"Services/{svc}/{svc}.Domain/ValueObjects", "Money.cs");
    if (File.Exists(path)) return;

    var content = $$"""
namespace {{svc}}.Domain.ValueObjects
{
    public class Money
    {
        public decimal Amount { get; }
        public string Currency { get; }

        public Money(decimal amount, string currency)
        {
            Amount = amount;
            Currency = currency;
        }

        public override string ToString() => $"{Currency} {Amount:F2}";
    }
}
""";
    File.WriteAllText(path, content);
}

static void WriteCreateCommand(string outSrc, string svc, string entity)
{
    var path = Path.Combine(outSrc, $"Services/{svc}/{svc}.Application/Commands", $"Create{entity}Command.cs");
    if (File.Exists(path)) return;

    var content = $$"""
namespace {{svc}}.Application.Commands
{
    public class Create{{entity}}Command
    {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public decimal Price { get; set; }
        public int StockQuantity { get; set; }
        public string Category { get; set; } = string.Empty;
    }
}
""";
    File.WriteAllText(path, content);
}

static void WriteGetByIdQuery(string outSrc, string svc, string entity)
{
    var path = Path.Combine(outSrc, $"Services/{svc}/{svc}.Application/Queries", $"Get{entity}ByIdQuery.cs");
    if (File.Exists(path)) return;

    var content = $$"""
namespace {{svc}}.Application.Queries
{
    public class Get{{entity}}ByIdQuery
    {
        public int Id { get; set; }
    }
}
""";
    File.WriteAllText(path, content);
}

static void WriteCreateRequestDto(string outSrc, string svc, string entity)
{
    var path = Path.Combine(outSrc, $"Services/{svc}/{svc}.Presentation/DTOs", $"{entity}CreateRequest.cs");
    if (File.Exists(path)) return;

    var content = $$"""
namespace {{svc}}.Presentation.DTOs
{
    public class {{entity}}CreateRequest
    {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public decimal Price { get; set; }
        public int StockQuantity { get; set; }
        public string Category { get; set; } = string.Empty;
    }
}
""";
    File.WriteAllText(path, content);
}

static void WriteValidationFilter(string outSrc, string svc)
{
    var path = Path.Combine(outSrc, $"Services/{svc}/{svc}.Presentation/Filters", "ValidationFilter.cs");
    if (File.Exists(path)) return;

    var content = $$"""
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace {{svc}}.Presentation.Filters
{
    public class ValidationFilter : IActionFilter
    {
        public void OnActionExecuting(ActionExecutingContext context)
        {
            if (!context.ModelState.IsValid)
                context.Result = new BadRequestObjectResult(context.ModelState);
        }

        public void OnActionExecuted(ActionExecutedContext context) { }
    }
}
""";
    File.WriteAllText(path, content);
}

static void WriteExceptionMiddleware(string outSrc, string svc)
{
    var path = Path.Combine(outSrc, $"Services/{svc}/{svc}.Presentation/Middleware", "ExceptionHandlingMiddleware.cs");
    if (File.Exists(path)) return;

    var content = $$"""
namespace {{svc}}.Presentation.Middleware
{
    public class ExceptionHandlingMiddleware
    {
        private readonly RequestDelegate _next;
        public ExceptionHandlingMiddleware(RequestDelegate next) => _next = next;
        public async Task InvokeAsync(HttpContext context) => await _next(context);
    }
}
""";
    File.WriteAllText(path, content);
}

static void WriteSmallDbContext(string outSrc, string projectName, List<string> entities)
{
    var path = Path.Combine(outSrc, $"{projectName}.Infrastructure", "Persistence", "AppDbContext.cs");
    if (File.Exists(path)) return;

    var sb = new System.Text.StringBuilder();
    sb.AppendLine("using Microsoft.EntityFrameworkCore;");
    sb.AppendLine($"using {projectName}.Domain.Entities;");
    sb.AppendLine();
    sb.AppendLine($"namespace {projectName}.Infrastructure.Persistence");
    sb.AppendLine("{");
    sb.AppendLine("    public class AppDbContext : DbContext");
    sb.AppendLine("    {");
    sb.AppendLine("        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }");
    sb.AppendLine();
    foreach (var entity in entities)
    {
        var plural = ToPlural(entity);
        sb.AppendLine($"        public DbSet<{entity}> {plural} => Set<{entity}>();");
    }
    sb.AppendLine();
    sb.AppendLine("        protected override void OnModelCreating(ModelBuilder modelBuilder)");
    sb.AppendLine("        {");
    foreach (var entity in entities)
    {
        sb.AppendLine($"            modelBuilder.Entity<{entity}>(e =>");
        sb.AppendLine("            {");
        sb.AppendLine("                e.HasKey(x => x.Id);");
        sb.AppendLine("            });");
        sb.AppendLine();
    }
    sb.AppendLine("        }");
    sb.AppendLine("    }");
    sb.AppendLine("}");
    File.WriteAllText(path, sb.ToString());
}

static void WriteEntityDbContext(string outSrc, string projectName, string entity)
{
    var dbCtxName = $"{entity}DbContext";
    var path = Path.Combine(outSrc, $"{projectName}.Infrastructure", "Persistence", $"{dbCtxName}.cs");
    if (File.Exists(path)) return;

    var content = $$"""
using Microsoft.EntityFrameworkCore;
using {{projectName}}.Domain.Entities;

namespace {{projectName}}.Infrastructure.Persistence
{
    public class {{dbCtxName}} : DbContext
    {
        public {{dbCtxName}}(DbContextOptions<{{dbCtxName}}> options) : base(options) { }

        public DbSet<{{entity}}> {{ToPlural(entity)}} => Set<{{entity}}>();
    }
}
""";
    File.WriteAllText(path, content);
}

static void WriteValueObject(string outSrc, string projectName)
{
    var path = Path.Combine(outSrc, $"{projectName}.Domain", "ValueObjects", "Address.cs");
    if (File.Exists(path)) return;

    var address = $$"""
namespace {{projectName}}.Domain.ValueObjects
{
    public class Address
    {
        public string Street { get; private set; }
        public string City { get; private set; }
        public string State { get; private set; }
        public string ZipCode { get; private set; }
        public string Country { get; private set; }

        public Address(string street, string city, string state, string zipCode, string country)
        {
            Street = street; City = city; State = state;
            ZipCode = zipCode; Country = country;
        }

        public override string ToString() => $"{Street}, {City}, {State} {ZipCode}, {Country}";
    }
}
""";
    File.WriteAllText(path, address);
}

static void WriteProductViewModel(string outSrc, string projectName)
{
    var presDir = Path.Combine(outSrc, $"{projectName}.Presentation", "Models");
    var path = Path.Combine(presDir, "ProductViewModel.cs");
    if (File.Exists(path)) return;

    var productVm = $$"""
namespace {{projectName}}.Presentation.Models
{
    public class ProductViewModel
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public decimal Price { get; set; }
        public int StockQuantity { get; set; }
        public string Category { get; set; } = string.Empty;
    }
}
""";
    File.WriteAllText(path, productVm);
}

static void WriteIndexView(string outSrc, string projectName)
{
    var presDir = Path.Combine(outSrc, $"{projectName}.Presentation", "Views");
    var path = Path.Combine(presDir, "Index.cshtml");
    if (File.Exists(path)) return;

    var indexHtml = $$"""
@{
    ViewData["Title"] = "Home";
}
<h1>Welcome to {{projectName}}</h1>
<p>Medium-tier MVC application.</p>
""";
    File.WriteAllText(path, indexHtml);
}

static void WriteViewImports(string outSrc, string projectName)
{
    var viewsDir = Path.Combine(outSrc, $"{projectName}.Presentation", "Views");
    var path = Path.Combine(viewsDir, "_ViewImports.cshtml");
    if (File.Exists(path)) return;

    var imports = $$"""
@using {{projectName}}.Presentation.Models
@addTagHelper *, Microsoft.AspNetCore.Mvc.TagHelpers
""";
    File.WriteAllText(path, imports);
}

static void WriteViewStart(string outSrc, string projectName)
{
    var viewsDir = Path.Combine(outSrc, $"{projectName}.Presentation", "Views");
    var path = Path.Combine(viewsDir, "_ViewStart.cshtml");
    if (File.Exists(path)) return;

    var start = """
@{
    Layout = "_Layout";
}
""";
    File.WriteAllText(path, start);
}

static void WriteProjectFiles(string outSrc, string projectName, string targetTier)
{
    if (targetTier == "large")
    {
        WriteLargeProjectFiles(outSrc, projectName);
        return;
    }

    // Small/Medium: Domain.csproj
    var domainCsproj = $$"""
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
""";
    File.WriteAllText(Path.Combine(outSrc, $"{projectName}.Domain", $"{projectName}.Domain.csproj"), domainCsproj);

    // Application.csproj
    var appCsproj = $$"""
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\{{projectName}}.Domain\{{projectName}}.Domain.csproj" />
  </ItemGroup>
</Project>
""";
    File.WriteAllText(Path.Combine(outSrc, $"{projectName}.Application", $"{projectName}.Application.csproj"), appCsproj);

    // Infrastructure.csproj
    var infraCsproj = $$"""
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.EntityFrameworkCore" Version="9.0.0" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.InMemory" Version="9.0.0" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Relational" Version="9.0.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\{{projectName}}.Application\{{projectName}}.Application.csproj" />
  </ItemGroup>
</Project>
""";
    File.WriteAllText(Path.Combine(outSrc, $"{projectName}.Infrastructure", $"{projectName}.Infrastructure.csproj"), infraCsproj);

    // Presentation.csproj
    var presCsproj = $$"""
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.OpenApi" Version="9.0.7" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.InMemory" Version="9.0.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\{{projectName}}.Application\{{projectName}}.Application.csproj" />
    <ProjectReference Include="..\{{projectName}}.Infrastructure\{{projectName}}.Infrastructure.csproj" />
  </ItemGroup>
</Project>
""";
    File.WriteAllText(Path.Combine(outSrc, $"{projectName}.Presentation", $"{projectName}.Presentation.csproj"), presCsproj);
}

static void WriteLargeProjectFiles(string outSrc, string projectName)
{
    // Get all service directories
    var svcDirs = new List<string>();
    var servicesPath = Path.Combine(outSrc, "Services");
    if (Directory.Exists(servicesPath))
    {
        foreach (var d in Directory.GetDirectories(servicesPath))
            svcDirs.Add(Path.GetFileName(d));
    }

    // SharedKernel.csproj
    var sharedCsproj = """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
""";
    File.WriteAllText(Path.Combine(outSrc, "SharedKernel", "SharedKernel.csproj"), sharedCsproj);

    // Per-service project files
    foreach (var svc in svcDirs)
    {
        var svcPath = $"Services/{svc}";

        // Domain.csproj
        var domainCsproj = $$"""
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\..\SharedKernel\SharedKernel.csproj" />
  </ItemGroup>
</Project>
""";
        File.WriteAllText(Path.Combine(outSrc, svcPath, $"{svc}.Domain", $"{svc}.Domain.csproj"), domainCsproj);

        // Application.csproj
        var appCsproj = $$"""
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\..\SharedKernel\SharedKernel.csproj" />
    <ProjectReference Include="..\{{svc}}.Domain\{{svc}}.Domain.csproj" />
  </ItemGroup>
</Project>
""";
        File.WriteAllText(Path.Combine(outSrc, svcPath, $"{svc}.Application", $"{svc}.Application.csproj"), appCsproj);

        // Infrastructure.csproj
        var infraCsproj = $$"""
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.EntityFrameworkCore" Version="9.0.0" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.InMemory" Version="9.0.0" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Relational" Version="9.0.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\..\SharedKernel\SharedKernel.csproj" />
    <ProjectReference Include="..\{{svc}}.Application\{{svc}}.Application.csproj" />
  </ItemGroup>
</Project>
""";
        File.WriteAllText(Path.Combine(outSrc, svcPath, $"{svc}.Infrastructure", $"{svc}.Infrastructure.csproj"), infraCsproj);

        // Presentation.csproj
        var presCsproj = $$"""
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.OpenApi" Version="9.0.7" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.InMemory" Version="9.0.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\..\SharedKernel\SharedKernel.csproj" />
    <ProjectReference Include="..\{{svc}}.Application\{{svc}}.Application.csproj" />
    <ProjectReference Include="..\{{svc}}.Infrastructure\{{svc}}.Infrastructure.csproj" />
  </ItemGroup>
</Project>
""";
        File.WriteAllText(Path.Combine(outSrc, svcPath, $"{svc}.Presentation", $"{svc}.Presentation.csproj"), presCsproj);
    }

    // Add cross-service project references by scanning entity files for cross-entity references
    BuildCrossServiceReferences(outSrc, svcDirs);

    // Gateway project files
    var gwApiCsproj = """
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.OpenApi" Version="9.0.7" />
  </ItemGroup>
</Project>
""";
    File.WriteAllText(Path.Combine(outSrc, "Gateways/API Gateway", "ApiGateway.csproj"), gwApiCsproj);

    var gwAuthCsproj = """
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.OpenApi" Version="9.0.7" />
    <PackageReference Include="Microsoft.EntityFrameworkCore" Version="9.0.0" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.InMemory" Version="9.0.0" />
  </ItemGroup>
</Project>
""";
    File.WriteAllText(Path.Combine(outSrc, "Gateways/Auth Service", "AuthService.csproj"), gwAuthCsproj);
}

static void BuildCrossServiceReferences(string outSrc, List<string> svcDirs)
{
    // Build entity-to-service mapping from directory structure
    var entityToSvc = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (var svc in svcDirs)
    {
        var entityDir = Path.Combine(outSrc, $"Services/{svc}/{svc}.Domain/Entities");
        if (!Directory.Exists(entityDir)) continue;
        foreach (var ef in Directory.GetFiles(entityDir, "*.cs"))
        {
            var entityName = Path.GetFileNameWithoutExtension(ef);
            entityToSvc[entityName] = svc;
        }
    }

    // Check each entity file for cross-service type references
    foreach (var svc in svcDirs)
    {
        var entityDir = Path.Combine(outSrc, $"Services/{svc}/{svc}.Domain/Entities");
        if (!Directory.Exists(entityDir)) continue;

        foreach (var entityFile in Directory.GetFiles(entityDir, "*.cs"))
        {
            var content = File.ReadAllText(entityFile);
            foreach (var kv in entityToSvc)
            {
                var otherEntity = kv.Key;
                var otherSvc = kv.Value;
                if (string.Equals(otherSvc, svc, StringComparison.OrdinalIgnoreCase)) continue;

                var fileName = Path.GetFileNameWithoutExtension(entityFile);
                if (string.Equals(fileName, otherEntity, StringComparison.OrdinalIgnoreCase)) continue;

                    if (content.Contains($" {otherEntity}", StringComparison.Ordinal)
                     || content.Contains($"({otherEntity}", StringComparison.Ordinal)
                     || content.Contains($"<{otherEntity}", StringComparison.Ordinal)
                     || content.Contains($".{otherEntity}", StringComparison.Ordinal)
                     || content.Contains($" {otherEntity}?", StringComparison.Ordinal))
                    {
                        var domainCsprojPath = Path.Combine(outSrc, $"Services/{svc}/{svc}.Domain/{svc}.Domain.csproj");
                        if (File.Exists(domainCsprojPath))
                        {
                            var csproj = File.ReadAllText(domainCsprojPath);
                            var refLine = $"    <ProjectReference Include=\"..\\..\\{otherSvc}\\{otherSvc}.Domain\\{otherSvc}.Domain.csproj\" />";
                            if (!csproj.Contains(refLine))
                            {
                                csproj = csproj.Replace("</Project>", $"  <ItemGroup>\n{refLine}\n  </ItemGroup>\n</Project>");
                                File.WriteAllText(domainCsprojPath, csproj);
                            }
                        }
                    }
            }
        }
    }
}

static void WriteSolutionFile(string outSrc, string projectName)
{
    // Detect if Large tier (has Services/ directory)
    var isLarge = Directory.Exists(Path.Combine(outSrc, "Services"));

    if (!isLarge)
    {
        WriteSmallMediumSolution(outSrc, projectName);
        return;
    }

    WriteLargeSolution(outSrc, projectName);
}

static void WriteSmallMediumSolution(string outSrc, string projectName)
{
    var gApp = "{" + Guid.NewGuid().ToString("D").ToUpperInvariant() + "}";
    var gInfra = "{" + Guid.NewGuid().ToString("D").ToUpperInvariant() + "}";
    var gDom = "{" + Guid.NewGuid().ToString("D").ToUpperInvariant() + "}";
    var gPres = "{" + Guid.NewGuid().ToString("D").ToUpperInvariant() + "}";
    var gItems = "{" + Guid.NewGuid().ToString("D").ToUpperInvariant() + "}";
    var gSln = "{" + Guid.NewGuid().ToString("D").ToUpperInvariant() + "}";

    var sln = $@"
Microsoft Visual Studio Solution File, Format Version 12.00
# Visual Studio Version 17
VisualStudioVersion = 17.0.31903.59
MinimumVisualStudioVersion = 10.0.40219.1
Project(""{{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}}"") = ""{projectName}.Application"", ""{projectName}.Application\{projectName}.Application.csproj"", ""{gApp}""
EndProject
Project(""{{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}}"") = ""{projectName}.Infrastructure"", ""{projectName}.Infrastructure\{projectName}.Infrastructure.csproj"", ""{gInfra}""
EndProject
Project(""{{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}}"") = ""{projectName}.Domain"", ""{projectName}.Domain\{projectName}.Domain.csproj"", ""{gDom}""
EndProject
Project(""{{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}}"") = ""{projectName}.Presentation"", ""{projectName}.Presentation\{projectName}.Presentation.csproj"", ""{gPres}""
EndProject
Project(""{{2150E333-8FDC-42A3-9474-1A3956D46DE8}}"") = ""Solution Items"", ""Solution Items"", ""{gItems}""
	ProjectSection(SolutionItems) = preProject
		Readme.md = Readme.md
	EndProjectSection
EndProject
Global
	GlobalSection(SolutionConfigurationPlatforms) = preSolution
		Debug|Any CPU = Debug|Any CPU
		Debug|x64 = Debug|x64
		Debug|x86 = Debug|x86
		Release|Any CPU = Release|Any CPU
		Release|x64 = Release|x64
		Release|x86 = Release|x86
	EndGlobalSection
	GlobalSection(ProjectConfigurationPlatforms) = postSolution
		{gApp}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
		{gApp}.Debug|Any CPU.Build.0 = Debug|Any CPU
		{gApp}.Debug|x64.ActiveCfg = Debug|Any CPU
		{gApp}.Debug|x64.Build.0 = Debug|Any CPU
		{gApp}.Debug|x86.ActiveCfg = Debug|Any CPU
		{gApp}.Debug|x86.Build.0 = Debug|Any CPU
		{gApp}.Release|Any CPU.ActiveCfg = Release|Any CPU
		{gApp}.Release|Any CPU.Build.0 = Release|Any CPU
		{gApp}.Release|x64.ActiveCfg = Release|Any CPU
		{gApp}.Release|x64.Build.0 = Release|Any CPU
		{gApp}.Release|x86.ActiveCfg = Release|Any CPU
		{gApp}.Release|x86.Build.0 = Release|Any CPU
		{gInfra}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
		{gInfra}.Debug|Any CPU.Build.0 = Debug|Any CPU
		{gInfra}.Debug|x64.ActiveCfg = Debug|Any CPU
		{gInfra}.Debug|x64.Build.0 = Debug|Any CPU
		{gInfra}.Debug|x86.ActiveCfg = Debug|Any CPU
		{gInfra}.Debug|x86.Build.0 = Debug|Any CPU
		{gInfra}.Release|Any CPU.ActiveCfg = Release|Any CPU
		{gInfra}.Release|Any CPU.Build.0 = Release|Any CPU
		{gInfra}.Release|x64.ActiveCfg = Release|Any CPU
		{gInfra}.Release|x64.Build.0 = Release|Any CPU
		{gInfra}.Release|x86.ActiveCfg = Release|Any CPU
		{gInfra}.Release|x86.Build.0 = Release|Any CPU
		{gDom}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
		{gDom}.Debug|Any CPU.Build.0 = Debug|Any CPU
		{gDom}.Debug|x64.ActiveCfg = Debug|Any CPU
		{gDom}.Debug|x64.Build.0 = Debug|Any CPU
		{gDom}.Debug|x86.ActiveCfg = Debug|Any CPU
		{gDom}.Debug|x86.Build.0 = Debug|Any CPU
		{gDom}.Release|Any CPU.ActiveCfg = Release|Any CPU
		{gDom}.Release|Any CPU.Build.0 = Release|Any CPU
		{gDom}.Release|x64.ActiveCfg = Release|Any CPU
		{gDom}.Release|x64.Build.0 = Release|Any CPU
		{gDom}.Release|x86.ActiveCfg = Release|Any CPU
		{gDom}.Release|x86.Build.0 = Release|Any CPU
		{gPres}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
		{gPres}.Debug|Any CPU.Build.0 = Debug|Any CPU
		{gPres}.Debug|x64.ActiveCfg = Debug|Any CPU
		{gPres}.Debug|x64.Build.0 = Debug|Any CPU
		{gPres}.Debug|x86.ActiveCfg = Debug|Any CPU
		{gPres}.Debug|x86.Build.0 = Debug|Any CPU
		{gPres}.Release|Any CPU.ActiveCfg = Release|Any CPU
		{gPres}.Release|Any CPU.Build.0 = Release|Any CPU
		{gPres}.Release|x64.ActiveCfg = Release|Any CPU
		{gPres}.Release|x64.Build.0 = Release|Any CPU
		{gPres}.Release|x86.ActiveCfg = Release|Any CPU
		{gPres}.Release|x86.Build.0 = Release|Any CPU
	EndGlobalSection
	GlobalSection(SolutionProperties) = preSolution
		HideSolutionNode = FALSE
	EndGlobalSection
	GlobalSection(ExtensibilityGlobals) = postSolution
		SolutionGuid = {gSln}
	EndGlobalSection
EndGlobal
";
    File.WriteAllText(Path.Combine(outSrc, $"{projectName}.sln"), sln.TrimStart());
}

static void WriteLargeSolution(string outSrc, string projectName)
{
    var sb = new System.Text.StringBuilder();
    sb.AppendLine(@"
Microsoft Visual Studio Solution File, Format Version 12.00
# Visual Studio Version 17
VisualStudioVersion = 17.0.31903.59
MinimumVisualStudioVersion = 10.0.40219.1");

    var projects = new List<(string name, string path, string folder)>();
    var folders = new List<(string name, string displayName)>();

    // SharedKernel
    projects.Add(("SharedKernel", "SharedKernel\\SharedKernel.csproj", "SharedKernel"));
    folders.Add(("SharedKernel", "SharedKernel"));

    // Discover services
    var svcDir = Path.Combine(outSrc, "Services");
    if (Directory.Exists(svcDir))
    {
        folders.Add(("Services", "Services"));
        foreach (var d in Directory.GetDirectories(svcDir))
        {
            var svc = Path.GetFileName(d);
            projects.Add(($"{svc}.Domain", $"Services\\{svc}\\{svc}.Domain\\{svc}.Domain.csproj", "Services"));
            projects.Add(($"{svc}.Application", $"Services\\{svc}\\{svc}.Application\\{svc}.Application.csproj", "Services"));
            projects.Add(($"{svc}.Infrastructure", $"Services\\{svc}\\{svc}.Infrastructure\\{svc}.Infrastructure.csproj", "Services"));
            projects.Add(($"{svc}.Presentation", $"Services\\{svc}\\{svc}.Presentation\\{svc}.Presentation.csproj", "Services"));
        }
    }

    // Gateways
    folders.Add(("Gateways", "Gateways"));
    projects.Add(("ApiGateway", "Gateways\\API Gateway\\ApiGateway.csproj", "Gateways"));
    projects.Add(("AuthService", "Gateways\\Auth Service\\AuthService.csproj", "Gateways"));

    var projectGuids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    var folderGuids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    foreach (var (name, _, folder) in projects)
    {
        projectGuids.TryAdd(name, "{" + Guid.NewGuid().ToString("D").ToUpperInvariant() + "}");
    }
    foreach (var (name, _) in folders)
    {
        folderGuids.TryAdd(name, "{" + Guid.NewGuid().ToString("D").ToUpperInvariant() + "}");
    }

    // Write folder entries first
    foreach (var (name, displayName) in folders)
    {
        var g = folderGuids[name];
        sb.AppendLine($@"Project(""{{2150E333-8FDC-42A3-9474-1A3956D46DE8}}"") = ""{displayName}"", ""{displayName}"", ""{g}""");
        sb.AppendLine("EndProject");
    }

    // Write project entries
    foreach (var (name, path, _) in projects)
    {
        var g = projectGuids[name];
        sb.AppendLine($@"Project(""{{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}}"") = ""{name}"", ""{path}"", ""{g}""");
        sb.AppendLine("EndProject");
    }

    // Global sections
    sb.AppendLine("Global");
    sb.AppendLine(@"	GlobalSection(SolutionConfigurationPlatforms) = preSolution");
    sb.AppendLine(@"		Debug|Any CPU = Debug|Any CPU");
    sb.AppendLine(@"		Debug|x64 = Debug|x64");
    sb.AppendLine(@"		Debug|x86 = Debug|x86");
    sb.AppendLine(@"		Release|Any CPU = Release|Any CPU");
    sb.AppendLine(@"		Release|x64 = Release|x64");
    sb.AppendLine(@"		Release|x86 = Release|x86");
    sb.AppendLine(@"	EndGlobalSection");
    sb.AppendLine(@"	GlobalSection(ProjectConfigurationPlatforms) = postSolution");

    foreach (var (name, _, _) in projects)
    {
        var g = projectGuids[name];
        sb.AppendLine($"		{g}.Debug|Any CPU.ActiveCfg = Debug|Any CPU");
        sb.AppendLine($"		{g}.Debug|Any CPU.Build.0 = Debug|Any CPU");
        sb.AppendLine($"		{g}.Release|Any CPU.ActiveCfg = Release|Any CPU");
        sb.AppendLine($"		{g}.Release|Any CPU.Build.0 = Release|Any CPU");
    }

    sb.AppendLine(@"	EndGlobalSection");
    sb.AppendLine(@"	GlobalSection(SolutionProperties) = preSolution");
    sb.AppendLine(@"		HideSolutionNode = FALSE");
    sb.AppendLine(@"	EndGlobalSection");

    // NestedProjects - map each project under its folder
    sb.AppendLine(@"	GlobalSection(NestedProjects) = preSolution");
    foreach (var (name, _, folder) in projects)
    {
        if (!folderGuids.TryGetValue(folder, out var parentGuid)) continue;
        var childGuid = projectGuids[name];
        sb.AppendLine($"		{childGuid} = {parentGuid}");
    }
    sb.AppendLine(@"	EndGlobalSection");

    var gSln = "{" + Guid.NewGuid().ToString("D").ToUpperInvariant() + "}";
    sb.AppendLine(@"	GlobalSection(ExtensibilityGlobals) = postSolution");
    sb.AppendLine($"		SolutionGuid = {gSln}");
    sb.AppendLine(@"	EndGlobalSection");
    sb.AppendLine("EndGlobal");

    File.WriteAllText(Path.Combine(outSrc, $"{projectName}.sln"), sb.ToString().TrimStart());
}

static void WriteAppSettings(string srcDir, string outSrc, string projectName)
{
    // For Large tier, generate default appsettings for each web project
    if (Directory.Exists(Path.Combine(outSrc, "Services")))
    {
        var appsettings = """
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*"
}
""";
        var presDirs = new[] { "Gateways/API Gateway", "Gateways/Auth Service" };
        if (Directory.Exists(Path.Combine(outSrc, "Services")))
        {
            foreach (var svcDir in Directory.GetDirectories(Path.Combine(outSrc, "Services")))
            {
                var pres = Path.Combine(svcDir, Path.GetFileName(svcDir) + ".Presentation");
                if (Directory.Exists(pres))
                    presDirs = presDirs.Append(Path.GetRelativePath(outSrc, pres)).ToArray();
            }
        }
        foreach (var dir in presDirs)
        {
            var fullDir = Path.Combine(outSrc, dir);
            Directory.CreateDirectory(fullDir);
            var path = Path.Combine(fullDir, "appsettings.json");
            if (!File.Exists(path)) File.WriteAllText(path, appsettings);
        }
        return;
    }

    var presDir = Path.Combine(srcDir, $"{projectName}.Presentation");
    if (!Directory.Exists(presDir)) return;

    var outPres = Path.Combine(outSrc, $"{projectName}.Presentation");
    foreach (var file in Directory.GetFiles(presDir, "appsettings*"))
    {
        var name = Path.GetFileName(file);
        Directory.CreateDirectory(outPres);
        File.Copy(file, Path.Combine(outPres, name), overwrite: true);
    }
}

static void WriteLaunchSettings(string srcDir, string outSrc)
{
    foreach (var file in Directory.GetFiles(srcDir, "launchSettings.json", SearchOption.AllDirectories))
    {
        var relPath = Path.GetRelativePath(srcDir, file);
        var destPath = Path.Combine(outSrc, relPath);
        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
        File.Copy(file, destPath, overwrite: true);
    }
}
