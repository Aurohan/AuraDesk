using System.IO;
using System.Text;

namespace GlassDesk.AppServices;

/// <summary>
/// 基于离线规则库（AppGroups.tsv）对应用程序进行自动分类与分组推荐。
/// </summary>
public static class AppClassifier
{
    private sealed record ClassificationRule(string Category, bool IsExact, string[] Patterns);

    private static readonly Lazy<List<ClassificationRule>> Rules = new(LoadRules);

    /// <summary>
    /// 根据应用显示名及目标执行文件路径，匹配其对应的分类类别。
    /// </summary>
    /// <param name="displayName">应用显示名称（如 Visual Studio Code、微信、黑神话：悟空）</param>
    /// <param name="targetPath">可执行文件或快捷方式的目标路径</param>
    /// <returns>匹配到的分类名称（如 开发、社交、游戏、办公、系统工具），未匹配到时返回 null。</returns>
    public static string? Classify(string? displayName, string? targetPath = null)
    {
        var candidates = new List<string>();

        if (!string.IsNullOrWhiteSpace(displayName))
        {
            candidates.Add(displayName.Trim());
        }

        if (!string.IsNullOrWhiteSpace(targetPath))
        {
            try
            {
                var fileName = Path.GetFileNameWithoutExtension(targetPath);
                if (!string.IsNullOrWhiteSpace(fileName) && !candidates.Contains(fileName, StringComparer.OrdinalIgnoreCase))
                {
                    candidates.Add(fileName.Trim());
                }
            }
            catch (ArgumentException) { }
        }

        if (candidates.Count == 0) return null;

        var allRules = Rules.Value;

        // 1. 优先比对 exact（精准全字匹配）规则，避免短缩写（如 ps, wt, pr, cmd）误伤普通词汇
        foreach (var rule in allRules.Where(r => r.IsExact))
        {
            foreach (var candidate in candidates)
            {
                foreach (var pattern in rule.Patterns)
                {
                    if (string.Equals(candidate, pattern, StringComparison.OrdinalIgnoreCase))
                    {
                        return rule.Category;
                    }
                }
            }
        }

        // 2. 比对 word（子串包含）规则
        foreach (var rule in allRules.Where(r => !r.IsExact))
        {
            foreach (var candidate in candidates)
            {
                foreach (var pattern in rule.Patterns)
                {
                    if (candidate.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                    {
                        return rule.Category;
                    }
                }
            }
        }

        return null;
    }

    private static List<ClassificationRule> LoadRules()
    {
        var list = new List<ClassificationRule>();
        try
        {
            using var stream = typeof(AppClassifier).Assembly.GetManifestResourceStream(
                "GlassDesk.AppServices.Resources.AppGroups.tsv");
            if (stream is null) return list;

            using var reader = new StreamReader(stream, Encoding.UTF8);
            while (reader.ReadLine() is { } line)
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;

                var parts = trimmed.Split('\t');
                if (parts.Length < 3) continue;

                var category = parts[0].Trim();
                var mode = parts[1].Trim().ToLowerInvariant();
                var aliases = parts[2].Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                if (category.Length > 0 && aliases.Length > 0)
                {
                    list.Add(new ClassificationRule(category, mode == "exact", aliases));
                }
            }
        }
        catch (Exception)
        {
            // 离线分类表若因特殊原因解析受阻，保持规则列表安全降级为空列表，不阻塞应用主流程
        }

        return list;
    }
}
