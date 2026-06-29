using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Reflection;

public class NullToEmptyStringContractResolver : DefaultContractResolver
{
    protected override JsonProperty CreateProperty(
        MemberInfo member,
        MemberSerialization memberSerialization)
    {
        var property = base.CreateProperty(member, memberSerialization);

        if (property.PropertyType == typeof(string))
        {
            // ✅ Handle Serialization (null → "")
            if (property.ValueProvider != null)
            {
                property.ValueProvider = new NullToEmptyStringValueProvider(property.ValueProvider);
            }

            // ✅ Handle Deserialization (null → "")
            property.DefaultValue = "";
            property.DefaultValueHandling = DefaultValueHandling.Populate;

            // ✅ Handle missing fields in JSON (missing → "")
            property.NullValueHandling = NullValueHandling.Include;
        }

        return property;
    }
}

public class NullToEmptyStringValueProvider : IValueProvider
{
    private readonly IValueProvider _valueProvider;

    public NullToEmptyStringValueProvider(IValueProvider valueProvider)
    {
        _valueProvider = valueProvider ?? throw new ArgumentNullException(nameof(valueProvider));
    }

    // Serialization - reading value FROM object
    public object? GetValue(object target)
    {
        var value = _valueProvider.GetValue(target);
        return value ?? "";
    }

    // Deserialization - setting value INTO object
    public void SetValue(object target, object? value)
    {
        // ✅ Convert null to empty string when setting
        _valueProvider.SetValue(target, value ?? "");
    }
}