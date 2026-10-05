using System.Text.Json.Serialization;

namespace Careers.Infrastructure.Ai;

// Wire format of careers-ai (its field names are Spanish). Kept here so the rest of the system never sees it.

internal sealed record AiContact(
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("telefono")] string? Phone,
    [property: JsonPropertyName("ubicacion")] string? Location,
    [property: JsonPropertyName("enlaces")] IReadOnlyDictionary<string, string> Links);

internal sealed record AiExperience(
    [property: JsonPropertyName("cargo")] string Role,
    [property: JsonPropertyName("organizacion")] string Organization,
    [property: JsonPropertyName("desde")] string From,
    [property: JsonPropertyName("hasta")] string? To,
    [property: JsonPropertyName("logros")] IReadOnlyList<string> Achievements,
    [property: JsonPropertyName("tecnologias")] IReadOnlyList<string> Technologies,
    [property: JsonPropertyName("ubicacion")] string? Location = null,
    [property: JsonPropertyName("incluir")] bool Include = true);

internal sealed record AiProject(
    [property: JsonPropertyName("nombre")] string Name,
    [property: JsonPropertyName("descripcion")] string Description,
    [property: JsonPropertyName("url")] string? Url,
    [property: JsonPropertyName("tecnologias")] IReadOnlyList<string> Technologies,
    [property: JsonPropertyName("incluir")] bool Include = true);

internal sealed record AiEducation(
    [property: JsonPropertyName("titulo")] string Title,
    [property: JsonPropertyName("institucion")] string Institution,
    [property: JsonPropertyName("desde")] string From,
    [property: JsonPropertyName("hasta")] string? To,
    [property: JsonPropertyName("ubicacion")] string? Location = null,
    [property: JsonPropertyName("incluir")] bool Include = true);

internal sealed record AiCertification(
    [property: JsonPropertyName("nombre")] string Name,
    [property: JsonPropertyName("emisor")] string Issuer,
    [property: JsonPropertyName("anio")] string Year,
    [property: JsonPropertyName("incluir")] bool Include = true);

internal sealed record AiLanguage(
    [property: JsonPropertyName("nombre")] string Name,
    [property: JsonPropertyName("nivel")] string Level,
    [property: JsonPropertyName("incluir")] bool Include = true);

internal sealed record AiSkillGroup(
    [property: JsonPropertyName("nombre")] string Name,
    [property: JsonPropertyName("items")] IReadOnlyList<string> Items);

internal sealed record AiCv(
    [property: JsonPropertyName("nombre")] string FullName,
    [property: JsonPropertyName("titular")] string Headline,
    [property: JsonPropertyName("resumen")] string Summary,
    [property: JsonPropertyName("contacto")] AiContact Contact,
    [property: JsonPropertyName("experiencia")] IReadOnlyList<AiExperience> Experience,
    [property: JsonPropertyName("proyectos")] IReadOnlyList<AiProject> Projects,
    [property: JsonPropertyName("educacion")] IReadOnlyList<AiEducation> Education,
    [property: JsonPropertyName("habilidades")] IReadOnlyList<AiSkillGroup> Skills,
    [property: JsonPropertyName("investigacion")] IReadOnlyList<AiExperience>? Research = null,
    [property: JsonPropertyName("certificaciones")] IReadOnlyList<AiCertification>? Certifications = null,
    [property: JsonPropertyName("idiomas")] IReadOnlyList<AiLanguage>? Languages = null,
    [property: JsonPropertyName("ocultas")] IReadOnlyList<string>? Hidden = null);

internal sealed record AiPreferences(
    [property: JsonPropertyName("excluir_titulos")] IReadOnlyList<string> ExcludeTitles,
    [property: JsonPropertyName("excluir_texto")] IReadOnlyList<string> ExcludeText,
    [property: JsonPropertyName("ubicaciones")] IReadOnlyList<string> Locations,
    [property: JsonPropertyName("obligatorias")] IReadOnlyList<string> RequiredTechnologies);

internal sealed record AiOffer(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("titulo")] string Title,
    [property: JsonPropertyName("empresa")] string Company,
    [property: JsonPropertyName("ubicacion")] string Location,
    [property: JsonPropertyName("etiquetas")] IReadOnlyList<string> Tags,
    [property: JsonPropertyName("descripcion")] string Description);

internal sealed record AiRankRequest(
    [property: JsonPropertyName("cv")] AiCv Cv,
    [property: JsonPropertyName("preferencias")] AiPreferences Preferences,
    [property: JsonPropertyName("ofertas")] IReadOnlyList<AiOffer> Offers);

internal sealed record AiRankedOffer(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("puntaje")] double Score,
    [property: JsonPropertyName("excluida")] string? Excluded,
    [property: JsonPropertyName("coinciden")] IReadOnlyList<string> Matches);

internal sealed record AiRankResponse(
    [property: JsonPropertyName("pendientes")] int Pending,
    [property: JsonPropertyName("resultados")] IReadOnlyList<AiRankedOffer> Results);

internal sealed record AiOfferRequest(
    [property: JsonPropertyName("cv")] AiCv Cv,
    [property: JsonPropertyName("oferta")] AiOffer Offer);

internal sealed record AiRenderRequest([property: JsonPropertyName("cv")] AiCv Cv);

internal sealed record AiAdaptResponse([property: JsonPropertyName("cv")] AiCv Cv);

internal sealed record AiLetterResponse([property: JsonPropertyName("carta")] string Letter);
