﻿#nullable enable

namespace CompiledBindings;

public class XamlParser
{
	public static XamlNode ParseAttribute(string file, XAttribute attribute, IList<XamlNamespace>? knownNamespaces)
	{
		var node = new XamlNode(file, attribute, attribute.Name);

		string str = attribute.Value.Trim();
		bool squareBracket = false;
		if (str.StartsWith("{") || (squareBracket = str.StartsWith("[")))
		{
			var expecedBracket = squareBracket ? "]" : "}";
			if (!str.EndsWith(expecedBracket))
			{
				throw new ParseException($"Expected {expecedBracket}", str.Length);
			}
			node.Children.Add(ParseMarkupExtension(file, attribute, knownNamespaces));
		}
		else
		{
			node.Value = str;
		}

		return node;
	}

	private static XamlNode ParseMarkupExtension(string file, XAttribute attribute, IList<XamlNamespace>? knownNamespaces)
	{
		return ParseMarkupExtension(file, attribute.Value.Trim(), attribute, knownNamespaces);
	}

	public static XamlNode ParseMarkupExtension(string file, string str, XAttribute attribute, IList<XamlNamespace>? knownNamespaces)
	{
		string? value;
		int valueOffset;

		// Get the name
		int pos = str.IndexOf(' ', 1);
		if (pos == -1)
		{
			pos = str.Length - 1;
			value = null;
			valueOffset = 0;
		}
		else
		{
			value = str.Substring(pos + 1, str.Length - pos - 2).TrimStart();
			valueOffset = str.Length - value.Length - 1;
			value = value.TrimEnd();
		}

		string name = str.Substring(1, pos - 1);
		var nodeName = GetTypeName(name, attribute.Parent, knownNamespaces);

		return new XamlNode(file, attribute, nodeName) { Value = value, ValueOffset = valueOffset };
	}

	public static XName GetTypeName(string value, XObject xobject, IList<XamlNamespace>? knownNamespaces)
	{
		XNamespace ns;

		string prefix, className;
		var parts = value.Split(':');
		if (parts.Length > 2)
		{
			throw new ParseException($"Wrong syntax.");
		}
		else if (parts.Length == 2)
		{
			prefix = parts[0];
			className = parts[1];

			var nsAttr = EnumerableExtensions
				.SelectSequence(xobject, e => e.Parent, xobject is XElement)
				.Cast<XElement>()
				.SelectMany(e => e.Attributes())
				.FirstOrDefault(a => a.Name.Namespace == XNamespace.Xmlns && a.Name.LocalName == prefix);
			if (nsAttr == null)
			{
				var ns2 = knownNamespaces?.FirstOrDefault(n => n.Prefix == prefix);
				if (ns2 == null)
				{
					throw new ParseException($"'{prefix}' is an undeclared prefix.");
				}
				ns = ns2.Namespace;
			}
			else
			{
				ns = nsAttr.Value;
			}
		}
		else
		{
			className = parts[0];

			var nsAttr = EnumerableExtensions
				.SelectSequence(xobject, e => e.Parent, xobject is XElement)
				.Cast<XElement>()
				.SelectMany(e => e.Attributes())
				.FirstOrDefault(a => a.Name.Namespace == XNamespace.None && a.Name.LocalName == "xmlns");
			ns = (XNamespace)nsAttr?.Value ?? XNamespace.None;
		}

		return ns + className;
	}
}

public class XamlNode
{
	private List<XamlNode>? _properties;
	private List<XamlNode>? _children;

	public XamlNode(string file, XObject element, XName name)
	{
		File = file;
		Element = element;
		Name = name;
	}

	public string File { get; }
	public XObject Element { get; }
	public XName Name { get; }

	public List<XamlNode> Properties => _properties ??= new List<XamlNode>();

	public List<XamlNode> Children => _children ??= new List<XamlNode>();

	public string? Value { get; set; }
	public int ValueOffset { get; set; }

	public void Remove()
	{
		if (Element is XElement xe)
		{
			xe.Remove();
		}
		else
		{
			((XAttribute)Element).Remove();
		}
	}
}

public class XamlNamespace
{
	private static readonly Regex _usingRegex = new Regex(@"^\s*(?:(global\s+))?using\s*:(.+)$");
	private static readonly Regex _clrNamespaceRegex = new Regex(@"^clr-namespace:(.+?)(?:;.+)?$");

	public XamlNamespace(string prefix, XNamespace ns)
	{
		Prefix = prefix;
		Namespace = ns;
		ClrNamespace = GetClrNamespace(ns.NamespaceName);
	}

	public string Prefix { get; }
	public XNamespace Namespace { get; }
	public string? ClrNamespace { get; }

	public static string? GetClrNamespace(string nsName)
	{
		var match = _usingRegex.Match(nsName);
		if (match.Success)
		{
			return match.Groups[match.Groups.Count - 1].Value.Trim();
		}
		else if ((match = _clrNamespaceRegex.Match(nsName)).Success)
		{
			return match.Groups[1].Value.Trim();
		}
		return null;
	}

	public static IEnumerable<XamlNamespace> GetClrNamespaces(XObject xobject)
	{
		var xelement = xobject as XElement ?? xobject.Parent;
		return EnumerableExtensions
			.SelectSequence(xelement, e => e.Parent, true)
			.SelectMany(e => e.Attributes())
			.Where(a => a.Name.Namespace == XNamespace.Xmlns)
			.Select(a => new XamlNamespace(a.Name.LocalName, a.Value))
			.Where(n => n.ClrNamespace != null);
	}

	public static IList<XamlNamespace> GetGlobalNamespaces(IEnumerable<XDocument> xdocs)
	{
		return xdocs
			.SelectMany(xdoc => xdoc.Descendants().Attributes())
			.Where(a => a.Name.Namespace == XNamespace.Xmlns)
			.Select(a => (a, m: _usingRegex.Match(a.Value)))
			.Where(e => e.m.Success && !string.IsNullOrEmpty(e.m.Groups[1].Value))
			.Select(e => new XamlNamespace(e.a.Name.LocalName, e.a.Value.Trim()))
			.Distinct(n => n.Prefix)
			.ToList();
	}
}

