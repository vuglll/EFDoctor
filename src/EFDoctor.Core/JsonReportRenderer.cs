using System.Text.Json;
using System.Text.Json.Serialization;

namespace EFDoctor.Core;

public static class JsonReportRenderer
{
    public static JsonSerializerOptions SerializerOptions { get; } = CreateOptions();

    public static string Render(IEnumerable<Finding> findings)
    {
        return JsonSerializer.Serialize(ReportEnvelope.Create(findings), SerializerOptions);
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
