namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using MobileGnollHackLogger.Data;
using Overseer.Controllers;
using Overseer.Services.Benchmarking;
using Overseer.Tests.Helpers;
using Xunit;

/// <summary>
/// Every Overseer controller action that takes the request's <see cref="CancellationToken"/> catches
/// its own cancellation and answers 499 Client Closed Request, so an aborted request never leaves
/// the action as an unhandled <see cref="OperationCanceledException"/>.
/// </summary>
public class ControllerClientAbortTests
{
    private static readonly Regex PublicTaskMethod = new(@"^(?<indent>[ \t]*)public\s+(?:static\s+)?(?:async\s+)?Task<.+?>\s+(?<name>\w+)\s*\(");
    private static readonly Regex TokenParameter = new(@"\bCancellationToken\s+(?<name>\w+)");

    [Fact]
    public void EveryActionTakingARequestTokenAnswers499WhenTheClientAborts()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "MobileGnollHackLogger.slnx")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        string controllersDir = Path.Combine(dir.FullName, "Overseer", "Controllers");
        Assert.True(Directory.Exists(controllersDir), $"Controllers source directory not found: {controllersDir}");

        var checkedActions = new List<string>();
        var offenders = new List<string>();

        foreach (string file in Directory.GetFiles(controllersDir, "*.cs", SearchOption.AllDirectories))
        {
            string controller = Path.GetFileNameWithoutExtension(file);
            string[] lines = File.ReadAllText(file).Replace("\r\n", "\n").Split('\n');

            for (int i = 0; i < lines.Length; i++)
            {
                var method = PublicTaskMethod.Match(lines[i]);
                if (!method.Success)
                {
                    continue;
                }

                // The parameter list may span several lines; it ends where its parentheses balance.
                int open = method.Index + method.Length - 1;
                int depth = 0;
                int end = i;
                int closeColumn = -1;
                for (int j = i; j < lines.Length && closeColumn < 0; j++)
                {
                    for (int c = j == i ? open : 0; c < lines[j].Length; c++)
                    {
                        if (lines[j][c] == '(')
                        {
                            depth++;
                        }
                        else if (lines[j][c] == ')' && --depth == 0)
                        {
                            end = j;
                            closeColumn = c;
                            break;
                        }
                    }
                }
                Assert.True(closeColumn >= 0, $"Unbalanced parameter list of {controller}.{method.Groups["name"].Value}");

                string parameters = string.Join("\n", lines[i..(end + 1)]);
                var token = TokenParameter.Match(parameters);
                if (!token.Success)
                {
                    continue;
                }

                string action = $"{controller}.{method.Groups["name"].Value}";
                checkedActions.Add(action);

                string afterSignature = lines[end][(closeColumn + 1)..];
                string? nextLine = lines.Skip(end + 1).FirstOrDefault(l => l.Trim().Length > 0);
                if (afterSignature.Contains("=>") || nextLine?.TrimStart().StartsWith("=>", StringComparison.Ordinal) == true)
                {
                    offenders.Add(action + " (expression-bodied)");
                    continue;
                }

                string closing = method.Groups["indent"].Value + "}";
                int bodyEnd = Array.FindIndex(lines, end + 1, l => l.TrimEnd() == closing);
                Assert.True(bodyEnd > end, $"No closing brace found for {action}");

                string body = string.Join("\n", lines[(end + 1)..bodyEnd]);
                string tokenName = token.Groups["name"].Value;
                if (!body.Contains($"catch (OperationCanceledException) when ({tokenName}.IsCancellationRequested)", StringComparison.Ordinal)
                    || !body.Contains("Status499ClientClosedRequest", StringComparison.Ordinal))
                {
                    offenders.Add(action);
                }
                i = bodyEnd;
            }
        }

        Assert.True(checkedActions.Count >= 100, $"Only {checkedActions.Count} actions taking a CancellationToken were found; the source scan is broken.");
        Assert.Contains("AdminBenchmarkReportDocumentsController.List", checkedActions);
        Assert.Contains("AdminChatConsistencyController.Runs", checkedActions);
        Assert.True(offenders.Count == 0,
            "These actions take a CancellationToken but do not answer 499 when the client aborts: " + string.Join(", ", offenders));
    }

    [Fact]
    public async Task ReportDocumentsListAnswers499WhenTheClientAborts()
    {
        using var db = new ApplicationDbContext(BenchmarkRunExamTests.InMemoryOptions());
        var controller = new AdminBenchmarkReportDocumentsController(
            new BenchmarkReportRenderService(db, TestChartStores.Unconfigured(), NullLogger<BenchmarkReportRenderService>.Instance));
        using var aborted = new CancellationTokenSource();
        aborted.Cancel();

        Assert.Equal(StatusCodes.Status499ClientClosedRequest,
            Assert.IsType<StatusCodeResult>(await controller.List(null, null, null, aborted.Token)).StatusCode);
    }
}
