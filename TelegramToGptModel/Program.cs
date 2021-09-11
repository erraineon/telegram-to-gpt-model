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
        public static void Main(string[] args)
        {
            var logsDirectoryPath = args.Length >= 1 ? args[0] : Environment.CurrentDirectory;
            var outputPath = args.Length >= 2 ? args[1] : "output.jsonl";
            var logFiles = Directory.GetFiles(logsDirectoryPath, "*.html");

            var logs = logFiles
                .OrderBy(f => f)
                .Select(CreateHtmlDocument)
                .AsParallel()
                .SelectMany(d => d.SelectSingleNode("//div[@class='history']")
                    .Elements("div")
                    .Where(e => e?.SelectSingleNode(".//div[@class='from_name']") != null)
                    .Select(GetSanitizedText)
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .Select(FormatText))
                .ToList();
            File.WriteAllLines(outputPath, logs);
            Console.WriteLine($"written {logs.Count} logs to {outputPath}");
        }
        
        private static HtmlNode CreateHtmlDocument(string logFilePath)
        {
            var document = new HtmlDocument();
            document.Load(logFilePath);

            // <br> nodes aren't automatically converted to new lines with .InnerText, so do it here
            var breakNodes = document.DocumentNode.SelectNodes("//br") ?? Enumerable.Empty<HtmlNode>();
            foreach (var node in breakNodes)
                node.ParentNode.ReplaceChild(document.CreateTextNode("\n"), node);

            return document.DocumentNode;
        }

        private static string FormatText(string text)
        {
            var formattedText = JsonConvert.ToString($"{text} END");
            return $"{{\"prompt\": \"\", \"completion\": {formattedText}}}";
        }

        private static IEnumerable<string> GetTextFromNodeAndRelated(HtmlNode node)
        {
            do
            {
                // when exported, follow-up messages lack context on the author; use this to detect them
                var messageText = node
                    .SelectSingleNode(".//div[@class='text']")?.InnerText
                    .Trim();
                yield return messageText;
            } while (
                // skip the :after node
                (node = node.NextSibling.NextSibling) is { } successor &&
                successor.HasClass("joined"));
        }

        private static string GetSanitizedText(HtmlNode e)
        {
            var messagesToJoin = GetTextFromNodeAndRelated(e)
                .Where(t => !string.IsNullOrWhiteSpace(t) &&
                    // ignore very small messages
                    t.Length > 2);
            var message = string.Join("\n", messagesToJoin);
            var decodedMessage = HttpUtility.HtmlDecode(message);
            var urlRemovedText = Regex.Replace(decodedMessage, @"http[^\s]+", string.Empty);
            return urlRemovedText.Trim('\n', ' ');
        }
    }
}