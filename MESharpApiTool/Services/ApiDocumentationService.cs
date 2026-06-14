using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;

namespace csharp_interop.Documentation.Services
{
	/// <summary>
	/// Service for discovering and documenting MESharp API through reflection and XML documentation
	/// </summary>
	public class ApiDocumentationService
	{
		private readonly Dictionary<string, XElement> _xmlDocs = new();
		private readonly Assembly _apiAssembly;

		public ApiDocumentationService()
		{
			// The browser documents the MESharp.API surface, which lives in csharp_interop — NOT in
			// this tool assembly. Resolve the already-loaded csharp_interop by name (ME loads it once
			// from Build_DLL; tools share that instance). Fall back to this assembly only if it cannot
			// be found, so design-time/test hosts still get a non-null assembly.
			_apiAssembly = AppDomain.CurrentDomain.GetAssemblies()
				              .FirstOrDefault(a => string.Equals(a.GetName().Name, "csharp_interop", StringComparison.OrdinalIgnoreCase))
			              ?? typeof(ApiDocumentationService).Assembly;
			LoadXmlDocumentation();
		}

		/// <summary>
		/// Load XML documentation file
		/// </summary>
		private void LoadXmlDocumentation()
		{
			try
			{
				var assemblyPath = _apiAssembly.Location;
				var xmlPath = Path.ChangeExtension(assemblyPath, ".xml");

				if (!File.Exists(xmlPath))
				{
					Console.WriteLine($"[ApiDocs] Warning: XML documentation not found at {xmlPath}");
					return;
				}

				var doc = XDocument.Load(xmlPath);
				var members = doc.Descendants("member");

				foreach (var member in members)
				{
					var name = member.Attribute("name")?.Value;
					if (!string.IsNullOrEmpty(name))
					{
						_xmlDocs[name] = member;
					}
				}

				Console.WriteLine($"[ApiDocs] Loaded {_xmlDocs.Count} XML documentation entries");
			}
			catch (Exception ex)
			{
				Console.WriteLine($"[ApiDocs] Error loading XML documentation: {ex.Message}");
			}
		}

		/// <summary>
		/// Get all public API classes from MESharp.API namespace
		/// </summary>
		public List<ApiClassInfo> GetAllApiClasses()
		{
			var classes = new List<ApiClassInfo>();

			try
			{
				var apiTypes = _apiAssembly.GetTypes()
					.Where(t => t.Namespace != null &&
					           t.Namespace.StartsWith("MESharp.API") &&
					           t.IsClass &&
					           t.IsPublic &&
					           !t.IsNested &&          // exclude Bank.Ids, Inventory.Item, etc.
					           t.IsAbstract && t.IsSealed) // static classes only
					.OrderBy(t => t.Name);

				foreach (var type in apiTypes)
				{
					classes.Add(GetClassInfo(type));
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"[ApiDocs] Error discovering API classes: {ex.Message}");
			}

			return classes;
		}

		/// <summary>
		/// Get detailed information about a specific class
		/// </summary>
		private ApiClassInfo GetClassInfo(Type type)
		{
			var memberName = $"T:{type.FullName}";
			var classExample = GetClassExample(memberName);

			var classInfo = new ApiClassInfo
			{
				Name = type.Name,
				FullName = type.FullName,
				Summary = GetSummary(memberName),
				Remarks = CombineDocumentationText(GetRemarks(memberName), classExample.Remarks),
				Example = classExample.Code
			};

			// Get all public methods
			var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
				.Where(m => !m.IsSpecialName) // Exclude property getters/setters
				.OrderBy(m => m.Name);

			foreach (var method in methods)
			{
				classInfo.Methods.Add(GetMethodInfo(method));
			}

			// Get all public properties
			var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Static)
				.OrderBy(p => p.Name);

			foreach (var property in properties)
			{
				classInfo.Properties.Add(GetPropertyInfo(property));
			}

			return classInfo;
		}

		/// <summary>
		/// Get detailed information about a method
		/// </summary>
		private ApiMethodInfo GetMethodInfo(MethodInfo method)
		{
			var memberName = GetMemberName(method);
			var parameters = method.GetParameters();

			var methodInfo = new ApiMethodInfo
			{
				Name = method.Name,
				ReturnType = GetFriendlyTypeName(method.ReturnType),
				Summary = GetSummary(memberName),
				Returns = GetReturns(memberName),
				Remarks = GetRemarks(memberName),
				Example = NormalizeExampleUsage(GetExample(memberName) ?? GenerateMethodExample(method, parameters))
			};

			// Get parameters
			foreach (var param in parameters)
			{
				methodInfo.Parameters.Add(new ApiParameterInfo
				{
					Name = param.Name,
					Type = GetFriendlyTypeName(param.ParameterType),
					Description = GetParamDescription(memberName, param.Name),
					IsOptional = param.IsOptional,
					DefaultValue = param.DefaultValue?.ToString()
				});
			}

			return methodInfo;
		}

		/// <summary>
		/// Get detailed information about a property
		/// </summary>
		private ApiPropertyInfo GetPropertyInfo(PropertyInfo property)
		{
			var memberName = $"P:{property.DeclaringType.FullName}.{property.Name}";

			return new ApiPropertyInfo
			{
				Name = property.Name,
				Type = GetFriendlyTypeName(property.PropertyType),
				Summary = GetSummary(memberName),
				Example = NormalizeExampleUsage(GetExample(memberName) ?? GeneratePropertyExample(property)),
				CanRead = property.CanRead,
				CanWrite = property.CanWrite
			};
		}

		/// <summary>
		/// Get XML member name for a method (handles overloads)
		/// </summary>
		private string GetMemberName(MethodInfo method)
		{
			var parameters = method.GetParameters();
			var paramString = parameters.Length > 0
				? "(" + string.Join(",", parameters.Select(p => p.ParameterType.FullName ?? p.ParameterType.Name)) + ")"
				: "";

			return $"M:{method.DeclaringType.FullName}.{method.Name}{paramString}";
		}

		/// <summary>
		/// Get summary from XML docs
		/// </summary>
		private string GetSummary(string memberName)
		{
			if (_xmlDocs.TryGetValue(memberName, out var element))
			{
				return CleanXmlText(element.Element("summary")?.Value);
			}
			return null;
		}

		/// <summary>
		/// Get returns description from XML docs
		/// </summary>
		private string GetReturns(string memberName)
		{
			if (_xmlDocs.TryGetValue(memberName, out var element))
			{
				return CleanXmlText(element.Element("returns")?.Value);
			}
			return null;
		}

		/// <summary>
		/// Get remarks from XML docs
		/// </summary>
		private string GetRemarks(string memberName)
		{
			if (_xmlDocs.TryGetValue(memberName, out var element))
			{
				return CleanXmlText(element.Element("remarks")?.Value);
			}
			return null;
		}

		/// <summary>
		/// Get example code from XML docs
		/// </summary>
		private string GetExample(string memberName)
		{
			if (_xmlDocs.TryGetValue(memberName, out var element))
			{
				return GetExampleText(element.Element("example"));
			}
			return null;
		}

		/// <summary>
		/// Split class-level examples into prose notes and code so the viewer can fill both boxes consistently.
		/// </summary>
		private ClassExampleParts GetClassExample(string memberName)
		{
			if (!_xmlDocs.TryGetValue(memberName, out var element))
			{
				return ClassExampleParts.Empty;
			}

			var example = element.Element("example");
			if (example == null)
			{
				return ClassExampleParts.Empty;
			}

			var code = GetExampleText(example);
			if (string.IsNullOrWhiteSpace(code))
			{
				return ClassExampleParts.Empty;
			}

			var remarkLines = new List<string>();
			var codeLines = new List<string>();

			foreach (var line in code.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
			{
				var trimmed = line.TrimStart();
				if (trimmed.StartsWith("//"))
				{
					var remark = trimmed.Substring(2).Trim();
					if (!string.IsNullOrWhiteSpace(remark))
					{
						remarkLines.Add(remark);
					}
					continue;
				}

				codeLines.Add(line);
			}

			return new ClassExampleParts
			{
				Remarks = CleanMultilineText(string.Join(Environment.NewLine, remarkLines)),
				Code = NormalizeCodeBlock(string.Join(Environment.NewLine, codeLines))
			};
		}

		/// <summary>
		/// Get parameter description from XML docs
		/// </summary>
		private string GetParamDescription(string memberName, string paramName)
		{
			if (_xmlDocs.TryGetValue(memberName, out var element))
			{
				var param = element.Elements("param")
					.FirstOrDefault(p => p.Attribute("name")?.Value == paramName);
				return CleanXmlText(param?.Value);
			}
			return null;
		}

		/// <summary>
		/// Clean up XML text (remove extra whitespace, newlines)
		/// </summary>
		private string CleanXmlText(string text)
		{
			if (string.IsNullOrWhiteSpace(text))
				return null;

			// Remove extra whitespace and trim
			text = System.Text.RegularExpressions.Regex.Replace(text.Trim(), @"\s+", " ");
			return text;
		}

		private string GetExampleText(XElement exampleElement)
		{
			if (exampleElement == null)
			{
				return null;
			}

			var codeElement = exampleElement.Element("code");
			if (codeElement != null)
			{
				return NormalizeCodeBlock(codeElement.Value);
			}

			return CleanMultilineText(exampleElement.Value);
		}

		private string NormalizeCodeBlock(string text)
		{
			if (string.IsNullOrWhiteSpace(text))
			{
				return null;
			}

			var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').ToList();

			while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[0]))
			{
				lines.RemoveAt(0);
			}

			while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[lines.Count - 1]))
			{
				lines.RemoveAt(lines.Count - 1);
			}

			var minIndent = lines
				.Where(line => !string.IsNullOrWhiteSpace(line))
				.Select(GetLeadingWhitespaceCount)
				.DefaultIfEmpty(0)
				.Min();

			var normalizedLines = lines
				.Select(line => line.Length >= minIndent ? line.Substring(minIndent).TrimEnd() : line.TrimEnd())
				.ToList();

			while (normalizedLines.Count > 0 && string.IsNullOrWhiteSpace(normalizedLines[0]))
			{
				normalizedLines.RemoveAt(0);
			}

			while (normalizedLines.Count > 0 && string.IsNullOrWhiteSpace(normalizedLines[normalizedLines.Count - 1]))
			{
				normalizedLines.RemoveAt(normalizedLines.Count - 1);
			}

			return normalizedLines.Count == 0 ? null : string.Join(Environment.NewLine, normalizedLines);
		}

		private string CleanMultilineText(string text)
		{
			if (string.IsNullOrWhiteSpace(text))
			{
				return null;
			}

			var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n')
				.Select(line => System.Text.RegularExpressions.Regex.Replace(line.Trim(), @"\s+", " "))
				.Where(line => !string.IsNullOrWhiteSpace(line));

			var cleaned = string.Join(Environment.NewLine, lines);
			return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
		}

		private string CombineDocumentationText(params string[] parts)
		{
			var cleanedParts = parts
				.Where(part => !string.IsNullOrWhiteSpace(part))
				.Select(part => part.Trim());

			var combined = string.Join(Environment.NewLine, cleanedParts);
			return string.IsNullOrWhiteSpace(combined) ? null : combined;
		}

		private int GetLeadingWhitespaceCount(string text)
		{
			var count = 0;
			while (count < text.Length && char.IsWhiteSpace(text[count]))
			{
				count++;
			}

			return count;
		}

		private string GenerateMethodExample(MethodInfo method, ParameterInfo[] parameters)
		{
			var className = method.DeclaringType?.Name;
			var arguments = string.Join(", ", parameters.Select(GetExampleArgument));
			var call = $"{className}.{method.Name}({arguments})";

			if (method.ReturnType == typeof(void))
			{
				return $"{call};";
			}

			var variableName = GetExampleVariableName(method.Name, method.ReturnType);
			return $"var {variableName} = {call};";
		}

		private string NormalizeExampleUsage(string example)
		{
			return example?.Replace("MESharp.API.", string.Empty);
		}

		private string GeneratePropertyExample(PropertyInfo property)
		{
			var className = property.DeclaringType?.Name;
			var propertyAccess = $"{className}.{property.Name}";

			if (property.CanRead)
			{
				var variableName = GetExampleVariableName(property.Name, property.PropertyType);
				return $"var {variableName} = {propertyAccess};";
			}

			return $"{propertyAccess} = {GetExampleValue(property.PropertyType, property.Name)};";
		}

		private string GetExampleArgument(ParameterInfo parameter)
		{
			if (parameter.IsOptional && parameter.HasDefaultValue)
			{
				return FormatLiteral(parameter.DefaultValue, parameter.ParameterType);
			}

			return GetExampleValue(parameter.ParameterType, parameter.Name);
		}

		private string GetExampleValue(Type type, string name)
		{
			var lowerName = name?.ToLowerInvariant() ?? string.Empty;

			if (type == typeof(string))
			{
				if (lowerName.Contains("option")) return "\"Use\"";
				if (lowerName.Contains("message")) return "\"Hello\"";
				if (lowerName.Contains("name")) return "\"Name\"";
				return "\"value\"";
			}

			if (type == typeof(bool)) return "true";
			if (type == typeof(float)) return "0f";
			if (type == typeof(double)) return "0d";
			if (type == typeof(long)) return "0L";
			if (type == typeof(decimal)) return "0m";
			if (type == typeof(char)) return "'a'";
			if (type.IsEnum)
			{
				var firstValue = Enum.GetNames(type).FirstOrDefault();
				var enumTypeName = GetQualifiedTypeName(type);
				return firstValue != null ? $"{enumTypeName}.{firstValue}" : $"default({enumTypeName})";
			}

			if (type == typeof(int))
			{
				if (lowerName.Contains("slot")) return "0";
				if (lowerName.Contains("index")) return "0";
				if (lowerName.Contains("count")) return "1";
				if (lowerName.Contains("quantity")) return "1";
				if (lowerName.Contains("amount")) return "1";
				if (lowerName.Contains("radius")) return "5";
				if (lowerName.Contains("distance")) return "5";
				if (lowerName.Contains("timeout")) return "5000";
				return "0";
			}

			if (type.IsArray)
			{
				return $"System.Array.Empty<{GetQualifiedTypeName(type.GetElementType())}>()";
			}

			if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
			{
				return $"System.Array.Empty<{GetQualifiedTypeName(type.GetGenericArguments()[0])}>()";
			}

			if (type.IsValueType)
			{
				return $"default({GetQualifiedTypeName(type)})";
			}

			return "null";
		}

		private string FormatLiteral(object value, Type type)
		{
			if (value == null) return "null";
			if (type == typeof(string)) return $"\"{value}\"";
			if (type == typeof(bool)) return value.ToString().ToLowerInvariant();
			if (type == typeof(float)) return $"{value}f";
			if (type == typeof(double)) return $"{value}d";
			if (type == typeof(long)) return $"{value}L";
			if (type == typeof(char)) return $"'{value}'";
			if (type.IsEnum) return $"{GetQualifiedTypeName(type)}.{value}";
			return value.ToString();
		}

		private string GetExampleVariableName(string memberName, Type type)
		{
			var name = memberName;
			if (name.StartsWith("Get", StringComparison.Ordinal) && name.Length > 3)
			{
				name = name.Substring(3);
			}
			else if (name.StartsWith("Is", StringComparison.Ordinal) && name.Length > 2)
			{
				name = name.Substring(2);
			}
			else if (name.StartsWith("Has", StringComparison.Ordinal) && name.Length > 3)
			{
				name = name.Substring(3);
			}

			if (string.IsNullOrWhiteSpace(name))
			{
				name = type == typeof(bool) ? "result" : "value";
			}

			return char.ToLowerInvariant(name[0]) + name.Substring(1);
		}

		private string GetQualifiedTypeName(Type type)
		{
			if (type == null) return "object";
			if (type == typeof(void)) return "void";
			if (type == typeof(int)) return "int";
			if (type == typeof(long)) return "long";
			if (type == typeof(float)) return "float";
			if (type == typeof(double)) return "double";
			if (type == typeof(bool)) return "bool";
			if (type == typeof(string)) return "string";
			if (type == typeof(object)) return "object";

			if (type.IsGenericType)
			{
				var genericName = type.FullName ?? type.Name;
				var tickIndex = genericName.IndexOf('`');
				if (tickIndex >= 0)
				{
					genericName = genericName.Substring(0, tickIndex);
				}

				var genericArgs = string.Join(", ", type.GetGenericArguments().Select(GetQualifiedTypeName));
				return $"{genericName.Replace('+', '.')}<{genericArgs}>";
			}

			return (type.FullName ?? type.Name).Replace('+', '.');
		}

		/// <summary>
		/// Get friendly type name (e.g., "Int32" -> "int")
		/// </summary>
		private string GetFriendlyTypeName(Type type)
		{
			if (type == typeof(void)) return "void";
			if (type == typeof(int)) return "int";
			if (type == typeof(long)) return "long";
			if (type == typeof(float)) return "float";
			if (type == typeof(double)) return "double";
			if (type == typeof(bool)) return "bool";
			if (type == typeof(string)) return "string";
			if (type == typeof(object)) return "object";

			// Handle generics
			if (type.IsGenericType)
			{
				var genericName = type.Name.Substring(0, type.Name.IndexOf('`'));
				var genericArgs = string.Join(", ", type.GetGenericArguments().Select(GetFriendlyTypeName));
				return $"{genericName}<{genericArgs}>";
			}

			return type.Name;
		}

		private class ClassExampleParts
		{
			public static readonly ClassExampleParts Empty = new();

			public string Remarks { get; set; }
			public string Code { get; set; }
		}
	}

	/// <summary>
	/// Information about an API class
	/// </summary>
	public class ApiClassInfo
	{
		public string Name { get; set; }
		public string FullName { get; set; }
		public string Summary { get; set; }
		public string Remarks { get; set; }
		public string Example { get; set; }
		public string RemarksDisplay => string.IsNullOrWhiteSpace(Remarks) ? "N/A" : Remarks;
		public string ExampleDisplay => string.IsNullOrWhiteSpace(Example) ? "N/A" : Example;
		public List<ApiMethodInfo> Methods { get; set; } = new();
		public List<ApiPropertyInfo> Properties { get; set; } = new();
	}

	/// <summary>
	/// Information about an API method
	/// </summary>
	public class ApiMethodInfo
	{
		public string Name { get; set; }
		public string ReturnType { get; set; }
		public string Summary { get; set; }
		public string Returns { get; set; }
		public string Remarks { get; set; }
		public string Example { get; set; }
		public List<ApiParameterInfo> Parameters { get; set; } = new();

		/// <summary>
		/// Formatted parameters string for display
		/// </summary>
		public string ParametersString
		{
			get
			{
				if (Parameters == null || Parameters.Count == 0)
					return string.Empty;

				return string.Join(", ", Parameters.Select(p =>
				{
					var paramStr = $"{p.Type} {p.Name}";
					if (p.IsOptional && p.DefaultValue != null)
					{
						paramStr += $" = {p.DefaultValue}";
					}
					return paramStr;
				}));
			}
		}

		/// <summary>
		/// Generate method signature
		/// </summary>
		public string GetSignature()
		{
			var parameters = string.Join(", ", Parameters.Select(p =>
			{
				var paramStr = $"{p.Type} {p.Name}";
				if (p.IsOptional && p.DefaultValue != null)
				{
					paramStr += $" = {p.DefaultValue}";
				}
				return paramStr;
			}));

			return $"{ReturnType} {Name}({parameters})";
		}
	}

	/// <summary>
	/// Information about a method parameter
	/// </summary>
	public class ApiParameterInfo
	{
		public string Name { get; set; }
		public string Type { get; set; }
		public string Description { get; set; }
		public bool IsOptional { get; set; }
		public string DefaultValue { get; set; }
	}

	/// <summary>
	/// Information about an API property
	/// </summary>
	public class ApiPropertyInfo
	{
		public string Name { get; set; }
		public string Type { get; set; }
		public string Summary { get; set; }
		public string Example { get; set; }
		public bool CanRead { get; set; }
		public bool CanWrite { get; set; }

		/// <summary>
		/// Generate property signature
		/// </summary>
		public string GetSignature()
		{
			var accessors = new List<string>();
			if (CanRead) accessors.Add("get");
			if (CanWrite) accessors.Add("set");

			return $"{Type} {Name} {{ {string.Join("; ", accessors)}; }}";
		}

		/// <summary>
		/// Computed property for WPF data binding (bindings cannot target methods).
		/// </summary>
		public string Signature => GetSignature();
	}
}
