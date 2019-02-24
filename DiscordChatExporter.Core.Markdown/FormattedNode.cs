﻿using System.Collections.Generic;

namespace DiscordChatExporter.Core.Markdown
{
    public class FormattedNode : Node
    {
        public TextFormatting Formatting { get; }

        public IReadOnlyList<Node> Children { get; }

        public FormattedNode(TextFormatting formatting, IReadOnlyList<Node> children)
        {
            Formatting = formatting;
            Children = children;
        }

        public FormattedNode(TextFormatting formatting, Node child)
            : this(formatting, new[] {child})
        {
        }

        public override string ToString() => $"<{Formatting}> ({Children.Count} direct children)";
    }
}