using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;

namespace BundleUltimateTool.UI;

public readonly record struct PropertyRow(string Category, string Name, string Value, bool IsCategory);

/// <summary>Pure reflection helper behind ThemedPropertyGrid: groups public properties by
/// [Category], honours [DisplayName], and produces compact Property/Value rows.</summary>
public static class PropertyRowBuilder
{
    public static IReadOnlyList<PropertyRow> Build(object target)
    {
        var rows = new List<PropertyRow>();
        if (target == null) return rows;

        var categories = new List<string>();
        var byCategory = new Dictionary<string, List<PropertyDescriptor>>(StringComparer.Ordinal);
        foreach (PropertyDescriptor property in TypeDescriptor.GetProperties(target))
        {
            string category = string.IsNullOrWhiteSpace(property.Category) ? "Misc" : property.Category;
            if (!byCategory.TryGetValue(category, out List<PropertyDescriptor> list))
            {
                list = new List<PropertyDescriptor>();
                byCategory.Add(category, list);
                categories.Add(category);
            }
            list.Add(property);
        }

        foreach (string category in categories)
        {
            rows.Add(new PropertyRow(category, category, string.Empty, true));
            foreach (PropertyDescriptor property in byCategory[category])
            {
                string name = string.IsNullOrWhiteSpace(property.DisplayName) ? property.Name : property.DisplayName;
                string value;
                try
                {
                    object raw = property.GetValue(target);
                    value = raw == null ? string.Empty
                        : TypeDescriptor.GetConverter(property.PropertyType).ConvertToString(
                            null, CultureInfo.CurrentCulture, raw) ?? raw.ToString();
                }
                catch (Exception ex) { value = ex.Message; }
                rows.Add(new PropertyRow(category, name, value, false));
            }
        }
        return rows;
    }
}
