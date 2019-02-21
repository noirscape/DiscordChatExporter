﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;
using DiscordChatExporter.Core.Markdown;
using DiscordChatExporter.Core.Models;
using Scriban.Runtime;

namespace DiscordChatExporter.Core.Services
{
    public partial class ExportService
    {
        private class TemplateModel
        {
            private readonly ChatLog _log;
            private readonly string _dateFormat;
            private readonly int _messageGroupLimit;

            public TemplateModel(ChatLog log, string dateFormat, int messageGroupLimit)
            {
                _log = log;
                _dateFormat = dateFormat;
                _messageGroupLimit = messageGroupLimit;
            }

            private IEnumerable<MessageGroup> GroupMessages(IEnumerable<Message> messages)
            {
                // Group adjacent messages by timestamp and author
                var buffer = new List<Message>();
                foreach (var message in messages)
                {
                    // Get first message of the group
                    var groupFirstMessage = buffer.FirstOrDefault();

                    // Group break condition
                    var breakCondition =
                        groupFirstMessage != null &&
                        (
                            message.Author.Id != groupFirstMessage.Author.Id || // when author changes
                            (message.Timestamp - groupFirstMessage.Timestamp).TotalHours > 1 || // when difference in timestamps is an hour or more
                            message.Timestamp.Hour != groupFirstMessage.Timestamp.Hour || // when the timestamp's hour changes
                            buffer.Count >= _messageGroupLimit // when group is full
                        );

                    // If condition is true - flush buffer
                    if (breakCondition)
                    {
                        var group = new MessageGroup(groupFirstMessage.Author, groupFirstMessage.Timestamp, buffer);

                        // Reset the buffer instead of clearing to avoid mutations on existing references
                        buffer = new List<Message>();

                        yield return group;
                    }

                    // Add message to buffer
                    buffer.Add(message);
                }

                // Add what's remaining in buffer
                if (buffer.Any())
                {
                    var groupFirstMessage = buffer.First();
                    var group = new MessageGroup(groupFirstMessage.Author, groupFirstMessage.Timestamp, buffer);

                    yield return group;
                }
            }

            private string Format(IFormattable obj, string format) =>
                obj.ToString(format, CultureInfo.InvariantCulture);

            private string FormatDate(DateTime dateTime) => Format(dateTime, _dateFormat);

            private string FormatFileSize(long fileSize)
            {
                string[] units = {"B", "KB", "MB", "GB", "TB", "PB", "EB", "ZB", "YB"};
                double size = fileSize;
                var unit = 0;

                while (size >= 1024)
                {
                    size /= 1024;
                    ++unit;
                }

                return $"{size:0.#} {units[unit]}";
            }

            private string FormatColor(Color color) => $"{color.R},{color.G},{color.B},{color.A}";

            private string GetFormattingTagHtml(TextFormatting formatting)
            {
                if (formatting == TextFormatting.Bold)
                    return "b";

                if (formatting == TextFormatting.Italic)
                    return "i";

                if (formatting == TextFormatting.Underline)
                    return "u";

                if (formatting == TextFormatting.Strikethrough)
                    return "s";

                if (formatting == TextFormatting.Spoiler)
                    return "span"; // todo

                throw new ArgumentOutOfRangeException(nameof(formatting), formatting, null);
            }

            private string RenderMarkdownHtml(IEnumerable<Node> nodes)
            {
                var buffer = new StringBuilder();

                // TODO: html encode
                // TODO: move this to templates
                foreach (var node in nodes)
                {
                    if (node is TextNode textNode)
                    {
                        buffer.Append(textNode.Text);
                    }

                    else if (node is FormattedNode formattedNode)
                    {
                        var tag = GetFormattingTagHtml(formattedNode.Formatting);
                        buffer.Append($"<{tag}>{RenderMarkdownHtml(formattedNode.Children)}</{tag}>");
                    }

                    else if (node is InlineCodeBlockNode inlineCodeBlockNode)
                    {
                        buffer.Append($"<span class=\"pre pre--inline\">{inlineCodeBlockNode.Code}</span>");
                    }

                    else if (node is MultilineCodeBlockNode multilineCodeBlockNode)
                    {
                        // TODO: add language
                        buffer.Append($"<div class=\"pre pre--multiline\">{multilineCodeBlockNode.Code}</div>");
                    }

                    else if (node is MentionNode mentionNode)
                    {
                        if (mentionNode.Type == MentionType.Meta)
                        {
                            buffer.Append($"<span class=\"mention\">@{mentionNode.Id}</span>");
                        }

                        else if (mentionNode.Type == MentionType.User)
                        {
                            var user = _log.Mentionables.GetUser(mentionNode.Id);
                            buffer.Append($"<span class=\"mention\" title=\"{user.FullName}\">@{user.Name}</span>");
                        }

                        else if (mentionNode.Type == MentionType.Channel)
                        {
                            var channel = _log.Mentionables.GetChannel(mentionNode.Id);
                            buffer.Append($"<span class=\"mention\">@{channel.Name}</span>");
                        }

                        else if (mentionNode.Type == MentionType.Role)
                        {
                            var role = _log.Mentionables.GetRole(mentionNode.Id);
                            buffer.Append($"<span class=\"mention\">@{role.Name}</span>");
                        }
                    }

                    else if (node is EmojiNode emojiNode)
                    {
                        buffer.Append($"<img class=\"emoji\" title=\"{emojiNode.Name}\" src=\"https://cdn.discordapp.com/emojis/{emojiNode.Id}.png\" />");
                    }

                    else if (node is LinkNode linkNode)
                    {
                        buffer.Append($"<a href=\"{linkNode.Url}\">{linkNode.Title}</a>");
                    }
                }

                return buffer.ToString();
            }

            private string RenderMarkdownHtml(string input) => RenderMarkdownHtml(MarkdownParser.Parse(input));

            public ScriptObject GetScriptObject()
            {
                // Create instance
                var scriptObject = new ScriptObject();

                // Import chat log
                scriptObject.SetValue("Model", _log, true);

                // Import functions
                scriptObject.Import(nameof(GroupMessages), new Func<IEnumerable<Message>, IEnumerable<MessageGroup>>(GroupMessages));
                scriptObject.Import(nameof(Format), new Func<IFormattable, string, string>(Format));
                scriptObject.Import(nameof(FormatDate), new Func<DateTime, string>(FormatDate));
                scriptObject.Import(nameof(FormatFileSize), new Func<long, string>(FormatFileSize));
                scriptObject.Import(nameof(FormatColor), new Func<Color, string>(FormatColor));
                scriptObject.Import(nameof(RenderMarkdownHtml), new Func<string, string>(RenderMarkdownHtml));

                return scriptObject;
            }
        }
    }
}