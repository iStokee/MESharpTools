using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;

namespace csharp_interop.Documentation.Converters
{
	/// <summary>
	/// Converts C# code to syntax-highlighted FlowDocument
	/// </summary>
	public class SyntaxHighlightConverter : IValueConverter
	{
		// Colors (theme-aware via DynamicResource would be better, but this works for now)
		private static readonly Brush KeywordBrush = new SolidColorBrush(Color.FromRgb(86, 156, 214));    // Blue
		private static readonly Brush StringBrush = new SolidColorBrush(Color.FromRgb(214, 157, 133));    // Orange
		private static readonly Brush CommentBrush = new SolidColorBrush(Color.FromRgb(87, 166, 74));     // Green
		private static readonly Brush TypeBrush = new SolidColorBrush(Color.FromRgb(78, 201, 176));       // Teal
		private static readonly Brush NumberBrush = new SolidColorBrush(Color.FromRgb(181, 206, 168));    // Light green

		private static readonly string[] Keywords = new[]
		{
			"public", "private", "protected", "internal", "static", "class", "interface", "namespace",
			"using", "var", "if", "else", "for", "foreach", "while", "do", "switch", "case", "break",
			"return", "new", "null", "true", "false", "this", "base", "void", "int", "string", "bool",
			"double", "float", "long", "object", "async", "await", "try", "catch", "finally", "throw"
		};

		public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
		{
			if (value is not string code || string.IsNullOrWhiteSpace(code))
				return null;

			var flowDoc = new FlowDocument
			{
				FontFamily = new FontFamily("Consolas, Courier New"),
				FontSize = 12,
				PagePadding = new Thickness(8),
				Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)) // Dark background
			};

			var para = new Paragraph();
			var lines = code.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

			foreach (var line in lines)
			{
				HighlightLine(line, para);
				para.Inlines.Add(new LineBreak());
			}

			flowDoc.Blocks.Add(para);
			return flowDoc;
		}

		private void HighlightLine(string line, Paragraph para)
		{
			if (string.IsNullOrEmpty(line))
				return;

			var trimmed = line.TrimStart();

			// Check if it's a comment
			if (trimmed.StartsWith("//"))
			{
				para.Inlines.Add(new Run(line) { Foreground = CommentBrush });
				return;
			}

			// Simple tokenization (this is basic, a proper parser would be better)
			var tokens = line.Split(new[] { ' ', '\t', '(', ')', '{', '}', ';', ',', '.', '[', ']' }, StringSplitOptions.RemoveEmptyEntries);
			var currentIndex = 0;

			foreach (var token in tokens)
			{
				// Find token in original string
				var tokenIndex = line.IndexOf(token, currentIndex);
				if (tokenIndex > currentIndex)
				{
					// Add whitespace/punctuation before token
					para.Inlines.Add(new Run(line.Substring(currentIndex, tokenIndex - currentIndex)));
				}

				// Add highlighted token
				var run = new Run(token);

				if (Array.IndexOf(Keywords, token) >= 0)
				{
					run.Foreground = KeywordBrush;
				}
				else if (token.StartsWith("\"") || token.EndsWith("\""))
				{
					run.Foreground = StringBrush;
				}
				else if (char.IsDigit(token[0]))
				{
					run.Foreground = NumberBrush;
				}
				else if (char.IsUpper(token[0]))
				{
					// Likely a type name
					run.Foreground = TypeBrush;
				}

				para.Inlines.Add(run);
				currentIndex = tokenIndex + token.Length;
			}

			// Add remaining characters
			if (currentIndex < line.Length)
			{
				para.Inlines.Add(new Run(line.Substring(currentIndex)));
			}
		}

		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		{
			throw new NotImplementedException();
		}
	}
}
