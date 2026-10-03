using System.Text.Json;

namespace Careers.Application;

public static class CareersJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        // A body missing a required field is a client error (400), not a null waiting to blow up later.
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
    };
}
