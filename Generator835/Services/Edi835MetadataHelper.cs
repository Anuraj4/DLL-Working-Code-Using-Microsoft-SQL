using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EdiFabric.Templates.Hipaa5010;
using EdiFabric.Core.Annotations.Validation;
using EdiFabric.Core.Model.Edi.X12;

public static class Edi835MetadataHelper
{
    public static Dictionary<string, List<string>> GetRequiredMetadata()
    {
        var requiredSegments = new List<string>();
        var requiredElements = new List<string>();
        var visitedTypes = new HashSet<Type>();

        // Envelope
        ExtractSegment(typeof(ISA), requiredElements);
        ExtractSegment(typeof(GS), requiredElements);
        ExtractSegment(typeof(ST), requiredElements);
        ExtractSegment(typeof(SE), requiredElements);
        ExtractSegment(typeof(GE), requiredElements);
        ExtractSegment(typeof(IEA), requiredElements);

        // Transaction
        ExtractRecursively(typeof(TS835), requiredSegments, requiredElements, visitedTypes);

        return new Dictionary<string, List<string>>
        {
            { "RequiredSegments", requiredSegments.Distinct().ToList() },
            { "RequiredElements", requiredElements.Distinct().ToList() }
        };
    }

    private static void ExtractSegment(Type type, List<string> requiredElements)
    {
        foreach (var prop in type.GetProperties())
        {
            if (prop.GetCustomAttribute<RequiredAttribute>() != null)
            {
                requiredElements.Add($"{type.Name}.{prop.Name}");
            }
        }
    }

    private static void ExtractRecursively(
        Type type,
        List<string> requiredSegments,
        List<string> requiredElements,
        HashSet<Type> visitedTypes)
    {
        if (visitedTypes.Contains(type))
            return;

        visitedTypes.Add(type);

        foreach (var prop in type.GetProperties())
        {
            var requiredAttr = prop.GetCustomAttribute<RequiredAttribute>();
            var propType = prop.PropertyType;

            if (propType.IsGenericType)
                propType = propType.GetGenericArguments()[0];

            bool isHipaaType =
                propType.Namespace != null &&
                propType.Namespace.Contains("Hipaa5010");

            if (requiredAttr != null)
            {
                if (isHipaaType)
                    requiredSegments.Add($"{type.Name}.{prop.Name}");
                else
                    requiredElements.Add($"{type.Name}.{prop.Name}");
            }

            if (isHipaaType && propType.IsClass)
                ExtractRecursively(propType, requiredSegments, requiredElements, visitedTypes);
        }
    }
}