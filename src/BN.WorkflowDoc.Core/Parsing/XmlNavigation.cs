using System.Xml.Linq;

namespace BN.WorkflowDoc.Core.Parsing;

internal static class XmlNavigation
{
    public static string? ReadAttributeOrElement(XElement parent, params string[] names)
    {
        foreach (var name in names)
        {
            var attribute = parent.Attributes().FirstOrDefault(x =>
                string.Equals(x.Name.LocalName, name, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(attribute?.Value))
            {
                return attribute.Value.Trim();
            }

            var child = parent.Elements().FirstOrDefault(x =>
                string.Equals(x.Name.LocalName, name, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(child?.Value))
            {
                return child.Value.Trim();
            }
        }

        return null;
    }

    public static IEnumerable<XElement> DescendantsByLocalName(XContainer parent, params string[] localNames)
    {
        if (localNames.Length == 0)
        {
            return Enumerable.Empty<XElement>();
        }

        var lookup = new HashSet<string>(localNames, StringComparer.OrdinalIgnoreCase);
        return parent.Descendants().Where(x => lookup.Contains(x.Name.LocalName));
    }

    public static XElement? FirstElementByLocalName(XContainer parent, params string[] localNames)
    {
        if (localNames.Length == 0)
        {
            return null;
        }

        var lookup = new HashSet<string>(localNames, StringComparer.OrdinalIgnoreCase);
        return parent.Elements().FirstOrDefault(x => lookup.Contains(x.Name.LocalName));
    }
}
