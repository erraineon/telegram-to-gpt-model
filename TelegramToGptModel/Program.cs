using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web;
using HtmlAgilityPack;
using Newtonsoft.Json;

namespace TelegramToGptModel
{
    public static class Program
    {
        static IEnumerable<string> GetTextFromNodeAndRelated(HtmlNode e)
        {
            do
            {
                var text = e.SelectSingleNode(".//div[@class='text']")?.InnerText.Trim();
                yield return text;
            } while ((e = e.NextSibling.NextSibling) is { } successor && successor.HasClass("joined"));
        }

        public static void Main(string[] args)
        {
            var logsDirectoryPath = args.Length >= 1 ? args[0] : Environment.CurrentDirectory;
            var outputPath = args.Length >= 2 ? args[1] : "output.jsonl";
            var logFiles = Directory.GetFiles(logsDirectoryPath, "*.html");

            var logs = logFiles
                .OrderBy(f => f)
                .Select(f =>
                {
                    var document = new HtmlDocument();
                    document.Load(f);
                    ReplaceBreakNodesWithNewLine(document);
                    return document.DocumentNode;
                })
                .AsParallel()
                .SelectMany(d =>
                {
                    return d.SelectSingleNode("//div[@class='history']")
                        .Elements("div")
                        .Where(e => !string.IsNullOrEmpty(e?.SelectSingleNode(".//div[@class='from_name']")?.InnerText.Trim()))
                        .Select(e =>
                        {
                            var messagesToJoin = GetTextFromNodeAndRelated(e)
                                .Where(t =>
                                    !string.IsNullOrWhiteSpace(t) &&
                                    // ignore very small messages
                                    t.Length > 2);
                            var message = string.Join("\n", messagesToJoin);
                            var decodedMessage = HttpUtility.HtmlDecode(message);
                            var urlRemovedText = Regex.Replace(decodedMessage, @"http[^\s]+", string.Empty);
                            return urlRemovedText.Trim('\n', ' ');
                        })
                        .Where(t => !string.IsNullOrWhiteSpace(t))
                        .Select(messageTextToTokenize =>
                        {
                            var sanitizedText = JsonConvert.ToString($"{messageTextToTokenize} END");
                            return $"{{\"prompt\": \"\", \"completion\": {sanitizedText}}}";
                        });
                })
                .ToList();
            File.WriteAllLines(outputPath, logs);
            Console.WriteLine($"written {logs.Count} logs to {outputPath}");
        }

        private static void ReplaceBreakNodesWithNewLine(HtmlDocument document)
        {
            var breakNodes = document.DocumentNode.SelectNodes("//br") ?? Enumerable.Empty<HtmlNode>();
            foreach (var node in breakNodes)
                node.ParentNode.ReplaceChild(document.CreateTextNode("\n"), node);
        }
    }
}

